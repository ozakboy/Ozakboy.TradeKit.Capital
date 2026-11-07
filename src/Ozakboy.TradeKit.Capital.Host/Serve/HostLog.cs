using System.Globalization;

namespace Ozakboy.TradeKit.Capital.Host.Serve;

/// <summary>
/// 宿主給人看的紀錄(stderr)。每一行寫出前先把登記過的字面值(登入帳號、密碼)換掉:帳號換成 <c>&lt;ID&gt;</c>,
/// 密碼換成 <c>&lt;PASSWORD&gt;</c>。stdout 只放協定訊息,不經過這裡。
/// The host's human-readable log on stderr. Every line has its registered literals (the login ID and the password)
/// replaced before it is written: the ID becomes <c>&lt;ID&gt;</c> and the password <c>&lt;PASSWORD&gt;</c>. stdout
/// carries protocol messages only and never goes through here.
/// </summary>
internal sealed class HostLog
{
    private readonly TextWriter _writer;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly List<(string Literal, string Replacement)> _secrets = [];

    /// <summary>
    /// 建立紀錄。
    /// Creates the log.
    /// </summary>
    /// <param name="writer">輸出(stderr)。The output, stderr.</param>
    /// <param name="time">時間來源。The time source.</param>
    public HostLog(TextWriter writer, TimeProvider time)
    {
        _writer = writer;
        _time = time;
    }

    /// <summary>
    /// 登記一個要換掉的字面值(比對忽略大小寫;空字串不登記)。
    /// Registers a literal to replace (case-insensitive; empty strings are ignored).
    /// </summary>
    /// <param name="literal">字面值。The literal.</param>
    /// <param name="replacement">換成什麼。The replacement.</param>
    public void AddSecret(string literal, string replacement)
    {
        if (string.IsNullOrEmpty(literal))
        {
            return;
        }

        lock (_gate)
        {
            _secrets.Add((literal, replacement));

            // 長的先換,避免短值是長值一部分時只換掉一半。Longer literals first so a short one inside a long one never leaves half.
            _secrets.Sort((left, right) => right.Literal.Length.CompareTo(left.Literal.Length));
        }
    }

    /// <summary>
    /// 把登記過的字面值換掉。
    /// Replaces the registered literals.
    /// </summary>
    /// <param name="text">原文。The text.</param>
    /// <returns>換過的文字。The redacted text.</returns>
    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        lock (_gate)
        {
            string result = text;

            foreach ((string literal, string replacement) in _secrets)
            {
                result = result.Replace(literal, replacement, StringComparison.OrdinalIgnoreCase);
            }

            return result;
        }
    }

    /// <summary>
    /// 寫一行(先遮再寫)。
    /// Writes one line, redacted first.
    /// </summary>
    /// <param name="message">訊息。The message.</param>
    public void Write(string message)
    {
        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"[{_time.GetUtcNow():HH:mm:ss.fff}Z] {Redact(message)}");

        lock (_gate)
        {
            try
            {
                _writer.WriteLine(line);
                _writer.Flush();
            }
            catch (IOException)
            {
                // stderr 被關掉(父行程結束)時不能讓紀錄拖垮宿主。A closed stderr must not bring the host down.
            }
            catch (ObjectDisposedException)
            {
                // 同上。Same as above.
            }
        }
    }
}
