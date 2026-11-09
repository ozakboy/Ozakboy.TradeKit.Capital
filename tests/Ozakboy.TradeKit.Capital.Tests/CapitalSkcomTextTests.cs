namespace Ozakboy.TradeKit.Capital.Tests;

/// <summary>
/// SKOrderLib 中文亂碼的辨識與部分還原。三個亂碼樣本取自 2026-11-09 probe-full 的 account-*.txt(群益原樣,未經遮罩改動),
/// 原文照抄(與輸出檔的 UTF-8 位元組相同;<c>μ</c> 是 U+03BC、<c>﹐</c> 是 U+FE50、<c>‥</c> 是 U+2025、<c>￥</c> 是 U+FFE5、<c>°</c> 是 U+00B0):「查無資料」→ <c>?dμL﹐eRA</c>、「查詢成功」→ <c>?d﹐s|‥￥\</c>、「NTD(基幣)」→ <c>NTD(°o1o)</c>。
/// 判斷依據:把中文以 Big5 編碼、每個位元組當 Latin-1 字元,再經 Windows 字碼頁 950 的最適對應來回轉一次,三個樣本逐字重現。
/// SKOrderLib's garbled Chinese, with the three samples measured on 2026-11-09.
/// </summary>
[TestClass]
public sealed class CapitalSkcomTextTests
{
    private const string NoData = "?dμL﹐eRA";
    private const string QuerySucceeded = "?d﹐s|‥￥\\";
    private const string BaseCurrency = "NTD(°o1o)";

    [TestMethod]
    [DataRow("查無資料", NoData)]
    [DataRow("查詢成功", QuerySucceeded)]
    [DataRow("NTD(基幣)", BaseCurrency)]
    public void TheGarblingReproducesTheMeasuredSamples(string chinese, string measured)
    {
        Assert.AreEqual(measured, CapitalSkcomText.Garble(chinese));
    }

    [TestMethod]
    public void MeasuredPhrasesAreRepaired()
    {
        Assert.AreEqual("001,查無資料,F020000<ACCT1>", CapitalSkcomText.Repair("001," + NoData + ",F020000<ACCT1>"));
        Assert.AreEqual("000,1,查詢成功", CapitalSkcomText.Repair("000,1," + QuerySucceeded));
        Assert.AreEqual("NTD(基幣)", CapitalSkcomText.Repair(BaseCurrency));
    }

    [TestMethod]
    [DataRow("NTD")]
    [DataRow("台指近")]
    [DataRow("查無資料")]
    [DataRow("")]
    public void TextThatIsNotGarbledStaysTheSame(string text)
    {
        Assert.AreEqual(text, CapitalSkcomText.Repair(text));
        Assert.IsFalse(CapitalSkcomText.LooksGarbled(text));
    }

    [TestMethod]
    public void NullStaysNull()
    {
        Assert.IsNull(CapitalSkcomText.Repair(null));
        Assert.IsFalse(CapitalSkcomText.LooksGarbled(null));
    }

    // 一般無法還原:不同位元組會變成同一個字(例如 B8EA「資」與 B8E9「賊」都是 ﹐e),認不得的亂碼原樣留著,只能判斷「像亂碼」。
    [TestMethod]
    public void UnknownGarbledTextIsKeptButDetected()
    {
        string name = CapitalSkcomText.Garble("王小明");

        Assert.AreEqual(name, CapitalSkcomText.Repair(name), "an unknown phrase is not guessed");
        Assert.IsTrue(CapitalSkcomText.LooksGarbled(name), name);
        Assert.IsTrue(CapitalSkcomText.LooksGarbled(NoData));
        Assert.IsTrue(CapitalSkcomText.LooksGarbled(BaseCurrency));
        Assert.AreEqual(CapitalSkcomText.Garble("資料"), CapitalSkcomText.Garble("賊料"), "best fit merges different characters, so the text cannot be restored in general");
    }
}
