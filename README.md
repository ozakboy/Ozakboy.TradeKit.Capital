# Ozakboy.TradeKit.Capital

A .NET 10 client for the **overseas quotes** of Capital Securities' (群益證券) SKCOM API: international FX and
overseas futures; since 0.2.0 also **domestic futures historical K-lines** (TAIEX, mini and micro TAIEX futures, near
month). Version 0.3.0 adds domestic futures and options live quotes, the overseas best 5
and best 10, overseas option quotes and **read-only futures account queries**. The package **cannot place, amend or
cancel orders and cannot move funds**. It does not support features that need a securities account.

[繁體中文](README_zh-TW.md)

SKCOM is a COM component that must live on an STA thread with a running message loop. This package therefore ships
a small x64 host process, `Ozakboy.TradeKit.Capital.Host`, that loads SKCOM and talks to its parent over
**newline-delimited JSON on stdio**. The client, `CapitalHostSession`, starts and supervises that process, sends
requests, reads quote and tick events, turns raw integer prices into `decimal` and exchange-local times into UTC. A
crash or a hang in SKCOM never takes your application down.

> **Current status:** the SKCOM calls, formats and time zones this package relies on were measured with real
> credentials against SKCOM 2.13.59 on 2026-11-06 (see "Measured and unmeasured"). The `serve` protocol, the
> supervision and the reconnection logic have so far been tested **only against the fake host** in `tests/`; the
> 3002 reconnect in particular has never been seen live. Read that section before relying on it.
> **The 0.3.0 features follow Capital's V2.13.59 manual and their formats were measured against real SKCOM with the
> host's `probe-full` command on 2026-11-09**, then corrected (a futures-only account without positions or orders).
> The "Measured" column below says what was measured and what only partly.

## What this release does

| Scope | Status | Measured |
| --- | --- | --- |
| Start and supervise the host (`CapitalHostSession`), credentials only through child-process environment variables | Done | Fake host only |
| A failed login is reported once and **never retried** (`capital.login.failed`, `Data["code"]`, `Data["locked"]`) | Done | Codes from the manual |
| Unexpected host exit: restart with 1, 2, 5, 10, 30 s backoff up to `MaxRestarts`, every subscription re-sent | Done | Fake host only |
| Quote-server disconnect (3002): the host reconnects with 5, 10, 30, 60 s backoff and re-sends every subscription | Done | Not measured |
| Overseas product list merged with product details (`GetProductsAsync`) | Done | Measured |
| Overseas quotes and ticks (`Events`, bounded channel, drops the oldest and counts `DroppedEvents`) | Done | Measured |
| Overseas historical K-lines: minute, day, week, month (`GetKLinesAsync`) | Done | Measured |
| Connection status (`GetConnectionStatusAsync`, with the domestic and overseas option states and the SKCOM version) | Done | Overseas part measured |
| Domestic futures historical K-lines: minute and daily, full or AM session (`GetDomesticKLinesAsync`, 0.2.0) | Done | Formats measured; `serve` not measured |
| Domestic state kept apart from the overseas one (`DomesticState`, `DomesticStateChanged`, 0.2.0) | Done | Fake host only |
| No orphaned host: Job Object (KILL_ON_JOB_CLOSE), ends on stdin EOF or when the parent disappears (0.2.0) | Done | Fake host only |
| SKCOM version label and start-up check (`SkcomVersion`, `IsTestedSkcomVersion`, 0.3.0) | Done | Measured (`2.13.59_x64`, readable before login); `serve` not measured |
| Domestic futures and options product lists (`GetDomesticProductsAsync`, 0.3.0) | Done | Measured (six fields, paired day-only "AM" codes) |
| Domestic futures and options live quotes, ticks, best 5 and live minute bars (`SubscribeDomesticAsync`, 0.3.0) | Done | Formats and times measured (night session); after midnight and the 10-code cap not measured |
| Overseas best 5 and market depth best 10 (`SubscribeDepthAsync`, 0.3.0) | Done | Formats measured; what triggers the best 5 not settled |
| Overseas option products, quotes, ticks, best 5 and best 10 (`OverseasOptionsState`, `SubscribeOverseasOptions*Async`, 0.3.0) | Done | Connect, list, quotes and books measured; **ticks and time zones not measured** |
| Read-only futures account queries: accounts, rights, open interest, order and fill reports (`GetAccountsAsync` and others, 0.3.0) | Done | Accounts and the empty-account replies measured; **rows with positions not measured**; Chinese arrives garbled |
| Placing, amending, reducing, re-pricing or cancelling orders, smart orders, fund transfers | **Not provided, and never will be** | — |
| Features that need a securities account: Taiwan stock quotes, listed K-lines, stock orders, holdings, sub-brokerage | **Not in scope** | — |

