using Ozakboy.TradeKit.Capital.Tests.Support;

namespace Ozakboy.TradeKit.Capital.Tests;

[TestClass]
public sealed class CapitalValueTypeTests
{
    [TestMethod]
    public void Symbol_ParsesAndPrintsTheRequestForm()
    {
        CapitalSymbol symbol = CapitalSymbol.Parse("FX,SUSDTWD");

        Assert.AreEqual("FX", symbol.Exchange);
        Assert.AreEqual("SUSDTWD", symbol.Code);
        Assert.AreEqual("FX,SUSDTWD", symbol.ToString());
        Assert.AreEqual("FX,SUSDTWD", symbol.ToRequestString());
        Assert.IsTrue(symbol.IsValid);
        Assert.AreEqual(new CapitalSymbol("FX", "SUSDTWD"), symbol);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("FX")]
    [DataRow(",SUSDTWD")]
    [DataRow("FX,")]
    [DataRow("FX,SUSD,TWD")]
    [DataRow("FX,SUSDTWD#CME,NQ0000")]
    [DataRow("FX, SUSDTWD")]
    public void Symbol_RefusesMalformedText(string text)
    {
        Assert.IsFalse(CapitalSymbol.TryParse(text, out _));
        Assert.ThrowsExactly<FormatException>(() => CapitalSymbol.Parse(text));
    }

    [TestMethod]
    public void Symbol_DefaultIsNotValid()
    {
        Assert.IsFalse(default(CapitalSymbol).IsValid);
    }

    [TestMethod]
    public void Credentials_AcceptOneLetterAndNineDigits_CaseInsensitive()
    {
        CapitalCredentials credentials = new(" a123456789 ", "secret");

        Assert.IsTrue(credentials.Validate().IsSuccess);
        Assert.AreEqual("A123456789", credentials.LoginId);
    }

    [TestMethod]
    [DataRow("123456789A", "pw")]
    [DataRow("A12345678", "pw")]
    [DataRow("A1234567890", "pw")]
    [DataRow("AB23456789", "pw")]
    [DataRow("A123456789", "  ")]
    public void Credentials_InvalidFormat_EchoesNothing(string loginId, string password)
    {
        Result result = new CapitalCredentials(loginId, password).Validate();

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(CapitalErrorCodes.InvalidCredentials, result.Error.Code);
        Assert.IsFalse(result.Error.Message.Contains(loginId.Trim(), StringComparison.OrdinalIgnoreCase), "the message must not echo the ID");

        foreach (KeyValuePair<string, string> item in result.Error.Data ?? new Dictionary<string, string>())
        {
            Assert.IsFalse(item.Value.Contains(loginId.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    [TestMethod]
    public void Credentials_ToStringCarriesNoContent()
    {
        CapitalCredentials credentials = new("A123456789", "Pw-Fake-2026");

        Assert.IsFalse(credentials.ToString().Contains("A123456789", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(credentials.ToString().Contains("Pw-Fake-2026", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Options_DefaultHostPathIsTheCapitalHostFolder()
    {
        string path = CapitalHostOptions.DefaultHostPath(@"C:\apps\MyApp");

        Assert.AreEqual(@"C:\apps\MyApp\capital-host\Ozakboy.TradeKit.Capital.Host.exe", path);
    }

    [TestMethod]
    public void Options_DefaultsFollowTheDesign()
    {
        CapitalHostOptions options = new();

        Assert.AreEqual(TimeSpan.FromSeconds(90), options.StartTimeout);
        Assert.AreEqual(5, options.MaxRestarts);
        Assert.AreEqual(CapitalErrorCodes.InvalidOptions, options.Validate().Failure().Code, "the host path is required");

        options.HostExecutablePath = "x.exe";
        Assert.IsTrue(options.Validate().IsSuccess);

        options.StartTimeout = TimeSpan.Zero;
        Assert.IsTrue(options.Validate().IsFailure);
    }

    [TestMethod]
    public void Session_RefusesInvalidOptionsAtConstruction()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CapitalHostSession(new CapitalHostOptions()));
    }
}
