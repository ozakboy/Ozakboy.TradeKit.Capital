# Changelog

本檔記錄本套件所有值得注意的變更。
All notable changes to this package are documented here.

格式依循 [Keep a Changelog](https://keepachangelog.com/zh-TW/1.1.0/),版號依循
[語意化版本](https://semver.org/lang/zh-TW/)。
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the versioning follows
[Semantic Versioning](https://semver.org/).

## [0.3.0] - 2026-11-10

**國內期貨 / 選擇權即時報價、海外五檔 / 十檔、海外選擇權報價、期貨帳務查詢(唯讀)與 SKCOM 版本標示。依群益手冊 V2.13.59 實作,
2026-11-09 以 `probe-full` 對真 SKCOM 2.13.59 x64 實測過格式並依實測修正(只有期貨戶、帳戶沒有部位與委託)。沒有任何下單、刪改單、出入金能力。**
**Domestic futures and options live quotes, the overseas best 5 and best 10, overseas option quotes, read-only futures
account queries and the SKCOM version label. Built from Capital's V2.13.59 manual, then measured against real SKCOM
2.13.59 x64 with `probe-full` on 2026-11-09 and corrected (a futures-only account without positions or orders). The
package cannot place, amend or cancel orders and cannot move funds.**

### 新增功能 / Added

- **SKCOM 版本**:宿主登入前讀 `SKCenterLib_GetSKAPIVersionAndBit`,之後每則 `status` 事件與 `status` 回應都帶 `skcomVersion`;
  `CapitalHostSession.SkcomVersion`、`IsTestedSkcomVersion`、`CapitalSkcomVersion.Tested`(目前只有 `2.13.59_x64`)。不是實測版本時記一筆
  `capital.skcom.version_untested` 警告(事件 4015)並照常運作。
  **The SKCOM version**: the host reads it before login and reports it on every status. A version outside the tested
  list logs one warning and everything keeps running.
- **國內期貨 / 選擇權即時報價**(SKQuoteLib):`GetDomesticProductsAsync`(`RequestStockList`)、`SubscribeDomesticAsync`
  (`RequestStocks` 整組重送、每檔一頁 `RequestTicks`,最多 10 檔);事件 `CapitalDomesticQuote`、`CapitalDomesticTick`(含試撮旗標與回補)、
  `CapitalBest5`(含衍生一檔)、`CapitalDomesticLiveBar`。國內重連後與宿主重啟後都整組重送。
  **Domestic live quotes**: product lists, a subscription of up to 10 codes, and quote, tick, best 5 and live bar
  events. The whole set is re-sent after a reconnect and after a host restart.
- **海外五檔 / 十檔**:`best5`(推定隨成交訂閱而來)、`SubscribeDepthAsync`(`RequestMarketDepth`)→ `CapitalBest10`。
  **The overseas best 5 and best 10**: the best 5 is assumed to come with the ticks; `SubscribeDepthAsync` adds the best 10.
- **海外選擇權**(SKOOQuoteLib):自己的狀態 `OverseasOptionsState` / `OverseasOptionsStateChanged`(不可用不影響其他報價與結束碼)、
  `GetOverseasOptionProductsAsync`、`SubscribeOverseasOptionsAsync`、`SubscribeOverseasOptionDepthAsync`;事件 `CapitalOverseasOptionQuote`、
  `CapitalOverseasOptionTick`、`CapitalBest5` / `CapitalBest10`。
  **Overseas options** with their own state, products, subscriptions and events. An unavailable side affects neither
  the other feeds nor the exit code.
- **期貨帳務查詢(唯讀)**:`GetAccountsAsync`、`GetFutureRightsAsync`、`GetOpenInterestAsync`、`GetOverseaFutureRightsAsync`、
  `GetOverseaOpenInterestAsync`、`GetOrderReportAsync`、`GetFulfillReportAsync`。宿主第一次帳務請求才初始化 SKOrderLib;
  委託 / 成交回報兩次之間強制間隔 5.5 秒(手冊要求 5 秒)。記錄型別有依手冊解析的具名屬性(解析不出為 null)與 `RawFields`;
  帳號、戶名、身分證字號登記遮罩、不進日誌,`ToString()` 只露帳號末 3 碼。
  **Read-only futures account queries.** The host initialises SKOrderLib on the first account request and keeps 5.5
  seconds between report queries. Each record has named properties (null when unreadable) and RawFields. Account
  numbers, names and IDs are masked and never logged.
- **宿主協定**:新 method `tw-products`、`tw-subscribe`、`depth`、`oo-products`、`oo-subscribe`、`oo-depth`、`accounts`、`account-rights`、
  `account-open-interest`、`account-os-rights`、`account-os-open-interest`、`account-orders`、`account-fills`;新事件 `tw-quote`、`tw-tick`、
  `tw-best5`、`tw-live-bar`、`best5`、`best10`、`oo-quote`、`oo-tick`、`oo-best5`、`oo-best10`;`status` 加 `skcomVersion`、`overseasOptions`、
  `overseasOptionsReason`、`overseasOptionsCode`;新錯誤碼 `capital.subscribe.limit_exceeded`、`capital.overseas_options.unavailable`、
  `capital.account.unavailable` / `init_failed` / `cert_failed` / `query_rejected` / `query_failed` / `query_timeout`。0.2.x 用戶端可以照常接
  0.3.0 宿主(新欄位與新事件會被忽略)。
  **Host protocol**: new methods, events, status members and error codes. A 0.2.x client still works with the 0.3.0 host.

### 依實測修正 / Measured and corrected (2026-11-09)

逐項結論見 [`docs/DESIGN.md`](docs/DESIGN.md) §5.3、§7。The item-by-item results are in DESIGN §5.3 and §7.

- **價格 0 → null**:訂閱後第一則國內報價只填一部分、其餘是 0(約 10 毫秒後補齊),試撮回補列可能整筆是 0,海外選擇權沒有成交時開高低收是 0。
  `CapitalDomesticQuote`、`CapitalDomesticTick`、`CapitalOverseasOptionQuote` 的價格欄位改成可空,0 一律給 null。
  **Zero prices are null**: the price members of these three events are now nullable, because a zero means "no value"
  (a partial first snapshot, a simulated replay row, no trade).
- **即時分 K 的時間是分鐘結束**:`CapitalDomesticLiveBar.TimeUtc` 改成 `OpenTimeUtc` = 台北時間的結束時刻 − 1 分鐘(與歷史分 K 相同)。
  **Live bars are labelled by their end minute**: `TimeUtc` became `OpenTimeUtc`, one minute before the label.
- **國內商品清單**:固定欄位位置(選擇權清單的 5 欄系列列沒有幣別,不再被誤讀成跳動點);文件說明每檔都有一對代碼,結尾 `AM` 的只有日盤,夜盤要訂全盤代碼。
  **The domestic product list** is read by fixed positions; each code comes as a day-only "AM" code and a full-session code.
- **海外權益數改 31 欄**:草稿漏了「IB 代號」與「未沖銷期貨浮動損益」兩欄,之後的欄位全部錯一格。`CapitalOverseaFutureRights` 加 `IbCode`、`FloatingProfitLoss`。
  **Overseas rights have 31 fields**: two missing fields shifted every later one; `IbCode` and `FloatingProfitLoss` added.
- **海外未平倉 `@@,001`** 是查無資料,改回空清單(原本當失敗)。**`@@,001` means no data** and is now an empty list.
- **SKOrderLib 的中文亂碼**:帳務事件字串裡的中文是 Big5 位元組經字碼頁 950 最適對應來回轉一次的結果,一般無法還原。新增 `CapitalSkcomText`
  (`Repair` 還原實測看過的固定字樣、`LooksGarbled` 判斷是否像亂碼),幣別與錯誤訊息經過它,`RawFields` 保留原樣。
  **Garbled Chinese from SKOrderLib** cannot be restored in general; `CapitalSkcomText` restores the measured fixed
  phrases and detects the garbling.
- `CapitalOverseasOptionProduct` 加 `LastTradeDate`(清單第 5 欄);`CapitalDomesticTick` 加 `Micros`(原值,實測只有毫秒)。
  `LastTradeDate` and `Micros` added.
- 文件:國內成交的 `nDate` 是日曆日、期貨只有毫秒精度、海外選擇權履約價不放大、同一個帳務查詢函式連續送會回 1019。
  Docs: calendar-day tick dates, millisecond ticks, an unscaled option strike, and code 1019 for back-to-back account queries.
- 探針:選擇權優先挑全盤代碼、海外選擇權挑最接近小那斯達克期貨價格的 NQ 買權與賣權、帳務查詢遇 1019 遞增等待再試。
  The probe prefers full-session codes, picks at-the-money NQ options and retries a query rejected with 1019.

### 技術改進 / Technical

- 宿主回應的錯誤訊息與字串欄位也經用戶端的遮罩(帳密與已知帳號)才放進 `Error`。
  Error messages from the host now go through the client's masking before they reach an `Error`.
- `tests/FakeCapitalHostExe` 與宿主協定層測試模擬全部新功能,含失敗情境與遮罩驗證;假宿主的帳務、商品清單、第一則報價、成交毫秒、海外選擇權清單
  改照 2026-11-09 實測,並以實測樣本(已遮罩)加回歸測試。
  The fake host and the protocol tests simulate every new feature; the fake host now follows the 2026-11-09
  measurement, and regression tests replay the redacted samples.

### 已知問題 / Known issues

- **仍待實測**:有部位 / 有委託時的帳務欄位與數值語意(這次帳戶全空)、權益數 sCoinType 1 / 2 與未平倉格式 2 / 3(被 1019 擋下)、夜盤跨午夜的日期、
  國內 10 檔上限、海外五檔由成交還是十檔訂閱觸發、海外選擇權成交的時區、回報查詢字串的中文編碼、`serve` 宿主本身對真 SKCOM 跑。清單見 DESIGN §7。
  **Still to be measured**: account rows with positions or orders, the queries blocked by 1019, dates after midnight,
  the 10-code cap, what triggers the overseas best 5, overseas option tick time zones, report encoding, and `serve`
  itself against real SKCOM; see DESIGN §7.
- SKOrderLib 的中文(戶名、訊息)在字碼頁 950 的電腦上是不可逆的亂碼,只能還原固定字樣。
  SKOrderLib's Chinese is irreversibly garbled on code page 950; only fixed phrases are restored.
- 同一個帳務查詢函式在前一次結束後 1–2 秒再送會回 1019(`capital.account.query_rejected`),宿主不替事件型查詢排間隔。
  The same account query sent again 1-2 seconds later is rejected with 1019; the host does not space these queries.
- 帳務查詢以「##」、狀態事件或安靜時間判斷結束(沿用 probe-full);前一個逾時查詢的遲到資料可能混進下一個同類查詢。
  Account queries end on "##", the status event or a quiet period; late rows of a timed-out query may mix into the next one.

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
- **文件**:README 的需求段誤寫宿主是 framework-dependent、要另裝 .NET 10 Desktop Runtime;宿主實際以 self-contained 出貨(0.1.0 起即如此),不用另裝。
  **Docs**: the README said the host needs the .NET 10 Desktop Runtime. The host ships self-contained, so no runtime
  install is needed.

### 已知問題 / Known issues

- **海外 1 分 K 只保留約一個月,一次請求也有筆數上限**(實機 2026-11-07):往回每 7 天查一次,10-08 以前每段都回 0 根(CME 約 30 天、外匯約三週);
  一次查 30 天時 CME 剛好回 30,000 根、外匯只回最後 16 個交易日,超過的最舊部分被截掉且不報錯。呼叫端記錄「已下載到哪裡」時,
  請以實際拿到的第一根為準,不要以請求的起日為準。
  **Overseas minute bars go back about one month, and one request has a row limit.** Older 7-day requests return
  no rows. A 30-day request returned exactly 30,000 rows for CME and only the last 16 trading days for FX. Record
  coverage from the first bar you receive, not from the requested start date.

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