## Compatible SKCOM versions

This package was measured against **SKCOM 2.13.59 x64**. The host's COM interfaces are generated from that version's
type library (`tools/SkComInteropGen`). COM dual interfaces are called by **method order**. If a new Capital build
reorders the methods, the host calls the wrong method. Therefore:

- At start-up the host reads `SKCenterLib_GetSKAPIVersionAndBit`. `CapitalHostSession.SkcomVersion` holds the value
  (for example `2.13.59_x64`) and `IsTestedSkcomVersion` tells whether it is in `CapitalSkcomVersion.Tested`.
- For a version that was not measured, the session logs one warning (`capital.skcom.version_untested`, event 4015)
  and **keeps running**.
- Before you upgrade SKCOM, run the host's `probe-full` probe with the new build. Then run the generator against the
  new `Interop.SKCOMLib.dll` and confirm that the method order did not change.

## Requirements

- Windows x64. The host ships self-contained in the package, with its own .NET and Windows Desktop runtime. You do
  not need to install a .NET runtime for the host. Your own app needs the .NET 10 runtime as usual.
- **SKCOM x64, downloaded and registered by you.** This package does **not** contain or redistribute any Capital file.
  Download the SKCOM API from Capital's official site, put the `x64` folder somewhere permanent, and run its
  `install.bat` **as an administrator**. If you later move or delete that folder, registration points at a missing
  DLL (`capital.skcom.file_missing`); register it again.
- A Capital Securities account with overseas futures quote access, and Capital's certificate installed as Capital
  instructs.
- Domestic futures K-lines need a Capital **futures account**; **without a securities account listed stocks are not
  available** (a futures-only account asking for `2330` was measured to get 9999). This package only commits to futures.

## Installation

```
dotnet add package Ozakboy.TradeKit.Capital
```

The package carries the host in `tools/capital-host/`, and its `buildTransitive` targets copy it to
`$(OutDir)capital-host\` (and to the publish folder) of any project that references the package, directly or
transitively. `CapitalHostOptions.DefaultHostPath(AppContext.BaseDirectory)` points there.

## Quick start

```csharp
using Ozakboy.TradeKit.Capital;

var options = new CapitalHostOptions
{
    HostExecutablePath = CapitalHostOptions.DefaultHostPath(AppContext.BaseDirectory),
    // Optional: SKCOM's own log folder. Its logs may contain the login ID; keep them private.
    LogDirectory = null,
};

await using var session = new CapitalHostSession(options, logger);

// The ID number is upper-cased; Validate() checks "one letter + nine digits" and echoes nothing.
var credentials = new CapitalCredentials(loginId, password);

var started = await session.StartAsync(credentials, cancellationToken);

if (started.IsFailure)
{
    // capital.login.failed       -> never retried; Data["code"] = 300/306/307/600/604, Data["locked"] for 307
    // capital.skcom.not_registered / capital.skcom.file_missing -> register SKCOM x64 as an administrator
    // capital.connect.timeout    -> no OnConnect(3001) within 60 s
    return;
}

await session.SubscribeAsync([CapitalSymbol.Parse("FX,SUSDTWD"), CapitalSymbol.Parse("CME,NQ0000")]);

await foreach (var item in session.Events.ReadAllAsync(cancellationToken))
{
    switch (item)
    {
        case CapitalQuote quote:
            // quote.Close is decimal; quote.TradingDay is Capital's trading day
            break;
        case CapitalTick tick:
            // tick.TimeUtc is UTC; tick.IsReplay marks the day's replay sent at subscription;
            // de-duplicate on (Symbol, Sequence) — replays repeat after a restart
            break;
    }
}
```

K-lines:

```csharp
var bars = await session.GetKLinesAsync(
    CapitalSymbol.Parse("FX,SUSDJPY"), CapitalKLineType.Minute,
    new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 5), decimals: 2);
