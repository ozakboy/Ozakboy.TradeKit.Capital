using System.Globalization;
using Ozakboy.TradeKit.Capital.Host.Serve;

namespace FakeCapitalHostExe;

// 0.3.0:期貨帳務查詢(唯讀)。格式依手冊 V2.13.59(4-2-a、4-2-d、4-2-i、4-2-k、4-2-q、4-2-t–w、5-4-4、5-4-5),
// 實測 2026-11-09(只有期貨戶、沒有部位與委託)的形狀照抄:OnAccount 7 欄(分公司欄 000、多一個第 7 欄 9)、權益數全幣別先基幣、
// 查無資料的 001 / @@,001 / M003、海外權益數第一筆 000,1,訊息 與 31 欄、結尾是 ## 加一串逗號,以及中文亂碼(CapitalSkcomText 的轉換,
// 例如「查無資料」→ ?dμL﹐eRA)。有部位時的資料列沒有實測過,金額照手冊欄位填假的。帳號、戶名、金額全部是假的。
// 可設成建不起 SKOrderLib、初始化失敗、讀憑證失敗、權益數狀態回 1(訊息含帳號,驗證遮罩)。
// 0.3.0: read-only futures account queries. The empty-account shapes copy the 2026-11-09 measurement, garbled Chinese
// included; rows with positions are unmeasured and filled by the manual. Every account number, name and amount is
// made up.
internal sealed partial class FakeSkQuoteSource
{
    /// <summary>國內期貨帳號(分公司代碼 F020000 + 帳號 1234567)。The domestic futures account.</summary>
    public const string DomesticAccount = "F0200001234567";

    /// <summary>海外期貨帳號。The overseas futures account.</summary>
    public const string OverseasAccount = "F0200007654321";

    /// <summary>
    /// 假戶名「王小明」經群益亂碼後的樣子(實測 2026-11-09:SKOrderLib 的中文是 Big5 位元組當 Latin-1 再經字碼頁 950 最適對應)。
    /// The made-up holder name as Capital garbles it.
    /// </summary>
    public const string GarbledHolderName = "?y?pcu";

    // 實測 2026-11-09 的亂碼字樣:「查無資料」「查詢成功」「NTD(基幣)」。The measured garbled phrases.
    private const string GarbledNoData = "?dμL﹐eRA";
    private const string GarbledQuerySucceeded = "?d﹐s|‥￥\\";
    private const string GarbledBaseCurrency = "NTD(°o1o)";

    /// <summary>實測的結尾列:## 加 50 個逗號。The measured end row.</summary>
    private static readonly string EndRow = "##" + new string(',', 50);

    public event Action<string, string>? AccountRow;

    public event Action<string, string>? AccountReply;

    public event Action<string, int, string>? AccountStatus;

    public SkOpenFailure? OpenAccounts()
    {
        _settings.Record("order_open");
        return _settings.OrderOpenHResult is { } hresult ? new SkOpenFailure(ServeStates.ReasonNotRegistered, hresult) : null;
    }

    public int InitializeAccounts()
    {
        _settings.Record("order_init");
        return _settings.OrderInitCode;
    }

    public int RequestAccounts()
    {
        _settings.Record("order_accounts");
        string id = _loginId;

        // 實測 2026-11-09:期貨戶回 TF 與 OF 兩行 市場,F020000,000,帳號,身分證,戶名(亂碼),9,沒有結尾標記。
        // 第三行證券戶 TS 沒有實測過(只有期貨戶),照手冊 4-2-a 推定,用來驗證「其他」類別。Measured TF / OF rows; TS is assumed.
        Later(
            () => AccountRow?.Invoke(id, $"TF,F020000,000,1234567,{id},{GarbledHolderName},9"),
            () => AccountRow?.Invoke(id, $"OF,F020000,000,7654321,{id},{GarbledHolderName},9"),
            () => AccountRow?.Invoke(id, $"TS,9A00,總公司,9876543,{id},{GarbledHolderName}"));
        return 0;
    }

    public int ReadCertificate(string loginId)
    {
        _settings.Record("order_cert");
        return _settings.OrderCertCode;
    }

