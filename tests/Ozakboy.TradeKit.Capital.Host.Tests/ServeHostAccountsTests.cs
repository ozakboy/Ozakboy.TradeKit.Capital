using System.Diagnostics;
using System.Text.Json;
using Ozakboy.TradeKit.Capital.Host.Tests.Support;

namespace Ozakboy.TradeKit.Capital.Host.Tests;

/// <summary>
/// 0.3.0 期貨帳務查詢(唯讀)的協定層:延遲初始化(第一次帳務請求才 Initialize → GetUserAccount → ReadCertByID)、失敗回明確錯誤碼、
/// 查詢以「##」與狀態事件結束(probe-full 的做法)、委託 / 成交回報強制間隔、帳號不進 stderr。格式依手冊推定,待實測。
/// The 0.3.0 read-only account protocol layer; formats follow the manual and are still to be measured.
/// </summary>
[TestClass]
public sealed class ServeHostAccountsTests
{
    private const string Account = """{"account":"F0200001234567"}""";

    [TestMethod]
    public async Task NothingIsInitialisedUntilTheFirstAccountRequestAndOnlyOnce()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();
        await Task.Delay(100);
        CollectionAssert.AreEqual(Expected("open"), Calls(rig), "the object is created before login, nothing else");

        JsonElement first = (await rig.RequestAsync(1, "accounts")).GetProperty("result");
        JsonElement second = (await rig.RequestAsync(2, "accounts")).GetProperty("result");