// Minute bars: OpenTimeUtc is the start of the minute (Capital gives the end; one minute is subtracted).
// Day / week / month bars: TradingDay only.
```

### Domestic futures historical K-lines (0.2.0)

```csharp
// Codes: TX00 TAIEX futures, MTX00 mini, TM0000 micro — near month, rolled like Sinopac's continuous months.
// The dates are trading days: Full includes the night session from 15:00 of the previous trading day,
// so a Monday starts at Friday 15:01.
var tx = await session.GetDomesticKLinesAsync(
    "TX00", CapitalDomesticKLineType.Minute, CapitalTradeSession.Full,
    new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 6));

if (tx.IsFailure)
{
    // capital.domestic.unavailable -> domestic side unavailable or disconnected; Data["reason"], see CapitalDomesticReasons
    // capital.kline.rejected       -> unknown code or no permission; Data["code"] = Capital's code (9999, 3023 measured)
    // capital.kline.timeout        -> no OnKLineComplete within 60 s
    return;
}

foreach (CapitalDomesticBar bar in tx.GetValueOrThrow())
{
    // bar.OpenTimeUtc = the Taipei end time minus one minute, in UTC; daily bars have bar.TradingDay only.
    // Volume == 0 is an empty bar (a minute without trades), returned as is.
}

session.DomesticStateChanged += (_, e) => { /* e.Current, e.Reason, e.Code */ };
```

Worth knowing:

- The domestic side connects after the overseas side is ready (`NotStarted` → `Connecting` → `Ready`, about 0.5 s); a
  request sent while `Connecting` waits in the host. A rejected domestic `EnterMonitorLONG` or no 3003 within 60 s
  makes it `Unavailable` (never retried) **while the overseas side stays ready and `StartAsync` still succeeds**; a
  domestic disconnect (3002) is reconnected with 5, 10, 30, 60 s backoff. Domestic changes raise only
  `DomesticStateChanged`, never `StateChanged`.
- **A close may be the settlement price.** The last day-session minute bar of a full-session query (13:45, or 13:30 on
  settlement days) and the full-session daily close were measured to be TAIFEX's settlement price; only the AM-session
  daily close is the last traded price (TAIEX futures on 2026-11-06: last 39786, settlement 39652). The data is
  returned unchanged.
- One request holds at most 60,000 rows; beyond that Capital **keeps the newest and drops the oldest without an error**.
  Minute bars go back about a year, daily bars to 2015. Split long ranges and check the first bar.
- Unreadable rows are counted and skipped (one warning log), never failing the batch.
- A known Capital data error: the volume of the Friday 2026-10-30 night session (belonging to 11/02) is twice TAIFEX's
  official figure; see [`docs/DESIGN.md`](docs/DESIGN.md) §6.

### Domestic live quotes, overseas best 10 and overseas options (0.3.0)

```csharp
// Domestic futures and options: quotes, ticks (the day's replay first), best 5 with the extended level, live
// minute bars. The whole set holds at most 10 codes (manual 4-4-1). Every code comes in a pair: the one ending in
// "AM" (TX00AM) is day session only, so subscribe the full-session code (TX00) for the night session.
IReadOnlyList<CapitalDomesticProduct> futures = (await session.GetDomesticProductsAsync(CapitalDomesticMarket.Futures)).GetValueOrThrow();
await session.SubscribeDomesticAsync(["TX00", "MTX00"]);

// Overseas best 10 (market depth). The overseas best 5 is assumed to come with the ticks of SubscribeAsync.
await session.SubscribeDepthAsync([CapitalSymbol.Parse("CME,NQ0000")]);

// Overseas options (SKOOQuoteLib) have their own state; an unavailable side does not affect the other feeds.
if (session.OverseasOptionsState == CapitalOverseasOptionsState.Ready)
{
    var products = (await session.GetOverseasOptionProductsAsync()).GetValueOrThrow();
    // products[i].Symbol is the "exchange,code" to subscribe with (e.g. CME,ES00100X6); Name and LastTradeDate too
}

