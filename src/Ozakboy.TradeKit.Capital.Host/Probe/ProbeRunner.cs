using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using Ozakboy.TradeKit.Capital.Host.Interop;

namespace Ozakboy.TradeKit.Capital.Host.Probe;

/// <summary>
/// 探針:登入群益、連海期報價主機,把原始回傳記下來,用來確認手冊沒寫清楚的格式(商品清單、報價欄位、成交時間的時區、
/// 歷史 K 格式、連線代碼)。只讀行情,不碰下單。帳密錯誤一律不重試(錯太多次群益會鎖帳號)。
/// The probe: logs in, connects to the overseas quote server and records raw replies so that formats the manual leaves
/// unclear can be confirmed. Market data only, never orders. A credential error is never retried.
/// </summary>
internal sealed class ProbeRunner
{
    private const int CodeConnected = 3001;
    private const int CodeReady = 3003;
    private const int MaxQuoteRecordsPerSymbol = 60;

    private static readonly string[] FxProbeCodes = ["USDTWD", "EURUSD", "USDJPY", "USDCNY"];
    private static readonly string[] FutureProbeRoots = ["NQ", "MNQ", "YM", "MYM", "ES", "MES", "GC", "CL"];

    // 第一輪實測:商品清單裡 CME / CBOT / NYM 的熱月代碼是「根代碼 + 0000」(NQ0000 = 小那熱 2612)
    private static readonly string[] FutureProbeSymbols = ["CME,NQ0000", "CBOT,YM0000", "NYM,GC0000"];
    private static readonly short[] KLineTypeCandidates = [1, 2, 3, 5, 6, 7];

    private readonly ConcurrentDictionary<int, (int Date, int Time, DateTime ReceivedUtc)> _lastTicks = new();

    private readonly ProbeOptions _options;
    private readonly ProbeLog _log;
    private readonly ConcurrentDictionary<int, int> _quoteCounts = new();
    private readonly List<string> _products = [];
    private int _tickCount;
    private DateTime _lastProductUtc = DateTime.MinValue;
    private DateTime _lastDetailUtc = DateTime.MinValue;
    private TaskCompletionSource<int> _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 建立探針。
    /// Creates the probe.
    /// </summary>
    /// <param name="options">選項。Options.</param>
    /// <param name="log">輸出。Output.</param>
    public ProbeRunner(ProbeOptions options, ProbeLog log)
    {
        _options = options;
        _log = log;
    }