        CollectionAssert.AreEqual(Expected("open", "init", "accounts", "cert " + HostRig.LoginId), Calls(rig), "Initialize, GetUserAccount and ReadCertByID once, in order");
        Assert.AreEqual(2, first.GetProperty("items").GetArrayLength());
        Assert.AreEqual(second.GetRawText(), first.GetRawText());
    }

    [TestMethod]
    public async Task AnInitialiseFailureIsExplicitAndRetriedOnTheNextRequest()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.AccountsInitCode = 1001;
        _ = await rig.StartReadyAsync();

        JsonElement error = (await rig.RequestAsync(1, "accounts")).GetProperty("error");
        Assert.AreEqual("capital.account.init_failed", error.GetProperty("code").GetString());
        Assert.AreEqual("initialize", error.GetProperty("data").GetProperty("step").GetString());
        Assert.AreEqual(1001, error.GetProperty("data").GetProperty("skCode").GetInt32());

        rig.Source.AccountsInitCode = 0;
        _ = (await rig.RequestAsync(2, "accounts")).GetProperty("result");
        Assert.AreEqual(2, rig.Source.AccountCallCount("init"));
    }

    [TestMethod]
    public async Task AFailedAccountListOrCertificateIsExplicit()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.AccountsListCode = 1002;
        _ = await rig.StartReadyAsync();

        JsonElement list = (await rig.RequestAsync(1, "accounts")).GetProperty("error");
        Assert.AreEqual("get_user_account", list.GetProperty("data").GetProperty("step").GetString());

        rig.Source.AccountsListCode = 0;
        rig.Source.CertificateCode = 1003;
        JsonElement cert = (await rig.RequestAsync(2, "account-rights", """{"account":"F0200001234567","coin":0}""")).GetProperty("error");
        Assert.AreEqual("capital.account.cert_failed", cert.GetProperty("code").GetString());
        Assert.AreEqual(0, rig.Source.AccountCallCount("rights"), "no query without the certificate");
    }

    [TestMethod]
    public async Task AMissingOrderLibMakesAccountsUnavailable()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.AccountsOpenFailure = new SkOpenFailure("skcom_not_registered", unchecked((int)0x80040154));
        _ = await rig.StartReadyAsync();

        JsonElement error = (await rig.RequestAsync(1, "account-os-rights", Account)).GetProperty("error");

        Assert.AreEqual("capital.account.unavailable", error.GetProperty("code").GetString());
        Assert.AreEqual("skcom_not_registered", error.GetProperty("data").GetProperty("reason").GetString());
        Assert.AreEqual(0, rig.Source.AccountCallCount("init"));
    }

    // 依據:手冊 4-2-i「以 ## 開頭的內容表示查詢結束」與 4-2-v OnFutureRightsStatus(0 成功)。
    [TestMethod]
    public async Task ARightsQueryEndsOnTheEndMarkerAndTheStatus()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.QueryReplies.Add(("OnFutureRights", "100,200,TWD"));
        rig.Source.QueryReplies.Add(("OnOpenInterest", "not mine"));
        rig.Source.QueryReplies.Add(("OnFutureRights", "##"));
        rig.Source.QueryStatus = ("OnFutureRightsStatus", 0, "Success!");
        _ = await rig.StartReadyAsync();
        _ = await rig.RequestAsync(99, "accounts");

        Stopwatch watch = Stopwatch.StartNew();
        JsonElement result = (await rig.RequestAsync(1, "account-rights", """{"account":"F0200001234567","coin":1}""")).GetProperty("result");

        CollectionAssert.AreEqual(Expected("100,200,TWD"), ServeHostLiveTests.Strings(result, "rows"), "only the rights event is collected");
        Assert.AreEqual(0, result.GetProperty("status").GetInt32());
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromMilliseconds(280), "the end marker plus the status ends it without waiting for the quiet");
        CollectionAssert.Contains(rig.Source.AccountCalls.Select(call => call.Call).ToArray(), "rights F0200001234567 1");
    }

    [TestMethod]
    public async Task AQueryWithoutAnyReplyEndsAfterTheQuietWithANullStatus()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();

        JsonElement result = (await rig.RequestAsync(1, "account-open-interest", """{"account":"F0200001234567","format":1}""")).GetProperty("result");

        Assert.AreEqual(0, result.GetProperty("rows").GetArrayLength());
        Assert.AreEqual(JsonValueKind.Null, result.GetProperty("status").ValueKind);
    }

    [TestMethod]
    public async Task AFailedStatusIsAQueryFailureWithTheAccountMasked()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.QueryStatus = ("OnOverseaFutureOpenInterestGWStatus", 1, "帳號 F0200007654321 查無權限");
        _ = await rig.StartReadyAsync();

        JsonElement error = (await rig.RequestAsync(1, "account-os-open-interest", """{"account":"F0200007654321","format":2}""")).GetProperty("error");

        Assert.AreEqual("capital.account.query_failed", error.GetProperty("code").GetString());
        string message = error.GetProperty("message").GetString()!;
        StringAssert.Contains(message, "<ACCT>");
        Assert.IsFalse(message.Contains("7654321", StringComparison.Ordinal), "the account number registered from OnAccount is masked");
    }

    [TestMethod]
    public async Task ARejectedQueryReturnsCapitalsCode()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.QueryCode = 1020;
        _ = await rig.StartReadyAsync();

        JsonElement error = (await rig.RequestAsync(1, "account-os-rights", Account)).GetProperty("error");

        Assert.AreEqual("capital.account.query_rejected", error.GetProperty("code").GetString());
        Assert.AreEqual(1020, error.GetProperty("data").GetProperty("skCode").GetInt32());
    }

    [TestMethod]
    public async Task RepliesThatNeverStopAreATimeout()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();
        _ = await rig.RequestAsync(1, "accounts");

        using CancellationTokenSource stop = new();
        Task chatter = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                rig.Source.RaiseAccountReply("OnOverSeaFutureRight", "000,1");
                await Task.Delay(30);
            }
        });

        JsonElement error = (await rig.RequestAsync(2, "account-os-rights", Account)).GetProperty("error");
        await stop.CancelAsync();
        await chatter;

        Assert.AreEqual("capital.account.query_timeout", error.GetProperty("code").GetString());
        Assert.IsTrue(error.GetProperty("data").GetProperty("received").GetInt32() > 0);
    }

    // 依據:手冊 4-2-97 / 4-2-98「限制每次查詢間需間隔五秒」;測試把間隔縮成 400 毫秒。
    [TestMethod]
    public async Task ReportQueriesAreSpacedByTheIntervalAndSplitIntoRows()
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();

        JsonElement orders = (await rig.RequestAsync(1, "account-orders", """{"account":"F0200001234567","format":1}""")).GetProperty("result");
        JsonElement fills = (await rig.RequestAsync(2, "account-fills", """{"account":"F0200001234567","format":5}""")).GetProperty("result");

        (string Call, long Timestamp)[] calls = [.. rig.Source.AccountCalls.Where(call => call.Call.Contains("_report", StringComparison.Ordinal))];
        Assert.AreEqual(2, calls.Length);
        Assert.IsTrue(Stopwatch.GetElapsedTime(calls[0].Timestamp, calls[1].Timestamp) >= TimeSpan.FromMilliseconds(380), "the second report waits for the interval");
        Assert.AreEqual(2, orders.GetProperty("rows").GetArrayLength());
        Assert.AreEqual("fulfill_report F0200001234567 5", calls[1].Call);
        Assert.AreEqual(2, fills.GetProperty("rows").GetArrayLength());
    }

    [TestMethod]
    [DataRow("account-rights", """{"account":"F0200001234567","coin":3}""")]
    [DataRow("account-rights", """{"account":"F020 0001234567","coin":0}""")]
    [DataRow("account-open-interest", """{"account":"F0200001234567","format":4}""")]
    [DataRow("account-os-open-interest", """{"account":"F0200001234567","format":3}""")]
    [DataRow("account-orders", """{"account":"F0200001234567","format":10}""")]
    [DataRow("account-fills", """{"account":"F0200001234567"}""")]
    [DataRow("account-os-rights", """{}""")]
    public async Task InvalidAccountParametersAreRejectedBeforeAnyCall(string method, string parameters)
    {
        using HostRig rig = await HostRig.Create().StartReadyAsync();

        JsonElement error = (await rig.RequestAsync(1, method, parameters)).GetProperty("error");

        Assert.AreEqual("capital.request.invalid", error.GetProperty("code").GetString());
        Assert.AreEqual(0, rig.Source.AccountCallCount("init"));
    }

    [TestMethod]
    public async Task AccountNumbersNamesAndIdsNeverReachStderr()
    {
        using HostRig rig = HostRig.Create();
        rig.Source.QueryStatus = ("OnFutureRightsStatus", 1, "帳號 1234567 王小明 查詢失敗");
        _ = await rig.StartReadyAsync();

        _ = await rig.RequestAsync(1, "accounts");
        _ = await rig.RequestAsync(2, "account-rights", """{"account":"F0200001234567","coin":0}""");
        rig.Source.RaiseAnnouncement("帳號 F0200001234567 的公告,戶名王小明");
        await Task.Delay(100);

        string stderr = rig.Stderr;
        Assert.IsFalse(stderr.Contains("1234567", StringComparison.Ordinal), stderr);
        Assert.IsFalse(stderr.Contains("王小明", StringComparison.Ordinal), stderr);
        Assert.IsFalse(stderr.Contains(HostRig.LoginId, StringComparison.OrdinalIgnoreCase), stderr);
        StringAssert.Contains(stderr, "<ACCT>");
    }

    private static string[] Expected(params string[] values) => values;

    private static string[] Calls(HostRig rig) => [.. rig.Source.AccountCalls.Select(call => call.Call)];
}