await foreach (var item in session.Events.ReadAllAsync(cancellationToken))
{
    switch (item)
    {
        case CapitalDomesticQuote quote: break;            // price = raw integer / 10^Decimals; a zero ("no value") is null
        case CapitalDomesticTick tick: break;              // IsSimulated marks a pre-open match; nDate is the calendar day
        case CapitalDomesticLiveBar bar: break;            // OpenTimeUtc = Capital's end-of-minute label minus 1 minute
        case CapitalBest5 best5: break;                    // best5.Market tells the feed
        case CapitalBest10 best10: break;
        case CapitalOverseasOptionQuote optionQuote: break;
        case CapitalOverseasOptionTick optionTick: break;  // TimeUtc is null for an unknown exchange
    }
}
```

- The symbol of a domestic event always has the exchange `CapitalSymbol.DomesticExchange` (`TAIFEX`). It only labels
  the event. Do not pass it to `SubscribeAsync`.
- Every subscription merges with the current set. The host re-sends the whole set after a reconnect, and the session
  re-sends it after a host restart. During the first domestic or overseas option connect, the host holds requests
  until the connect has an outcome.
- A value that is not available is not set to zero. A zero price in a domestic quote or tick or an overseas option
  quote is null (the first quote after subscribing was measured to be partly zero), live bar prices are null when the
  decimals are unknown, and empty 0 / 0 book levels stay as Capital sends them, so drop zero prices before use.
- Measured on 2026-11-09: a night-session tick's `nDate` is the calendar day, futures ticks carry milliseconds only,
  and a live bar's time is the end of its minute. Not measured yet: dates after midnight.

### Read-only futures account queries (0.3.0)

```csharp
// The host initialises SKOrderLib on the first account request (Initialize, GetUserAccount, ReadCertByID).
IReadOnlyList<CapitalAccount> accounts = (await session.GetAccountsAsync()).GetValueOrThrow();
string futuresAccount = accounts.First(a => a.Kind == CapitalAccountKind.DomesticFutures).FullAccount!;

