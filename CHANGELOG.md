# Changelog

本檔記錄本套件所有值得注意的變更。
All notable changes to this package are documented here.

格式依循 [Keep a Changelog](https://keepachangelog.com/zh-TW/1.1.0/),版號依循
[語意化版本](https://semver.org/lang/zh-TW/)。
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the versioning follows
[Semantic Versioning](https://semver.org/).

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
