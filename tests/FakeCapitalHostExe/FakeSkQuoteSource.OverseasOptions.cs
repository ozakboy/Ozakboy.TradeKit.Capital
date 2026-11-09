using System.Globalization;
using Ozakboy.TradeKit.Capital.Host.Serve;

namespace FakeCapitalHostExe;

// 0.3.0:海外選擇權(SKOOQuoteLib)。手冊沒有章節;實測 2026-11-09 的部分照實測:EnterMonitorLONG 後只送 3001(實測約 18 秒才到,
// 這裡縮短)、商品清單 5 欄「交易所,交易所名稱,代碼,名稱,最後交易日」以「##,##,##,##,##」結尾、代碼 = 根代碼 + 履約價 + 月份字母 + 年尾數
// (NQ25000L6 = 2026 年 12 月 25000 買權)、訂閱字串「交易所,代碼」以 # 串接、市場別字樣 7、小數 2 位、履約價原值(不放大)。
// 成交的時區沒有實測到(當時沒有成交),推定是交易所當地時間(與海期同一套)。價格是假的。
// 0.3.0: overseas options. Measured on 2026-11-09: only 3001, five-field product rows ending with "##,##,##,##,##",
// codes like NQ25000L6, '#'-joined subscriptions and an unscaled strike. Tick time zones were not measured. Prices are
// made up.
internal sealed partial class FakeSkQuoteSource
{
    private const int OptionIndex = 500;
    private const string OptionSymbol = "CME,NQ25000L6";

    private int _ooEnterCount;
    private bool _ooDisconnectedOnce;

    public event Action<int>? OverseasOptionConnection;

    public event Action<string>? OverseasOptionProduct;

    public event Action<int>? OverseasOptionQuote;

    public event Action<SkTickData>? OverseasOptionTick;

    public event Action<SkBook>? OverseasOptionBest5;

    public event Action<SkBook>? OverseasOptionBest10;

    public SkOpenFailure? OpenOverseasOptions()
    {
        _settings.Record("oo_open");
        return _settings.OoOpenHResult is { } hresult ? new SkOpenFailure(ServeStates.ReasonCreateFailed, hresult) : null;
    }

    public int EnterOverseasOptionMonitor()
    {
        int count = Interlocked.Increment(ref _ooEnterCount);
        _settings.Record(string.Create(CultureInfo.InvariantCulture, $"oo_enter {count}"));

        if (_settings.OoEnterCode != 0)
        {
            return _settings.OoEnterCode;
        }

        if (_settings.OoNoReady)
        {
            return 0;
        }

        Schedule(TimeSpan.FromMilliseconds(80), () =>
        {
            OverseasOptionConnection?.Invoke(CodeConnected);

            if (_settings.OoDisconnectAfter is { } after && !_ooDisconnectedOnce)
            {
                _ooDisconnectedOnce = true;
                Schedule(after, () =>
                {
                    _settings.Record("oo_disconnect");
                    OverseasOptionConnection?.Invoke(CodeDisconnected);
                });
            }
        });

        return 0;
    }

    public int LeaveOverseasOptionMonitor()
    {
        _settings.Record("oo_leave");
        return 0;
    }

    public int RequestOverseasOptionProducts()
    {
        _settings.Record("oo_products");
        Emit(
            [
                "CME,芝加哥商品交易所,NQ25000L6,小那季選2612C 25000 C,20261218",
                "CME,芝加哥商品交易所,NQ25000X6,小那季選2612P 25000 P,20261218",
                "##,##,##,##,##",
            ],
            row => OverseasOptionProduct?.Invoke(row));
        return 0;
    }

    public int RequestOverseasOptionStocks(string stockNos)
    {
        _settings.Record("oo_stocks " + stockNos);

        if (stockNos.Contains(OptionSymbol, StringComparison.Ordinal))
        {
            Schedule(TimeSpan.FromMilliseconds(60), () => OverseasOptionQuote?.Invoke(OptionIndex));
        }

        return 0;
    }

    public int RequestOverseasOptionTicks(string stockNo)
    {
        _settings.Record("oo_ticks " + stockNo);

        if (stockNo == OptionSymbol)
        {
            Schedule(TimeSpan.FromMilliseconds(30), () =>
            {
                // CME 是芝加哥時間:20261105 214356 → 2026-11-06T03:43:56Z(與海期同一套時區)。Chicago time, like CME futures.
                OverseasOptionTick?.Invoke(new SkTickData(OptionIndex, 0, 20261105, 214356, 125050, 2, IsHistory: true));
                OverseasOptionTick?.Invoke(new SkTickData(OptionIndex, 1, 20261105, 214400, 125100, 1, IsHistory: false));
                OverseasOptionBest5?.Invoke(FakeBooks.Book(OptionIndex, 125100, 5));
            });
        }

        return 0;
    }

    public int RequestOverseasOptionMarketDepth(string stockNo)
    {
        _settings.Record("oo_depth " + stockNo);

        if (stockNo == OptionSymbol)
        {
            Schedule(TimeSpan.FromMilliseconds(40), () => OverseasOptionBest10?.Invoke(FakeBooks.Book(OptionIndex, 125100, 10)));
        }

        return 0;
    }

    public int GetOverseasOptionStockByIndex(int index, out SkOptionQuote quote)
    {
        if (index != OptionIndex)
        {
            quote = default;
            return 1;
        }

        quote = new SkOptionQuote
        {
            MarketNo = "7",
            ExchangeNo = "CME",
            ExchangeName = "芝加哥商品交易所",
            StockNo = "NQ25000L6",
            StockName = "小那季選2612C",
            CallPut = "C",
            Decimals = 2,
            Denominator = 1,
            Open = 124000,
            High = 126000,
            Low = 123500,
            Close = 125100,
            Settle = 124500,
            TickQuantity = 1,
            Reference = 124500,
            Bid = 125000,
            BidQuantity = 3,
            Ask = 125200,
            AskQuantity = 4,
            TotalQuantity = 321,
            StrikePrice = 25000,
            TradingDay = 20261106,
        };

        return 0;
    }
}