var rights = await session.GetFutureRightsAsync(futuresAccount, CapitalCoinType.All);       // one row per currency
var positions = await session.GetOpenInterestAsync(futuresAccount);                         // "no data" is an empty list
var orders = await session.GetOrderReportAsync(futuresAccount, CapitalOrderReportFormat.All); // 5.5 s between report queries
```

- **Queries only.** In the host's `ISKOrderLib` COM definition, every method that could change an order, a position or
  funds is an uncallable placeholder. A reflection test checks it. This package will not add order functions.
- Each record type has named properties parsed by the manual's field order and `RawFields`. A named property is null
  when it cannot be read, never zero. The accounts, the rights (41 domestic fields, 31 overseas) and the "no data"
  replies were measured on 2026-11-09 with an account without positions; **rows with positions or orders are not
  measured yet**, so read `RawFields` when in doubt.
- **Chinese arrives garbled**: Capital's account replies (SKOrderLib) turn Chinese into irreversible mojibake ("查無資料"
  becomes `?dμL﹐eRA`). The package restores the measured fixed phrases only (`CapitalSkcomText.Repair`, already applied
  to currencies and error messages); for other text such as the holder's name use `CapitalSkcomText.LooksGarbled`.
  Chinese in the quote feeds is fine.
- **Do not send the same query back to back**: the same query function sent again 1-2 seconds after the previous one
  ended is rejected with 1019 (`capital.account.query_rejected`, `Data["code"]` = 1019). Wait a few seconds.
- Failures: `capital.account.unavailable`, `capital.account.init_failed` and `cert_failed` (with Capital's code and the
  step; the next request tries again), `capital.account.query_rejected`, `query_failed` and `query_timeout`.
- Account numbers, holder names and IDs never reach the host's stderr or your log. They are masked as `<ACCT>`,
  `<NAME>` and `<ID>` as soon as they arrive. `ToString()` of the record types shows only the last 3 characters of the
  account. `CapitalAccount.RawFields` holds the holder's ID and name, so keep it private.

## Design decisions worth knowing

### Why a child process on stdio rather than HTTP

The host has exactly one caller, the process that started it. stdio is reachable by that parent only: no port, no
token, nothing else on the machine can connect. The protocol has the shape of MCP's stdio transport and is easy to
test. See [`docs/DESIGN.md`](docs/DESIGN.md) §2 for the full protocol.

### Credentials travel only through the child process's environment

`StartAsync` puts the ID and password into `ProcessStartInfo.Environment` (`CAPITAL_LOGIN_ID`, `CAPITAL_PASSWORD`) and
nowhere else — never a command-line argument, a file or a log. The host reads them and removes them from its own
environment at once. Both are registered on the `SecretMasker`, and their literal values are additionally replaced in
everything the host writes to stderr (`<ID>` / `***`), because the masker refuses values shorter than 8 characters.
The host itself also replaces the ID with `<ID>` before writing a line. `CapitalCredentials.ToString()` carries no
content and the type is deliberately not a record.

### Login is never retried

Too many wrong passwords lock a Capital account (307), and repeated logins do not unlock it. A failed login stops the
session in `LoginFailed`; nothing retries it, including the restart loop. Only connection-class failures are retried:
a host crash (restart, which logs in again on a fresh process) and a quote-server disconnect (the host calls
`EnterMonitorLONG` again; it never logs in again).

### Exit codes of the host

| Code | Meaning | Client behaviour |
| --- | --- | --- |
| 0 | shutdown, end of stdin, or the parent is gone | none (expected) |
| 1 | internal host defect | restart |
| 2 | `CAPITAL_LOGIN_ID` / `CAPITAL_PASSWORD` missing; Login never called | Faulted, no restart |
| 3 | SKCOM objects cannot be created | Faulted, no restart |
| 4 | login failed | LoginFailed, no restart |
| 5 | no `OnConnect(3001)` within 60 s | restart (counts towards `MaxRestarts`) |

No domestic failure changes the exit code.

### The host never becomes an orphan (0.2.0)

A host that outlives its parent stays logged in to Capital. So there are three safety nets: the client puts the host
into a Windows **Job Object** (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, the handle held by `CapitalHostSession` until
`DisposeAsync`), so the system ends the host however the parent ends, even when killed; the host leaves both monitors
and exits 0 on stdin EOF; and every 5 s it checks the parent PID the client passes in `CAPITAL_PARENT_PID`, ending the
same way once it is gone. If an orderly shutdown has not finished 15 s after that decision (an STA stuck in a
synchronous SKCOM call, say), the host exits forcibly.

### Numbers and time

- Quote and tick prices are raw integers; `CapitalPriceScale.FromRaw` divides by 10^decimals in `decimal`. No `double`
  or `float` takes part anywhere.
- K-line prices arrive as single-precision float text (`157.889999`); they are read as `decimal` and rounded
  away from zero to the decimals you pass (`157.89`).
- `nDenominator` was always 1. A quote with another denominator is still scaled by decimals and logs
  `capital.price.fraction_unsupported` once per symbol.
- Times are the **exchange's local time**, converted by `CapitalExchangeClock` with daylight saving. Measured: `FX` and
  `NYM` are US Eastern, `CME` and `CBOT` are Chicago. Other exchanges are inferred from their location and marked
  `IsVerified = false`; `INDEX` and `ICS` have no mapping and fail with `capital.clock.unknown_exchange`.

## Measured and unmeasured

**Measured** (SKCOM 2.13.59 x64, real credentials, 2026-11-06, with the host's `probe` command):

- `OnReplyMessage` must be attached before login and answer -1; a successful login returns 0.
- After `EnterMonitorLONG` only `OnConnect(3001)` arrives — never 3003. `GetQuoteStatus` reports 0 connections, not
  over the limit.
- The product list (`exchange,exchangeName,code,name,lastTradeDate,firstNoticeDate`, 1,345 rows) and the details
  (`…,orderExchange,orderCode,lastTradeDate,firstNoticeDate,kind`) each end with a row starting with `##`. FX has
  last trading day `99991231` and first notice day `0`; a front-month future is the root plus `0000`
  (`CME,NQ0000`).
- Subscriptions: `RequestStocks(-1, "FX,SUSDTWD#CME,NQ0000")` and one `RequestTicks` per symbol.
- Quote prices divided by 10^`sDecimal` (USD/TWD 3, JPY 2, EUR and CNY 4, NQ 2, GC 1, YM 0); for FX `nTQty` counts
  quote updates, not volume.
- Ticks: a subscription first replays the whole trading day (about 208,000 ticks for 7 symbols), then live ticks;
  `nPtr` increases per symbol; ticks may arrive before the first quote.
