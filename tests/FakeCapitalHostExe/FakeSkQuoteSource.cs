using System.Collections.Concurrent;
using System.Globalization;
using Ozakboy.TradeKit.Capital.Host.Serve;

namespace FakeCapitalHostExe;

/// <summary>
/// 依實測行為模擬 SKCOM(docs/DESIGN.md §5;README「實測與未實測」):
/// <list type="bullet">
/// <item>EnterMonitorLONG 之後只送 <c>OnConnect(3001)</c>,沒有 3003(實測);事件一律非同步送出,不在呼叫裡面觸發(真 SKCOM 走訊息迴圈)。</item>
/// <item>商品清單與明細逐筆送,以 <c>##</c> 開頭那筆結尾(實測 <c>##,,,,</c> / <c>##,,,,,,</c>)。</item>
/// <item>RequestTicks 之後先送當個交易日的回補(history),再接即時;nPtr 每檔遞增(實測)。成交先於報價到(實測)。</item>
/// <item>K 線沒有結束事件(實測),格式 <c>yyyy/MM/dd HH:mm, O, H, L, C, V</c>、時間是那一分鐘的結束;另外先送一筆別的代碼的行,
/// 驗證宿主只收這次要的代碼。</item>
/// <item>時間是交易所當地時間:FX、NYM 美東,CME、CBOT 芝加哥(實測)。</item>
/// <item>國內報價(2026-11-07 實測):EnterMonitorLONG 回 0 → 3001 → 約 0.5 秒後 3003;K 線以 OnKLineComplete("##") 結束,
/// 資料規則見 <see cref="FakeDomesticMarket"/>。可設成建不起物件、EnterMonitorLONG 被拒、永遠不送 3003、斷線一次、不送結束事件。</item>
/// </list>
/// Simulates SKCOM by the measured behaviour: only 3001 after EnterMonitorLONG, asynchronous events, products ending
/// with "##", ticks replayed before live ones with increasing nPtr and arriving before quotes, K-lines without an end
/// event in the measured format, and exchange-local times.
/// </summary>
internal sealed class FakeSkQuoteSource : ISkQuoteSource
{
    private const int CodeConnected = 3001;
    private const int CodeDisconnected = 3002;
    private const int CodeRejected = 1012;
    private const int CodeDomesticReady = 3003;

    private readonly FakeSettings _settings;
    private readonly ConcurrentDictionary<int, FakeInstrument> _byIndex = new();
    private readonly ConcurrentDictionary<string, FakeInstrument> _bySymbol = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, long> _lastPrice = new();
    private readonly ConcurrentDictionary<int, int> _lastPointer = new();
    private readonly ConcurrentDictionary<int, Timer> _liveTimers = new();
    private readonly List<Timer> _timers = [];
    private readonly Lock _gate = new();
    private string _loginId = string.Empty;
    private int _enterCount;
    private int _reconnectFailuresLeft;
    private int _stocksCalls;
    private bool _disconnectedOnce;
    private volatile bool _connected;
    private int _twEnterCount;
    private bool _twDisconnectedOnce;

    public FakeSkQuoteSource(FakeSettings settings)
    {
        _settings = settings;
        _reconnectFailuresLeft = settings.ReconnectFailures;

        foreach (FakeInstrument instrument in FakeMarketData.Instruments)
        {
            _byIndex[instrument.Index] = instrument;
            _bySymbol[instrument.Symbol] = instrument;
        }
    }

    public event Action<string>? Announcement;

    public event Action<int>? Connection;

    public event Action<string>? Product;

    public event Action<string>? ProductDetail;

    public event Action<string, string>? KLine;

    public event Action<int>? Quote;

    public event Action<SkTickData>? Tick;

    public event Action<int>? DomesticConnection;

    public event Action<string, string>? DomesticKLine;

    public event Action<string>? DomesticKLineComplete;

    public SkOpenFailure? Open()
    {
        _settings.Record("open");

        if (_settings.OpenHResult is not { } hresult)
        {
            return null;
        }

        // 與 SkComQuoteSource 同一套對照。Same mapping as SkComQuoteSource.
        string reason = hresult switch
        {
            unchecked((int)0x80040154) => ServeStates.ReasonNotRegistered,
            unchecked((int)0x8007007E) => ServeStates.ReasonFileMissing,
            _ => ServeStates.ReasonCreateFailed,
        };

        return new SkOpenFailure(reason, hresult);
    }

    public int SetLogPath(string path)
    {
        _settings.Record("set_log_path");
        return 0;
    }

