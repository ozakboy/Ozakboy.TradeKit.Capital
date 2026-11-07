using System.Globalization;
using System.Text.Json;
using Ozakboy.TradeKit.Capital.Protocol;

namespace Ozakboy.TradeKit.Capital;

// 國內報價(SKQuoteLib):國內狀態與國內期貨歷史 K 線。
// The domestic quote side: its state and domestic futures historical K-lines.
public sealed partial class CapitalHostSession
{
    private const int MaxDomesticCodeLength = 32;

    private CapitalDomesticState _domesticState = CapitalDomesticState.NotStarted;
    private string? _domesticReason;

    /// <summary>
    /// 國內報價狀態改變(在觸發改變的執行緒上同步呼叫;處理常式不要阻塞)。與 <see cref="StateChanged"/> 分開:國內的任何變化都不會
    /// 觸發 <see cref="StateChanged"/>,海外的 <see cref="State"/> 也不受國內影響。宿主重啟或停止時回到
    /// <see cref="CapitalDomesticState.NotStarted"/>,新的宿主就緒後再重新回報。
    /// Raised synchronously on the thread that changed the domestic state; handlers must not block. Separate from
    /// StateChanged: no domestic change raises StateChanged and the overseas State is never affected. A host restart or
    /// stop goes back to NotStarted, and the new host reports again once ready.
    /// </summary>
    public event EventHandler<CapitalDomesticStateChangedEventArgs>? DomesticStateChanged;

    /// <summary>國內報價目前的狀態。The current domestic quote state.</summary>
    public CapitalDomesticState DomesticState
    {
        get
        {
            lock (_stateLock)
            {
                return _domesticState;
            }
        }
    }

    /// <summary>
    /// 國內不可用或斷線的原因(<see cref="CapitalDomesticReasons"/>);就緒、連線中或還沒開始時為 <see langword="null"/>。
    /// Why the domestic side is unavailable or disconnected; null while ready, connecting or not started.
    /// </summary>
    public string? DomesticReason
    {
        get
        {
            lock (_stateLock)
            {
                return _domesticReason;
            }
        }
    }

    /// <summary>
    /// 國內期貨歷史 K 線(<c>SKQuoteLib_RequestKLineAMByDate</c>)。需要群益<b>期貨戶</b>;沒有證券戶拿不到上市櫃資料,本套件只承諾期貨
    /// (實測可用 <c>TX00</c> 台指近月、<c>MTX00</c> 小台近月、<c>TM0000</c> 微台近月,換月方式與永豐連續月相同)。
    /// Domestic futures historical K-lines. A Capital <b>futures account</b> is required; without a securities account
    /// listed stocks are unavailable, and this package only commits to futures (measured: TX00, MTX00 and TM0000, the
    /// near months, rolled the same way as Sinopac's continuous months).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="from"/> / <paramref name="to"/> 是<b>交易日</b>:<see cref="CapitalTradeSession.Full"/> 會含前一個交易日 15:00 起的夜盤
    /// (查週一會從上週五 15:01 開始)。分 K 的 <see cref="CapitalDomesticBar.OpenTimeUtc"/> = 台北時間的結束時刻 − 1 分鐘轉 UTC;
    /// 量為 0 的空 K 棒照原樣回。一個請求最多 60,000 筆,超過時群益保留最新、截掉最舊(不會報錯,請自己檢查第一根);
    /// 1 分 K 約只保留最近 1 年,日 K 到 2015 年都有。
    /// The dates are <b>trading days</b>: Full includes the night session from 15:00 of the previous trading day. A minute
    /// bar's open time is the Taipei end time minus one minute, in UTC; empty zero-volume bars are kept. One request holds
    /// at most 60,000 rows and Capital keeps the newest and drops the oldest beyond that without an error, so check the
    /// first bar; minute bars go back about a year, daily bars to 2015.
    /// </para>
    /// <para>
    /// <b>收盤價可能是結算價</b>(全盤分 K 的日盤最後一根與全盤日 K),見 <see cref="CapitalTradeSession"/>;本套件不修正。
    /// <b>The close may be the settlement price</b>; see <see cref="CapitalTradeSession"/>. This package does not correct it.
    /// </para>
    /// <para>
    /// 國內在海外就緒之後才連線,剛啟動時(<see cref="CapitalDomesticState.Connecting"/>)送的請求由宿主等到國內有結果再處理。
    /// 失敗:<see cref="CapitalErrorCodes.DomesticUnavailable"/>(國內不可用或斷線)、<see cref="CapitalErrorCodes.KLineRejected"/>
    /// (代碼不存在或沒權限,群益代碼在 <see cref="CapitalErrorDataKeys.Code"/>,實測 9999、3023)、<see cref="CapitalErrorCodes.KLineTimeout"/>
    /// (60 秒沒有結束事件)。讀不懂的行記數略過並寫一筆警告日誌,不讓整批失敗。
    /// The domestic side connects after the overseas side is ready; a request sent while it is still Connecting waits in
    /// the host. Failures: DomesticUnavailable, KLineRejected (unknown code or no permission; Capital's code in Data),
    /// KLineTimeout (no end event within 60 seconds). Unreadable rows are counted, skipped and logged as one warning.
    /// </para>
    /// </remarks>
    /// <param name="code">群益國內商品代碼(例如 <c>TX00</c>)。Capital's domestic code, for example TX00.</param>
    /// <param name="type">K 線類型。The bar type.</param>
    /// <param name="session">盤別。The session.</param>
    /// <param name="from">起始交易日(含)。The first trading day, inclusive.</param>
    /// <param name="to">結束交易日(含)。The last trading day, inclusive.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>K 線(可能是空的),照群益的順序。The bars, possibly empty, in Capital's order.</returns>
    public async Task<Result<IReadOnlyList<CapitalDomesticBar>>> GetDomesticKLinesAsync(
        string code,
        CapitalDomesticKLineType type,
        CapitalTradeSession session,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidDomesticCode(code))
        {
            return CapitalErrors.InvalidArgument("國內商品代碼不合法(不可空白,不可含逗號、# 或空白字元)。The domestic code is invalid.");
        }

