using System.Globalization;
using System.Text.Json;
using Ozakboy.TradeKit.Capital.Protocol;

namespace Ozakboy.TradeKit.Capital;

// 0.3.0:期貨帳務查詢(唯讀)。宿主在第一次帳務請求才初始化 SKOrderLib(Initialize → GetUserAccount → ReadCertByID);本套件<b>沒有</b>
// 任何下單、刪改單、出入金能力(宿主的 COM 定義裡那些方法都是呼叫不到的佔位)。帳號一拿到就登記到遮罩,不進日誌。
// 0.3.0: read-only futures account queries. This package has <b>no</b> order, amend, cancel or fund-transfer capability.
// Account numbers are registered for masking as soon as they arrive and never logged.
public sealed partial class CapitalHostSession
{
    private const int MaxAccountLength = 32;
    private static readonly TimeSpan AccountTimeout = TimeSpan.FromSeconds(180);

    private readonly Lock _accountGate = new();
    private readonly List<string> _accountLiterals = [];

    /// <summary>
    /// 帳號清單(<c>GetUserAccount</c> → <c>OnAccount</c>,手冊 4-2-2、4-2-a)。第一次帳務請求時宿主才初始化 SKOrderLib(要等 OnAccount 安靜 6 秒,
    /// 第一次比較慢;實測 2026-11-09 約 6.3 秒)。欄位見 <see cref="CapitalAccount"/>(實測過)。
    /// The account list. The host initialises SKOrderLib on the first account request, which waits for OnAccount to go
    /// quiet for six seconds (about 6.3 s as measured).
    /// </summary>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>
    /// 帳號;失敗:<see cref="CapitalErrorCodes.AccountUnavailable"/>、<see cref="CapitalErrorCodes.AccountInitFailed"/>、
    /// <see cref="CapitalErrorCodes.AccountCertFailed"/>。The accounts, or a failure.
    /// </returns>
    public async Task<Result<IReadOnlyList<CapitalAccount>>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        Result<JsonElement> response = await SendAsync("accounts", null, AccountTimeout, cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
        {
            return response.Error;
        }

        if (!CapitalWire.TryReadStrings(response.GetValueOrThrow(), "items", out List<string> rows))
        {
            return CapitalErrors.Protocol("accounts 的回應沒有 items 陣列。The accounts result has no items array.");
        }

        IReadOnlyList<CapitalAccount> accounts = CapitalAccountParser.ParseAccounts(rows);

        foreach (CapitalAccount account in accounts)
        {
            RegisterAccount(account.AccountNo);
            RegisterAccount(account.FullAccount);
        }

        return Result.Success(accounts);
    }

    /// <summary>
    /// 國內期貨權益數(<c>GetFutureRights</c>,手冊 4-2-41;每個幣別一筆,全幣別時第一筆是基幣)。實測 2026-11-09 確認欄位位置與基幣在前,
    /// 金額的數值語意待有部位時再驗;見 <see cref="CapitalFutureRights"/>。
    /// <para>
    /// <b>同一個查詢連續送會被擋</b>(實測):前一次結束後 1–2 秒再送同一個查詢函式(例如換 <paramref name="coin"/> 再查),群益回 1019
    /// SK_ERROR_QUERY_IN_PROCESSING(查詢尚未結束請稍後再試)→ <see cref="CapitalErrorCodes.AccountQueryRejected"/>,
    /// <c>Data["code"]</c> = 1019;隔多久才行還沒量到,請間隔幾秒再查。其他帳務查詢也一樣。
    /// </para>
    /// Domestic futures rights, one row per currency, the base currency first (measured). Sending the same query
    /// function again within a few seconds is rejected with code 1019 (AccountQueryRejected); wait a few seconds.
    /// </summary>
    /// <param name="account">完整帳號(<see cref="CapitalAccount.FullAccount"/>)。The full account.</param>
    /// <param name="coin">幣別。The currency selection.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>
    /// 權益數;失敗另有 <see cref="CapitalErrorCodes.AccountQueryRejected"/>(群益代碼在 <c>Data["code"]</c>,1019 = 前一個同類查詢還沒結束)、
    /// <see cref="CapitalErrorCodes.AccountQueryFailed"/>、<see cref="CapitalErrorCodes.AccountQueryTimeout"/>。The rights, or a failure.
    /// </returns>
    public async Task<Result<IReadOnlyList<CapitalFutureRights>>> GetFutureRightsAsync(string account, CapitalCoinType coin = CapitalCoinType.All, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(coin))
        {
            return CapitalErrors.InvalidArgument("幣別只能是 All / BaseCurrency / Rmb。The coin type must be All, BaseCurrency or Rmb.");
        }

