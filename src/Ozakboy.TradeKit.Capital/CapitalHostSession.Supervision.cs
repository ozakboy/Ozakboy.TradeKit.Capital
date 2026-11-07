using System.Globalization;
using System.Text.Json;
using Ozakboy.TradeKit.Capital.Protocol;

namespace Ozakboy.TradeKit.Capital;

// 子行程的啟動、監督與重啟,以及宿主事件的轉換。
// Starting, supervising and restarting the child process, and converting the host's events.
public sealed partial class CapitalHostSession
{
    /// <summary>
    /// 啟動一個宿主行程並等到 ready;失敗時行程已終止並釋放。成功時設為 <see cref="_host"/>。
    /// Starts one host process and waits for ready; on failure it is already killed and disposed.
    /// </summary>
    private async Task<Result> LaunchAsync(CancellationToken cancellationToken)
    {
        CapitalCredentials? credentials = _credentials;

        if (credentials is null)
        {
            return CapitalErrors.InvalidState("沒有帳密可以啟動宿主。There are no credentials to start the host with.");
        }

        Result<CapitalHostProcess> started = CapitalHostProcess.Start(
            _options,
            credentials,
            Mask,
            OnHostStatus,
            OnMarketEvent,
            line => CapitalHostLog.HostOutput(_logger, line),
            reason => WarnOnce("protocol:" + reason, () => CapitalHostLog.ProtocolWarning(_logger, reason)),
            _time);

        if (started.IsFailure)
        {
            return started.Error;
        }

        CapitalHostProcess host = started.GetValueOrThrow();

        lock (_stateLock)
        {
            _host = host;
        }

        Result outcome;

        try
        {
            outcome = await host.Startup.WaitAsync(_options.StartTimeout, _time, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            outcome = CapitalErrors.HostStartTimeout(_options.StartTimeout);
        }
        catch (OperationCanceledException)
        {
            outcome = CapitalErrors.RequestCancelled("start");
        }

        if (outcome.IsFailure)
        {
            lock (_stateLock)
            {
                if (ReferenceEquals(_host, host))
                {
                    _host = null;
                }
            }

            await host.DisposeAsync().ConfigureAwait(false);
        }

        return outcome;
    }

    private void StartSupervisor()
    {
        CapitalHostProcess? host;
        CancellationTokenSource source = new();

        lock (_stateLock)
        {
            host = _host;
            _supervisorSource = source;
        }

        Task task = Task.Run(() => SuperviseAsync(host!, source.Token));

        lock (_stateLock)
        {
            _supervisorTask = task;
        }
    }

    private async Task<Task?> CancelSupervisorAsync()
    {
        CancellationTokenSource? source;
        Task? task;

        lock (_stateLock)
        {
            source = _supervisorSource;
            task = _supervisorTask;
        }

        try
        {
            if (source is not null)
            {
                await source.CancelAsync().ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException)
        {
            // 已經被 StopAsync 釋放。Already disposed by StopAsync.
        }

        return task;
    }

    /// <summary>
    /// 監督:等目前的宿主結束;結束碼 2 / 3 / 4 不重啟,其他以退避重啟並重送訂閱。
    /// Supervision: waits for the current host to end; exit codes 2, 3 and 4 are not restarted, anything else is
    /// restarted with backoff and the subscriptions are re-sent.
    /// </summary>
    private async Task SuperviseAsync(CapitalHostProcess first, CancellationToken cancellationToken)
    {
        CapitalHostProcess? current = first;
        long readyAt = _time.GetTimestamp();
        int consecutive = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (current is not null)
            {
                int? exitCode = await current.Exited.WaitAsync(cancellationToken).ConfigureAwait(false);

                lock (_stateLock)
                {
                    if (ReferenceEquals(_host, current))
                    {
                        _host = null;
                    }
                }

                await current.DisposeAsync().ConfigureAwait(false);
                current = null;

                if (_time.GetElapsedTime(readyAt) >= _options.StableAfter)
                {
                    consecutive = 0;
                }

                switch (exitCode)
                {
                    case 4:
                        CapitalHostLog.Faulted(_logger, "登入失敗,不重試 / login failed, never retried");
                        SetState(CapitalHostState.LoginFailed);
                        return;
                    case 2 or 3:
                        CapitalHostLog.Faulted(_logger, string.Create(CultureInfo.InvariantCulture, $"宿主結束碼 {exitCode} / host exit code {exitCode}"));
                        SetState(CapitalHostState.Faulted);
                        return;
                }

                if (consecutive < _options.MaxRestarts)
                {
                    CapitalHostLog.HostExited(_logger, exitCode ?? -1, consecutive + 1, RestartDelay(consecutive));
                }
            }

            if (consecutive >= _options.MaxRestarts)
            {
                CapitalHostLog.Faulted(_logger, string.Create(CultureInfo.InvariantCulture, $"重啟 {_options.MaxRestarts} 次仍失敗 / still failing after {_options.MaxRestarts} restarts"));
                SetState(CapitalHostState.Faulted);
                return;
            }

            TimeSpan delay = RestartDelay(consecutive);
            consecutive++;
            SetState(CapitalHostState.Starting);
            await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);

            Result launched = await LaunchAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (launched.IsFailure)
            {
                string code = launched.Error.Code;

                if (code == CapitalErrorCodes.LoginFailed)
                {
                    CapitalHostLog.Faulted(_logger, "重啟時登入失敗,不重試 / login failed on restart, never retried");
                    SetState(CapitalHostState.LoginFailed);
                    return;
                }

                if (code is CapitalErrorCodes.SkComNotRegistered or CapitalErrorCodes.SkComFileMissing or CapitalErrorCodes.SkComCreateFailed or CapitalErrorCodes.CredentialsMissing)
                {
                    CapitalHostLog.Faulted(_logger, code);
                    SetState(CapitalHostState.Faulted);
                    return;
                }

                // 連線逾時、行程又掛掉之類:算一次失敗的重啟,繼續退避。A connect timeout or another crash counts as a failed restart.
                continue;
            }

            lock (_stateLock)
            {
                current = _host;
            }

            await ResubscribeAsync(cancellationToken).ConfigureAwait(false);
            readyAt = _time.GetTimestamp();
            SetState(CapitalHostState.Ready);
        }
    }