- Time zones of FX, NYM, CME, CBOT (tick times against receive times, under 1 s of delay).
- K-lines: types 0 minute, 1 day, 2 week, 3 month (4–7 answer 1042); format
  `yyyy/MM/dd HH:mm, O, H, L, C, V` (no time for day bars); a minute's time is its **end**; data stops at the previous
  trading day; there is no end event.

**Domestic futures K-lines, measured** (SKCOM 2.13.59 x64, a futures-only account, 2026-11-07, with the host's
`probe-tw` command):

- `SKQuoteLib_EnterMonitorLONG` answers 0, then `OnConnection(3001)`, then 3003 about 0.5 s later (product data
  loaded; queries work only after it). Running the overseas `EnterMonitorLONG` in the same process works.
- `RequestKLineAMByDate(code, type, 1, session, from, to, 1)`: 0 means sent; an unknown code or a missing permission
  answers non-zero at once (`2330` gave 9999, `TX00AM` 3023). Each bar arrives as one `OnNotifyKLineData`, and
  **every request ends with exactly one `OnKLineComplete("##")`**, even with no rows.
- Type 0 minute, 4 daily; session 0 full (including the previous night), 1 AM; the dates are trading days.
- Formats: minute `yyyy/MM/dd HH:mm, O, H, L, C, V`, daily `yyyy/MM/dd, O, H, L, C, V`; a minute's time is its end, in
  Taipei time, dated by calendar day; day session 08:46–13:45, night session 15:01–05:00; empty zero-volume bars
  are sent.

**Not measured:**

- The `serve` mode as a whole against real SKCOM (including 0.2.0's domestic state machine, `kline-tw`, the domestic
  3002 reconnect, the Job Object and the parent watchdog). Every call it makes was measured in the probe, in the same order,
  but `serve` itself has run only with the fake source.
- The 3002 disconnect and the reconnect (whether `EnterMonitorLONG` again without `LeaveMonitor` works, and whether
  stock indexes change afterwards; the host drops its index cache on reconnect to be safe).
- Login failure codes (300, 306, 307, 600, 604) come from Capital's manual, not from a live failure.
- The time zones of exchanges other than FX, NYM, CME and CBOT.
- Capital's daily login limit and subscription limit (around 100 symbols by hearsay) are unconfirmed and not enforced.
- Quotes with `nDenominator` other than 1, and the meaning of the trading day for `INDEX` products.
- **The parts of 0.3.0 the 2026-11-09 `probe-full` run could not reach**: account rows with positions or orders and
  the meaning of their amounts, rights coin types 1 / 2 and open-interest formats 2 / 3 (blocked by 1019), whether
  `ReadCertByID` is required, tick and live bar dates after midnight, the domestic 10-code cap, what triggers the
  overseas best 5, overseas option ticks and time zones, and the text encoding of report strings. The item-by-item
  results are in [`docs/DESIGN.md`](docs/DESIGN.md) §7.

**0.3.0 measured** (SKCOM 2.13.59 x64, a futures-only account, the 2026-11-09 night session, with the host's
`probe-full` command; details in DESIGN §5.3):

- The version string `2.13.59_x64`, readable before login.
- Domestic product lists have six fields (option series rows five), and every code comes as an "AM" day-only code and a
  full-session code; quote `sDecimal` is 2 for futures and options; the first quote after subscribing is partial.
- Domestic tick `nDate` is the calendar day, futures carry milliseconds only, and the replay starts with the pre-open
  simulated match (`nSimulate` = 1); a live bar's time is the end of its minute; empty book levels are 0 / 0.
- Overseas best 5 and best 10 prices divide by 10^decimals and the best 10's first five equal the best 5; overseas
  options send 3001 only, list five fields ending with `##,##,##,##,##`, accept `#`-joined subscriptions and send an
  unscaled strike.
- Accounts are `TF` / `OF`; queries take the broker code plus the account; the status event arrives before the rows;
  without positions the domestic open interest returns `001,…`, the overseas one `@@,001,…` and the reports `M003`;
  overseas rights have 31 fields.

## The host's `probe` command

