using System.Reflection;
using Ozakboy.TradeKit.Capital.Host.Interop;

namespace Ozakboy.TradeKit.Capital.Host.Tests;

/// <summary>
/// ISKOrderLib 的安全網:套件只讀、群益沒有模擬平台,任何會改變委託、部位、資金的方法都只能是 Unused 佔位(見 SkComOrderInterop.cs 檔頭)。
/// 產生器已對同一組字樣硬擋;這裡以反射再擋一次,防止有人手改產生出來的檔。
/// </summary>
[TestClass]
public sealed class SkComOrderInteropSafetyTests
{
    private const string PlaceholderPrefix = "Unused";

    private static readonly string[] ForbiddenWords =
    [
        "Send", "Cancel", "Decrease", "Correct", "Alter", "WithDraw", "Offset", "Cover", "Assemble", "Disassemble",
        "Basket", "Proxy", "Unlock", "SetMax", "Special", "Duplex", "Strategy",
    ];

    // 0.3.0 探針與之後正式功能用到的唯讀查詢;要加新的真簽章,先確認它不會改變委託、部位或資金,再改這份清單與產生器規格
    private static readonly string[] ReadOnlyMethods =
    [
        "SKOrderLib_Initialize", "GetUserAccount", "ReadCertByID", "GetFutureRights", "GetRequestOverSeaFutureRight",
        "GetOverseaFutureOpenInterestGW", "GetOpenInterestGW", "GetOrderReport", "GetFulfillReport", "GetOFOpenInterestWithDetails",
    ];

    [TestMethod]
    public void OrderLib_HasNoCallableMethodThatCouldChangeOrdersPositionsOrFunds()
    {
        string[] offending = [.. typeof(ISKOrderLib).GetMethods()
            .Where(method => !method.Name.StartsWith(PlaceholderPrefix, StringComparison.Ordinal))
            .Where(method => ForbiddenWords.Any(word => method.Name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Select(method => method.Name)];

        Assert.IsEmpty(offending, "ISKOrderLib 出現可呼叫的下單 / 刪改單 / 出入金方法:" + string.Join(", ", offending));
    }

    [TestMethod]
    public void OrderLib_RealMethods_AreExactlyTheReviewedReadOnlyQueries()
    {
        string[] real = [.. typeof(ISKOrderLib).GetMethods()
            .Where(method => !method.Name.StartsWith(PlaceholderPrefix, StringComparison.Ordinal))
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)];

        CollectionAssert.AreEqual(ReadOnlyMethods.Order(StringComparer.Ordinal).ToArray(), real, "ISKOrderLib 的真簽章清單變了:" + string.Join(", ", real));
    }

    [TestMethod]
    public void OrderLib_Placeholders_TakeOnlyIntPtr()
    {
        string[] typed = [.. typeof(ISKOrderLib).GetMethods()
            .Where(method => method.Name.StartsWith(PlaceholderPrefix, StringComparison.Ordinal))
            .Where(method => method.GetParameters().Any(parameter => parameter.ParameterType != typeof(IntPtr)))
            .Select(method => method.Name)];

        Assert.IsEmpty(typed, "佔位方法只能有 IntPtr 參數:" + string.Join(", ", typed));
    }

    [TestMethod]
    [DataRow(typeof(ISKCenterLib), 15)]
    [DataRow(typeof(ISKOSQuoteLib), 30)]
    [DataRow(typeof(ISKQuoteLib), 43)]
    [DataRow(typeof(ISKOrderLib), 148)]
    [DataRow(typeof(ISKOOQuoteLib), 18)]
    public void DualInterfaces_DeclareEverySlotOfTheTypeLibrary(Type type, int slots)
    {
        // vtable 看的是順序:少一格,之後的方法全部錯位(SKCOM 2.13.59 型別庫的格數)
        MethodInfo[] methods = type.GetMethods();

        Assert.HasCount(slots, methods, type.Name);
    }
}