    public int Login(string loginId, string password)
    {
        _loginId = loginId;

        // 記下宿主是否已把帳密環境變數清掉(不記值本身)。Records whether the host cleared the variables, never the values.
        bool cleared = Environment.GetEnvironmentVariable(ServeEnvironmentNames.LoginId) is null
            && Environment.GetEnvironmentVariable(ServeEnvironmentNames.Password) is null;
        _settings.Record(string.Create(CultureInfo.InvariantCulture, $"login env_cleared={cleared}"));

        if (_settings.AnnounceId)
        {
            Announcement?.Invoke($"親愛的客戶 {loginId} 您好,這是公告。");
        }

        if (_settings.ExpectedId is { } expectedId && !string.Equals(expectedId, loginId, StringComparison.Ordinal))
        {
            return 306;
        }

        if (_settings.ExpectedPassword is { } expectedPassword && !string.Equals(expectedPassword, password, StringComparison.Ordinal))
        {
            return 300;
        }

        return _settings.LoginCode;
    }

    public string GetReturnCodeMessage(int code) => code switch
    {
        0 => "SK_SUCCESS",
        300 => "SK_ERROR_LOGIN_PASSWORD_ERROR 密碼錯誤",
        306 => "SK_ERROR_LOGIN_ID_ERROR 身分證錯誤",
        307 => "SK_ERROR_LOGIN_PASSWORD_LOCKED 密碼被鎖",
        _ => string.Create(CultureInfo.InvariantCulture, $"FAKE_CODE_{code}"),
    };

    public string GetLastLogInfo() => $"[{_loginId}] 最後一筆紀錄";

    public int EnterMonitor()
    {
        int count = Interlocked.Increment(ref _enterCount);
        _settings.Record(string.Create(CultureInfo.InvariantCulture, $"enter {count}"));

        if (_settings.NoConnect)
        {
            return 0;
        }

        if (count > 1 && Interlocked.Decrement(ref _reconnectFailuresLeft) >= 0)
        {
            // 這次重連沒有回音。This reconnect attempt gets no answer.
            return 0;
        }

        Schedule(TimeSpan.FromMilliseconds(count == 1 ? 100 : 50), () =>
        {
            _connected = true;
            Connection?.Invoke(CodeConnected);

            if (_settings.DisconnectAfter is { } after && !_disconnectedOnce)
            {
                _disconnectedOnce = true;
                Schedule(after, () =>
                {
                    _connected = false;
                    _settings.Record("disconnect");
                    Connection?.Invoke(CodeDisconnected);
                });
            }
        });

        return 0;
    }

    public int LeaveMonitor()
    {
        _settings.Record("leave");
        _connected = false;
        return 0;
    }

    public int IsConnected() => _connected ? 1 : 0;

    public int GetQuoteStatus(out int connections, out bool outLimit)
    {
        // 實測:連線數 0、未超限。Measured: 0 connections, not over the limit.
        connections = 0;
        outLimit = false;
        return 0;
    }

    public int RequestOverseaProducts()
    {
        _settings.Record("products");
        Emit(FakeMarketData.Products, row => Product?.Invoke(row));
        return 0;
    }

    public int GetOverseaProductDetail(short type)
    {
        _settings.Record(string.Create(CultureInfo.InvariantCulture, $"detail {type}"));
        Emit(FakeMarketData.ProductDetails, row => ProductDetail?.Invoke(row));
        return 0;
    }

    public int RequestStocks(string stockNos)
    {
        _settings.Record("stocks " + stockNos);

        if (_settings.RejectSymbol is { } reject && stockNos.Contains(reject, StringComparison.Ordinal))
        {
            return CodeRejected;
        }

        List<FakeInstrument> instruments = [];

        foreach (string symbol in stockNos.Split('#', StringSplitOptions.RemoveEmptyEntries))
        {
            instruments.Add(Resolve(symbol));
        }

        // 報價稍後才到(實測成交先到)。Quotes arrive a little later; ticks were measured to arrive first.
        Schedule(TimeSpan.FromMilliseconds(80), () =>
        {
            foreach (FakeInstrument instrument in instruments)
            {
                Quote?.Invoke(instrument.Index);
            }
        });

        if (Interlocked.Increment(ref _stocksCalls) == 1 && _settings.CrashAfter is { } crashAfter && ShouldCrash())
        {
            Schedule(crashAfter, () =>
            {
                _settings.Record("crash");
                Environment.Exit(1);
            });
        }

        return 0;
    }