    /// <summary>
    /// 在 STA 執行緒上跑完整個探針流程。
    /// Runs the whole probe on the STA thread.
    /// </summary>
    /// <param name="userId">身分證字號(大寫)。The login ID (upper case).</param>
    /// <param name="password">密碼。The password.</param>
    /// <returns>結束碼:0 成功,其他見 Program。Exit code.</returns>
    public async Task<int> RunAsync(string userId, string password)
    {
        ISKCenterLib center;
        object replyLib;
        ISKOSQuoteLib quote;

        try
        {
            center = (ISKCenterLib)Create(SkComIds.CenterLibClsid);
            replyLib = Create(SkComIds.ReplyLibClsid);
            quote = (ISKOSQuoteLib)Create(SkComIds.OsQuoteLibClsid);
        }
        catch (Exception exception) when (exception is COMException or IOException or InvalidCastException)
        {
            // 0x80040154 = 沒註冊;0x8007007E = 註冊了但登錄檔指向的 SKCOM.dll(或它相依的 DLL)不存在,常見於註冊後搬走或刪掉資料夾
            _log.Event("create_failed", new Dictionary<string, object?> { ["hresult"] = $"0x{exception.HResult:X8}", ["message"] = exception.Message });
            ProbeLog.Say(exception.HResult == unchecked((int)0x8007007E)
                ? "SKCOM 已註冊,但登錄檔指向的 SKCOM.dll 找不到(資料夾被搬走或刪掉)。請把 x64 資料夾放到固定位置後,以系統管理員身分重新執行裡面的 install.bat。"
                : $"建立 SKCOM 物件失敗(0x{exception.HResult:X8})。SKCOM 可能還沒註冊:請以系統管理員身分執行 SKCOM x64 資料夾的 install.bat。");
            return 3;
        }

        string apiVersion = Safe(() => center.SKCenterLib_GetSKAPIVersionAndBit(userId));
        _log.Event("api_version", new Dictionary<string, object?> { ["value"] = apiVersion });
        ProbeLog.Say($"SKCOM 版本:{apiVersion}");

        string skLogPath = Path.Combine(_options.OutputDirectory, "sklog");
        Directory.CreateDirectory(skLogPath);
        Report(center, "set_log_path", center.SKCenterLib_SetLogPath(skLogPath));

        ReplyEventsSink replySink = new();
        replySink.MessageReceived += message =>
        {
            _log.Event("announcement", new Dictionary<string, object?> { ["message"] = message });
        };
        using ComEventConnection replyConnection = ComEventConnection.Advise(replyLib, SkComIds.ReplyLibEventsIid, replySink);

        OsQuoteEventsSink quoteSink = new();
        HookQuoteEvents(quoteSink, quote);
        using ComEventConnection quoteConnection = ComEventConnection.Advise(quote, SkComIds.OsQuoteLibEventsIid, quoteSink);

        ProbeLog.Say("登入群益…");
        int loginCode = center.SKCenterLib_Login(userId, password);
        Report(center, "login", loginCode);

        if (loginCode != 0)
        {
            string last = Safe(center.SKCenterLib_GetLastLogInfo);
            _log.Event("login_last_log", new Dictionary<string, object?> { ["value"] = Redact(last, userId) });
            ProbeLog.Say($"登入失敗(代碼 {loginCode}):{Safe(() => center.SKCenterLib_GetReturnCodeMessage(loginCode))}。帳密類錯誤請勿連續重試,錯太多次會鎖帳號。");
            return 4;
        }

        ProbeLog.Say("登入成功,連線海期報價主機…");
        Report(center, "enter_monitor", quote.SKOSQuoteLib_EnterMonitorLONG());

        Task finished = await Task.WhenAny(_connected.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(true);

        if (finished != _connected.Task)
        {
            ProbeLog.Say("60 秒內沒收到海期報價主機的連線事件,結束。");
            return 5;
        }

        // 收到 3001 後再給 3 秒,讓 3003 之類的後續事件先到
        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(true);
        Report(center, "is_connected", quote.SKOSQuoteLib_IsConnected());
        Report(center, "request_server_time", quote.SKOSQuoteLib_RequestServerTime());

        ProbeLog.Say("下載海外商品清單…");
        Report(center, "request_products", quote.SKOSQuoteLib_RequestOverseaProducts());
        await WaitQuietAsync(() => _lastProductUtc, TimeSpan.FromSeconds(60)).ConfigureAwait(true);
        _log.Event("products_done", new Dictionary<string, object?> { ["count"] = _products.Count });
        ProbeLog.Say($"商品清單 {_products.Count} 筆。");

        Report(center, "product_detail_1", quote.SKOSQuoteLib_GetOverseaProductDetail(1));
        await WaitQuietAsync(() => _lastDetailUtc, TimeSpan.FromSeconds(30)).ConfigureAwait(true);

        List<string> symbols = ChooseSymbols();
        _log.Event("symbols", new Dictionary<string, object?> { ["values"] = symbols });
        ProbeLog.Say(symbols.Count == 0 ? "商品清單找不到外匯候選,只記錄清單。" : $"訂閱:{string.Join(" / ", symbols)}");

        if (symbols.Count > 0)
        {
            short page = -1;
            int code = quote.SKOSQuoteLib_RequestStocks(ref page, string.Join('#', symbols));
            Report(center, "request_stocks", code, ("page", page));

            foreach (string symbol in symbols)
            {
                short tickPage = -1;
                Report(center, "request_ticks", quote.SKOSQuoteLib_RequestTicks(ref tickPage, symbol), ("symbol", symbol), ("page", tickPage));
            }

            string end = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string start = DateTime.Now.AddDays(-_options.KLineDays).ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            foreach (string symbol in symbols)
            {
                Report(center, "request_kline_minute", quote.SKOSQuoteLib_RequestKLineByDate(symbol, 0, start, end, 1), ("symbol", symbol), ("start", start), ("end", end));
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
            }

            // 第一輪 sKLineType = 4(國內的「完整日線」)回 1042 SK_ERROR_OVERSEA_KLINE_DATA_TYPE_NOT_FOUND;逐一試其他代碼找日線
            string longStart = DateTime.Now.AddDays(-30).ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            foreach (short type in KLineTypeCandidates)
            {
                Report(center, "request_kline_type", quote.SKOSQuoteLib_RequestKLineByDate(symbols[0], type, longStart, end, 1), ("symbol", symbols[0]), ("type", type));
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
            }

            int status = quote.SKOSQuoteLib_GetQuoteStatus(out int connections, out bool outLimit);
            Report(center, "quote_status", status, ("connections", connections), ("out_limit", outLimit));
        }

        ProbeLog.Say($"收集即時報價 {_options.Seconds} 秒…");
        await Task.Delay(TimeSpan.FromSeconds(_options.Seconds)).ConfigureAwait(true);

        // 每檔最後一筆成交與它的接收時間:用來判斷成交時間是哪個時區
        foreach ((int index, (int date, int time, DateTime receivedUtc)) in _lastTicks)
        {
            _log.Event("last_tick", new Dictionary<string, object?>
            {
                ["index"] = index,
                ["date"] = date,
                ["time"] = time,
                ["received_utc"] = receivedUtc.ToString("O", CultureInfo.InvariantCulture),
            });
        }

        Report(center, "leave_monitor", quote.SKOSQuoteLib_LeaveMonitor());
        _log.Event("done", new Dictionary<string, object?> { ["ticks"] = _tickCount, ["products"] = _products.Count });
        ProbeLog.Say($"完成:成交 {_tickCount} 筆。輸出在 {_options.OutputDirectory}");
        return 0;
    }

    private static object Create(Guid clsid)
    {
        Type type = Type.GetTypeFromCLSID(clsid, throwOnError: true)!;
        return Activator.CreateInstance(type)!;
    }

    private static string Safe(Func<string> read)
    {
        try
        {
            return read() ?? string.Empty;
        }
        catch (COMException exception)
        {
            return $"(COMException 0x{exception.HResult:X8})";
        }
    }

    private static string Redact(string text, string userId) =>
        string.IsNullOrEmpty(userId) ? text : text.Replace(userId, "<ID>", StringComparison.OrdinalIgnoreCase);

    private static async Task WaitQuietAsync(Func<DateTime> lastEventUtc, TimeSpan max)
    {
        DateTime deadline = DateTime.UtcNow + max;
        DateTime started = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(true);
            DateTime last = lastEventUtc();
            DateTime reference = last == DateTime.MinValue ? started : last;

            if (DateTime.UtcNow - reference > TimeSpan.FromSeconds(5))
            {
                return;
            }
        }
    }

