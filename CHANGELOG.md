# Changelog

本檔記錄本套件所有值得注意的變更。
All notable changes to this package are documented here.

格式依循 [Keep a Changelog](https://keepachangelog.com/zh-TW/1.1.0/),版號依循
[語意化版本](https://semver.org/lang/zh-TW/)。
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the versioning follows
[Semantic Versioning](https://semver.org/).

## [0.2.0] - 2026-11-07

**國內期貨歷史 K 線(台指 / 小台 / 微台近月),以及宿主不再留下孤兒行程。**
**Domestic futures historical K-lines (TAIEX, mini and micro TAIEX futures, near month), and no more orphaned hosts.**

### 新增功能 / Added

- **`CapitalHostSession.GetDomesticKLinesAsync`**(`SKQuoteLib_RequestKLineAMByDate`):`TX00` / `MTX00` / `TM0000`,
  1 分 K 與日 K(`CapitalDomesticKLineType`)、全盤或 AM 盤(`CapitalTradeSession`),起訖是交易日(全盤含前一晚夜盤)。
  `CapitalDomesticBar` 的分 K `OpenTimeUtc` = 台北時間的結束時刻 − 1 分鐘轉 UTC,日 K 只有 `TradingDay`;價格以 `decimal` 原樣讀入;
  量為 0 的空 K 棒照原樣回;讀不懂的行記數略過並記一筆警告。需要群益期貨戶,本套件只承諾期貨。
  **`GetDomesticKLinesAsync`** for TX00, MTX00 and TM0000: minute and daily bars, full or AM session, trading-day dates.
  Minute bars open at the Taipei end time minus one minute, in UTC; prices are read as decimal unchanged; empty bars are
  kept; unreadable rows are counted, skipped and logged once. A Capital futures account is required; only futures are
  supported.
- **國內狀態**:`DomesticState`、`DomesticReason`、`DomesticStateChanged`(`CapitalDomesticState`、`CapitalDomesticReasons`),
  與海外的 `State` / `StateChanged` 分開。國內在海外就緒後才連,`EnterMonitorLONG` 被拒或 60 秒內沒有 3003 → 不可用(不重試),
  **海外照常 ready、結束碼不受影響**;國內 3002 以 5、10、30、60 秒退避重連。`CapitalConnectionStatus` 加 `Domestic`、`DomesticReason`。
  **A domestic state** separate from the overseas one. The domestic side connects after the overseas side is ready;
  a rejected EnterMonitorLONG or no 3003 within 60 seconds makes it unavailable without affecting the overseas side or
  the exit code; a domestic 3002 is reconnected with backoff.
- **宿主協定**:新 method `kline-tw`(`{code, type: 0|4, session: 0|1, start, end}` → `{lines}`,以 `OnKLineComplete` 結束);
  `status` 事件與 `status` 回應加 `domestic` / `domesticReason` / `domesticCode`;新錯誤碼 `capital.kline.timeout`、
  `capital.domestic.unavailable`。0.1.x 用戶端可以照常接 0.2.0 宿主(新欄位會被忽略)。
  **Host protocol**: the kline-tw method, domestic fields on the status event and response, and two new error codes.
  A 0.1.x client still works with the 0.2.0 host.

### 問題修正 / Fixed

- **父行程結束後宿主還一直跑(可能還掛著群益登入)**:用戶端把宿主放進 Windows Job Object(`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`,
  handle 由工作階段持有到 `DisposeAsync`),父行程不論正常結束或被強制結束,系統都會收掉宿主;宿主另外看守用戶端以 `CAPITAL_PARENT_PID`
  傳入的父行程(每 5 秒),不在了就與 stdin EOF 一樣 LeaveMonitor(海外與國內)後以 0 結束;決定結束後 15 秒仍未結束就強制結束。
  **A host kept running after its parent ended**, possibly still logged in. The client now puts the host in a
  KILL_ON_JOB_CLOSE Job Object held until DisposeAsync, the host also watches its parent PID every 5 seconds and ends
  like on stdin EOF once it is gone, and a shutdown that has not finished within 15 seconds is forced.

### 已知問題 / Known issues

- **全盤的收盤是結算價**:全盤分 K 的日盤最後一根(13:45;結算日 13:30)與全盤日 K 的收盤是期交所的結算價,AM 盤日 K 的收盤才是最後成交價
  (2026-11-06 台指:最後成交 39786、結算 39652)。套件回原始資料、不修正。
  **Full-session closes are settlement prices**; only the AM-session daily close is the last traded price. Returned
  unchanged.
- **群益資料錯誤**:2026-10-30 週五夜盤(歸屬 11/02)群益分 K 與日 K 的量是期交所官方的 2 倍(官方 44,305 口、群益 88,442),其他夜盤正常。
  套件不處理。
  **A Capital data error**: the volume of the 2026-10-30 night session is twice TAIFEX's figure. Not handled.
- 一個請求最多 60,000 筆,超過時群益保留最新、截掉最舊且不報錯;1 分 K 約只保留最近 1 年。
  One request holds at most 60,000 rows, the oldest dropped silently beyond that; minute bars go back about a year.
- 國內的 `serve` 流程(連線狀態機、`kline-tw`、國內 3002 重連)與 Job Object、父行程看門狗都只對假宿主測過;國內的 SKCOM 呼叫與格式是
  2026-11-07 以 `probe-tw` 實測的。
  The domestic serve flow, the Job Object and the parent watchdog are tested against the fake host only; the domestic
  SKCOM calls and formats were measured with probe-tw on 2026-11-07.

## [0.1.0] - 2026-11-06

**第一版:群益 SKCOM 海外報價(外匯 + 海期),只接報價。**
**First release: Capital SKCOM overseas quotes (FX and overseas futures), quotes only.**

### 新增功能 / Added

- **宿主 `serve` 模式**(`Ozakboy.TradeKit.Capital.Host serve`):STA 執行緒掛 SKCOM,以 stdio 換行分隔 JSON 提供
  `products` / `subscribe` / `kline` / `status` / `shutdown` 與 `status` / `quote` / `tick` 事件。帳密只從環境變數讀、讀完即清;
  結束碼 0 正常、1 內部錯誤、2 缺帳密(不呼叫 Login)、3 SKCOM 不可用、4 登入失敗(不重試)、5 連不上。3002 斷線以 5、10、30、60 秒
  退避重新 EnterMonitorLONG,回到 3001 後重送全部訂閱。協定層 `ServeHost` 只認 `ISkQuoteSource`,COM 實作是 `SkComQuoteSource`。
  **The host's serve mode**: SKCOM on an STA thread behind newline-delimited JSON on stdio, with five methods and three events.
  Credentials come only from the environment and are cleared after reading; a 3002 disconnect is reconnected with backoff and
  every subscription is re-sent.
- **`CapitalHostSession`**:啟動與監督宿主(帳密只走子行程環境變數)、狀態機與 `StateChanged`、有界 `Channel` 事件
  (滿了丟最舊並計數)、`GetProductsAsync`、`SubscribeAsync`、`GetKLinesAsync`、`GetConnectionStatusAsync`、`StopAsync`。
  登入失敗回 `capital.login.failed` 且不重啟;意外結束以 1、2、5、10、30 秒退避重啟、最多 `MaxRestarts` 次並重送訂閱。
  **`CapitalHostSession`** starts and supervises the host, exposes the state machine and a bounded event channel, and offers
  products, subscriptions, K-lines and connection status. A failed login is never retried; an unexpected exit is restarted
  with backoff and the subscriptions are re-sent.
- **換算與時間**:`CapitalPriceScale`(原始整數 ÷ 10^小數位數;K 線浮點文字依小數位數四捨五入)、`CapitalExchangeClock`
  (交易所當地時間 → UTC,含夏令時間;分 K 開始時刻 = 結束 − 1 分鐘)。
  **Conversion and time**: `CapitalPriceScale` and `CapitalExchangeClock`.
- 型別:`CapitalHostOptions`(`DefaultHostPath`)、`CapitalCredentials`、`CapitalSymbol`、`CapitalQuote`、`CapitalTick`、
  `CapitalBar`、`CapitalProduct`、`CapitalConnectionStatus`、`CapitalKLineType`、`CapitalHostState`;錯誤碼集中在
  `CapitalErrorCodes`(`capital.*`)。
  Types, and the `capital.*` error codes in `CapitalErrorCodes`.
- **宿主隨套件出貨**:nupkg 的 `tools/capital-host/` 內含宿主(framework-dependent、win-x64),`buildTransitive` 在使用端
  建置時複製到 `$(OutDir)capital-host\`。
  **The host ships in the package** and is copied to `$(OutDir)capital-host\` by the buildTransitive targets.

### 技術改進 / Technical

- `tests/FakeCapitalHostExe` 連結宿主真正的協定層,只把 SKCOM 換成依實測模擬的假實作;`Ozakboy.TradeKit.Capital.Tests`
  與 `Ozakboy.TradeKit.Capital.Host.Tests` 涵蓋啟動、登入失敗不重啟、崩潰退避重啟並重送訂閱、斷線重連、價格換算、時區、
  商品解析與帳密不外洩。
  The fake host links the real protocol layer with a simulated SKCOM; the two test projects cover start-up, failed logins,
  restarts, reconnection, conversion, time zones, product parsing and credential masking.

### 已知限制 / Known limitations

- `serve` 模式、監督與重連只對假宿主測過;SKCOM 的呼叫與格式是 2026-11-06 以探針實測的(SKCOM 2.13.59)。
  The serve mode, supervision and reconnection are tested only against the fake host; the SKCOM calls and formats were
  measured with the probe on 2026-11-06.
- 3002 斷線重連、登入失敗代碼、FX / NYM / CME / CBOT 以外交易所的時區、群益每日登入與訂閱上限都未實測。
  The 3002 reconnect, login failure codes, other exchanges' time zones and Capital's daily login and subscription limits
  are unmeasured.
- 需使用者自行從群益官網下載 SKCOM,並以系統管理員身分註冊 x64 版;需要 .NET 10 Desktop Runtime。
  SKCOM must be downloaded from Capital and its x64 build registered by the user as an administrator; the .NET 10 Desktop
  Runtime is required.