    private TimeSpan RestartDelay(int attempt)
    {
        IReadOnlyList<TimeSpan> delays = _options.RestartDelays;
        return delays.Count == 0 ? TimeSpan.FromSeconds(30) : delays[Math.Clamp(attempt, 0, delays.Count - 1)];
    }

    private async Task ResubscribeAsync(CancellationToken cancellationToken)
    {
        CapitalSymbol[] symbols;
        CapitalHostProcess? host;

        lock (_stateLock)
        {
            symbols = [.. _subscriptions];
            host = _host;
        }

        if (symbols.Length == 0 || host is null)
        {
            return;
        }

        Result<JsonElement> response = await host.RequestAsync("subscribe", writer => WriteSymbols(writer, symbols), ShortRequestTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (response.IsFailure)
        {
            CapitalHostLog.ResubscribeFailed(_logger, response.Error.Code);
        }
    }

    private void OnHostStatus(CapitalHostProcess source, HostStatus status)
    {
        lock (_stateLock)
        {
            // 只理會目前這個行程(或正在啟動的那個)的狀態。Only the current (or starting) process counts.
            if (_host is not null && !ReferenceEquals(_host, source))
            {
                return;
            }
        }

        CapitalHostState current = State;

        switch (status.State)
        {
            case "logging_in" when current is CapitalHostState.Starting:
                SetState(CapitalHostState.LoggingIn);
                break;
            case "connecting" when current is CapitalHostState.Starting or CapitalHostState.LoggingIn:
                SetState(CapitalHostState.Connecting);
                break;
            case "disconnected" when current is CapitalHostState.Ready:
                SetState(CapitalHostState.Disconnected);
                break;
            case "ready" when current is CapitalHostState.Disconnected:
                // 宿主自己重連成功(它已重送訂閱)。The host reconnected by itself and already re-sent the subscriptions.
                SetState(CapitalHostState.Ready);
                break;
        }
    }

    private void OnMarketEvent(string name, JsonElement data)
    {
        switch (name)
        {
            case "quote":
                if (!CapitalWire.TryParseQuote(data, _time.GetUtcNow(), out CapitalQuote? quote, out int denominator, out string problem))
                {
                    WarnOnce("quote:" + problem, () => CapitalHostLog.EventDiscarded(_logger, problem));
                    return;
                }

                if (denominator != 1)
                {
                    string symbol = quote.Symbol.ToString();
                    WarnOnce("fraction:" + symbol, () => CapitalHostLog.PriceFraction(_logger, symbol, denominator));
                }

                _ = _events.Writer.TryWrite(quote);
                break;
            case "tick":
                if (!CapitalWire.TryParseTick(data, out CapitalTick? tick, out Error? error))
                {
                    WarnOnce("tick:" + error.Code + ":" + error.Message, () => CapitalHostLog.EventDiscarded(_logger, error.Message));
                    return;
                }

                _ = _events.Writer.TryWrite(tick);
                break;
            default:
                WarnOnce("event:" + name, () => CapitalHostLog.EventDiscarded(_logger, "不認得的事件 / unknown event " + name));
                break;
        }
    }
}
