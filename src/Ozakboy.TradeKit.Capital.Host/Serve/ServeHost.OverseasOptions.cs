using System.Globalization;
using System.Text.Json;

namespace Ozakboy.TradeKit.Capital.Host.Serve;

// 0.3.0:海外選擇權報價(SKOOQuoteLib)。自己一個狀態機(與國內同一套階段:connecting / ready / disconnected / unavailable),
// 在海外第一次 ready 之後 EnterMonitorLONG;任何失敗只改 overseasOptions 欄位,不影響海外與國內、不影響結束碼。
// 手冊 V2.13.59 沒有這個物件的章節:連線代碼(推定 3001 / 3003 都算連上、3002 斷線)、商品清單格式與結尾、
// 訂閱字串都比照 SKOSQuoteLib,全部待 probe-full 實測。
// 0.3.0: overseas option quotes with their own state machine, entered after the first overseas ready. Any failure only
// changes the overseasOptions fields. The manual has no chapter for this object, so every rule is assumed from
// SKOSQuoteLib and still to be measured.
internal sealed partial class ServeHost
{
    private const int OptionsCodeConnected = 3001;
    private const int OptionsCodeDisconnected = 3002;
    private const int OptionsCodeReady = 3003;

    private readonly Queue<string> _optionsDeferred = new();
    private readonly List<string> _optionSubscriptions = [];
    private readonly List<string> _optionDepth = [];
    private readonly Dictionary<int, SymbolInfo> _optionIndex = [];

    // 階段沿用國內的列舉(同一套轉換規則)。The phases reuse the domestic enum; the transitions are the same.
    private DomesticPhase _optionsPhase = DomesticPhase.Idle;
    private string? _optionsReason;
    private int? _optionsCode;
    private bool _optionsOpened;
    private bool _optionsEntered;
    private int _optionsAttempt;
    private int _optionsGeneration;
    private ITimer? _optionsTimer;
    private OptionProductCollector? _optionProductCollector;

