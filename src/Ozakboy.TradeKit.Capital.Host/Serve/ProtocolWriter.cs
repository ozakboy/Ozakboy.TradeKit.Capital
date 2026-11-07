using System.Buffers;
using System.Text.Json;

namespace Ozakboy.TradeKit.Capital.Host.Serve;

/// <summary>
/// stdout 的協定輸出:每行一個 JSON 物件(UTF-8、無 BOM、<c>\n</c> 結尾),寫完立刻 flush。執行緒安全。
/// The protocol output on stdout: one JSON object per line (UTF-8, no BOM, ending in <c>\n</c>), flushed right away.
/// Thread-safe.
/// </summary>
internal sealed class ProtocolWriter
{
    private static readonly byte[] NewLine = [(byte)'\n'];

    private readonly Stream _output;
    private readonly Lock _gate = new();
    private readonly ArrayBufferWriter<byte> _buffer = new(1024);
    private bool _broken;

    /// <summary>
    /// 建立輸出。
    /// Creates the output.
    /// </summary>
    /// <param name="output">stdout 串流。The stdout stream.</param>
    public ProtocolWriter(Stream output)
    {
        _output = output;
    }

    /// <summary>
    /// 寫一個事件:<c>{"event":名稱,"data":{…}}</c>。
    /// Writes an event.
    /// </summary>
    /// <param name="name">事件名稱。The event name.</param>
    /// <param name="data">寫 data 物件內容的委派。Writes the data object's members.</param>
    public void WriteEvent(string name, Action<Utf8JsonWriter> data) =>
        WriteLine(writer =>
        {
            writer.WriteString("event", name);
            writer.WriteStartObject("data");
            data(writer);
            writer.WriteEndObject();
        });

    /// <summary>
    /// 寫一個 status 事件。
    /// Writes a status event.
    /// </summary>
    /// <param name="state">狀態。The state.</param>
    /// <param name="extra">其他欄位。Extra members.</param>
    public void WriteStatus(string state, Action<Utf8JsonWriter>? extra = null) =>
        WriteEvent("status", writer =>
        {
            writer.WriteString("state", state);
            extra?.Invoke(writer);
        });

    /// <summary>
    /// 寫成功回應:<c>{"id":…,"result":{…}}</c>。
    /// Writes a success response.
    /// </summary>
    /// <param name="id">請求編號。The request id.</param>
    /// <param name="result">寫 result 物件內容的委派。Writes the result object's members.</param>
    public void WriteResult(long id, Action<Utf8JsonWriter> result) =>
        WriteLine(writer =>
        {
            writer.WriteNumber("id", id);
            writer.WriteStartObject("result");
            result(writer);
            writer.WriteEndObject();
        });

    /// <summary>
    /// 寫失敗回應:<c>{"id":…,"error":{"code":…,"message":…,"data":{…}}}</c>;讀不出編號時 id 為 null。
    /// Writes a failure response; the id is null when the request's id could not be read.
    /// </summary>
    /// <param name="id">請求編號。The request id.</param>
    /// <param name="code">錯誤碼(capital.*)。The error code.</param>
    /// <param name="message">訊息。The message.</param>
    /// <param name="data">寫 data 物件內容的委派(可空)。Writes the data object's members, optional.</param>
    public void WriteError(long? id, string code, string message, Action<Utf8JsonWriter>? data = null) =>
        WriteLine(writer =>
        {
            if (id is { } value)
            {
                writer.WriteNumber("id", value);
            }
            else
            {
                writer.WriteNull("id");
            }

            writer.WriteStartObject("error");
            writer.WriteString("code", code);
            writer.WriteString("message", message);

            if (data is not null)
            {
                writer.WriteStartObject("data");
                data(writer);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        });

    private void WriteLine(Action<Utf8JsonWriter> members)
    {
        lock (_gate)
        {
            if (_broken)
            {
                return;
            }

            _buffer.ResetWrittenCount();

            using (Utf8JsonWriter writer = new(_buffer))
            {
                writer.WriteStartObject();
                members(writer);
                writer.WriteEndObject();
            }

            try
            {
                _output.Write(_buffer.WrittenSpan);
                _output.Write(NewLine);
                _output.Flush();
            }
            catch (IOException)
            {
                // 父行程關掉了 stdout:之後的輸出都沒有人收,不要讓它變成宿主的例外。The parent closed stdout; nothing more can be delivered.
                _broken = true;
            }
            catch (ObjectDisposedException)
            {
                _broken = true;
            }
        }
    }
}