`Ozakboy.TradeKit.Capital.Host probe --out <folder>` logs in interactively (the password is masked) and records raw
SKCOM output (product lists, quotes, ticks, K-lines) to measure formats. It reads quotes only. The output folder may
contain the login ID in SKCOM's own logs (`sklog\`); keep it private. `probe-tw --out <folder>` is the domestic
counterpart, running a fixed set of `RequestKLineAMByDate` queries (it produced 0.2.0's domestic measurements).
`probe-full --out <folder> [--seconds 180] [--accounts-only]` measures the 0.3.0 formats: the version, the account
queries, domestic futures and options, the overseas best 5 and best 10, and overseas options. Its output masks account
numbers, holder names and the login ID (except `sklog\`), and its account part calls read-only queries only. 0.3.0 was
checked item by item against its 2026-11-09 output; the items it could not reach (see above) need another run under
other conditions (with positions, after midnight, with overseas option trades). Known issue: it does not exit on its
own after writing `done`; close it by hand (DESIGN §6).

## Testing

No test touches SKCOM or needs credentials:

- **`tests/FakeCapitalHostExe`** links the host's real protocol layer (`Serve/*.cs`) and replaces SKCOM with a
  simulation of the measured behaviour: `##`-terminated products, only 3001, replay before live ticks with increasing
  `nPtr`, measured K-line text with minute-end times, exchange-local times, and a stray K-line row for another code.
  Environment variables make it fail the login (300 / 307), fail to create SKCOM, never connect, crash once or every
  time, or drop the connection and recover. `Ozakboy.TradeKit.Capital.Tests` drives `CapitalHostSession` against it.
  Since 0.2.0 it also simulates the domestic side as measured on 2026-11-07 (3001 then 3003, trading-day dates with the
  previous night session, minute-end times, empty bars, the 60,000-row cap keeping the newest, 9999 for unknown
  codes) and can make it unavailable, disconnect or never send the end event; further tests start the fake host
  directly and check that it ends on stdin EOF, when its parent disappears, and when its Job Object closes.
  Since 0.3.0 it also simulates domestic live quotes, the overseas best 5 and best 10, overseas options and the account
  queries: what the 2026-11-09 run measured follows the measurement (product lists and AM codes, the partial first
  quote, millisecond ticks, the "no data" replies and garbled Chinese of the account queries, 31 overseas rights fields,
  the overseas option list format), and the rest (account rows with positions, overseas option tick time zones)
  follows the manual. It can make the overseas options unavailable, fail to
  create SKOrderLib, fail the initialisation or the certificate, fail the rights query with the account in the message
  (to check masking), and report another SKCOM version.
- **`Ozakboy.TradeKit.Capital.Host.Tests`** drives the protocol layer in memory with a scriptable `ISkQuoteSource`.

```
dotnet test -c Release
```

## Security

- No default credentials; none is written to a file, a log, a command line or a `ToString`.
- The host's stderr is masked line by line before it reaches your logger; stdout carries protocol messages only.
- SKCOM's own log folder (`LogDirectory`) may contain the login ID; it is your folder, keep it out of version control.
- 0.3.0: account numbers, holder names and IDs are registered for masking as soon as they arrive, in the host and in
  the client. They never reach stderr or a log. `ToString()` of the account record types shows only the last 3
  characters of the account.
- 0.3.0: **no order capability.** Every method of the host's `ISKOrderLib` that could change an order, a position or
  funds is a placeholder that takes only `IntPtr`. The generator rejects forbidden names, and
  `SkComOrderInteropSafetyTests` checks by reflection that the callable methods are exactly the reviewed read-only queries.

## Dependency policy

Only the .NET BCL, first-party Microsoft packages and `Ozakboy.*` packages, judged by the **transitive** graph. No
Capital file is referenced or redistributed: `tools/SkComInteropGen` generates the COM interfaces from the metadata of
Capital's type library on your own machine, and Capital's DLL is not in the repository.

| Package | Version | Used for |
| --- | --- | --- |
| `Ozakboy.Core.Abstractions` | 0.3.0 | `Result` / `Error` |
| `Ozakboy.Security` | 0.1.1 | `SecretMasker` |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.14 | logging |

The test projects additionally use MSTest on Microsoft.Testing.Platform.

## License

MIT, see [LICENSE](LICENSE). SKCOM is Capital Securities' software under Capital's own terms.
