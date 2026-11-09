using System.Globalization;
using System.Text.Json;

namespace Ozakboy.TradeKit.Capital.Host.Serve;

// 0.3.0:期貨帳務查詢(唯讀)。登入成功後不動它,第一次帳務請求才依序 SKOrderLib_Initialize → GetUserAccount(等 OnAccount 安靜)→
// ReadCertByID(登入帳號);每一步成功一次就不再做,失敗下次請求再試(不是登入,重試無害)。查詢的等待沿用 probe-full:
// 收過狀態事件後安靜 2 秒、或收到「##」、或安靜 6 秒就結束,最多 60 秒。委託 / 成交回報是阻塞式、直接回字串,兩次之間強制間隔 5.5 秒。
// 原文含帳號、身分證字號與戶名:只放進 stdout 的回應,絕不寫 stderr;OnAccount 解析出的帳號、身分證字號、戶名一拿到就登記到遮罩。
// 0.3.0: read-only futures account queries. Nothing happens at login; the first account request runs Initialize,
// GetUserAccount (until OnAccount goes quiet) and ReadCertByID in order, each step once, retried on the next request
// after a failure. Queries end the probe-full way. The report queries block and are spaced at least 5.5 seconds apart.
// The raw replies carry account numbers, the holder's ID and name: they only go into stdout responses, never stderr,
// and every account number, ID and name parsed from OnAccount is registered for redaction at once.
internal sealed partial class ServeHost
{
    private const int MaxAccountLength = 32;

    private readonly List<string> _accountRows = [];
    private SkOpenFailure? _accountsOpenFailure;
    private bool _accountsInitialized;
    private bool _accountsListed;
    private bool _certificateRead;
    private AccountCollector? _accountCollector;
    private long _lastReportTimestamp;
    private bool _reportCalled;

    private void HookAccountEvents()
    {
        _source.AccountRow += OnAccountRow;
        _source.AccountReply += OnAccountReply;
        _source.AccountStatus += OnAccountStatus;
    }

    private void OpenAccounts()
    {
        try
        {
            _accountsOpenFailure = _source.OpenAccounts();
        }
#pragma warning disable CA1031 // 帳務是選用的:建不起來只讓帳務請求回錯誤。Optional: a failure only fails the account requests.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _accountsOpenFailure = new SkOpenFailure(ServeStates.ReasonCreateFailed, exception.HResult);
        }