        Result<IReadOnlyList<string>> rows = await QueryRowsAsync("account-rights", account, "coin", (int)coin, cancellationToken).ConfigureAwait(false);
        return rows.IsFailure ? rows.Error : Result.Success(CapitalAccountParser.ParseFutureRights(rows.GetValueOrThrow()));
    }

    /// <summary>
    /// 國內期貨未平倉(<c>GetOpenInterestGW</c>,手冊 4-2-89)。<paramref name="format"/> 手冊只列 1(具名屬性只解析格式 1);2、3 照送、只有
    /// <see cref="CapitalOpenInterest.RawFields"/>(實測時被 1019 擋下,接不接受仍待實測)。查無資料(實測 <c>001,查無資料,帳號</c>)回空清單;
    /// 有部位時的欄位待實測。
    /// Domestic open interest. The manual lists format 1 only; 2 and 3 are sent as they are and only carry RawFields. No
    /// data (measured) is an empty list.
    /// </summary>
    /// <param name="account">完整帳號。The full account.</param>
    /// <param name="format">1–3。</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>未平倉。The open interest.</returns>
    public async Task<Result<IReadOnlyList<CapitalOpenInterest>>> GetOpenInterestAsync(string account, int format = 1, CancellationToken cancellationToken = default)
    {
        if (format is < 1 or > 3)
        {
            return CapitalErrors.InvalidArgument("國內未平倉的 format 只能是 1–3。format must be 1 to 3.");
        }

        Result<IReadOnlyList<string>> rows = await QueryRowsAsync("account-open-interest", account, "format", format, cancellationToken).ConfigureAwait(false);
        return rows.IsFailure ? rows.Error : Result.Success(CapitalAccountParser.ParseOpenInterest(rows.GetValueOrThrow(), format));
    }

    /// <summary>
    /// 海外期貨權益數(<c>GetRequestOverSeaFutureRight</c>,手冊 4-2-45)。第一筆是查詢結果,失敗回 <see cref="CapitalErrorCodes.AccountQueryFailed"/>。
    /// 實測 2026-11-09:資料列 31 欄(見 <see cref="CapitalOverseaFutureRights"/>),金額的數值語意待有部位時再驗。
    /// Overseas futures rights; a failed first row is AccountQueryFailed. Rows have 31 fields as measured.
    /// </summary>
    /// <param name="account">完整帳號。The full account.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>權益數。The rights.</returns>
    public async Task<Result<IReadOnlyList<CapitalOverseaFutureRights>>> GetOverseaFutureRightsAsync(string account, CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<string>> rows = await QueryRowsAsync("account-os-rights", account, null, 0, cancellationToken).ConfigureAwait(false);
        return rows.IsFailure ? rows.Error : CapitalAccountParser.ParseOverseaFutureRights(rows.GetValueOrThrow(), Mask);
    }

    /// <summary>
    /// 海外期貨 / 選擇權未平倉(<c>GetOverseaFutureOpenInterestGW</c>,手冊 4-2-76)。第一筆「@@」代碼 0 是成功、001 是查無資料(實測,回空清單),
    /// 其他代碼回 <see cref="CapitalErrorCodes.AccountQueryFailed"/>。<b>有部位時的欄位位置待實測。</b>
    /// Overseas open interest; "@@" code 0 succeeds, 001 is no data (measured, an empty list) and any other code is
    /// AccountQueryFailed. <b>Rows with positions are not measured yet.</b>
    /// </summary>
    /// <param name="account">完整帳號。The full account.</param>
    /// <param name="format">彙總或明細。Summary or detail.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>未平倉。The open interest.</returns>
    public async Task<Result<IReadOnlyList<CapitalOverseaOpenInterest>>> GetOverseaOpenInterestAsync(
        string account,
        CapitalOverseaOpenInterestFormat format = CapitalOverseaOpenInterestFormat.Summary,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(format))
        {
            return CapitalErrors.InvalidArgument("海外未平倉的 format 只能是 Summary / Detail。format must be Summary or Detail.");
        }

        Result<IReadOnlyList<string>> rows = await QueryRowsAsync("account-os-open-interest", account, "format", (int)format, cancellationToken).ConfigureAwait(false);
        return rows.IsFailure ? rows.Error : CapitalAccountParser.ParseOverseaOpenInterest(rows.GetValueOrThrow(), format, Mask);
    }

    /// <summary>
    /// 委託回報查詢(<c>GetOrderReport</c>,手冊 4-2-97;只有國內證期選)。阻塞式查詢,宿主強制兩次回報查詢之間隔 5.5 秒(手冊要求 5 秒),
    /// 所以連續呼叫會變慢。查無資料(M003,實測 2026-11-09 九種格式都只回 <c>M003</c>)回空清單、查詢錯誤(M999)回
    /// <see cref="CapitalErrorCodes.AccountQueryFailed"/>。<b>有委託時的欄位位置與中文編碼待實測。</b>
    /// The order report query. It blocks, and the host keeps 5.5 seconds between two report queries, so back-to-back
    /// calls slow down. M003 (measured) is an empty list. <b>Rows with orders are not measured yet.</b>
    /// </summary>
    /// <param name="account">完整帳號。The full account.</param>
    /// <param name="format">查詢種類。The kind.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>委託回報。The order reports.</returns>
    public async Task<Result<IReadOnlyList<CapitalOrderReport>>> GetOrderReportAsync(
        string account,
        CapitalOrderReportFormat format = CapitalOrderReportFormat.All,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(format))
        {
            return CapitalErrors.InvalidArgument("委託回報的 format 只能是 1–9。format must be 1 to 9.");
        }

        Result<IReadOnlyList<string>> rows = await QueryRowsAsync("account-orders", account, "format", (int)format, cancellationToken).ConfigureAwait(false);
        return rows.IsFailure ? rows.Error : CapitalAccountParser.ParseOrderReports(rows.GetValueOrThrow(), format, Mask);
    }

    /// <summary>
    /// 成交回報查詢(<c>GetFulfillReport</c>,手冊 4-2-98)。間隔與回應規則同 <see cref="GetOrderReportAsync"/>(實測沒有成交時五種格式都回
    /// <c>M003</c>)。<b>有成交時的欄位位置待實測。</b>
    /// The fill report query, with the same spacing and answers as GetOrderReportAsync. <b>Rows with fills are not measured yet.</b>
    /// </summary>
    /// <param name="account">完整帳號。The full account.</param>
    /// <param name="format">查詢種類。The kind.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>成交回報。The fill reports.</returns>
    public async Task<Result<IReadOnlyList<CapitalFulfillReport>>> GetFulfillReportAsync(
        string account,
        CapitalFulfillReportFormat format = CapitalFulfillReportFormat.Full,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(format))
        {
            return CapitalErrors.InvalidArgument("成交回報的 format 只能是 1–5。format must be 1 to 5.");
        }

        Result<IReadOnlyList<string>> rows = await QueryRowsAsync("account-fills", account, "format", (int)format, cancellationToken).ConfigureAwait(false);
        return rows.IsFailure ? rows.Error : CapitalAccountParser.ParseFulfillReports(rows.GetValueOrThrow(), format, Mask);
    }

    /// <summary>
    /// 帳號:非空、最多 32 字元、不含逗號、<c>#</c>、空白或控制字元(與宿主同一條規則)。
    /// An account: non-empty, at most 32 characters, without commas, '#', whitespace or control characters.
    /// </summary>
    internal static bool IsValidAccount(string? account) => account is { Length: > 0 and <= MaxAccountLength } && IsValidDomesticCode(account);

    private async Task<Result<IReadOnlyList<string>>> QueryRowsAsync(string method, string account, string? optionName, int option, CancellationToken cancellationToken)
    {
        if (!IsValidAccount(account))
        {
            return CapitalErrors.InvalidArgument("帳號不合法(請傳 CapitalAccount.FullAccount)。The account is invalid; pass CapitalAccount.FullAccount.");
        }

        RegisterAccount(account);

        Result<JsonElement> response = await SendAsync(
                method,
                writer =>
                {
                    writer.WriteString("account", account);

                    if (optionName is not null)
                    {
                        writer.WriteNumber(optionName, option);
                    }
                },
                AccountTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        if (response.IsFailure)
        {
            return response.Error;
        }

        if (!CapitalWire.TryReadStrings(response.GetValueOrThrow(), "rows", out List<string> rows))
        {
            return CapitalErrors.Protocol(string.Create(CultureInfo.InvariantCulture, $"{method} 的回應沒有 rows 陣列。The {method} result has no rows array."));
        }

        return rows;
    }

    /// <summary>把帳號登記成要遮的字面值(短於 4 字元不登記)。Registers an account literal for masking.</summary>
    private void RegisterAccount(string? account)
    {
        if (account is not { Length: >= 4 })
        {
            return;
        }

        lock (_accountGate)
        {
            if (_accountLiterals.Contains(account, StringComparer.Ordinal))
            {
                return;
            }

            _accountLiterals.Add(account);

            // 長的先換:完整帳號(分公司代碼 + 帳號)含帳號號碼。Longer first, since the full account contains the number.
            _accountLiterals.Sort((left, right) => right.Length.CompareTo(left.Length));
        }
    }

    private string MaskAccounts(string text)
    {
        lock (_accountGate)
        {
            foreach (string literal in _accountLiterals)
            {
                text = text.Replace(literal, "<ACCT>", StringComparison.Ordinal);
            }
        }

        return text;
    }
}