    public int RequestTicks(string stockNo)
    {
        _settings.Record("ticks " + stockNo);
        FakeInstrument instrument = Resolve(stockNo);

        _ = Task.Run(() =>
        {
            // 訂閱當下先回補當日成交。The day's ticks are replayed first.
            foreach ((int pointer, int date, int time, long price, int quantity) in instrument.HistoryTicks.Take(_settings.HistoryTicks))
            {
                _lastPointer.AddOrUpdate(instrument.Index, pointer, (_, previous) => Math.Max(previous, pointer));
                _lastPrice[instrument.Index] = price;
                Tick?.Invoke(new SkTickData(instrument.Index, pointer, date, time, price, quantity, IsHistory: true));
            }

            StartLiveTicks(instrument);
        });

        return 0;
    }

    public int RequestKLineByDate(string stockNo, short type, string startDate, string endDate, short minuteNumber)
    {
        _settings.Record(string.Create(CultureInfo.InvariantCulture, $"kline {stockNo} {type} {startDate} {endDate}"));
        string code = stockNo[(stockNo.IndexOf(',', StringComparison.Ordinal) + 1)..];

        string[] rows = type switch
        {
            0 => FakeMarketData.MinuteBars.GetValueOrDefault(code, []),
            1 => FakeMarketData.DailyBars.GetValueOrDefault(code, []),
            _ => [],
        };

        _ = Task.Run(async () =>
        {
            await Task.Delay(30).ConfigureAwait(false);

            // 別的代碼的一行(前一個請求的尾巴):宿主必須丟掉。A stray row for another code that the host must drop.
            string strayCode = code == "SEURUSD" ? "SUSDJPY" : "SEURUSD";
            KLine?.Invoke(strayCode, "2026/11/02 16:01, 1.100000, 1.100000, 1.100000, 1.100000, 1");

            foreach (string row in rows)
            {
                KLine?.Invoke(code, row);
                await Task.Delay(5).ConfigureAwait(false);
            }
        });

        return 0;
    }

    public int GetStockByIndex(int index, out SkQuoteData quote)
    {
        if (!_byIndex.TryGetValue(index, out FakeInstrument? instrument))
        {
            quote = default;
            return 1;
        }

        long close = _lastPrice.TryGetValue(index, out long last) ? last : instrument.Quote.Close;

        quote = instrument.Quote with
        {
            ExchangeNo = instrument.Exchange,
            StockNo = instrument.Code,
            StockName = instrument.Name,
            Decimals = instrument.Decimals,
            Denominator = 1,
            TradingDay = 20261106,
            Close = close,
        };

        return 0;
    }

    // ── 國內報價:依 2026-11-07 probe-tw 實測(見 FakeDomesticMarket)。Domestic quotes, by the 2026-11-07 measurements. ──

    public SkOpenFailure? OpenDomestic()
    {
        _settings.Record("tw_open");

        return _settings.TwOpenHResult is { } hresult
            ? new SkOpenFailure(hresult == unchecked((int)0x80040154) ? ServeStates.ReasonNotRegistered : ServeStates.ReasonCreateFailed, hresult)
            : null;
    }

    public int EnterDomesticMonitor()
    {
        int count = Interlocked.Increment(ref _twEnterCount);
        _settings.Record(string.Create(CultureInfo.InvariantCulture, $"tw_enter {count}"));

        if (_settings.TwEnterCode != 0)
        {
            return _settings.TwEnterCode;
        }

        // 實測:回 0 → OnConnection(3001) → 約 0.5 秒後 3003(商品基本資料完成)。Measured: 3001, then 3003 about 0.5 s later.
        Schedule(TimeSpan.FromMilliseconds(50), () => DomesticConnection?.Invoke(CodeConnected));

        if (_settings.TwNoReady)
        {
            return 0;
        }

        Schedule(TimeSpan.FromMilliseconds(50) + _settings.TwReadyDelay, () =>
        {
            DomesticConnection?.Invoke(CodeDomesticReady);

            if (_settings.TwDisconnectAfter is { } after && !_twDisconnectedOnce)
            {
                _twDisconnectedOnce = true;
                Schedule(after, () =>
                {
                    _settings.Record("tw_disconnect");
                    DomesticConnection?.Invoke(CodeDisconnected);
                });
            }
        });

        return 0;
    }

    public int LeaveDomesticMonitor()
    {
        _settings.Record("tw_leave");
        return 0;
    }