    public int RequestFutureRights(string loginId, string account, short coinType)
    {
        _settings.Record(string.Create(CultureInfo.InvariantCulture, $"rights {coinType}"));

        if (_settings.RightsFail)
        {
            Later(() => AccountStatus?.Invoke("OnFutureRightsStatus", 1, $"帳號 {account} 查詢失敗"));
            return 0;
        }

        // 手冊 4-2-i:41 欄;全幣別時第一筆是基幣。實測 2026-11-09(sCoinType 0):第一筆幣別 NTD(基幣)(亂碼)、第二筆 NTD,再來 ## 加逗號、
        // 狀態 0 Success!。sCoinType 1 / 2 當時被 1019 擋下沒測到,這裡推定只有一筆。
        // Measured with all currencies: the garbled base-currency row first, then NTD; other coin types are assumed.
        // 實測的順序:狀態事件先到(約 2 毫秒後才是資料列)。Measured order: the status event comes first.
        List<Action> replies =
        [
            () => AccountStatus?.Invoke("OnFutureRightsStatus", 0, "Success!"),
            () => AccountReply?.Invoke("OnFutureRights", RightsRow(loginId, account, coinType == 0 ? GarbledBaseCurrency : "NTD", 1)),
        ];

        if (coinType == 0)
        {
            replies.Add(() => AccountReply?.Invoke("OnFutureRights", RightsRow(loginId, account, "NTD", 2)));
        }

        replies.Add(() => AccountReply?.Invoke("OnFutureRights", EndRow));
        Later([.. replies]);
        return 0;
    }

    public int RequestOpenInterest(string loginId, string account, int format)
    {
        _settings.Record(string.Create(CultureInfo.InvariantCulture, $"open_interest {format}"));

        // 手冊 4-2-d(GW 格式 1):市場別,帳號,商品,買賣別,未平倉,當沖未平倉,平均成本,單口手續費,交易稅,LOGIN_ID,下單代碼(有部位的列未實測)。
        // 實測 2026-11-09 沒有部位:狀態先到(0、訊息空白)、再 001,查無資料(亂碼),帳號 與 ## 加逗號。
        // The position row follows the manual; the measured no-data shape is status, then 001 with the garbled message.
        if (account != DomesticAccount)
        {
            Later(
                () => AccountStatus?.Invoke("OnOpenInterestGWStatus", 0, string.Empty),
                () => AccountReply?.Invoke("OnOpenInterest", $"001,{GarbledNoData},{account}"),
                () => AccountReply?.Invoke("OnOpenInterest", EndRow));
            return 0;
        }

        Later(
            () => AccountReply?.Invoke("OnOpenInterest", $"TM,{account},TXFK6,B,2,0,39786.50,50,2,{loginId},TXFK6"),
            () => AccountReply?.Invoke("OnOpenInterest", EndRow),
            () => AccountStatus?.Invoke("OnOpenInterestGWStatus", 0, string.Empty));
        return 0;
    }

    public int RequestOverseaFutureRights(string loginId, string account)
    {
        _settings.Record("os_rights");

        // 手冊 4-2-k;實測 2026-11-09:第一筆「000,1,查詢成功(亂碼)」,之後每筆 31 欄(期貨商代碼, IB 代號 000, 帳號, 幣別, …,第 22 欄參考匯率),
        // 狀態事件先到。金額是假的。Measured: "000,1,<message>" first, then 31 fields per row; the amounts are made up.
        string[] fields = [.. Enumerable.Range(0, 31).Select(i => (i * 10 + 0.5m).ToString(CultureInfo.InvariantCulture))];
        fields[0] = "F020000";
        fields[1] = "000";
        fields[2] = "7654321";
        fields[3] = GarbledBaseCurrency;
        fields[22] = "1";

        Later(
            () => AccountStatus?.Invoke("OnOverseaFutureRightsStatus", 0, "Success!"),
            () => AccountReply?.Invoke("OnOverSeaFutureRight", "000,1," + GarbledQuerySucceeded),
            () => AccountReply?.Invoke("OnOverSeaFutureRight", string.Join(',', fields)),
            () => AccountReply?.Invoke("OnOverSeaFutureRight", EndRow));
        return 0;
    }

