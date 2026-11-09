using Ozakboy.TradeKit.Capital.Host.Probe;

namespace Ozakboy.TradeKit.Capital.Host.Tests;

/// <summary>probe-full 的遮罩、參數、帳號解析與商品挑選(全部是假資料,不碰 SKCOM)。</summary>
[TestClass]
public sealed class FullProbeTests
{
    private const string LoginId = "A123456789";
    private const string AccountData = "TF,F020000,台北分公司,1234567,A123456789,王小明";

    [TestMethod]
    public void Redactor_ReplacesIdCaseInsensitively_AccountsInOrder_AndTheName()
    {
        ProbeRedactor redactor = new();
        redactor.AddLoginId(LoginId);

        string? first = redactor.AddAccount("1234567");
        string? second = redactor.AddAccount("7654321");
        string? again = redactor.AddAccount(" 1234567 ");
        redactor.AddName("王小明");

        Assert.AreEqual("<ACCT1>", first);
        Assert.AreEqual("<ACCT2>", second);
        Assert.AreEqual("<ACCT1>", again);
        Assert.AreEqual(2, redactor.AccountCount);

        string text = redactor.Redact("a123456789,F0200001234567,7654321,王小明,權益數 1234567.00");

        Assert.AreEqual("<ID>,F020000<ACCT1>,<ACCT2>,<NAME>,權益數 <ACCT1>.00", text);
    }

    [TestMethod]
    public void Redactor_ReplacesLongerAccountsFirst()
    {
        ProbeRedactor redactor = new();
        redactor.AddAccount("123456");
        redactor.AddAccount("1234567");

        Assert.AreEqual("<ACCT2> <ACCT1>", redactor.Redact("1234567 123456"));
    }

    [TestMethod]
    public void Redactor_IgnoresEmptyValues()
    {
        ProbeRedactor redactor = new();
        redactor.AddLoginId("  ");
        redactor.AddName(null);

        Assert.IsNull(redactor.AddAccount(""));
        Assert.AreEqual("沒有任何東西要換", redactor.Redact("沒有任何東西要換"));
        Assert.AreEqual(string.Empty, redactor.Redact(null));
    }

    [TestMethod]
    public void Shape_KeepsStructureButNoContent()
    {
        Assert.AreEqual("AA,A999999,字字字字字,9999999;", ProbeRedactor.Shape("TF,F020000,台北分公司,1234567;"));
        Assert.AreEqual(string.Empty, ProbeRedactor.Shape(null));
    }

    [TestMethod]
    public void AccountSafeRaw_UnparsableOrExtraFields_OnlyKeepTheShape()
    {
        Assert.AreEqual("UNPARSED 9999999|A999999", ProbeAccount.ToSafeRaw("1234567|F020000"));
        Assert.AreEqual(AccountData, ProbeAccount.ToSafeRaw(AccountData));
        Assert.AreEqual(AccountData + ",9999-999-999", ProbeAccount.ToSafeRaw(AccountData + ",0912-345-678"));
        Assert.AreEqual("##,,;", ProbeAccount.ToSafeRaw("##,,;"));
    }