        if (_accountsOpenFailure is { } failure)
        {
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"建立 SKOrderLib 失敗:{failure.Reason}(0x{failure.HResult:X8}),帳務查詢不可用,其他照常。"));
        }
    }

    private async Task HandleAccountRequestAsync(long id, string method, JsonElement parameters)
    {
        if (!TryReadAccountParameters(method, parameters, out string account, out int option, out string problem))
        {
            _out.WriteError(id, WireErrorCodes.RequestInvalid, problem);
            return;
        }

        if (_accountsOpenFailure is { } failure)
        {
            _out.WriteError(
                id,
                WireErrorCodes.AccountUnavailable,
                "SKOrderLib 建不起來,帳務查詢不可用。SKOrderLib could not be created, so account queries are unavailable.",
                writer =>
                {
                    writer.WriteString("reason", failure.Reason);
                    writer.WriteString("hresult", string.Create(CultureInfo.InvariantCulture, $"0x{failure.HResult:X8}"));
                });
            return;
        }

        if (!await EnsureAccountsReadyAsync(id).ConfigureAwait(true))
        {
            return;
        }

        switch (method)
        {
            case "accounts":
                _out.WriteResult(id, writer => WriteStringArray(writer, "items", _accountRows));
                break;
            case "account-rights":
                await QueryAccountAsync(id, "OnFutureRights", "OnFutureRightsStatus", () => _source.RequestFutureRights(_loginId, account, (short)option)).ConfigureAwait(true);
                break;
            case "account-open-interest":
                await QueryAccountAsync(id, "OnOpenInterest", "OnOpenInterestGWStatus", () => _source.RequestOpenInterest(_loginId, account, option)).ConfigureAwait(true);
                break;
            case "account-os-rights":
                await QueryAccountAsync(id, "OnOverSeaFutureRight", "OnOverseaFutureRightsStatus", () => _source.RequestOverseaFutureRights(_loginId, account)).ConfigureAwait(true);
                break;
            case "account-os-open-interest":
                await QueryAccountAsync(id, "OnOFOpenInterestGWReport", "OnOverseaFutureOpenInterestGWStatus", () => _source.RequestOverseaOpenInterest(_loginId, account, option)).ConfigureAwait(true);
                break;
            case "account-orders":
                await QueryReportAsync(id, () => _source.GetOrderReport(_loginId, account, option)).ConfigureAwait(true);
                break;
            default:
                await QueryReportAsync(id, () => _source.GetFulfillReport(_loginId, account, option)).ConfigureAwait(true);
                break;
        }
    }

    /// <summary>
    /// 第一次帳務請求才初始化(手冊 4-2-1 → 4-2-2 → 4-2-3);每一步成功一次就不再做。失敗時已寫好錯誤回應並回 <see langword="false"/>。
    /// Initialises on the first account request; each step runs once on success. On failure the error is already
    /// written and false is returned.
    /// </summary>
    private async Task<bool> EnsureAccountsReadyAsync(long id)
    {
        if (!_accountsInitialized)
        {
            _log.Write("第一次帳務請求:初始化 SKOrderLib(只用來查帳務)。");
            int code = _source.InitializeAccounts();

            if (code != 0)
            {
                WriteAccountStepError(id, WireErrorCodes.AccountInitFailed, "initialize", code, "SKOrderLib 初始化失敗", "SKOrderLib could not be initialised");
                return false;
            }

            _accountsInitialized = true;
        }

        if (!_accountsListed)
        {
            AccountCollector collector = new(null, null);

            lock (_eventGate)
            {
                _accountCollector = collector;
            }

            int code = _source.RequestAccounts();

            if (code != 0)
            {
                ClearAccountCollector();
                WriteAccountStepError(id, WireErrorCodes.AccountInitFailed, "get_user_account", code, "取帳號清單失敗", "The account list request failed");
                return false;
            }

            _ = await WaitAccountAsync(collector).ConfigureAwait(true);
            ClearAccountCollector();

            lock (_eventGate)
            {
                _accountRows.Clear();
                _accountRows.AddRange(collector.Rows);
            }

            _accountsListed = true;
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"帳號清單 {_accountRows.Count} 筆。"));
        }

        if (!_certificateRead)
        {
            int code = _source.ReadCertificate(_loginId);

            if (code != 0)
            {
                WriteAccountStepError(id, WireErrorCodes.AccountCertFailed, "read_cert", code, "讀取憑證失敗", "Reading the certificate failed");
                return false;
            }

            _certificateRead = true;
        }

        return true;
    }

    /// <summary>
    /// 送一個以事件回傳的查詢,等它結束(<see cref="WaitAccountAsync"/>);狀態事件回 1 → <c>capital.account.query_failed</c>。
    /// Sends an event-based query and waits for it to end; a status of 1 is capital.account.query_failed.
    /// </summary>
    private async Task QueryAccountAsync(long id, string dataEvent, string statusEvent, Func<int> call)
    {
        AccountCollector collector = new(dataEvent, statusEvent);

        lock (_eventGate)
        {
            _accountCollector = collector;
        }

        int code = call();

        if (code != 0)
        {
            ClearAccountCollector();
            string message = _log.Redact(SafeMessage(code));
            _out.WriteError(
                id,
                WireErrorCodes.AccountQueryRejected,
                string.Create(CultureInfo.InvariantCulture, $"帳務查詢被拒(代碼 {code}):{message}。The account query was rejected (code {code})."),
                writer =>
                {
                    writer.WriteNumber("skCode", code);
                    writer.WriteString("skMessage", message);
                });
            return;
        }

        bool completed = await WaitAccountAsync(collector).ConfigureAwait(true);
        ClearAccountCollector();

        string[] rows;
        int? status;
        string statusMessage;

        lock (_eventGate)
        {
            rows = [.. collector.Rows];
            status = collector.Status;
            statusMessage = _log.Redact(collector.StatusMessage);
        }

        if (!completed)
        {
            _out.WriteError(
                id,
                WireErrorCodes.AccountQueryTimeout,
                string.Create(CultureInfo.InvariantCulture, $"帳務查詢在 {_timings.AccountQueryLimit.TotalSeconds:F0} 秒內沒有結束。The account query did not end within the time limit."),
                writer => writer.WriteNumber("received", rows.Length));
            return;
        }

        if (status is { } value && value != 0)
        {
            // 手冊 4-2-t–w:0 成功、1 失敗,失敗時 bstrErrorMsg 是原因(已遮罩)。0 success, 1 failure with a reason.
            _out.WriteError(
                id,
                WireErrorCodes.AccountQueryFailed,
                $"帳務查詢失敗:{statusMessage}。The account query failed: {statusMessage}.",
                writer =>
                {
                    writer.WriteNumber("skCode", value);
                    writer.WriteString("skMessage", statusMessage);
                });
            return;
        }

        _out.WriteResult(id, writer =>
        {
            WriteStringArray(writer, "rows", rows);

            if (status is { } success)
            {
                writer.WriteNumber("status", success);
                writer.WriteString("statusMessage", statusMessage);
            }
            else
            {
                writer.WriteNull("status");
            }
        });
    }

    /// <summary>
    /// 委託 / 成交回報查詢:阻塞式、直接回字串(手冊 4-2-97、4-2-98),兩次之間至少 <see cref="ServeTimings.ReportInterval"/>;
    /// 以 <c>\r\n</c> 分行,「M003 查無資料」「M999 查詢錯誤」照原文交給用戶端判斷。
    /// The blocking report queries, spaced by ReportInterval; rows are split on CRLF and the M003 and M999 answers are
    /// passed through for the client to interpret.
    /// </summary>
    private async Task QueryReportAsync(long id, Func<string> call)
    {
        if (_reportCalled)
        {
            TimeSpan wait = _timings.ReportInterval - _time.GetElapsedTime(_lastReportTimestamp);

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _time).ConfigureAwait(true);
            }
        }

        string text;

        try
        {
            text = call() ?? string.Empty;
        }
