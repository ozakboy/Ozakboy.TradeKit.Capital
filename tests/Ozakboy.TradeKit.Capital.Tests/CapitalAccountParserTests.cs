using Ozakboy.TradeKit.Capital.Protocol;
using Ozakboy.TradeKit.Capital.Tests.Support;

namespace Ozakboy.TradeKit.Capital.Tests;

/// <summary>
/// 帳務原文解析。欄位位置依手冊 V2.13.59(4-2-a、4-2-d、4-2-i、4-2-k、4-2-q、5-4-4、5-4-5);這裡驗證的是「依手冊解析、
/// 解析不出為 null(不填 0)、RawFields 一律保留、ToString 不露帳號全碼」這組契約,名稱含 Measured 的測試以 2026-11-09 probe-full
/// 的實測原文(已遮罩)做回歸。
/// Account reply parsing by the manual: named fields, null when unreadable, raw fields kept, and ToString without the
/// full account; the Measured tests replay the redacted 2026-11-09 probe-full replies.
/// </summary>
[TestClass]
public sealed class CapitalAccountParserTests
{
    private static readonly Func<string, string> NoMask = text => text;

    [TestMethod]
    public void AccountsFollowTheManualAndSwapTheBrokerFieldsWhenTheyLookReversed()
    {
        IReadOnlyList<CapitalAccount> accounts = CapitalAccountParser.ParseAccounts(
        [
            "TF,F020000,群益期貨,1234567,A123456789,王小明",
            "OF,群益期貨,F020000,7654321,A123456789,王小明;",
            "TS,9A00,總公司,9876543",
            "##",
            "XX,only,three",
        ]);

        Assert.HasCount(3, accounts);
        Assert.AreEqual(CapitalAccountKind.DomesticFutures, accounts[0].Kind);
        Assert.AreEqual("F0200001234567", accounts[0].FullAccount);
        Assert.AreEqual("群益期貨", accounts[0].BrokerName);
        Assert.AreEqual(CapitalAccountKind.OverseasFutures, accounts[1].Kind);
        Assert.AreEqual("F020000", accounts[1].BrokerId, "the second field is not a code, so the third is used");
        Assert.AreEqual(CapitalAccountKind.Other, accounts[2].Kind);
        Assert.HasCount(6, accounts[0].RawFields);

        string text = accounts[0].ToString();
        Assert.IsFalse(text.Contains("1234567", StringComparison.Ordinal), text);
        Assert.IsFalse(text.Contains("A123456789", StringComparison.Ordinal), text);
        Assert.IsFalse(text.Contains("王小明", StringComparison.Ordinal), text);
        StringAssert.Contains(text, "***567");
    }

    [TestMethod]
    public void AnAccountWithoutABrokerHasNoFullAccount()
    {
        CapitalAccount account = CapitalAccountParser.ParseAccounts(["TF,,群益,1234567"]).Single();

        Assert.IsNull(account.BrokerId);
        Assert.IsNull(account.FullAccount);
    }

