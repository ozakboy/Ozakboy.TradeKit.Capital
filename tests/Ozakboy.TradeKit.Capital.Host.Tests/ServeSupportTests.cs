namespace Ozakboy.TradeKit.Capital.Host.Tests;

[TestClass]
public sealed class ServeSupportTests
{
    [TestMethod]
    public void ReadAndClear_ReadsUpperCasesAndRemovesAllThreeVariables()
    {
        Environment.SetEnvironmentVariable(ServeEnvironmentNames.LoginId, " a123456789 ");
        Environment.SetEnvironmentVariable(ServeEnvironmentNames.Password, "Pw-Fake-2026");
        Environment.SetEnvironmentVariable(ServeEnvironmentNames.LogDirectory, @"C:\logs");

        ServeLogin login = ServeLogin.ReadAndClear();

        Assert.AreEqual("A123456789", login.LoginId);
        Assert.AreEqual("Pw-Fake-2026", login.Password);
        Assert.AreEqual(@"C:\logs", login.LogDirectory);
        Assert.IsTrue(login.IsComplete);
        Assert.IsNull(Environment.GetEnvironmentVariable(ServeEnvironmentNames.LoginId));
        Assert.IsNull(Environment.GetEnvironmentVariable(ServeEnvironmentNames.Password));
        Assert.IsNull(Environment.GetEnvironmentVariable(ServeEnvironmentNames.LogDirectory));
        Assert.IsFalse(login.ToString().Contains("A123456789", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReadAndClear_MissingValues_AreIncomplete()
    {
        Environment.SetEnvironmentVariable(ServeEnvironmentNames.LoginId, null);
        Environment.SetEnvironmentVariable(ServeEnvironmentNames.Password, "x");

        ServeLogin login = ServeLogin.ReadAndClear();

        Assert.IsFalse(login.IsComplete);
        Assert.IsNull(login.LogDirectory);
        Assert.IsNull(Environment.GetEnvironmentVariable(ServeEnvironmentNames.Password));
    }

    [TestMethod]
    public void HostLog_ReplacesTheIdCaseInsensitively_AndThePassword()
    {
        using StringWriter writer = new();
        HostLog log = new(writer, TimeProvider.System);
        log.AddSecret("A123456789", "<ID>");
        log.AddSecret("Pw-Fake-2026", "<PASSWORD>");

        log.Write("user a123456789 / A123456789 used Pw-Fake-2026");

        string text = writer.ToString();
        Assert.IsFalse(text.Contains("123456789", StringComparison.Ordinal), text);
        Assert.IsFalse(text.Contains("Pw-Fake", StringComparison.Ordinal), text);
        StringAssert.Contains(text, "user <ID> / <ID> used <PASSWORD>");
    }

    [TestMethod]
    public void ProductRows_KeepTheListOrder_AndTolerateCommasInNames()
    {
        IReadOnlyList<ProductRow> rows = ProductRows.Merge(
            ["CME,芝加哥商品交易所,NQ0000,小那熱,2612,20261218,20261218", "FX,國際外匯,SUSDTWD,新台幣,99991231,0", "bad,row"],
            ["FX,國際外匯,SUSDTWD,新台幣,--,--,99991231,0,2", "CME,芝加哥商品交易所,NQ0000,小那熱,2612,CME,NQ_202612,20261218,20261218,5"],
            out int skipped);

        Assert.AreEqual(1, skipped);
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual("小那熱,2612", rows[0].Name);
        Assert.AreEqual("20261218", rows[0].LastTradeDate);
        Assert.AreEqual(5, rows[0].Kind);
        Assert.AreEqual("NQ_202612", rows[0].OrderCode);
        Assert.AreEqual("SUSDTWD", rows[1].Code);
        Assert.AreEqual(2, rows[1].Kind);
    }

    [TestMethod]
    [DataRow("FX,SUSDTWD", true)]
    [DataRow("Eurex,FDAX0000", true)]
    [DataRow("FX", false)]
    [DataRow("FX,", false)]
    [DataRow(",SUSDTWD", false)]
    [DataRow("FX,A,B", false)]
    [DataRow("FX,A#B", false)]
    [DataRow("FX, A", false)]
    public void IsValidSymbol_RequiresExactlyExchangeCommaCode(string symbol, bool expected)
    {
        Assert.AreEqual(expected, ServeHost.IsValidSymbol(symbol));
    }

    [TestMethod]
    public void ReconnectDelay_RepeatsTheLastStep()
    {
        ServeTimings timings = new();

        Assert.AreEqual(TimeSpan.FromSeconds(5), timings.ReconnectDelay(0));
        Assert.AreEqual(TimeSpan.FromSeconds(10), timings.ReconnectDelay(1));
        Assert.AreEqual(TimeSpan.FromSeconds(30), timings.ReconnectDelay(2));
        Assert.AreEqual(TimeSpan.FromSeconds(60), timings.ReconnectDelay(3));
        Assert.AreEqual(TimeSpan.FromSeconds(60), timings.ReconnectDelay(10));
    }
}
