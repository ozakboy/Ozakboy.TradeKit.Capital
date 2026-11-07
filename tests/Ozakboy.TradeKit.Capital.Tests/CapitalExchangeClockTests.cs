using Ozakboy.TradeKit.Capital.Tests.Support;

namespace Ozakboy.TradeKit.Capital.Tests;

[TestClass]
public sealed class CapitalExchangeClockTests
{
    // 依據:probe.jsonl 的 last_tick,FX 索引 137 的 20261105 224357 在 03:43:57.34Z 收到(延遲 < 1 秒,美東 UTC−5)。
    // Source: the probe's last_tick; FX index 137 at 20261105 224357 arrived at 03:43:57.34Z (US Eastern, UTC−5).
    [TestMethod]
    public void TickToUtc_Fx_IsUsEastern()
    {
        DateTimeOffset utc = CapitalExchangeClock.TickToUtc("FX", "20261105", "224357").GetValueOrThrow();

        Assert.AreEqual(new DateTimeOffset(2026, 11, 6, 3, 43, 57, TimeSpan.Zero), utc);
        Assert.AreEqual(TimeSpan.Zero, utc.Offset);
    }

    // 依據:CME 索引 310 的 20261105 214356 在 03:43:57.09Z 收到(芝加哥 UTC−6)。
    // Source: CME index 310 at 20261105 214356 arrived at 03:43:57.09Z (Chicago, UTC−6).
    [TestMethod]
    public void TickToUtc_Cme_IsChicago()
    {
        DateTimeOffset utc = CapitalExchangeClock.TickToUtc("CME", "20261105", "214356").GetValueOrThrow();

        Assert.AreEqual(new DateTimeOffset(2026, 11, 6, 3, 43, 56, TimeSpan.Zero), utc);
    }

    [TestMethod]
    public void TickToUtc_PadsAShortTime()
    {
        // nTime 是整數:00:12:34 會是 1234。nTime is an integer, so 00:12:34 arrives as 1234.
        DateTimeOffset utc = CapitalExchangeClock.TickToUtc("FX", "20261106", "1234").GetValueOrThrow();

        Assert.AreEqual(new DateTimeOffset(2026, 11, 6, 5, 12, 34, TimeSpan.Zero), utc);
    }

    // 依據:kline.txt 外匯第一根 2026/11/02 16:01 = 美東 16:00–16:01 那一分鐘(11/01 起是標準時間 UTC−5)。
    // Source: the first FX row of kline.txt; US standard time (UTC−5) applies from 2026-11-01.
    [TestMethod]
    public void MinuteBarOpenUtc_FxInStandardTime_IsEndMinusOneMinute()
    {
        DateTimeOffset open = CapitalExchangeClock.MinuteBarOpenUtc("FX", new DateTime(2026, 11, 2, 16, 1, 0)).GetValueOrThrow();

        Assert.AreEqual(new DateTimeOffset(2026, 11, 2, 21, 0, 0, TimeSpan.Zero), open);
    }

    [TestMethod]
    public void MinuteBarOpenUtc_FxInDaylightTime_UsesUtcMinusFour()
    {
        // 2026-10-30 仍是美東夏令時間(UTC−4,11/01 才結束)。Still US daylight time; it ends on 2026-11-01.
        DateTimeOffset open = CapitalExchangeClock.MinuteBarOpenUtc("FX", new DateTime(2026, 10, 30, 16, 1, 0)).GetValueOrThrow();

        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 20, 0, 0, TimeSpan.Zero), open);
    }

    [TestMethod]
    public void MinuteBarOpenUtc_CbotInDaylightTime_UsesUtcMinusFive()
    {
        DateTimeOffset open = CapitalExchangeClock.MinuteBarOpenUtc("CBOT", new DateTime(2026, 10, 30, 17, 1, 0)).GetValueOrThrow();

        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 22, 0, 0, TimeSpan.Zero), open);
    }

    [TestMethod]
    public void ToUtc_ATimeSkippedBySpringForward_UsesTheStandardOffset()
    {
        // 2026-03-08 02:30 在美東不存在;以標準時間 UTC−5 換算,不擲例外。Does not exist locally; standard offset, no throw.
        DateTimeOffset utc = CapitalExchangeClock.ToUtc("FX", new DateTime(2026, 3, 8, 2, 30, 0)).GetValueOrThrow();

        Assert.AreEqual(new DateTimeOffset(2026, 3, 8, 7, 30, 0, TimeSpan.Zero), utc);
    }

    [TestMethod]
    public void VerifiedExchanges_AreExactlyTheMeasuredOnes()
    {
        string[] verified = [.. CapitalExchangeClock.Known.Where(zone => zone.IsVerified).Select(zone => zone.Exchange).Order(StringComparer.Ordinal)];

        Assert.AreEqual("CBOT,CME,FX,NYM", string.Join(',', verified));
    }

    [TestMethod]
    public void EveryKnownZone_ResolvesOnThisMachine()
    {
        foreach (CapitalExchangeTimeZone zone in CapitalExchangeClock.Known)
        {
            Assert.IsNotNull(zone.TimeZone, zone.Exchange);
        }
    }

    [TestMethod]
    [DataRow("INDEX")]
    [DataRow("ICS")]
    [DataRow("fx")]
    public void UnknownExchange_FailsWithItsCode(string exchange)
    {
        Result<DateTimeOffset> result = CapitalExchangeClock.TickToUtc(exchange, "20261105", "224357");

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(CapitalErrorCodes.UnknownExchange, result.Error.Code);
    }

    [TestMethod]
    public void TickToUtc_UnreadableTime_IsAnInvalidArgument()
    {
        Result<DateTimeOffset> result = CapitalExchangeClock.TickToUtc("FX", "2026115", "xx");

        Assert.AreEqual(CapitalErrorCodes.InvalidArgument, result.Failure().Code);
    }
}