        if (!Enum.IsDefined(type))
        {
            return CapitalErrors.InvalidArgument("國內 K 線類型只能是 Minute / Day。The domestic bar type must be Minute or Day.");
        }

        if (!Enum.IsDefined(session))
        {
            return CapitalErrors.InvalidArgument("盤別只能是 Full / AmOnly。The session must be Full or AmOnly.");
        }

        if (from > to)
        {
            return CapitalErrors.InvalidArgument("起日不可晚於迄日。from must not be after to.");
        }

        Result<JsonElement> response = await SendAsync(
                "kline-tw",
                writer =>
                {
                    writer.WriteString("code", code);
                    writer.WriteNumber("type", (int)type);
                    writer.WriteNumber("session", (int)session);
                    writer.WriteString("start", from.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                    writer.WriteString("end", to.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                },
                KLineTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        if (response.IsFailure)
        {
            return response.Error;
        }

        JsonElement result = response.GetValueOrThrow();

        if (!result.TryGetProperty("lines", out JsonElement linesElement) || linesElement.ValueKind != JsonValueKind.Array)
        {
            return CapitalErrors.Protocol("kline-tw 的回應沒有 lines 陣列。The kline-tw result has no lines array.");
        }

        List<string> lines = new(linesElement.GetArrayLength());

        foreach (JsonElement line in linesElement.EnumerateArray())
        {
            lines.Add(line.GetString() ?? string.Empty);
        }

        IReadOnlyList<CapitalDomesticBar> bars = CapitalDomesticKLineParser.Parse(code, type, lines, out int skipped);

        if (skipped > 0)
        {
            CapitalHostLog.DomesticKLineSkipped(_logger, code, skipped, bars.Count);
        }

        return Result.Success(bars);
    }

    /// <summary>
    /// 國內商品代碼:非空、最多 32 字元、不含逗號、<c>#</c>、空白或控制字元(與宿主同一條規則)。
    /// A domestic code: non-empty, at most 32 characters, without commas, '#', whitespace or control characters.
    /// </summary>
    internal static bool IsValidDomesticCode(string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > MaxDomesticCodeLength)
        {
            return false;
        }

        foreach (char c in code)
        {
            if (c is ',' or '#' || char.IsWhiteSpace(c) || char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>宿主的 status 事件帶了國內欄位就更新國內狀態。Updates the domestic state when a status event carries it.</summary>
    private void ApplyDomesticStatus(HostStatus status)
    {
        if (status.Domestic is null)
        {
            return;
        }

        if (!CapitalWire.TryParseDomesticState(status.Domestic, out CapitalDomesticState state))
        {
            WarnOnce("domestic:" + status.Domestic, () => CapitalHostLog.ProtocolWarning(_logger, "不認得的國內狀態 / unknown domestic state " + status.Domestic));
            return;
        }

        SetDomesticState(state, status.DomesticReason, status.DomesticCode);
    }

    private void SetDomesticState(CapitalDomesticState next, string? reason, int? code)
    {
        CapitalDomesticState previous;

        lock (_stateLock)
        {
            previous = _domesticState;

            if (previous == next && string.Equals(_domesticReason, reason, StringComparison.Ordinal))
            {
                return;
            }

            _domesticState = next;
            _domesticReason = reason;
        }

        CapitalHostLog.DomesticStateChanged(_logger, previous, next, reason);

        try
        {
            DomesticStateChanged?.Invoke(this, new CapitalDomesticStateChangedEventArgs(previous, next, reason, code));
        }
#pragma warning disable CA1031 // 呼叫端處理常式的缺陷不能拖垮讀取迴圈或監督;記錄後繼續。A handler defect must not stop the reader or the supervisor.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            CapitalHostLog.DomesticStateHandlerFailed(_logger, exception);
        }
    }
}