#pragma warning disable CA1031 // COM 呼叫失敗(HRESULT)轉成查詢失敗回應,不讓宿主結束。A failed COM call becomes a query failure.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _out.WriteError(
                id,
                WireErrorCodes.AccountQueryFailed,
                string.Create(CultureInfo.InvariantCulture, $"回報查詢失敗(0x{exception.HResult:X8})。The report query failed (0x{exception.HResult:X8})."),
                writer => writer.WriteString("hresult", string.Create(CultureInfo.InvariantCulture, $"0x{exception.HResult:X8}")));
            return;
        }
        finally
        {
            _lastReportTimestamp = _time.GetTimestamp();
            _reportCalled = true;
        }

        string[] rows = text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        _out.WriteResult(id, writer => WriteStringArray(writer, "rows", rows));
    }

    /// <summary>
    /// 等一個帳務查詢結束(沿用 probe-full):收過狀態事件後、或收到「##」後安靜 <see cref="ServeTimings.AccountQuietAfterStatus"/>,
    /// 或從最後一則事件(沒有事件就從送出時)起安靜 <see cref="ServeTimings.AccountQuiet"/>;超過 <see cref="ServeTimings.AccountQueryLimit"/>
    /// 回 <see langword="false"/>。
    /// Waits for an account query to end the probe-full way; false after AccountQueryLimit.
    /// </summary>
    private async Task<bool> WaitAccountAsync(AccountCollector collector)
    {
        long started = _time.GetTimestamp();

        while (true)
        {
            await Task.Delay(_timings.AccountPoll, _time).ConfigureAwait(true);

            lock (_eventGate)
            {
                TimeSpan quiet = _time.GetElapsedTime(collector.LastEventTimestamp == 0 ? started : collector.LastEventTimestamp);

                if ((collector.Status is not null || collector.EndSeen) && quiet >= _timings.AccountQuietAfterStatus)
                {
                    return true;
                }

                if (collector.Status is not null && collector.EndSeen)
                {
                    return true;
                }

                if (quiet >= _timings.AccountQuiet)
                {
                    return true;
                }

                if (_time.GetElapsedTime(started) >= _timings.AccountQueryLimit)
                {
                    return false;
                }
            }
        }
    }

    private void ClearAccountCollector()
    {
        lock (_eventGate)
        {
            _accountCollector = null;
        }
    }

    private void OnAccountRow(string loginId, string data)
    {
        // 帳號、身分證字號、戶名一拿到就登記遮罩,之後任何紀錄都看不到。Registered for redaction at once.
        RegisterAccountSecrets(data);

        lock (_eventGate)
        {
            AccountCollector? collector = _accountCollector;

            if (collector is null || collector.DataEvent is not null)
            {
                return;
            }

            collector.LastEventTimestamp = _time.GetTimestamp();

            if (data.TrimStart().StartsWith("##", StringComparison.Ordinal))
            {
                collector.EndSeen = true;
                return;
            }

            if (data.Trim().Length > 0)
            {
                collector.Rows.Add(data);
            }
        }
    }

    private void OnAccountReply(string eventName, string data)
    {
        lock (_eventGate)
        {
            AccountCollector? collector = _accountCollector;

            if (collector is null || !string.Equals(collector.DataEvent, eventName, StringComparison.Ordinal))
            {
                return;
            }

            collector.LastEventTimestamp = _time.GetTimestamp();

            // 手冊 4-2-d / i / k / q:全部送完以「##」開頭那筆結尾。Every reply list ends with a ## row.
            if (data.TrimStart().StartsWith("##", StringComparison.Ordinal))
            {
                collector.EndSeen = true;
                return;
            }

            collector.Rows.Add(data);
        }
    }

    private void OnAccountStatus(string eventName, int status, string message)
    {
        lock (_eventGate)
        {
            AccountCollector? collector = _accountCollector;

            if (collector is null || !string.Equals(collector.StatusEvent, eventName, StringComparison.Ordinal))
            {
                return;
            }

            collector.LastEventTimestamp = _time.GetTimestamp();
            collector.Status = status;
            collector.StatusMessage = message ?? string.Empty;
        }
    }

    /// <summary>
    /// 從 OnAccount 原文(手冊 4-2-a:市場,分公司代碼,分公司,帳號,身份證字號,姓名)登記遮罩:帳號號碼與完整帳號 → <c>&lt;ACCT&gt;</c>、
    /// 身分證字號 → <c>&lt;ID&gt;</c>、戶名 → <c>&lt;NAME&gt;</c>。第 2、3 欄兩種接法都登記(實測 2026-11-09 是
    /// <c>TF,F020000,000,帳號,身分證,戶名,9</c>,分公司代碼在第 2 欄;戶名是群益亂碼後的字串,遮罩登記的就是那個字串)。
    /// Registers the account numbers, the holder's ID and name for redaction; both broker-field layouts are registered.
    /// The measured name arrives garbled, and that very string is what gets registered.
    /// </summary>
    private void RegisterAccountSecrets(string data)
    {
        string[] fields = [.. data.Split(',').Select(field => field.Trim())];

        if (fields.Length < 4 || fields[0].StartsWith("##", StringComparison.Ordinal))
        {
            return;
        }

        string account = fields[3];

        if (account.Length >= 4)
        {
            _log.AddSecret(fields[1] + account, "<ACCT>");
            _log.AddSecret(fields[2] + account, "<ACCT>");
            _log.AddSecret(account, "<ACCT>");
        }

        if (fields.Length > 4 && fields[4].Length >= 4)
        {
            _log.AddSecret(fields[4], "<ID>");
        }

        if (fields.Length > 5 && fields[5].Length >= 2)
        {
            _log.AddSecret(fields[5], "<NAME>");
        }
    }

    private void WriteAccountStepError(long id, string errorCode, string step, int code, string zh, string en)
    {
        string message = _log.Redact(SafeMessage(code));
        _log.Write(string.Create(CultureInfo.InvariantCulture, $"{zh}(代碼 {code}):{message}。"));
        _out.WriteError(
            id,
            errorCode,
            string.Create(CultureInfo.InvariantCulture, $"{zh}(代碼 {code}):{message}。{en} (code {code})."),
            writer =>
            {
                writer.WriteNumber("skCode", code);
                writer.WriteString("skMessage", message);
                writer.WriteString("step", step);
            });
    }

    /// <summary>
    /// 帳號:非空、最多 32 字元、不含逗號、<c>#</c>、空白或控制字元。帳號格式(IB + 帳號)待實測,這裡只擋明顯不對的。
    /// An account: non-empty, at most 32 characters, no commas, '#', whitespace or control characters.
    /// </summary>
    internal static bool IsValidAccount(string account)
    {
        if (account.Length is 0 or > MaxAccountLength)
        {
            return false;
        }

        foreach (char c in account)
        {
            if (c is ',' or '#' || char.IsWhiteSpace(c) || char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadAccountParameters(string method, JsonElement parameters, out string account, out int option, out string problem)
    {
        account = string.Empty;
        option = 0;
        problem = string.Empty;

        if (method == "accounts")
        {
            return true;
        }

        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("account", out JsonElement accountElement)
            || accountElement.ValueKind != JsonValueKind.String
            || !IsValidAccount(accountElement.GetString() ?? string.Empty))
        {
            problem = $"{method} 需要 account(完整帳號:分公司代碼 + 帳號)。{method} needs account, the full account.";
            return false;
        }

        account = accountElement.GetString()!;

        // 各查詢的選項範圍(手冊):權益數 sCoinType 0–2;國內未平倉 nFormat 手冊只列 1(2、3 照送,待實測);海外未平倉 1 彙總 / 2 明細;
        // 委託回報 1–9;成交回報 1–5。The option ranges by the manual.
        (string name, int min, int max) = method switch
        {
            "account-rights" => ("coin", 0, 2),
            "account-open-interest" => ("format", 1, 3),
            "account-os-open-interest" => ("format", 1, 2),
            "account-orders" => ("format", 1, 9),
            "account-fills" => ("format", 1, 5),
            _ => (string.Empty, 0, 0),
        };

        if (name.Length == 0)
        {
            return true;
        }

        if (!parameters.TryGetProperty(name, out JsonElement optionElement)
            || optionElement.ValueKind != JsonValueKind.Number
            || !optionElement.TryGetInt32(out option)
            || option < min
            || option > max)
        {
            problem = string.Create(CultureInfo.InvariantCulture, $"{method} 的 {name} 必須是 {min}–{max}。{method} needs {name} from {min} to {max}.");
            return false;
        }

        return true;
    }

    private sealed class AccountCollector
    {
        public AccountCollector(string? dataEvent, string? statusEvent)
        {
            DataEvent = dataEvent;
            StatusEvent = statusEvent;
        }

        /// <summary>結果事件名稱;<see langword="null"/> 表示收的是 OnAccount。The reply event; null collects OnAccount.</summary>
        public string? DataEvent { get; }

        public string? StatusEvent { get; }

        public List<string> Rows { get; } = [];

        public long LastEventTimestamp { get; set; }

        public bool EndSeen { get; set; }

        public int? Status { get; set; }

        public string StatusMessage { get; set; } = string.Empty;
    }
}