    public int RequestOverseaOpenInterest(string loginId, string account, int format)
    {
        _settings.Record(string.Create(CultureInfo.InvariantCulture, $"os_open_interest {format}"));

        // 手冊 4-2-q:第一筆「@@,代碼,訊息」,彙總 21 欄 / 明細 17 欄(選擇權另有履約價等欄),以 ## 結尾。昨日結算價可能含分數「379 02/4」。
        // 有部位的列與 @@,0 的成功列沒有實測過(照手冊);實測 2026-11-09 沒有部位時是 狀態 → @@,001,查無資料(亂碼),帳號 → ## 加逗號,
        // 這裡以國內帳號查海外未平倉模擬這種情況。The measured no-data shape is used for the domestic account.
        if (account == DomesticAccount)
        {
            Later(
                () => AccountStatus?.Invoke("OnOverseaFutureOpenInterestGWStatus", 0, string.Empty),
                () => AccountReply?.Invoke("OnOFOpenInterestGWReport", $"@@,001,{GarbledNoData},{account}"),
                () => AccountReply?.Invoke("OnOFOpenInterestGWReport", EndRow));
            return 0;
        }

        string row = format == 1
            ? "7654321,CME,芝加哥商品交易所,NQ 202612,NQ,202612,小那斯達克 202612,B,1,26000.25,26100.00,2000.00,0,0,0,0,0,379 02/4,0,17600.00,0"
            : "7654321,20261105,CME,芝加哥商品交易所,NQ,202612,小那斯達克 202612,B,1,1,26000.25,26100.00,2000.00,AB12345,N,17600.00,0";

        Later(
            () => AccountStatus?.Invoke("OnOverseaFutureOpenInterestGWStatus", 0, string.Empty),
            () => AccountReply?.Invoke("OnOFOpenInterestGWReport", "@@,0,"),
            () => AccountReply?.Invoke("OnOFOpenInterestGWReport", row),
            () => AccountReply?.Invoke("OnOFOpenInterestGWReport", EndRow));
        return 0;
    }

    public string GetOrderReport(string loginId, string account, int format)
    {
        _settings.Record(string.Create(CultureInfo.InvariantCulture, $"order_report {format} {Environment.TickCount64}"));

        // 實測 2026-11-09:沒有委託時格式 1–9 都只回 4 個字元「M003」。Measured: exactly "M003" without orders.
        if (account != DomesticAccount)
        {
            return "M003";
        }

        // 手冊 5-4-4(格式 1–6、9):70 欄,1 起算。70 fields, numbered from 1.
        string[] fields = new string[70];
        Array.Fill(fields, string.Empty);
        Set(fields, 1, "TF", 2, "FUT", 3, "TAIFEX", 4, "F020000", 6, "1234567", 8, "k0001", 9, "0000000000001", 11, "2");
        Set(fields, 12, "20261106", 13, "084601", 14, "20261106", 16, "TXFK6", 23, "B", 26, "0", 27, "2", 28, "39786", 29, "39786");
        Set(fields, 30, "2", 31, "2", 32, "2", 33, "0", 34, "N", 35, "A", 36, "N", 53, "20261106", 54, "084602");
        return string.Join(',', fields) + "\r\n";
    }

    public string GetFulfillReport(string loginId, string account, int format)
    {
        _settings.Record(string.Create(CultureInfo.InvariantCulture, $"fulfill_report {format} {Environment.TickCount64}"));

        // 實測 2026-11-09:沒有成交時格式 1–5 都只回「M003」。Measured: exactly "M003" without fills.
        if (account != DomesticAccount)
        {
            return "M003";
        }

        // 手冊 5-4-5(格式 1、5):52 欄,1 起算。52 fields, numbered from 1.
        string[] fields = new string[52];
        Array.Fill(fields, string.Empty);
        Set(fields, 1, "TF", 2, "FUT", 3, "TAIFEX", 4, "F020000", 6, "1234567", 8, "k0001", 9, "00001", 10, "20261106", 11, "084602");
        Set(fields, 12, "20261106", 13, "TXFK6", 14, "台指期11", 23, "B", 27, "39786", 28, "2", 29, "N", 30, "A", 38, "20261106", 39, "084601");
        return string.Join(',', fields) + "\r\n";
    }

    /// <summary>國內權益數一筆(41 欄:0–38 金額 / 比率,25 幣別,39 LOGIN_ID,40 ACCOUNT_NO)。One rights row.</summary>
    private static string RightsRow(string loginId, string account, string currency, int scale)
    {
        string[] fields = [.. Enumerable.Range(0, 41).Select(i => (i * 1000m * scale + 0.25m).ToString(CultureInfo.InvariantCulture))];
        fields[23] = "N";
        fields[25] = currency;
        fields[39] = loginId;
        fields[40] = account;
        return string.Join(',', fields);
    }

    private static void Set(string[] fields, params object[] pairs)
    {
        for (int i = 0; i < pairs.Length; i += 2)
        {
            fields[(int)pairs[i] - 1] = (string)pairs[i + 1];
        }
    }

    /// <summary>依序非同步送出(真 SKCOM 走訊息迴圈)。Raises the actions in order, asynchronously.</summary>
    private static void Later(params Action[] actions) => _ = Task.Run(async () =>
    {
        foreach (Action action in actions)
        {
            await Task.Delay(15).ConfigureAwait(false);
            action();
        }
    });
}
