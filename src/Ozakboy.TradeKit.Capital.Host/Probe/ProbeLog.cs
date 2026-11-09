using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Ozakboy.TradeKit.Capital.Host.Probe;

/// <summary>
/// 探針的輸出:<c>probe.jsonl</c> 每行一個事件(含收到時的 UTC 時間),另外把大量的原始字串(商品清單、K 線)寫成純文字檔。
/// 登入帳號與密碼絕不寫進任何檔案。
/// Probe output: one JSON event per line in probe.jsonl (with the UTC receive time), plus plain-text files for bulky raw
/// strings. The login ID and password are never written anywhere.
/// </summary>
internal sealed class ProbeLog : IDisposable
{
    // 有遮罩器時事件改用不跳脫中文的編碼器,遮罩才比對得到中文戶名(預設編碼器會把中文寫成 \uXXXX)。
    // With a redactor, events use an encoder that keeps CJK text as is, so a Chinese name can be matched and replaced.
    private static readonly JsonSerializerOptions RelaxedJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly object _gate = new();
    private readonly StreamWriter _events;
    private readonly Dictionary<string, StreamWriter> _raw = new(StringComparer.Ordinal);
    private readonly string _directory;
    private readonly ProbeRedactor? _redactor;

    /// <summary>
    /// 建立輸出目錄與事件檔(不遮罩;既有探針用)。
    /// Creates the output directory and the event file, without redaction.
    /// </summary>
    /// <param name="directory">輸出目錄。Output directory.</param>
    public ProbeLog(string directory)
        : this(directory, null)
    {
    }

    /// <summary>
    /// 建立輸出目錄與事件檔;給了遮罩器時,每個事件與每行原文寫出前都先遮罩。
    /// Creates the output directory and the event file; with a redactor, every event and raw line is redacted first.
    /// </summary>
    /// <param name="directory">輸出目錄。Output directory.</param>
    /// <param name="redactor">遮罩器(可空)。The redactor, optional.</param>
    public ProbeLog(string directory, ProbeRedactor? redactor)
    {
        _directory = directory;
        _redactor = redactor;
        Directory.CreateDirectory(directory);
        _events = new StreamWriter(Path.Combine(directory, "probe.jsonl"), append: false, new UTF8Encoding(false)) { AutoFlush = true };
    }

    /// <summary>
    /// 記一個事件,同時印到主控台。
    /// Records an event and echoes it to the console.
    /// </summary>
    /// <param name="kind">事件種類。Event kind.</param>
    /// <param name="fields">欄位。Fields.</param>
    public void Event(string kind, IReadOnlyDictionary<string, object?> fields)
    {
        Dictionary<string, object?> line = new(StringComparer.Ordinal)
        {
            ["utc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["kind"] = kind,
        };

        foreach (KeyValuePair<string, object?> field in fields)
        {
            line[field.Key] = field.Value;
        }

        string json = _redactor is null ? JsonSerializer.Serialize(line) : _redactor.Redact(JsonSerializer.Serialize(line, RelaxedJson));

        lock (_gate)
        {
            _events.WriteLine(json);
        }
    }

    /// <summary>
    /// 在主控台印一行進度(不進檔案)。
    /// Prints a progress line to the console only.
    /// </summary>
    /// <param name="message">訊息。Message.</param>
    public static void Say(string message) =>
        Console.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] {message}");

    /// <summary>
    /// 把一行原始字串附加到 <paramref name="name"/>.txt。
    /// Appends one raw line to name.txt.
    /// </summary>
    /// <param name="name">檔名(不含副檔名)。File name without extension.</param>
    /// <param name="line">原始字串。The raw line.</param>
    public void Raw(string name, string line)
    {
        if (_redactor is not null)
        {
            line = _redactor.Redact(line);
        }

        lock (_gate)
        {
            if (!_raw.TryGetValue(name, out StreamWriter? writer))
            {
                writer = new StreamWriter(Path.Combine(_directory, name + ".txt"), append: false, new UTF8Encoding(false)) { AutoFlush = true };
                _raw[name] = writer;
            }

            writer.WriteLine(line);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            _events.Dispose();

            foreach (StreamWriter writer in _raw.Values)
            {
                writer.Dispose();
            }

            _raw.Clear();
        }
    }
}
