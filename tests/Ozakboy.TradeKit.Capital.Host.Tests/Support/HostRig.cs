using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Ozakboy.TradeKit.Capital.Host.Tests.Support;

/// <summary>
/// 一個在記憶體裡跑的 <see cref="ServeHost"/>:stdout 收進 <see cref="LineCaptureStream"/>、stderr 收進字串,時限縮短。
/// An in-memory <see cref="ServeHost"/> with stdout captured as lines, stderr captured as text and short timings.
/// </summary>
internal sealed class HostRig : IDisposable
{
    public const string LoginId = "A123456789";
    public const string Password = "Pw-Fake-2026";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private readonly StringWriter _stderr = new();

    private HostRig(ServeTimings timings)
    {
        Output = new LineCaptureStream();
        Log = new HostLog(TextWriter.Synchronized(_stderr), TimeProvider.System);
        Host = new ServeHost(Source, new ProtocolWriter(Output), Log, timings, TimeProvider.System);
    }

    public ScriptedSkQuoteSource Source { get; } = new();

    public LineCaptureStream Output { get; }

    public HostLog Log { get; }

    public ServeHost Host { get; }

    public Task<int>? Run { get; private set; }

    public string Stderr => _stderr.ToString();

    public static HostRig Create(ServeTimings? timings = null) => new(timings ?? new ServeTimings
    {
        ConnectTimeout = TimeSpan.FromMilliseconds(500),
        ProductsTimeout = TimeSpan.FromMilliseconds(500),
        KLineQuiet = TimeSpan.FromMilliseconds(200),
        KLineNoData = TimeSpan.FromMilliseconds(500),
        KLinePoll = TimeSpan.FromMilliseconds(20),
        ReconnectDelays = [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100)],
        DomesticReadyTimeout = TimeSpan.FromMilliseconds(500),
        DomesticKLineTimeout = TimeSpan.FromMilliseconds(500),
    });

    /// <summary>
    /// 海外狀態的轉換序列:status 事件的 state,連續重複的合併成一個。0.2.0 起國內狀態改變會再送一次 state 不變的 status,
    /// 所以要看海外轉換就得把重複的合併。
    /// The overseas transitions: the states of the status events with consecutive repeats merged. Since 0.2.0 a domestic
    /// change sends another status with the same state, so repeats are merged to see the overseas transitions.
    /// </summary>
    public string[] OverseasStates()
    {
        List<string> states = [];

        foreach (JsonElement message in Output.Messages.Where(IsEvent("status")))
        {
            string state = message.GetProperty("data").GetProperty("state").GetString()!;

            if (states.Count == 0 || states[^1] != state)
            {
                states.Add(state);
            }
        }

        return [.. states];
    }

    /// <summary>國內狀態變成指定值的 status 事件。A status event whose domestic state is the given one.</summary>
    public static Func<JsonElement, bool> IsDomestic(string domestic) => element =>
        element.TryGetProperty("event", out JsonElement name) && name.GetString() == "status"
        && element.GetProperty("data").TryGetProperty("domestic", out JsonElement value) && value.GetString() == domestic;

    /// <summary>啟動 RunAsync(不等它結束)。Starts RunAsync without waiting for it.</summary>
    public HostRig Start(ServeLogin? login = null)
    {
        ServeLogin effective = login ?? ServeLogin.Create(LoginId, Password);
        Run = Task.Run(() => Host.RunAsync(effective));
        return this;
    }

    /// <summary>啟動並等到 ready。Starts and waits for ready.</summary>
    public async Task<HostRig> StartReadyAsync()
    {
        _ = Start();
        _ = await Output.WaitForAsync(IsStatus("ready"), Wait);
        return this;
    }

    public void Send(string json) => Host.EnqueueRequest(json);

    public async Task<JsonElement> RequestAsync(long id, string method, string? parameters = null)
    {
        Send(parameters is null
            ? $$"""{"id":{{id}},"method":"{{method}}"}"""
            : $$"""{"id":{{id}},"method":"{{method}}","params":{{parameters}}}""");

        return await Output.WaitForAsync(element => element.TryGetProperty("id", out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.GetInt64() == id, Wait);
    }

    public async Task<int> ExitCodeAsync()
    {
        Assert.IsNotNull(Run, "not started");
        return await Run.WaitAsync(Wait);
    }

    /// <summary>還在跑的協定層以 stdin 結束收尾。A still-running host is ended through end of input.</summary>
    public void Dispose()
    {
        Host.CompleteInput();
        _stderr.Dispose();
    }

    public static Func<JsonElement, bool> IsStatus(string state) => element =>
        element.TryGetProperty("event", out JsonElement name) && name.GetString() == "status"
        && element.GetProperty("data").GetProperty("state").GetString() == state;

    public static Func<JsonElement, bool> IsEvent(string eventName) => element =>
        element.TryGetProperty("event", out JsonElement name) && name.GetString() == eventName;
}

/// <summary>
/// 把寫進來的位元組依 <c>\n</c> 切成行並逐行解析成 JSON(驗證「每行一個 JSON 物件、UTF-8、無 BOM」)。
/// Splits written bytes into lines on '\n' and parses each as JSON, checking one object per line, UTF-8, no BOM.
/// </summary>
internal sealed class LineCaptureStream : Stream
{
    private readonly Lock _gate = new();
    private readonly List<byte> _pending = [];
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_gate)
            {
                return [.. _lines];
            }
        }
    }

    public IReadOnlyList<JsonElement> Messages => [.. Lines.Select(line => JsonDocument.Parse(line).RootElement)];

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public async Task<JsonElement> WaitForAsync(Func<JsonElement, bool> match, TimeSpan timeout)
    {
        Stopwatch watch = Stopwatch.StartNew();

        while (true)
        {
            foreach (JsonElement message in Messages)
            {
                if (match(message))
                {
                    return message;
                }
            }

            if (watch.Elapsed > timeout)
            {
                Assert.Fail("the expected line never arrived. Lines so far:\n" + string.Join('\n', Lines));
            }

            await Task.Delay(10);
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        lock (_gate)
        {
            foreach (byte value in buffer)
            {
                if (value == (byte)'\n')
                {
                    byte[] bytes = [.. _pending];
                    _pending.Clear();

                    Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "no BOM");
                    _lines.Add(new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes));
                }
                else
                {
                    _pending.Add(value);
                }
            }
        }
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}