    private void Report(ISKCenterLib center, string step, int code, params (string Key, object? Value)[] extra)
    {
        Dictionary<string, object?> fields = new(StringComparer.Ordinal)
        {
            ["code"] = code,
            ["message"] = Safe(() => center.SKCenterLib_GetReturnCodeMessage(code)),
        };

        foreach ((string key, object? value) in extra)
        {
            fields[key] = value;
        }

        _log.Event(step, fields);
    }

    private void HookQuoteEvents(OsQuoteEventsSink sink, ISKOSQuoteLib quote)
    {
        sink.Connected += (code, socketCode) =>
        {
            _log.Event("on_connect", new Dictionary<string, object?> { ["code"] = code, ["socket_code"] = socketCode });

            if (code is CodeConnected or CodeReady)
            {
                _connected.TrySetResult(code);
            }
        };

        sink.Product += value =>
        {
            _lastProductUtc = DateTime.UtcNow;
            _products.Add(value);
            _log.Raw("products", value);
        };

        sink.ProductDetail += value =>
        {
            _lastDetailUtc = DateTime.UtcNow;
            _log.Raw("product-detail", value);
        };

        sink.KLine += (stockNo, data) =>
        {
            _log.Raw("kline", stockNo + "\t" + data);
        };

        sink.ServerTime += (hour, minute, second) =>
        {
            _log.Event("server_time", new Dictionary<string, object?> { ["hour"] = hour, ["minute"] = minute, ["second"] = second });
        };

        sink.Quote += index =>
        {
            int count = _quoteCounts.AddOrUpdate(index, 1, (_, previous) => previous + 1);

            if (count > MaxQuoteRecordsPerSymbol)
            {
                return;
            }

            int code = quote.SKOSQuoteLib_GetStockByIndexNineDigitLONG(index, out SKFOREIGN_9LONG item);
            _log.Event("quote", new Dictionary<string, object?>
            {
                ["index"] = index,
                ["code"] = code,
                ["decimal"] = item.sDecimal,
                ["denominator"] = item.nDenominator,
                ["market_no"] = item.bstrMarketNo,
                ["exchange_no"] = item.bstrExchangeNo,
                ["exchange_name"] = item.bstrExchangeName,
                ["stock_no"] = item.bstrStockNo,
                ["stock_name"] = item.bstrStockName,
                ["call_put"] = item.bstrCallPut,
                ["open"] = item.nOpen,
                ["high"] = item.nHigh,
                ["low"] = item.nLow,
                ["close"] = item.nClose,
                ["settle"] = item.nSettlePrice,
                ["tick_qty"] = item.nTickQty,
                ["ref"] = item.nRef,
                ["bid"] = item.nBid,
                ["bid_qty"] = item.nBc,
                ["ask"] = item.nAsk,
                ["ask_qty"] = item.nAc,
                ["total_qty"] = item.nTQty,
                ["strike"] = item.nStrikePrice,
                ["trading_day"] = item.nTradingDay,
            });
        };

        sink.Tick += (index, pointer, date, time, close, qty, history) =>
        {
            // 全部寫進 ticks.txt(不設上限:第一版只記前 3000 筆,全是訂閱當下的回補,看不到即時成交的時間)
            Interlocked.Increment(ref _tickCount);
            DateTime received = DateTime.UtcNow;
            _lastTicks[index] = (date, time, received);
            _log.Raw("ticks", string.Join(
                '\t',
                index.ToString(CultureInfo.InvariantCulture),
                pointer.ToString(CultureInfo.InvariantCulture),
                date.ToString(CultureInfo.InvariantCulture),
                time.ToString("D6", CultureInfo.InvariantCulture),
                close.ToString(CultureInfo.InvariantCulture),
                qty.ToString(CultureInfo.InvariantCulture),
                history ? "H" : "L",
                received.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)));
        };
    }

    /// <summary>
    /// 從商品清單挑要訂的商品:使用者指定的優先;否則找含外匯代碼的那幾筆,以「第一欄,含代碼的那一欄」組成請求(格式待本探針確認)。
    /// 期貨候選只記錄、不訂(近月代碼的寫法要先看清單)。
    /// </summary>
    private List<string> ChooseSymbols()
    {
        if (_options.Symbols.Count > 0)
        {
            return [.. _options.Symbols];
        }

        List<string> chosen = [];

        foreach (string line in _products)
        {
            string[] fields = line.Split(',');

            foreach (string fx in FxProbeCodes)
            {
                if (line.Contains(fx, StringComparison.OrdinalIgnoreCase))
                {
                    _log.Raw("fx-candidates", line);
                    string? code = fields.FirstOrDefault(field => field.Trim().EndsWith(fx, StringComparison.OrdinalIgnoreCase));

                    if (code is not null && chosen.Count < 4 && fields.Length > 1)
                    {
                        string request = fields[0].Trim() + "," + code.Trim();

                        if (!chosen.Contains(request, StringComparer.Ordinal))
                        {
                            chosen.Add(request);
                        }
                    }
                }
            }

            foreach (string root in FutureProbeRoots)
            {
                if (fields.Any(field => field.Trim().Equals(root, StringComparison.OrdinalIgnoreCase)))
                {
                    _log.Raw("future-candidates", line);
                }
            }
        }

        chosen.AddRange(FutureProbeSymbols);
        return chosen;
    }
}