    private void HookOverseasOptionEvents()
    {
        _source.OverseasOptionConnection += code =>
        {
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"海外選擇權 OnConnect({code})"));
            _ = _inbox.Writer.TryWrite(new OverseasOptionsConnectItem(code));
        };
        _source.OverseasOptionProduct += OnOverseasOptionProduct;
        _source.OverseasOptionQuote += OnOverseasOptionQuote;
        _source.OverseasOptionTick += OnOverseasOptionTick;
        _source.OverseasOptionBest5 += book => OnOverseasOptionBook("oo-best5", book);
        _source.OverseasOptionBest10 += book => OnOverseasOptionBook("oo-best10", book);
    }

    private void OpenOverseasOptions()
    {
        SkOpenFailure? failure;

        try
        {
            failure = _source.OpenOverseasOptions();
        }
#pragma warning disable CA1031 // 海外選擇權是選用的:任何失敗都只讓它不可用。Optional: any failure only disables it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            failure = new SkOpenFailure(ServeStates.ReasonCreateFailed, exception.HResult);
        }

        if (failure is null)
        {
            _optionsOpened = true;
            return;
        }

        _log.Write(string.Create(CultureInfo.InvariantCulture, $"建立海外選擇權報價物件失敗:{failure.Reason}(0x{failure.HResult:X8}),海外選擇權不可用,其他照常。"));
        SetOverseasOptions(DomesticPhase.Unavailable, failure.Reason, null);
    }

    /// <summary>海外第一次 ready 之後呼叫。Called after the overseas side is ready for the first time.</summary>
    private void StartOverseasOptions()
    {
        if (!_optionsOpened || _optionsPhase != DomesticPhase.Idle)
        {
            return;
        }

        int code = _source.EnterOverseasOptionMonitor();

        if (code != 0)
        {
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"海外選擇權 EnterMonitorLONG 回 {code}:{_log.Redact(SafeMessage(code))}。海外選擇權不可用,其他照常。"));
            SetOverseasOptions(DomesticPhase.Unavailable, DomesticStates.ReasonEnterRejected, code);
            EmitDomesticStatus();
            return;
        }

        _optionsEntered = true;
        SetOverseasOptions(DomesticPhase.Connecting, null, null);
        _ = ArmOverseasOptionsTimer(TimerKind.OverseasOptionsReadyDeadline, _timings.OverseasOptionsReadyTimeout);
        EmitDomesticStatus();
    }

    private void HandleOverseasOptionsConnect(int code)
    {
        switch (code)
        {
            case OptionsCodeConnected or OptionsCodeReady when _optionsEntered && _optionsPhase != DomesticPhase.Ready:
                CancelOverseasOptionsTimer();
                _optionsAttempt = 0;
                ClearOptionIndex();
                SetOverseasOptions(DomesticPhase.Ready, null, null);
                _log.Write("海外選擇權報價主機就緒。");
                ResendOverseasOptionSubscriptions();
                EmitDomesticStatus();
                ReleaseOverseasOptionsDeferred();
                break;
            case OptionsCodeDisconnected when _optionsPhase is DomesticPhase.Ready or DomesticPhase.Connecting:
                CancelOverseasOptionsTimer();
                _optionsAttempt = 0;
                ClearOptionIndex();
                SetOverseasOptions(DomesticPhase.Disconnected, DomesticStates.ReasonConnectionLost, null);
                EmitDomesticStatus();
                ReleaseOverseasOptionsDeferred();
                ScheduleOverseasOptionsReconnect();
                break;
            case OptionsCodeDisconnected when _optionsPhase == DomesticPhase.Reconnecting:
                _optionsAttempt++;
                _optionsPhase = DomesticPhase.Disconnected;
                ScheduleOverseasOptionsReconnect();
                break;
        }
    }

    private void HandleOverseasOptionsTimer(TimerItem timer)
    {
        if (timer.Generation != _optionsGeneration)
        {
            return;
        }

        switch (timer.Kind)
        {
            case TimerKind.OverseasOptionsReadyDeadline when _optionsPhase == DomesticPhase.Connecting:
                _log.Write(string.Create(CultureInfo.InvariantCulture, $"{_timings.OverseasOptionsReadyTimeout.TotalSeconds:F0} 秒內沒有收到海外選擇權的連線事件,海外選擇權不可用,其他照常。"));
                SetOverseasOptions(DomesticPhase.Unavailable, DomesticStates.ReasonReadyTimeout, null);
                EmitDomesticStatus();
                ReleaseOverseasOptionsDeferred();
                break;
            case TimerKind.OverseasOptionsReadyDeadline when _optionsPhase == DomesticPhase.Reconnecting:
                _log.Write("海外選擇權重連逾時,稍後再試。");
                _optionsAttempt++;
                _optionsPhase = DomesticPhase.Disconnected;
                ScheduleOverseasOptionsReconnect();
                break;
            case TimerKind.OverseasOptionsReconnectDue when _optionsPhase == DomesticPhase.Disconnected:
                int code = _source.EnterOverseasOptionMonitor();
                LogCode("重新海外選擇權 EnterMonitorLONG", code);

                if (code != 0)
                {
                    _optionsAttempt++;
                    ScheduleOverseasOptionsReconnect();
                }
                else
                {
                    _optionsPhase = DomesticPhase.Reconnecting;
                    _ = ArmOverseasOptionsTimer(TimerKind.OverseasOptionsReadyDeadline, _timings.OverseasOptionsReadyTimeout);
                }

                break;
        }
    }

    private void ScheduleOverseasOptionsReconnect()
    {
        TimeSpan delay = _timings.ReconnectDelay(_optionsAttempt);
        _log.Write(string.Create(CultureInfo.InvariantCulture, $"海外選擇權報價主機斷線,{delay.TotalSeconds:F1} 秒後第 {_optionsAttempt + 1} 次重連。"));
        _ = ArmOverseasOptionsTimer(TimerKind.OverseasOptionsReconnectDue, delay);
    }

    private void LeaveOverseasOptions()
    {
        if (_optionsEntered)
        {
            LogCode("海外選擇權 LeaveMonitor", _source.LeaveOverseasOptionMonitor());
        }
    }

    private int ArmOverseasOptionsTimer(TimerKind kind, TimeSpan due)
    {
        CancelOverseasOptionsTimer();
        int generation = ++_optionsGeneration;
        _optionsTimer = _time.CreateTimer(_ => _inbox.Writer.TryWrite(new TimerItem(kind, generation)), null, due, Timeout.InfiniteTimeSpan);
        return generation;
    }

    private void CancelOverseasOptionsTimer()
    {
        _optionsTimer?.Dispose();
        _optionsTimer = null;
        _optionsGeneration++;
    }

    private void SetOverseasOptions(DomesticPhase phase, string? reason, int? code)
    {
        _optionsPhase = phase;
        _optionsReason = reason;
        _optionsCode = code;
    }

    private void ReleaseOverseasOptionsDeferred()
    {
        while (_optionsDeferred.TryDequeue(out string? line))
        {
            _deferred.Enqueue(line);
        }
    }

    private string OverseasOptionsWireState() => _optionsPhase switch
    {
        DomesticPhase.Ready => DomesticStates.Ready,
        DomesticPhase.Disconnected or DomesticPhase.Reconnecting => DomesticStates.Disconnected,
        DomesticPhase.Unavailable => DomesticStates.Unavailable,
        _ => DomesticStates.Connecting,
    };

    private void WriteOverseasOptionsFields(Utf8JsonWriter writer)
    {
        writer.WriteString("overseasOptions", OverseasOptionsWireState());

        if (_optionsReason is { } reason)
        {
            writer.WriteString("overseasOptionsReason", reason);
        }

        if (_optionsCode is { } code)
        {
            writer.WriteNumber("overseasOptionsCode", code);
        }
    }

    private void WriteOverseasOptionsUnavailable(long id)
    {
        string state = OverseasOptionsWireState();
        _out.WriteError(
            id,
            WireErrorCodes.OverseasOptionsUnavailable,
            $"海外選擇權報價目前不可用({state})。The overseas option quote service is not available right now ({state}).",
            writer =>
            {
                writer.WriteString("overseasOptions", state);

                if (_optionsReason is { } reason)
                {
                    writer.WriteString("reason", reason);
                }

                if (_optionsCode is { } code)
                {
                    writer.WriteNumber("skCode", code);
                }
            });
    }

    // ── oo-products ──

    private async Task HandleOverseasOptionProductsAsync(long id, string line)
    {
        if (_optionsPhase is DomesticPhase.Idle or DomesticPhase.Connecting)
        {
            _optionsDeferred.Enqueue(line);
            return;
        }

        if (_optionsPhase != DomesticPhase.Ready)
        {
            WriteOverseasOptionsUnavailable(id);
            return;
        }

        OptionProductCollector collector = new();

        lock (_eventGate)
        {
            _optionProductCollector = collector;
        }

        int code = _source.RequestOverseasOptionProducts();
        bool completed = false;

        if (code == 0)
        {
            using CancellationTokenSource delayCancel = new();
            Task finished = await Task.WhenAny(collector.Completed.Task, Task.Delay(_timings.ProductsTimeout, _time, delayCancel.Token)).ConfigureAwait(true);
            completed = finished == collector.Completed.Task;
            await delayCancel.CancelAsync().ConfigureAwait(true);
        }

        string[] rows;

        lock (_eventGate)
        {
            _optionProductCollector = null;
            rows = [.. collector.Rows];
        }

        if (code != 0)
        {
            string message = _log.Redact(SafeMessage(code));
            _out.WriteError(
                id,
                WireErrorCodes.ProductsRejected,
                string.Create(CultureInfo.InvariantCulture, $"海外選擇權商品清單請求被拒(代碼 {code}):{message}。The overseas option product request was rejected (code {code})."),
                writer =>
                {
                    writer.WriteNumber("skCode", code);
                    writer.WriteString("skMessage", message);
                });
            return;
        }

        if (!completed)
        {
            _out.WriteError(
                id,
                WireErrorCodes.ProductsTimeout,
                string.Create(CultureInfo.InvariantCulture, $"海外選擇權商品清單在 {_timings.ProductsTimeout.TotalSeconds:F0} 秒內沒有收到結尾。The overseas option product list did not end in time."),
                writer => writer.WriteNumber("received", rows.Length));
            return;
        }

        _out.WriteResult(id, writer => WriteStringArray(writer, "items", rows));
    }

    private void OnOverseasOptionProduct(string row)
    {
        lock (_eventGate)
        {
            OptionProductCollector? collector = _optionProductCollector;

            if (collector is null)
            {
                return;
            }

            // 實測 2026-11-09:以「##,##,##,##,##」那筆結尾。Measured: a "##,##,##,##,##" row ends the list.
            if (row.TrimStart().StartsWith("##", StringComparison.Ordinal))
            {
                _ = collector.Completed.TrySetResult();
                return;
            }

            collector.Rows.Add(row);
        }
    }

    // ── oo-subscribe / oo-depth ──

    private void HandleOverseasOptionSubscribe(long id, string line, JsonElement parameters, bool depth)
    {
        string method = depth ? "oo-depth" : "oo-subscribe";

        if (!TryReadStringArray(parameters, "symbols", IsValidSymbol, out List<string> symbols) || symbols.Count == 0)
        {
            _out.WriteError(id, WireErrorCodes.RequestInvalid, $"{method} 需要 symbols 陣列(交易所,代碼)。{method} needs a symbols array of \"exchange,code\".");
            return;
        }

        if (_optionsPhase is DomesticPhase.Idle or DomesticPhase.Connecting)
        {
            _optionsDeferred.Enqueue(line);
            return;
        }

        if (_optionsPhase == DomesticPhase.Unavailable)
        {
            WriteOverseasOptionsUnavailable(id);
            return;
        }

        List<string> target = depth ? _optionDepth : _optionSubscriptions;
        List<string> fresh = [.. symbols.Where(symbol => !target.Contains(symbol, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal)];

        if (fresh.Count > 0 && _optionsPhase == DomesticPhase.Ready)
        {
            if (depth)
            {
                foreach (string symbol in fresh)
                {
                    int code = _source.RequestOverseasOptionMarketDepth(symbol);

                    if (code != 0)
                    {
                        WriteSubscribeRejected(id, code, symbol);
                        return;
                    }

                    target.Add(symbol);
                }
            }
            else
            {
                int code = _source.RequestOverseasOptionStocks(string.Join('#', fresh));

                if (code != 0)
                {
                    WriteSubscribeRejected(id, code, string.Join('#', fresh));
                    return;
                }

                target.AddRange(fresh);

                foreach (string symbol in fresh)
                {
                    int ticks = _source.RequestOverseasOptionTicks(symbol);

                    if (ticks != 0)
                    {
                        WriteSubscribeRejected(id, ticks, symbol);
                        return;
                    }
                }
            }
        }
        else
        {
            // 斷線中:先記住,重連後重送。While disconnected: remembered and re-sent after the reconnect.
            target.AddRange(fresh);
        }

        _out.WriteResult(id, writer => WriteStringArray(writer, depth ? "depth" : "subscribed", target));
    }

    private void ResendOverseasOptionSubscriptions()
    {
        if (_optionSubscriptions.Count > 0)
        {
            LogCode("重送海外選擇權 RequestStocks", _source.RequestOverseasOptionStocks(string.Join('#', _optionSubscriptions)));

            foreach (string symbol in _optionSubscriptions)
            {
                LogCode("重送海外選擇權 RequestTicks " + symbol, _source.RequestOverseasOptionTicks(symbol));
            }
        }

        foreach (string symbol in _optionDepth)
        {
            LogCode("重送海外選擇權 RequestMarketDepth " + symbol, _source.RequestOverseasOptionMarketDepth(symbol));
        }
    }

    // ── 事件 ──

    private void ClearOptionIndex()
    {
        lock (_eventGate)
        {
            _optionIndex.Clear();
        }
    }

    private bool TryResolveOption(int index, out SymbolInfo info)
    {
        lock (_eventGate)
        {
            if (_optionIndex.TryGetValue(index, out info))
            {
                return true;
            }
        }

        int code = _source.GetOverseasOptionStockByIndex(index, out SkOptionQuote quote);

        if (code != 0 || string.IsNullOrEmpty(quote.StockNo))
        {
            LogCode(string.Create(CultureInfo.InvariantCulture, $"海外選擇權 GetStockByIndexLONG({index})"), code == 0 ? -1 : code);
            info = default;
            return false;
        }

        info = new SymbolInfo(quote.ExchangeNo + "," + quote.StockNo, quote.Decimals);

        lock (_eventGate)
        {
            _optionIndex[index] = info;
        }

        return true;
    }

    private void OnOverseasOptionQuote(int index)
    {
        int code = _source.GetOverseasOptionStockByIndex(index, out SkOptionQuote quote);

        if (code != 0)
        {
            LogCode(string.Create(CultureInfo.InvariantCulture, $"海外選擇權 GetStockByIndexLONG({index})"), code);
            return;
        }

        string symbol = quote.ExchangeNo + "," + quote.StockNo;

        lock (_eventGate)
        {
            _optionIndex[index] = new SymbolInfo(symbol, quote.Decimals);
        }

        _out.WriteEvent("oo-quote", writer =>
        {
            writer.WriteString("symbol", symbol);
            writer.WriteString("marketNo", quote.MarketNo);
            writer.WriteString("exchangeName", quote.ExchangeName);
            writer.WriteString("name", quote.StockName);
            writer.WriteString("callPut", quote.CallPut);
            writer.WriteNumber("decimals", quote.Decimals);
            writer.WriteNumber("denominator", quote.Denominator);
            writer.WriteNumber("tradingDay", quote.TradingDay);
            writer.WriteNumber("open", quote.Open);
            writer.WriteNumber("high", quote.High);
            writer.WriteNumber("low", quote.Low);
            writer.WriteNumber("close", quote.Close);
            writer.WriteNumber("settle", quote.Settle);
            writer.WriteNumber("ref", quote.Reference);
            writer.WriteNumber("bid", quote.Bid);
            writer.WriteNumber("bidQty", quote.BidQuantity);
            writer.WriteNumber("ask", quote.Ask);
            writer.WriteNumber("askQty", quote.AskQuantity);
            writer.WriteNumber("totalQty", quote.TotalQuantity);
            writer.WriteNumber("tickQty", quote.TickQuantity);
            writer.WriteNumber("strike", quote.StrikePrice);
        });
    }

    private void OnOverseasOptionTick(SkTickData tick)
    {
        if (!TryResolveOption(tick.Index, out SymbolInfo info))
        {
            return;
        }

        _out.WriteEvent("oo-tick", writer =>
        {
            writer.WriteString("symbol", info.Symbol);
            writer.WriteNumber("ptr", tick.Pointer);
            writer.WriteString("date", tick.Date.ToString("D8", CultureInfo.InvariantCulture));
            writer.WriteString("time", tick.Time.ToString("D6", CultureInfo.InvariantCulture));
            writer.WriteNumber("price", tick.Price);
            writer.WriteNumber("qty", tick.Quantity);
            writer.WriteBoolean("history", tick.IsHistory);
            writer.WriteNumber("decimals", info.Decimals);
        });
    }

    private void OnOverseasOptionBook(string eventName, SkBook book)
    {
        if (!TryResolveOption(book.Index, out SymbolInfo info))
        {
            return;
        }

        WriteBook(eventName, "symbol", info.Symbol, info.Decimals, book);
    }

    private sealed class OptionProductCollector
    {
        public List<string> Rows { get; } = [];

        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
