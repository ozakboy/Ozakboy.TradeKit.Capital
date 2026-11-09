using System.Globalization;
using System.Runtime.InteropServices;
using Ozakboy.TradeKit.Capital.Host.Interop;

namespace Ozakboy.TradeKit.Capital.Host.Probe;

/// <summary>B. 期貨帳務查詢(唯讀)。B. Futures account queries, read-only.</summary>
internal sealed partial class FullProbeRunner
{
    private static readonly TimeSpan QueryQuiet = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan QueryQuietAfterStatus = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan QueryLimit = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan QueryGap = TimeSpan.FromSeconds(1);

    // SK_ERROR_QUERY_IN_PROCESSING(手冊錯誤代碼表:查詢尚未結束請稍後再試)與它之後的重試間隔
    private const int QueryInProcessing = 1019;
    private static readonly TimeSpan[] QueryBusyRetries = [TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)];

    // 手冊 4-2-97 / 4-2-98:委託、成交回報查詢「限制每次查詢間需間隔五秒」,多留半秒
    private static readonly TimeSpan ReportGap = TimeSpan.FromSeconds(5.5);

    // 手冊 4-2-41 sCoinType:0 全幣別(含基幣)、1 基幣(台幣)、2 人民幣
    private static readonly short[] CoinTypes = [0, 1, 2];

    // 手冊 4-2-89 GetOpenInterestGW 的 nFormat 只列 1;2、3 是試探(舊的 GetOpenInterestWithFormat 有 完整 / 1 / 2 三種)
    private static readonly int[] OpenInterestFormats = [1, 2, 3];

    // 手冊 4-2-76:1 彙總、2 明細
    private static readonly int[] OverseasOpenInterestFormats = [1, 2];

    // 手冊 4-2-97:1 全部、2 有效、3 可消、4 已消、5 已成、6 失敗、7 合併同價格、8 合併同商品、9 預約
    private static readonly int[] OrderReportFormats = [1, 2, 3, 4, 5, 6, 7, 8, 9];

    // 手冊 4-2-98:1 完整、2 合併同書號、3 合併同價格、4 合併同商品、5 T+1 成交回報
    private static readonly int[] FulfillReportFormats = [1, 2, 3, 4, 5];

    private readonly List<ProbeAccount> _accounts = [];
    private string _orderStep = "(none)";
    private DateTime _lastOrderEventUtc = DateTime.MinValue;
    private int _stepEvents;
    private bool _stepStatus;
    private DateTime _lastReportCallUtc = DateTime.MinValue;

    private async Task RunAccountsAsync(ISKOrderLib order, string userId)
    {
        ProbeLog.Say("初始化 SKOrderLib(只用來查帳務,這個探針沒有任何下單能力)…");
        int init = order.SKOrderLib_Initialize();
        Report("order_initialize", init);

        if (init != 0)
        {
            ProbeLog.Say($"SKOrderLib 初始化失敗(代碼 {init}):{Safe(() => Center.SKCenterLib_GetReturnCodeMessage(init))}。帳務查詢跳過。");
            return;
        }

        ProbeLog.Say("取得帳號清單…");
        await QueryAsync("get_user_account", order.GetUserAccount, expectStatus: false).ConfigureAwait(true);
        Report("read_cert", order.ReadCertByID(userId));

        List<ProbeAccount> domestic = [.. Distinct(ProbeAccountKind.DomesticFutures)];
        List<ProbeAccount> overseas = [.. Distinct(ProbeAccountKind.OverseasFutures)];

        _log.Event("accounts", new Dictionary<string, object?>
        {
            ["count"] = _accounts.Count,
            ["items"] = _accounts.Select(account => new Dictionary<string, object?>
            {
                ["market"] = account.Market,
                ["broker_id"] = account.BrokerId,
                ["broker_name"] = account.BrokerName,
                ["account"] = _redactor.AddAccount(account.AccountNo),
                ["kind"] = account.Kind.ToString(),
                ["broker_field_swapped"] = account.BrokerFieldSwapped,
            }).ToArray(),
            ["domestic"] = domestic.Count,
            ["overseas"] = overseas.Count,
        });

        if (domestic.Count == 0 && overseas.Count == 0 && _accounts.Count > 0)
        {
            // 市場別字樣跟推定的不一樣:每個帳號兩套查詢都跑(都是唯讀查詢),看回什麼
            ProbeLog.Say("帳號的市場別不是推定的 TF / TO / OF / OO,每個帳號國內、海外查詢都試。");
            domestic = [.. _accounts.DistinctBy(account => account.FullAccount, StringComparer.Ordinal)];
            overseas = domestic;
        }

        ProbeLog.Say($"帳號 {_accounts.Count} 筆:國內期貨 {domestic.Count}、海外期貨 {overseas.Count}。");

        foreach (ProbeAccount account in domestic)
        {
            await QueryDomesticAsync(order, userId, account).ConfigureAwait(true);
        }

        foreach (ProbeAccount account in overseas)
        {
            await QueryOverseasAsync(order, userId, account).ConfigureAwait(true);
        }
    }

    private IEnumerable<ProbeAccount> Distinct(ProbeAccountKind kind) =>
        _accounts.Where(account => account.Kind == kind).DistinctBy(account => account.FullAccount, StringComparer.Ordinal);

    private async Task QueryDomesticAsync(ISKOrderLib order, string userId, ProbeAccount account)
    {
        string label = Label(account);
        string full = account.FullAccount;
        ProbeLog.Say($"國內期貨帳號 {label}({account.Market}):權益數、未平倉、委託 / 成交回報…");

        foreach (short coin in CoinTypes)
        {
            await QueryAsync($"tf_rights_coin{coin}_{label}", () => order.GetFutureRights(userId, full, coin), expectStatus: true).ConfigureAwait(true);
        }

        foreach (int format in OpenInterestFormats)
        {
            await QueryAsync($"tf_open_interest_gw_f{format}_{label}", () => order.GetOpenInterestGW(userId, full, format), expectStatus: true).ConfigureAwait(true);
        }

        foreach (int format in OrderReportFormats)
        {
            await SyncReportAsync("GetOrderReport", $"tf_order_report_f{format}_{label}", () => order.GetOrderReport(userId, full, format)).ConfigureAwait(true);
        }

        foreach (int format in FulfillReportFormats)
        {
            await SyncReportAsync("GetFulfillReport", $"tf_fulfill_report_f{format}_{label}", () => order.GetFulfillReport(userId, full, format)).ConfigureAwait(true);
        }
    }

    private async Task QueryOverseasAsync(ISKOrderLib order, string userId, ProbeAccount account)
    {
        string label = Label(account);
        string full = account.FullAccount;
        ProbeLog.Say($"海外期貨帳號 {label}({account.Market}):權益數、未平倉…");

        await QueryAsync($"of_rights_{label}", () => order.GetRequestOverSeaFutureRight(userId, full), expectStatus: true).ConfigureAwait(true);

        foreach (int format in OverseasOpenInterestFormats)
        {
            await QueryAsync($"of_open_interest_gw_f{format}_{label}", () => order.GetOverseaFutureOpenInterestGW(userId, full, format), expectStatus: true).ConfigureAwait(true);
        }

        await QueryAsync($"of_open_interest_details_{label}", () => order.GetOFOpenInterestWithDetails(userId, full), expectStatus: false).ConfigureAwait(true);

        // 手冊說委託 / 成交回報查詢只有國內證期選;海外帳號各試一種格式,確認回什麼
        await SyncReportAsync("GetOrderReport", $"of_order_report_f1_{label}", () => order.GetOrderReport(userId, full, 1)).ConfigureAwait(true);
        await SyncReportAsync("GetFulfillReport", $"of_fulfill_report_f1_{label}", () => order.GetFulfillReport(userId, full, 1)).ConfigureAwait(true);
    }

    private string Label(ProbeAccount account) =>
        (_redactor.AddAccount(account.AccountNo) ?? "<ACCT?>").Trim('<', '>');

    /// <summary>
    /// 送一個以事件回傳的查詢:回 0 就等到「收過狀態事件後安靜 2 秒」或「安靜 6 秒」,最多 60 秒。之後到的事件記在下一步的名稱下
    /// (jsonl 有每則事件的接收時間,可自行對照)。
    /// Sends an event-based query and waits for its replies to go quiet.
    /// </summary>
    private async Task QueryAsync(string step, Func<int> call, bool expectStatus)
    {
        _orderStep = step;
        _stepEvents = 0;
        _stepStatus = false;
        DateTime started = DateTime.UtcNow;
        int code;
        int attempt = 0;

        while (true)
        {
            try
            {
                code = call();
            }
            catch (COMException exception)
            {
                _log.Event(step, new Dictionary<string, object?> { ["hresult"] = $"0x{exception.HResult:X8}", ["message"] = exception.Message });
                return;
            }

            Report(step, code, ("attempt", attempt), ("since_start_ms", (long)(DateTime.UtcNow - started).TotalMilliseconds));

            // 2026-11-09 實測:同一個查詢函式在前一次結束後 1–2 秒再送,回 1019 SK_ERROR_QUERY_IN_PROCESSING(手冊:查詢尚未結束請稍後再試),
            // 權益數 sCoinType 1 / 2 與未平倉格式 2 / 3 因此沒測到。唯讀查詢重送無害:以遞增間隔再試,順便量出需要的間隔。
            // Measured: the same query function sent 1-2 s after the previous one ended returns 1019; read-only, so retried
            // with growing waits, which also measures the interval needed.
            if (code != QueryInProcessing || attempt >= QueryBusyRetries.Length)
            {
                break;
            }

            await Task.Delay(QueryBusyRetries[attempt]).ConfigureAwait(true);
            attempt++;
        }

        if (code == 0)
        {
            await WaitQuietAsync(
                () => _lastOrderEventUtc,
                () => _stepStatus && DateTime.UtcNow - _lastOrderEventUtc > QueryQuietAfterStatus,
                QueryQuiet,
                QueryLimit).ConfigureAwait(true);
        }

        _log.Event(step + "_done", new Dictionary<string, object?>
        {
            ["events"] = _stepEvents,
            ["status_event"] = _stepStatus,
            ["status_expected"] = expectStatus,
            ["elapsed_ms"] = (long)(DateTime.UtcNow - started).TotalMilliseconds,
        });
        _orderStep = "(between)";
        await Task.Delay(QueryGap).ConfigureAwait(true);
    }

    /// <summary>委託 / 成交回報查詢:阻塞式、直接回字串,每次間隔 5 秒以上(手冊 4-2-97、4-2-98)。</summary>
    private async Task SyncReportAsync(string function, string step, Func<string> call)
    {
        TimeSpan wait = ReportGap - (DateTime.UtcNow - _lastReportCallUtc);

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait).ConfigureAwait(true);
        }

        DateTime started = DateTime.UtcNow;
        string result;

        try
        {
            result = call() ?? string.Empty;
        }
        catch (COMException exception)
        {
            result = $"(COMException 0x{exception.HResult:X8})";
        }

        _lastReportCallUtc = DateTime.UtcNow;
        string[] rows = result.Split(["\r\n", "\n"], StringSplitOptions.None);
        string file = "account-" + function;
        _log.Raw(file, "### " + step);

        foreach (string row in rows)
        {
            _log.Raw(file, row);
        }

        _log.Event(step, new Dictionary<string, object?>
        {
            ["length"] = result.Length,
            ["rows"] = rows.Length,
            ["first"] = rows.Take(3).ToArray(),
            ["elapsed_ms"] = (long)(DateTime.UtcNow - started).TotalMilliseconds,
        });
    }

    private void HookOrderEvents(OrderEventsSink sink)
    {
        sink.Account += (loginId, data) =>
        {
            MarkOrderEvent();

            // 先解析、登記遮罩,再寫出:帳號號碼、身分證字號、戶名第一次出現就在這裡
            ProbeAccount? account = ProbeAccount.Parse(data);

            if (account is not null)
            {
                _redactor.AddAccount(account.AccountNo);
                _redactor.AddLoginId(account.OwnerId);
                _redactor.AddName(account.OwnerName);
                _accounts.Add(account);
            }

            // 解析不了的只寫形狀(帳號沒登記到遮罩);登入帳號欄若不是登記過的 ID(會換成 <ID>),也只寫形狀
            string safe = ProbeAccount.ToSafeRaw(data);
            string safeLogin = _redactor.Redact(loginId).Contains("<ID>", StringComparison.Ordinal) ? loginId : ProbeRedactor.Shape(loginId);
            _log.Raw("account-OnAccount", _orderStep + "\t" + safeLogin + "\t" + safe);
            _log.Event("on_account", new Dictionary<string, object?> { ["step"] = _orderStep, ["login_id"] = safeLogin, ["data"] = safe, ["parsed"] = account is not null });
        };

        sink.Data += (name, data) =>
        {
            MarkOrderEvent();
            _log.Raw("account-" + name, _orderStep + "\t" + data);
            _log.Event("order_event", new Dictionary<string, object?> { ["step"] = _orderStep, ["event"] = name, ["data"] = data });
        };

        sink.Status += (name, status, message) =>
        {
            MarkOrderEvent();
            _stepStatus = true;
            _log.Raw("account-" + name, _orderStep + "\t" + status.ToString(CultureInfo.InvariantCulture) + "\t" + message);
            _log.Event("order_status", new Dictionary<string, object?> { ["step"] = _orderStep, ["event"] = name, ["status"] = status, ["message"] = message });
        };
    }

    private void MarkOrderEvent()
    {
        _lastOrderEventUtc = DateTime.UtcNow;
        _stepEvents++;
    }
}
