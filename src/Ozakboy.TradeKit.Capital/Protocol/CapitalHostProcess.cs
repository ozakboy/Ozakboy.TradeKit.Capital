using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Ozakboy.TradeKit.Capital.Protocol;

/// <summary>
/// 一個宿主子行程:帳密只放進 <see cref="ProcessStartInfo.Environment"/>、stdin 送請求、stdout 讀回應與事件、stderr 轉給呼叫端
/// (已遮罩)。行程結束時所有進行中的請求以 <see cref="CapitalErrorCodes.HostExited"/> 失敗。
/// One host child process: the credentials go only into <see cref="ProcessStartInfo.Environment"/>, requests are written
/// to stdin, responses and events read from stdout, and stderr handed to the caller (masked). When the process ends,
/// every request in flight fails with <see cref="CapitalErrorCodes.HostExited"/>.
/// </summary>
internal sealed class CapitalHostProcess : IAsyncDisposable
{
    private const string LoginIdVariable = "CAPITAL_LOGIN_ID";
    private const string PasswordVariable = "CAPITAL_PASSWORD";
    private const string LogDirectoryVariable = "CAPITAL_LOG_DIR";

    private readonly Process _process;
    private readonly Func<string, string> _mask;
    private readonly Action<CapitalHostProcess, HostStatus> _onStatus;
    private readonly Action<string, JsonElement> _onMarketEvent;
    private readonly Action<string> _onProtocolWarning;
    private readonly ConcurrentDictionary<long, PendingRequest> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly TaskCompletionSource<Result> _startup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<int?> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeProvider _time;
    private Task? _readLoop;
    private long _nextId;
    private int _disposed;
    private int? _exitCode;

    private CapitalHostProcess(
        Process process,
        Func<string, string> mask,
        Action<CapitalHostProcess, HostStatus> onStatus,
        Action<string, JsonElement> onMarketEvent,
        Action<string> onProtocolWarning,
        TimeProvider time)
    {
        _process = process;
        _mask = mask;
        _onStatus = onStatus;
        _onMarketEvent = onMarketEvent;
        _onProtocolWarning = onProtocolWarning;
        _time = time;
    }

    /// <summary>行程識別碼。The process id.</summary>
    public int ProcessId { get; private init; }

    /// <summary>
    /// 啟動結果:ready 為成功;login_failed、faulted 或行程先結束為失敗。
    /// The start outcome: success on ready, failure on login_failed, faulted or an early exit.
    /// </summary>
    public Task<Result> Startup => _startup.Task;

    /// <summary>行程結束(stdout 讀完且行程退出)時完成,值是結束碼。Completes with the exit code once the process ended.</summary>
    public Task<int?> Exited => _exited.Task;