    // 依據:手冊 4-2-i 的 41 欄(0 起算);第 25 欄幣別、第 40 欄帳號。
    [TestMethod]
    public void FutureRightsReadTheManualsColumnsAsDecimal()
    {
        string[] fields = [.. Enumerable.Range(0, 41).Select(i => (i * 10 + 0.5m).ToString(System.Globalization.CultureInfo.InvariantCulture))];
        fields[1] = "-1234.75";
        fields[6] = "abc";
        fields[23] = "N";
        fields[25] = "TWD";
        fields[40] = "1234567";

        CapitalFutureRights rights = CapitalAccountParser.ParseFutureRights([string.Join(',', fields), "##"]).Single();

        Assert.AreEqual(0.5m, rights.AccountBalance);
        Assert.AreEqual(-1234.75m, rights.FloatingProfitLoss);
        Assert.IsNull(rights.Equity, "unreadable is null, never zero");
        Assert.AreEqual(130.5m, rights.InitialMargin);
        Assert.AreEqual(220.5m, rights.PreviousBalance);
        Assert.AreEqual("N", rights.OptionCombinationMarginFlag);
        Assert.AreEqual("TWD", rights.Currency);
        Assert.AreEqual(310.5m, rights.AvailableBalance);
        Assert.AreEqual(380.5m, rights.AdditionalMargin);
        Assert.AreEqual("1234567", rights.AccountNo);
        Assert.HasCount(41, rights.RawFields);
        Assert.IsFalse(rights.ToString().Contains("1234567", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AShortRightsRowLeavesTheMissingColumnsNull()
    {
        CapitalFutureRights rights = CapitalAccountParser.ParseFutureRights(["1000,2000"]).Single();

        Assert.AreEqual(1000m, rights.AccountBalance);
        Assert.IsNull(rights.Currency);
        Assert.IsNull(rights.AccountNo);
    }

    // 依據:手冊 4-2-d「(新)期貨未平倉 GW 格式 1」;查無資料回 001,查無資料,帳號。
    [TestMethod]
    public void OpenInterestFormatOneIsNamedAndNoDataIsEmpty()
    {
        IReadOnlyList<CapitalOpenInterest> rows = CapitalAccountParser.ParseOpenInterest(
            ["TM,F0200001234567,TXFK6,B,2,0,39786.50,50,2,A123456789,TXFK6", "##"], 1);

        CapitalOpenInterest row = rows.Single();
        Assert.AreEqual("TXFK6", row.Product);
        Assert.AreEqual("B", row.Side);
        Assert.AreEqual(2L, row.Quantity);
        Assert.AreEqual(0L, row.DayTradeQuantity);
        Assert.AreEqual(39786.50m, row.AverageCost);
        Assert.AreEqual("TXFK6", row.OrderCode);
        Assert.IsFalse(row.ToString().Contains("1234567", StringComparison.Ordinal));

        Assert.IsEmpty(CapitalAccountParser.ParseOpenInterest(["001,查無資料,F0200001234567"], 1));

        CapitalOpenInterest other = CapitalAccountParser.ParseOpenInterest(["TM,x,y"], 2).Single();
        Assert.AreEqual(2, other.Format);
        Assert.IsNull(other.Product, "formats other than 1 only carry the raw fields");
        Assert.HasCount(3, other.RawFields);
    }

    // 依據:手冊 4-2-k:第一筆成功「000,筆數」、失敗「代碼;訊息」;資料列 31 欄(期貨商代碼、IB 代號、帳號、幣別…)。
    [TestMethod]
    public void OverseaRightsSkipTheResultRowOrFailOnIt()
    {
        string[] fields = [.. Enumerable.Range(0, 31).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))];
        fields[0] = "F020000";
        fields[1] = "000";
        fields[2] = "7654321";
        fields[3] = "USD";

        CapitalOverseaFutureRights rights = CapitalAccountParser.ParseOverseaFutureRights(["000,1", string.Join(',', fields), "##"], NoMask).GetValueOrThrow().Single();
        Assert.AreEqual("F020000", rights.BrokerId);
        Assert.AreEqual("000", rights.IbCode);
        Assert.AreEqual("7654321", rights.AccountNo);
        Assert.AreEqual("USD", rights.Currency);
        Assert.AreEqual(4m, rights.RiskIndicator);
        Assert.AreEqual(14m, rights.Equity);
        Assert.AreEqual(15m, rights.UnrealizedProfitLoss);
        Assert.AreEqual(16m, rights.FloatingProfitLoss);
        Assert.AreEqual(22m, rights.ExchangeRate);
        Assert.AreEqual(30m, rights.VariableMargin);
        Assert.IsFalse(rights.ToString().Contains("7654321", StringComparison.Ordinal));

        Error error = CapitalAccountParser.ParseOverseaFutureRights(["E01;帳號 7654321 不存在"], text => text.Replace("7654321", "<ACCT>", StringComparison.Ordinal)).Failure();
        Assert.AreEqual(CapitalErrorCodes.AccountQueryFailed, error.Code);
        StringAssert.Contains(error.Message, "<ACCT>");
        Assert.IsFalse(error.Message.Contains("7654321", StringComparison.Ordinal));

        Assert.HasCount(1, CapitalAccountParser.ParseOverseaFutureRights([string.Join(',', fields)], NoMask).GetValueOrThrow(), "no result row: parsed as data");
        Assert.IsEmpty(CapitalAccountParser.ParseOverseaFutureRights([], NoMask).GetValueOrThrow());
    }

    // 依據:手冊 4-2-q:@@ 狀態列、彙總 21 欄 / 明細 17 欄、選擇權另有履約價與 C/P、價格可能含分數。
    [TestMethod]
    public void OverseaOpenInterestReadsSummaryAndDetailRows()
    {
        const string summary = "7654321,CME,芝加哥商品交易所,NQ 202612,NQ,202612,小那斯達克 202612,B,1,26000.25,26100.00,2000.00,0,0,0,0,0,379 02/4,0,17600.00,1,25000,C";
        CapitalOverseaOpenInterest row = CapitalAccountParser.ParseOverseaOpenInterest(["@@,0,", summary, "##"], CapitalOverseaOpenInterestFormat.Summary, NoMask)
            .GetValueOrThrow().Single();

        Assert.AreEqual("CME", row.ExchangeCode);
        Assert.AreEqual("NQ", row.Product);
        Assert.AreEqual(26000.25m, row.AveragePrice);
        Assert.IsNull(row.PreviousSettlement, "a fractional price is not guessed");
        Assert.AreEqual("379 02/4", row.PreviousSettlementText);
        Assert.IsTrue(row.IsOption);
        Assert.AreEqual(25000m, row.StrikePrice);
        Assert.AreEqual("C", row.CallPut);
        Assert.IsNull(row.TradeDate, "detail only");

        const string detail = "7654321,20261105,CME,芝加哥商品交易所,NQ,202612,小那斯達克 202612,B,1,1,26000.25,26100.00,2000.00,AB12345,N,17600.00,0";
        CapitalOverseaOpenInterest line = CapitalAccountParser.ParseOverseaOpenInterest([detail, "001,查無資料,x"], CapitalOverseaOpenInterestFormat.Detail, NoMask)
            .GetValueOrThrow().Single();

        Assert.AreEqual(new DateOnly(2026, 11, 5), line.TradeDate);
        Assert.AreEqual(26000.25m, line.TradePrice);
        Assert.AreEqual("AB12345", line.BookNo);
        Assert.IsFalse(line.IsOption);
        Assert.IsNull(line.StrikePrice);
        Assert.IsFalse(line.ToString().Contains("7654321", StringComparison.Ordinal));

        Error error = CapitalAccountParser.ParseOverseaOpenInterest(["@@,5,查詢失敗"], CapitalOverseaOpenInterestFormat.Summary, NoMask).Failure();
        Assert.AreEqual(CapitalErrorCodes.AccountQueryFailed, error.Code);
        Assert.IsTrue(error.TryGetData(CapitalErrorDataKeys.Reason, out string? reason));
        Assert.AreEqual("5", reason);
    }

    // ── 2026-11-09 probe-full 實測樣本(account-*.txt 原文照抄;帳號、身分證、戶名是探針的遮罩字樣,中文是群益原樣的亂碼)──

    private const string MeasuredDomesticAccount = "TF,F020000,000,<ACCT1>,<ID>,<NAME>,9";
    private const string MeasuredOverseasAccount = "OF,F020000,000,<ACCT1>,<ID>,<NAME>,9";
    private static readonly string MeasuredEnd = "##" + new string(',', 50);

    // 「查無資料」的亂碼(?dμL﹐eRA)、「查詢成功」的亂碼(?d﹐s|‥￥\)、「NTD(基幣)」的亂碼(NTD(°o1o))
    private const string MeasuredNoData = "?dμL﹐eRA";
    private const string MeasuredQuerySucceeded = "?d﹐s|‥￥\\";
    private const string MeasuredBaseCurrency = "NTD(°o1o)";

    [TestMethod]
    public void MeasuredAccountsAreTfAndOfWithAnExtraField()
    {
        IReadOnlyList<CapitalAccount> accounts = CapitalAccountParser.ParseAccounts([MeasuredDomesticAccount, MeasuredOverseasAccount]);

        Assert.HasCount(2, accounts);
        Assert.AreEqual(CapitalAccountKind.DomesticFutures, accounts[0].Kind);
        Assert.AreEqual(CapitalAccountKind.OverseasFutures, accounts[1].Kind);
        Assert.AreEqual("F020000", accounts[0].BrokerId, "the broker code is in the second field; no swap");
        Assert.AreEqual("000", accounts[0].BrokerName);
        Assert.AreEqual("<ACCT1>", accounts[0].AccountNo);
        Assert.AreEqual("F020000<ACCT1>", accounts[0].FullAccount, "the measured rights rows carry this full account");
        Assert.HasCount(7, accounts[0].RawFields, "the seventh field is not in the manual and stays raw");
    }

    [TestMethod]
    public void MeasuredDomesticRightsHaveABaseCurrencyRowFirst()
    {
        IReadOnlyList<CapitalFutureRights> rows = CapitalAccountParser.ParseFutureRights(
        [
            "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,Y,*********," + MeasuredBaseCurrency + ",0,0,0,0,0,0,0,0,100,0,0,0,0,<ID>,F020000<ACCT1>",
            "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,,*********,NTD,0,0,0,0,0,0,0,0,*********,0,0,0,0,<ID>,F020000<ACCT1>",
            MeasuredEnd,
        ]);

        Assert.HasCount(2, rows);
        Assert.AreEqual("NTD(基幣)", rows[0].Currency, "the garbled base-currency label is repaired");
        Assert.AreEqual("NTD", rows[1].Currency);
        Assert.AreEqual(0m, rows[0].Equity);
        Assert.IsNull(rows[0].MaintenanceRatio, "********* is not a number");
        Assert.AreEqual(100m, rows[0].RiskIndicator);
        Assert.IsNull(rows[1].RiskIndicator);
        Assert.AreEqual("Y", rows[0].OptionCombinationMarginFlag);
        Assert.AreEqual("F020000<ACCT1>", rows[0].AccountNo);
        Assert.HasCount(41, rows[0].RawFields);
        Assert.AreEqual(MeasuredBaseCurrency, rows[0].RawFields[25], "RawFields keep Capital's text as it came");
    }

    [TestMethod]
    public void MeasuredNoDataRepliesAreEmptyLists()
    {
        Assert.IsEmpty(CapitalAccountParser.ParseOpenInterest(["001," + MeasuredNoData + ",F020000<ACCT1>", MeasuredEnd], 1));

        IReadOnlyList<CapitalOverseaOpenInterest> overseas = CapitalAccountParser
            .ParseOverseaOpenInterest(["@@,001," + MeasuredNoData + ",F020000<ACCT1>", MeasuredEnd], CapitalOverseaOpenInterestFormat.Summary, NoMask)
            .GetValueOrThrow();
        Assert.IsEmpty(overseas, "@@,001 is no data, not a failure");

        Assert.IsEmpty(CapitalAccountParser.ParseOrderReports(["M003"], CapitalOrderReportFormat.All, NoMask).GetValueOrThrow());
        Assert.IsEmpty(CapitalAccountParser.ParseFulfillReports(["M003"], CapitalFulfillReportFormat.Full, NoMask).GetValueOrThrow());
    }

    [TestMethod]
    public void MeasuredOverseaRightsHave31FieldsAndAResultMessage()
    {
        CapitalOverseaFutureRights rights = CapitalAccountParser.ParseOverseaFutureRights(
            [
                "000,1," + MeasuredQuerySucceeded,
                "F020000,000,<ACCT1>," + MeasuredBaseCurrency + ",,,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0",
                MeasuredEnd,
            ],
            NoMask).GetValueOrThrow().Single();

        Assert.AreEqual("F020000", rights.BrokerId);
        Assert.AreEqual("000", rights.IbCode);
        Assert.AreEqual("<ACCT1>", rights.AccountNo);
        Assert.AreEqual("NTD(基幣)", rights.Currency);
        Assert.IsNull(rights.RiskIndicator, "blank without positions");
        Assert.IsNull(rights.FullMaintenanceRatio);
        Assert.AreEqual(1m, rights.ExchangeRate, "the NTD reference rate anchors the 31-field order");
        Assert.AreEqual(0m, rights.VariableMargin);
        Assert.HasCount(31, rights.RawFields);
    }

    [TestMethod]
    public void FailureMessagesAreRepairedBeforeMasking()
    {
        Error error = CapitalAccountParser.ParseOverseaOpenInterest(["@@,999," + MeasuredNoData + " 7654321"], CapitalOverseaOpenInterestFormat.Summary, text => text.Replace("7654321", "<ACCT>", StringComparison.Ordinal)).Failure();

        StringAssert.Contains(error.Message, "查無資料");
        StringAssert.Contains(error.Message, "<ACCT>");
    }

    // 依據:手冊 5-4-4 格式 1(70 欄,1 起算)、4-2-97 的 M003 / M999。
    [TestMethod]
    public void OrderReportsFollowTheManualsNumbering()
    {
        string[] fields = new string[70];
        Array.Fill(fields, string.Empty);
        Set(fields, (1, "TF"), (2, "FUT"), (3, "TAIFEX"), (4, "F020000"), (6, "1234567"), (8, "k0001"), (9, "0000000000001"), (11, "2"), (12, "20261106"), (13, "84601"));
        Set(fields, (14, "20261106"), (16, "TXFK6"), (23, "B"), (26, "0"), (27, "2"), (28, "39786"), (30, "2"), (31, "2"), (32, "2"), (33, "0"), (34, "N"), (35, "A"), (36, "Y"));
        Set(fields, (41, "C"), (50, "OK"));

        CapitalOrderReport report = CapitalAccountParser.ParseOrderReports([string.Join(',', fields)], CapitalOrderReportFormat.All, NoMask).GetValueOrThrow().Single();

        Assert.AreEqual("TF", report.Market);
        Assert.AreEqual("k0001", report.BookNo);
        Assert.AreEqual("2", report.Status);
        Assert.AreEqual(new DateOnly(2026, 11, 6), report.OrderDate);
        Assert.AreEqual(new TimeOnly(8, 46, 1), report.OrderTime, "leading zeros may be missing");
        Assert.AreEqual("TXFK6", report.Symbol);
        Assert.AreEqual(39786m, report.Price);
        Assert.AreEqual(2L, report.FilledQuantity);
        Assert.AreEqual(0L, report.RemainingQuantity);
        Assert.IsTrue(report.IsErrorReport);
        Assert.AreEqual("C", report.CallPut);
        Assert.AreEqual("OK", report.ExchangeMessage);
        Assert.IsFalse(report.ToString().Contains("1234567", StringComparison.Ordinal));

        Assert.IsEmpty(CapitalAccountParser.ParseOrderReports(["M003: 查無資料"], CapitalOrderReportFormat.All, NoMask).GetValueOrThrow());
        Assert.AreEqual(CapitalErrorCodes.AccountQueryFailed, CapitalAccountParser.ParseOrderReports(["M999: 查詢錯誤"], CapitalOrderReportFormat.All, NoMask).Failure().Code);

        CapitalOrderReport merged = CapitalAccountParser.ParseOrderReports(["TF,FUT,TAIFEX"], CapitalOrderReportFormat.MergedByPrice, NoMask).GetValueOrThrow().Single();
        Assert.IsNull(merged.Market, "formats 7 and 8 only carry the raw fields");
    }

    // 依據:手冊 5-4-5 格式 1(52 欄,1 起算)。
    [TestMethod]
    public void FulfillReportsFollowTheManualsNumbering()
    {
        string[] fields = new string[52];
        Array.Fill(fields, string.Empty);
        Set(fields, (1, "TF"), (6, "1234567"), (8, "k0001"), (9, "00001"), (10, "20261106"), (11, "084602123"), (12, "20261106"), (13, "TXFK6"), (14, "台指期11"));
        Set(fields, (23, "S"), (27, "39786.5"), (28, "3"), (29, "N"), (30, "B"), (38, "20261106"), (39, "084601"));

        CapitalFulfillReport fill = CapitalAccountParser.ParseFulfillReports([string.Join(',', fields), string.Empty], CapitalFulfillReportFormat.NextSession, NoMask).GetValueOrThrow().Single();

        Assert.AreEqual("00001", fill.FillSequenceNo);
        Assert.AreEqual(new TimeOnly(8, 46, 2, 123), fill.FillTime, "hhmmssfff is read too");
        Assert.AreEqual("台指期11", fill.Name);
        Assert.AreEqual(39786.5m, fill.Price);
        Assert.AreEqual(3L, fill.Quantity);
        Assert.AreEqual("B", fill.SessionFlag);
        Assert.AreEqual(new TimeOnly(8, 46, 1), fill.OrderTime);
        Assert.IsFalse(fill.ToString().Contains("1234567", StringComparison.Ordinal));

        CapitalFulfillReport merged = CapitalAccountParser.ParseFulfillReports(["TF,x"], CapitalFulfillReportFormat.MergedByProduct, NoMask).GetValueOrThrow().Single();
        Assert.IsNull(merged.Market);
        Assert.AreEqual(CapitalErrorCodes.AccountQueryFailed, CapitalAccountParser.ParseFulfillReports(["M999"], CapitalFulfillReportFormat.Full, NoMask).Failure().Code);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("abc")]
    [DataRow("250000")]
    [DataRow("1234567890")]
    public void UnreadableTimesAreNull(string text)
    {
        Assert.IsNull(CapitalAccountParser.Time([text], 0));
    }

    [TestMethod]
    public void AccountMaskKeepsOnlyTheLastThree()
    {
        Assert.AreEqual(string.Empty, CapitalAccountText.Mask(null));
        Assert.AreEqual("***", CapitalAccountText.Mask("123"));
        Assert.AreEqual("***567", CapitalAccountText.Mask("1234567"));
    }

    private static void Set(string[] fields, params (int Number, string Value)[] values)
    {
        foreach ((int number, string value) in values)
        {
            fields[number - 1] = value;
        }
    }
}