    [TestMethod]
    public void ProbeLog_WithRedactor_NeverWritesTheIdAccountOrName()
    {
        string directory = Path.Combine(Path.GetTempPath(), "probe-full-test-" + Guid.NewGuid().ToString("N"));

        try
        {
            ProbeRedactor redactor = new();
            redactor.AddLoginId(LoginId);
            ProbeAccount account = ProbeAccount.Parse(AccountData)!;
            redactor.AddAccount(account.AccountNo);
            redactor.AddName(account.OwnerName);

            string[] nested = ["F0200001234567"];

            using (ProbeLog log = new(directory, redactor))
            {
                log.Event("on_account", new Dictionary<string, object?> { ["data"] = AccountData, ["nested"] = nested });
                log.Raw("account-OnAccount", "get_user_account\t" + AccountData);
            }

            string events = File.ReadAllText(Path.Combine(directory, "probe.jsonl"));
            string raw = File.ReadAllText(Path.Combine(directory, "account-OnAccount.txt"));

            foreach (string text in new[] { events, raw })
            {
                Assert.IsFalse(text.Contains("123456789", StringComparison.Ordinal), text);
                Assert.IsFalse(text.Contains("1234567", StringComparison.Ordinal), text);
                Assert.IsFalse(text.Contains("王小明", StringComparison.Ordinal), text);
                StringAssert.Contains(text, "<ACCT1>");
                StringAssert.Contains(text, "<NAME>");
                StringAssert.Contains(text, "<ID>");
            }

            StringAssert.Contains(events, "F020000<ACCT1>");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Options_Defaults()
    {
        FullProbeOptions? options = FullProbeOptions.Parse(["--out", "out-dir"], out string error);

        Assert.IsNotNull(options, error);
        Assert.AreEqual(Path.GetFullPath("out-dir"), options.OutputDirectory);
        Assert.AreEqual(180, options.Seconds);
        Assert.IsFalse(options.AccountsOnly);
    }

    [TestMethod]
    public void Options_AccountsOnlyAndSeconds_InAnyOrder()
    {
        FullProbeOptions? options = FullProbeOptions.Parse(["--accounts-only", "--seconds", "600", "--out", "x"], out string error);

        Assert.IsNotNull(options, error);
        Assert.IsTrue(options.AccountsOnly);
        Assert.AreEqual(600, options.Seconds);
    }

    [TestMethod]
    [DataRow("", DisplayName = "沒有 --out")]
    [DataRow("--out", DisplayName = "--out 沒有值")]
    [DataRow("--out --accounts-only", DisplayName = "--out 後面接旗標")]
    [DataRow("--out x --seconds 0", DisplayName = "秒數 0")]
    [DataRow("--out x --seconds 3601", DisplayName = "秒數超過上限")]
    [DataRow("--out x --seconds -5", DisplayName = "秒數負的")]
    [DataRow("--out x --symbol FX,SUSDTWD", DisplayName = "不認得的參數")]
    public void Options_Invalid_ReturnNullWithAMessage(string commandLine)
    {
        string[] args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        FullProbeOptions? options = FullProbeOptions.Parse(args, out string error);

        Assert.IsNull(options);
        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
    }

    [TestMethod]
    public void Account_ParsesTheManualLayout()
    {
        ProbeAccount? account = ProbeAccount.Parse(AccountData);

        Assert.IsNotNull(account);
        Assert.AreEqual("TF", account.Market);
        Assert.AreEqual("F020000", account.BrokerId);
        Assert.AreEqual("台北分公司", account.BrokerName);
        Assert.AreEqual("1234567", account.AccountNo);
        Assert.AreEqual("F0200001234567", account.FullAccount);
        Assert.AreEqual("王小明", account.OwnerName);
        Assert.AreEqual(ProbeAccountKind.DomesticFutures, account.Kind);
        Assert.IsFalse(account.BrokerFieldSwapped);
    }

    [TestMethod]
    public void Account_SwappedBrokerColumns_UseTheOneThatLooksLikeACode()
    {
        ProbeAccount? account = ProbeAccount.Parse("OF,台北分公司,F020000,7654321,A123456789,王小明;");

        Assert.IsNotNull(account);
        Assert.AreEqual("F0200007654321", account.FullAccount);
        Assert.AreEqual(ProbeAccountKind.OverseasFutures, account.Kind);
        Assert.IsTrue(account.BrokerFieldSwapped);
    }

    [TestMethod]
    [DataRow("TS,9A95,證券,1234567,A123456789,王小明", "Other")]
    [DataRow("TO,F020000,期貨,1234567", "DomesticFutures")]
    [DataRow("OO,F020000,期貨,1234567", "OverseasFutures")]
    [DataRow("XX,F020000,期貨,1234567", "Other")]
    public void Account_Kind_FollowsTheMarket(string data, string kind)
    {
        // ProbeAccountKind 是 internal,不能當公開測試方法的參數型別,改傳名稱
        Assert.AreEqual(Enum.Parse<ProbeAccountKind>(kind), ProbeAccount.Parse(data)!.Kind);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("##")]
    [DataRow("TF,F020000,期貨")]
    public void Account_TooShortOrEndMarker_IsNull(string data)
    {
        Assert.IsNull(ProbeAccount.Parse(data));
    }

    [TestMethod]
    public void StockList_SplitsEntriesAndGroups_AndSkipsTheEndMarker()
    {
        IReadOnlyList<StockListEntry> entries = ProbeSymbolPicker.ParseStockList(
            ["TXO28500K6,臺指選擇權,20261118,TXO28500K6,0.1,NTD;TXO28600K6,臺指選擇權,20261118,TXO28600K6,0.1,NTD;\nTXO28500W6,臺指選擇權,20261118,TXO28500W6,0.1,NTD;", "##,,;"]);

        Assert.HasCount(3, entries);
        Assert.AreEqual("TXO28600K6", entries[1].Code);
        Assert.AreEqual("20261118", entries[1].LastTradeDay);
    }

    [TestMethod]
    public void TaiexCall_PicksNearMonthMonthlyCallClosestToTheUnderlying()
    {
        IReadOnlyList<StockListEntry> entries =
        [
            new("A1", "臺指選擇權", "20261118", "TXO28500K6"),
            new("A2", "臺指選擇權", "20261118", "TXO28600K6"),
            new("A3", "臺指選擇權", "20261118", "TXO28600W6"),
            new("A4", "臺指選擇權", "20261216", "TXO28600L6"),
            new("A5", "臺指選擇權", "20261111", "TX128600K6"),
            new("A6", "臺指選擇權", "20261021", "TXO28600J6"),
        ];

        StockListEntry? chosen = ProbeSymbolPicker.PickTaiexCall(entries, 28590m, 20261108, out string reason);

        Assert.AreEqual("A2", chosen?.Code, reason);
    }

    [TestMethod]
    public void TaiexCall_WithoutUnderlying_TakesTheMiddleStrike()
    {
        IReadOnlyList<StockListEntry> entries =
        [
            new("A1", "臺指選擇權", "20261118", "TXO28400K6"),
            new("A2", "臺指選擇權", "20261118", "TXO28500K6"),
            new("A3", "臺指選擇權", "20261118", "TXO28600K6"),
        ];

        Assert.AreEqual("A2", ProbeSymbolPicker.PickTaiexCall(entries, null, 20261108, out _)?.Code);
    }

    [TestMethod]
    public void TaiexCall_UnknownFormat_FallsBackToTheFirstTaiexThenTheFirstEntry()
    {
        IReadOnlyList<StockListEntry> taiex = [new("X1", "電子選擇權", "0", "TEO"), new("X2", "台指選擇權", "0", "?")];
        IReadOnlyList<StockListEntry> other = [new("Y1", "電子選擇權", "0", "TEO")];

        Assert.AreEqual("X2", ProbeSymbolPicker.PickTaiexCall(taiex, 28000m, 20261108, out _)?.Code);
        Assert.AreEqual("Y1", ProbeSymbolPicker.PickTaiexCall(other, 28000m, 20261108, out _)?.Code);
        Assert.IsNull(ProbeSymbolPicker.PickTaiexCall([], 28000m, 20261108, out _));
    }

    // 2026-11-09 實測:清單裡每檔都有一對代碼,AM 結尾的只有日盤;夜盤挑到 TXO41500K6AM 只收到日盤回補。樣本照實測清單的格式。
    [TestMethod]
    public void TaiexCall_PrefersTheFullSessionCodeOverTheDayOnlyAmCode()
    {
        IReadOnlyList<StockListEntry> entries =
        [
            new("TXO41500K6AM", "台選11C", "20261118", "TXO41500K6"),
            new("TXO41500K6", "台選11C", "20261118", "TXO41500K6"),
            new("TXO41600K6AM", "台選11C", "20261118", "TXO41600K6"),
            new("TXO41600K6", "台選11C", "20261118", "TXO41600K6"),
            new("TXO41500W6AM", "台選11P", "20261118", "TXO41500W6"),
        ];

        StockListEntry? chosen = ProbeSymbolPicker.PickTaiexCall(entries, 41478m, 20261109, out string reason);

        Assert.AreEqual("TXO41500K6", chosen?.Code, reason);
        StringAssert.Contains(reason, "全盤代碼");
    }

    [TestMethod]
    public void TaiexCall_TakesAnAmCodeOnlyWhenNothingElseExists()
    {
        IReadOnlyList<StockListEntry> entries = [new("TXO41500K6AM", "台選11C", "20261118", "TXO41500K6")];

        Assert.AreEqual("TXO41500K6AM", ProbeSymbolPicker.PickTaiexCall(entries, 41478m, 20261109, out _)?.Code);
        Assert.IsTrue(ProbeSymbolPicker.IsDaySessionCode("TX00AM"));
        Assert.IsFalse(ProbeSymbolPicker.IsDaySessionCode("TX00"));
        Assert.IsFalse(ProbeSymbolPicker.IsDaySessionCode("AM"));
    }

    // 2026-11-09 實測:照清單順序挑到 ES 履約價 100 的深度價內 / 價外,三分鐘沒有成交;有小那斯達克期貨價格時改挑最近一期最接近它的買權與賣權。
    // 列的格式照實測 oo-products.txt(交易所,交易所名稱,代碼,名稱,最後交易日)。
    [TestMethod]
    public void OverseasOptions_WithTheNasdaqPrice_PickTheNearestExpiryAtTheMoney()
    {
        string[] lines =
        [
            "CME,芝加哥商品交易所,ES00100X6,小SP季選2612P 100 P,20261218",
            "CME,芝加哥商品交易所,NQ02500X6,小那季選2612P 2500 P,20261218",
            "CME,芝加哥商品交易所,NQ27200X6,小那季選2612P 27200 P,20261218",
            "CME,芝加哥商品交易所,NQ27200L6,小那季選2612C 27200 C,20261218",
            "CME,芝加哥商品交易所,NQ27250L6,小那季選2612C 27250 C,20261218",
            "CME,芝加哥商品交易所,NQ27200H7,小那季選2703C 27200 C,20270319",
            "CME,芝加哥商品交易所,NQ27200W6,小那季選2611P 27200 P,20261020",
            "##,##,##,##,##",
        ];

        IReadOnlyList<(string Request, string Line)> chosen = ProbeSymbolPicker.PickOverseasOptions(lines, 2, 27218m, 20261109);

        string[] expected = ["CME,NQ27200L6", "CME,NQ27200X6"];
        CollectionAssert.AreEqual(expected, chosen.Select(item => item.Request).ToArray(), "an expired series is skipped");

        string[] fallback = ["CME,ES00100X6", "CME,NQ02500X6"];
        CollectionAssert.AreEqual(fallback, ProbeSymbolPicker.PickOverseasOptions(lines, 2).Select(item => item.Request).ToArray(), "without a price the list order is kept");
    }

    [TestMethod]
    [DataRow("小那季選2612P 27200 P", "27200")]
    [DataRow("十年債券2702P 12350 P", "12350")]
    [DataRow("玉米選擇權", null)]
    [DataRow("x 12 Q", null)]
    public void TheStrikeIsReadFromTheEndOfTheName(string name, string? expected)
    {
        Assert.AreEqual(expected is null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), ProbeSymbolPicker.StrikeFromName(name));
    }

    [TestMethod]
    public void OverseasOptions_PreferCmeNasdaqOrSp_ThenTheFirstRows()
    {
        IReadOnlyList<(string Request, string Line)> preferred = ProbeSymbolPicker.PickOverseasOptions(
            ["CBOT,芝加哥期貨交易所,OZC,玉米選擇權", "CME,芝商所,ES,迷你S&P選擇權", "CME,芝商所,NQ,小那斯達克選擇權", "##"],
            2);
        IReadOnlyList<(string Request, string Line)> fallback = ProbeSymbolPicker.PickOverseasOptions(["CBOT,芝加哥期貨交易所,OZC,玉米", "NYM,紐約商業交易所,LO,原油", "x"], 2);

        string[] expectedPreferred = ["CME,ES", "CME,NQ"];
        string[] expectedFallback = ["CBOT,OZC", "NYM,LO"];

        CollectionAssert.AreEqual(expectedPreferred, preferred.Select(item => item.Request).ToArray());
        CollectionAssert.AreEqual(expectedFallback, fallback.Select(item => item.Request).ToArray());
    }
}