    /// <summary>
    /// 啟動宿主子行程。
    /// Starts the host child process.
    /// </summary>
    public static Result<CapitalHostProcess> Start(
        CapitalHostOptions options,
        CapitalCredentials credentials,
        Func<string, string> mask,
        Action<CapitalHostProcess, HostStatus> onStatus,
        Action<string, JsonElement> onMarketEvent,
        Action<string> onStderr,
        Action<string> onProtocolWarning,
        TimeProvider time)
    {
        ProcessStartInfo startInfo = new(options.HostExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = Path.GetDirectoryName(options.HostExecutablePath) ?? string.Empty,
        };

        startInfo.ArgumentList.Add("serve");

        IDictionary<string, string?> environment = startInfo.Environment;

        foreach ((string key, string value) in options.AdditionalEnvironment)
        {
            environment[key] = value;
        }

        // 帳密只走子行程環境變數,最後才設,任何額外設定都蓋不掉。Credentials go only into the child's environment, set last.
        environment[LoginIdVariable] = credentials.LoginId;
        environment[PasswordVariable] = credentials.Password;

        if (string.IsNullOrWhiteSpace(options.LogDirectory))
        {
            _ = environment.Remove(LogDirectoryVariable);
        }
        else
        {
            environment[LogDirectoryVariable] = options.LogDirectory;
        }

        Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { } line)
            {
                onStderr(mask(line));
            }
        };

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                return CapitalErrors.HostStartFailed("Process.Start 回傳 false。Process.Start returned false.");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            process.Dispose();
            return CapitalErrors.HostStartFailed(mask(exception.Message));
        }

        // 行程已起來:之後的擁有權交給回傳的 CapitalHostProcess(呼叫端負責 DisposeAsync)。
        // The process is running; ownership passes to the returned instance, which the caller disposes.
        CapitalHostProcess host = new(process, mask, onStatus, onMarketEvent, onProtocolWarning, time)
        {
            ProcessId = SafeProcessId(process),
        };

        process.BeginErrorReadLine();
        host._readLoop = Task.Run(host.ReadLoopAsync);
        return host;
    }

    /// <summary>
    /// 送一個請求並等回應。
    /// Sends a request and waits for its response.
    /// </summary>
    /// <param name="method">method。</param>
    /// <param name="writeParams">寫 params 物件內容的委派(可空)。Writes the params members, optional.</param>
    /// <param name="timeout">等回應的上限。How long to wait.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>result 物件,或失敗。The result object or a failure.</returns>
    public async Task<Result<JsonElement>> RequestAsync(
        string method,
        Action<Utf8JsonWriter>? writeParams,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (_exited.Task.IsCompleted)
        {
            return CapitalErrors.HostExited(_exitCode);
        }

        long id = Interlocked.Increment(ref _nextId);
        PendingRequest pending = new(method);
        _pending[id] = pending;

        // 加進待回應表之後再檢查一次:行程剛好在這之間結束時,結束處理可能已經清過表了。
        // Checked again after registering: the exit handler may have drained the table in between.
        if (_exited.Task.IsCompleted)
        {
            _ = _pending.TryRemove(id, out _);
            return CapitalErrors.HostExited(_exitCode);
        }

        string line = BuildRequest(id, method, writeParams);

        try
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _ = _writeGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            _ = _pending.TryRemove(id, out _);
            return CapitalErrors.RequestCancelled(method);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            _ = _pending.TryRemove(id, out _);
            return CapitalErrors.HostExited(_exitCode);
        }

        try
        {
            return await pending.Completion.Task.WaitAsync(timeout, _time, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _ = _pending.TryRemove(id, out _);
            return CapitalErrors.RequestTimeout(method, timeout);
        }
        catch (OperationCanceledException)
        {
            _ = _pending.TryRemove(id, out _);
            return CapitalErrors.RequestCancelled(method);
        }
    }

    /// <summary>
    /// 請宿主結束:送 shutdown,等行程退出;逾時就終止。
    /// Asks the host to end: sends shutdown and waits for the exit, killing it on timeout.
    /// </summary>
    /// <param name="timeout">上限。The limit.</param>
    /// <returns>是否被強制終止。Whether it had to be killed.</returns>
    public async Task<bool> ShutdownAsync(TimeSpan timeout)
    {
        if (!_exited.Task.IsCompleted)
        {
            _ = await RequestAsync("shutdown", null, timeout, CancellationToken.None).ConfigureAwait(false);
        }

        try
        {
            _ = await _exited.Task.WaitAsync(timeout, _time).ConfigureAwait(false);
            return false;
        }
        catch (TimeoutException)
        {
            Kill();
            return true;
        }
    }

    /// <summary>終止整棵行程樹。Kills the whole process tree.</summary>
    public void Kill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // 已經結束。Already ended.
        }
        catch (Win32Exception)
        {
            // 正在結束或沒有權限,沒有更多能做的。Ending or access denied; nothing more to do.
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Kill();

        if (_readLoop is not null)
        {
            try
            {
                await _readLoop.WaitAsync(TimeSpan.FromSeconds(5), _time).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // 讀取迴圈卡住也不影響釋放。A stuck read loop does not block disposal.
            }
        }

        _process.Dispose();
        _writeGate.Dispose();
    }

    private static int SafeProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private static string BuildRequest(long id, string method, Action<Utf8JsonWriter>? writeParams)
    {
        ArrayBufferWriter<byte> buffer = new(256);

        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", id);
            writer.WriteString("method", method);
            writer.WriteStartObject("params");
            writeParams?.Invoke(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// 宿主結束前沒有送出狀態時,以結束碼推斷原因(DESIGN §2:2 缺帳密、3 SKCOM、4 登入、5 連線)。
    /// Infers the reason from the exit code when the host ended without a status.
    /// </summary>
    private static Error ErrorForExit(int? exitCode) => exitCode switch
    {
        2 => CapitalErrors.CredentialsMissing(),
        3 => CapitalErrors.Faulted("skcom_create_failed", null, null, null),
        4 => CapitalErrors.LoginFailed(-1, string.Empty),
        5 => CapitalErrors.Faulted("connect_timeout", null, null, null),
        _ => CapitalErrors.HostExited(exitCode),
    };

    private async Task ReadLoopAsync()
    {
        try
        {
            StreamReader reader = _process.StandardOutput;

            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                Dispatch(line);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // 管線被關掉:往下照「行程結束」處理。A broken pipe is handled as the process ending.
        }

        int? exitCode = null;

        try
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
            exitCode = _process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            // 行程物件已釋放。The process object was disposed.
        }

        _exitCode = exitCode;
        Error exited = CapitalErrors.HostExited(exitCode);

        foreach (long id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out PendingRequest? pending))
            {
                _ = pending.Completion.TrySetResult(exited);
            }
        }

        _ = _startup.TrySetResult(ErrorForExit(exitCode));
        _ = _exited.TrySetResult(exitCode);
    }

    private void Dispatch(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            _onProtocolWarning("stdout 有一行不是 JSON,略過。A stdout line is not JSON and was skipped.");
            return;
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                _onProtocolWarning("stdout 有一行不是 JSON 物件,略過。A stdout line is not a JSON object and was skipped.");
                return;
            }

            if (root.TryGetProperty("event", out JsonElement eventName) && eventName.ValueKind == JsonValueKind.String)
            {
                JsonElement data = root.TryGetProperty("data", out JsonElement value) ? value : default;
                string name = eventName.GetString() ?? string.Empty;

                if (name == "status")
                {
                    HandleStatus(CapitalWire.ParseStatus(data));
                }
                else
                {
                    _onMarketEvent(name, data);
                }

                return;
            }

            if (root.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt64(out long id))
            {
                if (!_pending.TryRemove(id, out PendingRequest? pending))
                {
                    return;
                }

                _ = pending.Completion.TrySetResult(ReadResponse(pending.Method, root));
                return;
            }

            _onProtocolWarning("stdout 有一行既不是事件也不是回應,略過。A stdout line is neither an event nor a response.");
        }
    }

    private static Result<JsonElement> ReadResponse(string method, JsonElement root)
    {
        if (root.TryGetProperty("result", out JsonElement result))
        {
            return result.Clone();
        }

        if (root.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object)
        {
            JsonElement data = error.TryGetProperty("data", out JsonElement value) ? value : default;

            return CapitalErrors.FromHost(
                method,
                CapitalWire.GetString(error, "code") ?? CapitalErrorCodes.ProtocolError,
                CapitalWire.GetString(error, "message") ?? string.Empty,
                CapitalWire.GetInt32(data, "skCode"),
                CapitalWire.GetString(data, "skMessage"),
                CapitalWire.GetString(data, "symbol"));
        }

        return CapitalErrors.Protocol("回應既沒有 result 也沒有 error。The response has neither result nor error.");
    }

    private void HandleStatus(HostStatus raw)
    {
        // 宿主已把帳號換成 <ID>;這裡再遮一次,群益的說明文字才能放進錯誤與日誌。The host already redacted; masked again here.
        HostStatus status = raw.Message is null ? raw : raw with { Message = _mask(raw.Message) };

        switch (status.State)
        {
            case "ready":
                _ = _startup.TrySetResult(Result.Success());
                break;
            case "login_failed":
                _ = _startup.TrySetResult(CapitalErrors.LoginFailed(status.Code ?? -1, status.Message ?? string.Empty));
                break;
            case "faulted":
                _ = _startup.TrySetResult(CapitalErrors.Faulted(status.Reason ?? string.Empty, status.HResult, status.Code, status.Message));
                break;
        }

        _onStatus(this, status);
    }

    private sealed class PendingRequest
    {
        public PendingRequest(string method)
        {
            Method = method;
        }

        public string Method { get; }

        public TaskCompletionSource<Result<JsonElement>> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
