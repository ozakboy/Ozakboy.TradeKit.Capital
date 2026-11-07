# Ozakboy.TradeKit.Capital

A .NET 10 client for the **overseas quotes** of Capital Securities' (群益證券) SKCOM API: international FX and
overseas futures; since 0.2.0 also **domestic futures historical K-lines** (TAIEX, mini and micro TAIEX futures, near
month). **Quotes only** — no orders, no accounts.

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

## What this release does

| Scope | Status |
| --- | --- |
| Start and supervise the host (`CapitalHostSession`), credentials only through child-process environment variables | Done |
| A failed login is reported once and **never retried** (`capital.login.failed`, `Data["code"]`, `Data["locked"]`) | Done |
| Unexpected host exit: restart with 1, 2, 5, 10, 30 s backoff up to `MaxRestarts`, subscriptions re-sent | Done |
| Quote-server disconnect (3002): the host reconnects with 5, 10, 30, 60 s backoff and re-sends every subscription | Done, unmeasured |
| Overseas product list merged with product details (`GetProductsAsync`) | Done |
| Quotes and ticks (`Events`, bounded channel, drops the oldest and counts `DroppedEvents`) | Done |
| Historical K-lines: minute, day, week, month (`GetKLinesAsync`) | Done |
| Connection status (`GetConnectionStatusAsync`; includes the domestic state since 0.2.0) | Done |
| Domestic futures historical K-lines: minute and daily, full or AM session (`GetDomesticKLinesAsync`, 0.2.0) | Done, formats measured; `serve` unmeasured live |
| Domestic state kept apart from the overseas one (`DomesticState`, `DomesticStateChanged`); an unavailable domestic side affects neither overseas quotes nor the exit code (0.2.0) | Done |
| No orphaned host: Job Object (KILL_ON_JOB_CLOSE), ends on stdin EOF or when the parent disappears (0.2.0) | Done |
| Orders, accounts, live Taiwan quotes, K-lines for listed stocks / ETFs / indexes, options chains | **Not in scope** |

## Requirements

- Windows x64 with the **.NET 10 Desktop Runtime** (the host uses the Windows Forms message loop; it is
  framework-dependent).
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

## The host's `probe` command

`Ozakboy.TradeKit.Capital.Host probe --out <folder>` logs in interactively (the password is masked) and records raw
SKCOM output (product lists, quotes, ticks, K-lines) to measure formats. It reads quotes only. The output folder may
contain the login ID in SKCOM's own logs (`sklog\`); keep it private. `probe-tw --out <folder>` is the domestic
counterpart, running a fixed set of `RequestKLineAMByDate` queries (it produced 0.2.0's domestic measurements).

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
- **`Ozakboy.TradeKit.Capital.Host.Tests`** drives the protocol layer in memory with a scriptable `ISkQuoteSource`.

```
dotnet test -c Release
```

## Security

- No default credentials; none is written to a file, a log, a command line or a `ToString`.
- The host's stderr is masked line by line before it reaches your logger; stdout carries protocol messages only.
- SKCOM's own log folder (`LogDirectory`) may contain the login ID; it is your folder, keep it out of version control.

## Dependency policy

Only the .NET BCL, first-party Microsoft packages and `Ozakboy.*` packages, judged by the **transitive** graph. No
Capital file is referenced or redistributed: the COM interfaces are declared by hand.

| Package | Version | Used for |
| --- | --- | --- |
| `Ozakboy.Core.Abstractions` | 0.3.0 | `Result` / `Error` |
| `Ozakboy.Security` | 0.1.1 | `SecretMasker` |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.14 | logging |

The test projects additionally use MSTest on Microsoft.Testing.Platform.

## License

MIT, see [LICENSE](LICENSE). SKCOM is Capital Securities' software under Capital's own terms.