    public int RequestDomesticKLine(string stockNo, short kLineType, short outType, short tradeSession, string startDate, string endDate, short minuteNumber)
    {
        _settings.Record(string.Create(CultureInfo.InvariantCulture, $"tw_kline {stockNo} {kLineType} {outType} {tradeSession} {startDate} {endDate} {minuteNumber}"));

        // 代碼不存在或沒權限立即回非 0,不送任何事件(實測)。An unknown code answers non-zero at once with no events.
        int code = FakeDomesticMarket.Check(stockNo);

        if (code != 0)
        {
            return code;
        }

        List<string> rows = FakeDomesticMarket.Rows(stockNo, kLineType, tradeSession, startDate, endDate, _settings.TwKLineLimit);

        _ = Task.Run(async () =>
        {
            await Task.Delay(20).ConfigureAwait(false);

            // 別的代碼的一行(OnNotifyKLineData 帶代碼,宿主必須只收這次要的)。A stray row for another code that the host drops.
            DomesticKLine?.Invoke(stockNo == "MTX00" ? "TX00" : "MTX00", "2026/11/06 13:45, 1.00, 1.00, 1.00, 1.00, 1");

            foreach (string row in rows)
            {
                DomesticKLine?.Invoke(stockNo, row);
            }

            // 實測:每個請求結束一定有一則 OnKLineComplete("##"),0 筆時也有。Measured: one completion per request, even with no rows.
            if (!_settings.TwNoComplete)
            {
                DomesticKLineComplete?.Invoke("##");
            }
        });

        return 0;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (Timer timer in _timers)
            {
                timer.Dispose();
            }

            _timers.Clear();
        }

        foreach (Timer timer in _liveTimers.Values)
        {
            timer.Dispose();
        }
    }

    private bool ShouldCrash()
    {
        if (_settings.CrashOnceFile is not { } marker)
        {
            return true;
        }

        try
        {
            using FileStream stream = new(marker, FileMode.CreateNew);
            return true;
        }
        catch (IOException)
        {
            // 已經崩潰過一次。Already crashed once.
            return false;
        }
    }

    private FakeInstrument Resolve(string symbol)
    {
        return _bySymbol.GetOrAdd(symbol, key =>
        {
            int comma = key.IndexOf(',', StringComparison.Ordinal);
            int index = 900 + _bySymbol.Count;
            FakeInstrument created = new(
                index,
                key[..comma],
                key[(comma + 1)..],
                key[(comma + 1)..],
                2,
                new SkQuoteData { Open = 10000, High = 10100, Low = 9900, Close = 10050, Settle = 10000, Reference = 10000, Bid = 10049, Ask = 10051, TotalQuantity = 1, TickQuantity = 1 },
                [(1, 20261105, 160004, 10000, 1)]);
            _byIndex[index] = created;
            return created;
        });
    }

    private void StartLiveTicks(FakeInstrument instrument)
    {
        if (_settings.LiveTickInterval <= TimeSpan.Zero)
        {
            return;
        }

        _ = _liveTimers.GetOrAdd(instrument.Index, _ => new Timer(
            _ =>
            {
                if (!_connected)
                {
                    return;
                }

                int pointer = _lastPointer.AddOrUpdate(instrument.Index, 1, (_, previous) => previous + 1);
                long price = _lastPrice.AddOrUpdate(instrument.Index, instrument.Quote.Close, (_, previous) => previous + (pointer % 2 == 0 ? 1 : -1));
                DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ZoneOf(instrument.Exchange));
                int date = int.Parse(local.ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                int time = int.Parse(local.ToString("HHmmss", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

                Tick?.Invoke(new SkTickData(instrument.Index, pointer, date, time, price, 1, IsHistory: false));
                Quote?.Invoke(instrument.Index);
            },
            null,
            _settings.LiveTickInterval,
            _settings.LiveTickInterval));
    }

    private static TimeZoneInfo ZoneOf(string exchange) =>
        TimeZoneInfo.FindSystemTimeZoneById(exchange is "CME" or "CBOT" ? "Central Standard Time" : "Eastern Standard Time");

    private static void Emit(string[] rows, Action<string> raise)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(20).ConfigureAwait(false);

            foreach (string row in rows)
            {
                raise(row);
            }
        });
    }

    private void Schedule(TimeSpan due, Action action)
    {
        lock (_gate)
        {
            _timers.Add(new Timer(_ => action(), null, due, Timeout.InfiniteTimeSpan));
        }
    }
}
