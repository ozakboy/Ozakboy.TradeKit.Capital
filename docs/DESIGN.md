# Ozakboy.TradeKit.Capital 設計(0.3.0,未發佈)

群益證券 SKCOM 海外報價(外匯 + 海外期貨)的 .NET 10 用戶端,0.2.0 起另可查國內期貨歷史 K 線(§2.1、§3.1)。0.3.0 加上 SKCOM 版本標示(§2.2)、
國內期貨 / 選擇權即時報價(§2.3)、海外五檔 / 十檔(§2.4)、海外選擇權報價(§2.5)與期貨帳務查詢(唯讀,§2.6);用戶端見 §3.2。
**沒有任何下單、刪改單、出入金能力**,也不做需要證券戶的功能(股票報價、證券下單、庫存、複委託)。0.3.0 的新功能依手冊
《策略王COM元件使用說明》V2.13.59 實作,**2026-11-09 以 `probe-full` 對真 SKCOM 實測過格式**(§5.3,只有期貨戶、帳戶沒有部位與委託),
並依實測修正;§7 是 23 項的逐項結論,仍待實測的(有部位時的帳務數值、夜盤跨午夜、海外選擇權成交時區、`serve` 本身等)在那裡列明。

**相容版本**:以 SKCOM 2.13.59 x64 實測;COM 介面依該版型別庫產生(§5.2 最後)。COM 雙重介面照方法順序走 vtable,群益改版若動到方法順序,
宿主會呼叫錯方法——升版前請先跑探針並重跑產生器。宿主回報的版本不在已實測清單時,用戶端記一筆警告日誌並照常運作。

## 1. 分層

| 專案 | 型態 | 責任 |
|---|---|---|
| `Ozakboy.TradeKit.Capital.Host` | x64 exe(`net10.0-windows`、WinForms 訊息迴圈) | 掛 SKCOM(STA + 訊息迴圈),以 **stdio 換行分隔 JSON** 對外提供服務;`probe` / `probe-tw` / `probe-full`(§5.2)指令保留 |
| `Ozakboy.TradeKit.Capital` | 類別庫(`net10.0`) | 啟動 / 監督宿主子行程、送請求、讀事件;把整數價格轉 decimal、交易所當地時間轉 UTC;預期失敗回 `Result<T>` |
| `tests/FakeCapitalHostExe` | exe | 假宿主:同一套 stdio 協定,行為比照實測(見 §5),給用戶端測試用 |
| `tests/Ozakboy.TradeKit.Capital.Tests` | MSTest(MTP) | 協定、解析、時區、監督 |
| `tests/Ozakboy.TradeKit.Capital.Host.Tests` | MSTest(MTP) | 宿主的協定層(以假 `ISkQuoteSource` 取代 COM) |

**為什麼 stdio 不用 HTTP**:宿主只有一個呼叫端(啟動它的父行程),stdio 天生只有父行程拿得到,不必開 port、不必 token、不會被本機其他行程連上;協定同 MCP stdio 的模式,好測。

## 2. 宿主 `serve` 模式

啟動:`Ozakboy.TradeKit.Capital.Host serve`。

**環境變數**(讀完立刻從自己的環境清掉;不寫檔、不進日誌):
- `CAPITAL_LOGIN_ID`:身分證字號(宿主轉大寫)
- `CAPITAL_PASSWORD`:登入密碼
- `CAPITAL_LOG_DIR`:SKCOM 自己的紀錄目錄(`SKCenterLib_SetLogPath`;可空)
- `CAPITAL_PARENT_PID`(0.2.0):父行程 PID,父行程看門狗用(不是機密,不清;沒有時只靠 stdin EOF)

**輸出**:stdout 只放協定訊息(每行一個 JSON 物件,UTF-8、無 BOM);人看的紀錄走 stderr(不得含帳密;遇到登入帳號字面值一律換成 `<ID>`)。

**啟動流程**(在 STA 執行緒上):
1. 建立 SKCenterLib、SKReplyLib、SKOSQuoteLib。失敗 → 事件 `status{state:"faulted", reason:"skcom_not_registered"|"skcom_file_missing"|"skcom_create_failed", hresult}`,結束碼 3。(0x80040154 = 沒註冊;0x8007007E = 登錄檔指向的 DLL 不存在)
2. 掛 `OnReplyMessage`(回 -1,手冊必要)與海外報價事件。接著建立國內的 SKQuoteLib 並掛 `_ISKQuoteLibEvents`(0.2.0;探針實測的順序是登入前建立);
   失敗只讓國內不可用(`domesticReason` = `skcom_not_registered` / `skcom_file_missing` / `skcom_create_failed`),海外照常。
3. `status{state:"logging_in"}` → `SKCenterLib_Login`。非 0 → `status{state:"login_failed", code, message}`,結束碼 4,**不重試**(300 密碼錯 / 306 身分證錯 / 307 鎖定 / 600 憑證錯 / 604 憑證過期)。
4. `status{state:"connecting"}` → `SKOSQuoteLib_EnterMonitorLONG`;等 `OnConnect(3001)`(實測只會有 3001,沒有 3003),60 秒沒來 → `status{state:"faulted", reason:"connect_timeout"}`,結束碼 5。
5. `status{state:"ready"}`。
6. 之後收到 `OnConnect(3002)` → `status{state:"disconnected"}`,以 5、10、30、60 秒退避重新 `EnterMonitorLONG`;回到 3001 後**重送全部訂閱**再發 `ready`。這是連線類錯誤,可以重試;登入不重登。
7. (0.2.0)海外第一次 ready 之後連國內,見 §2.1。國內的任何結果都**不影響海外的 ready 與結束碼**。

**結束訊號**(0.2.0 起兩個,效果相同:LeaveMonitor 海外與國內後以 0 結束):stdin EOF(父行程關掉管線或消失),以及父行程看門狗——
啟動時以 `CAPITAL_PARENT_PID` 開住父行程(握著行程控制代碼,PID 重複使用不會認錯),每 5 秒檢查,不在了就視同 stdin 結束。
任一訊號(或協定層回傳)之後**結束保險**開始倒數 15 秒,時間到行程還在就 `Environment.Exit`:防 STA 卡在 SKCOM 的同步呼叫(Login、EnterMonitorLONG、
LeaveMonitor、解除連接點)時協定層根本處理不到結束訊號。父行程看門狗是 EOF 的保險:父行程 stdin 寫入端若被其他行程繼承,父行程消失後 EOF 不會來。
用戶端另把宿主放進 Job Object(§3),父行程被強制結束時由系統收掉宿主。

**請求**(stdin,每行一個):`{"id":<整數>,"method":"<名稱>","params":{...}}`;**回應**:`{"id":<同>,"result":{...}}` 或 `{"id":<同>,"error":{"code":"capital.*","message":"…"}}`。請求在 STA 上依序處理,一次一個。

| method | params | result | 說明 |
|---|---|---|---|
| `products` | — | `{items:[{exchange, exchangeName, code, name, lastTradeDate, firstNoticeDate, kind, orderExchange, orderCode}]}` | `RequestOverseaProducts` 收到 `##` 開頭那筆為止;再 `GetOverseaProductDetail(1)` 收到 `##` 為止,依 (exchange, code) 併入 `kind` 等欄位;各 60 秒逾時 → `capital.products.timeout` |
| `subscribe` | `{symbols:["FX,SUSDTWD", ...]}` | `{subscribed:[...]}` | 與已訂集合取聯集;新的那批以 `RequestStocks(ref -1, 以 # 串接)`,每檔再 `RequestTicks(ref -1, 代碼)`;宿主記住集合以便重連後重送。回傳非 0 → `capital.subscribe.rejected`(附代碼與訊息) |
| `kline` | `{symbol, type: 0\|1\|2\|3, start:"yyyyMMdd", end:"yyyyMMdd"}` | `{lines:["2026/11/02 16:01, 157.889999, …, 141", ...]}` | `RequestKLineByDate(symbol, type, start, end, 1)`;沒有結束事件,以「最後一筆後 1.5 秒沒新資料」或「10 秒都沒資料」結束;只收 `bstrStockNo` 等於這次代碼(去掉交易所)的那些行 |
| `kline-tw` | `{code, type: 0\|4, session: 0\|1, start:"yyyyMMdd", end:"yyyyMMdd"}` | `{lines:["2026/11/06 13:45, 45395.00, …, 812", ...]}` | 0.2.0,國內期貨歷史 K,見 §2.1 |
| `status` | — | `{connected, connections, outLimit, domestic, domesticReason?, domesticCode?, overseasOptions, overseasOptionsReason?, overseasOptionsCode?, skcomVersion?}` | `IsConnected` + `GetQuoteStatus`(海外);國內欄位 0.2.0 起,海外選擇權與版本 0.3.0 起 |
| `shutdown` | — | `{}` | `LeaveMonitor`(海外選擇權、國內、海外)後結束,結束碼 0 |
| `tw-products` | `{market: 2\|3}` | `{market, rows:["TX00,台指近,20261118,TXFK6,1,NTD", ...]}` | 0.3.0,§2.3 |
| `tw-subscribe` | `{codes:[...]}` | `{subscribed:[...]}` | 0.3.0,§2.3 |
| `depth` | `{symbols:[...]}` | `{depth:[...]}` | 0.3.0,§2.4 |
| `oo-products` | — | `{items:[...]}` | 0.3.0,§2.5 |
| `oo-subscribe` / `oo-depth` | `{symbols:[...]}` | `{subscribed:[...]}` / `{depth:[...]}` | 0.3.0,§2.5 |
| `accounts` | — | `{items:[OnAccount 原文...]}` | 0.3.0,§2.6 |
| `account-rights` | `{account, coin: 0\|1\|2}` | `{rows, status, statusMessage?}` | 0.3.0,§2.6 |
| `account-open-interest` | `{account, format: 1–3}` | 同上 | 0.3.0,§2.6 |
| `account-os-rights` | `{account}` | 同上 | 0.3.0,§2.6 |
| `account-os-open-interest` | `{account, format: 1\|2}` | 同上 | 0.3.0,§2.6 |
| `account-orders` / `account-fills` | `{account, format: 1–9 / 1–5}` | `{rows}` | 0.3.0,§2.6 |

**事件**(stdout):`{"event":"<名稱>","data":{...}}`
- `status`:如上。`state` 永遠是**海外**狀態。0.2.0 起國內開始後(或確定不可用後)每則 status 都帶 `domestic`(`connecting` / `ready` / `disconnected` /
  `unavailable`)、`domesticReason`(`enter_rejected` / `ready_timeout` / `connection_lost` / `skcom_*`)、`domesticCode`(國內 EnterMonitorLONG 被拒的群益代碼);
  國內狀態改變時再送一則 status,`state` 照舊是海外目前的狀態(舊版用戶端看到的只是重複的同一個狀態,無害)。要看海外的轉換就把連續重複的 state 合併。
- `quote`:`{symbol:"CME,NQ0000", name, decimals, denominator, tradingDay, open, high, low, close, settle, ref, bid, bidQty, ask, askQty, totalQty, tickQty}`,價格欄位是**原始整數**(long)。來源:`OnNotifyQuoteLONG(idx)` → `GetStockByIndexNineDigitLONG(idx)`。
- `tick`:`{symbol, ptr, date, time, price, qty, history}`,`price` 原始整數、`date` = yyyyMMdd、`time` = HHmmss(交易所當地時間)、`history` = 訂閱當下的回補。
- 索引 → 代碼的對照:第一次看到某個索引(報價或成交都可能先到;實測成交先到)就 `GetStockByIndexNineDigitLONG` 取 `bstrExchangeNo + "," + bstrStockNo` 並快取;小數位數同時快取,成交事件的 `price` 一樣是原始整數,由用戶端依該代碼的 `decimals` 換算(宿主在 tick 事件也帶 `decimals`,省得用戶端等報價)。

- 0.3.0 事件:`tw-quote`、`tw-tick`、`tw-best5`、`tw-live-bar`(§2.3)、`best5`、`best10`(§2.4)、`oo-quote`、`oo-tick`、`oo-best5`、`oo-best10`(§2.5)。
  `status` 在宿主讀到版本後一律帶 `skcomVersion`(`ProtocolWriter.SetStatusSuffix`);海外選擇權開始後(或確定不可用後)帶 `overseasOptions` /
  `overseasOptionsReason` / `overseasOptionsCode`,字串與國內同一組,`state` 照舊是海外狀態。

**COM 抽象**:`ISkQuoteSource`(登入、連線、商品、訂閱、K 線、事件;0.2.0 起加國內的 `OpenDomestic` / `EnterDomesticMonitor` / `LeaveDomesticMonitor` /
`RequestDomesticKLine` 與 `DomesticConnection` / `DomesticKLine` / `DomesticKLineComplete` 事件)隔開 COM;`SkComQuoteSource` 是真實作(國內用
`SkComDomesticInterop` 的 ISKQuoteLib 43 格 vtable 與 `QuoteEventsSink`),協定層只認介面,測試以假實作驅動。

### 2.1 國內報價(0.2.0)

**連線狀態機**(在 STA 的收件匣裡,與海外各自計次、各自一個計時器):

| 狀態(協定字串) | 進入 | 離開 |
|---|---|---|
| `connecting` | 海外第一次 ready 後 `SKQuoteLib_EnterMonitorLONG` 回 0 | 收到 3003 → `ready`;60 秒(`DomesticReadyTimeout`)沒有 3003 → `unavailable{ready_timeout}`;3002 → `disconnected` |
| `ready` | 收到 3003(3001 只記紀錄;首次逾時後才到的 3003 也接受) | 3002 → `disconnected{connection_lost}` |
| `disconnected` | 3002 | 以 5、10、30、60 秒退避重新 EnterMonitorLONG;重連期間 3002 或 60 秒沒 3003 → 次數 +1 再退避;收到 3003 → `ready` |
| `unavailable` | 建不起物件、EnterMonitorLONG 回非 0(`enter_rejected`,代碼在 `domesticCode`)、首次 3003 逾時 | 不重試 |

**`kline-tw`**:參數驗證(代碼非空、≤ 32 字元、不含逗號 / `#` / 空白 / 控制字元;type 0 或 4;session 0 或 1;日期 yyyyMMdd)→ 國內 `connecting`(首次連線)時
**先存著**,有結果(ready / unavailable / disconnected)再依序處理,其他請求照常;不是 `ready` → `capital.domestic.unavailable`(data:`domestic`、`reason`、`skCode`)。
送 `RequestKLineAMByDate(code, type, 1, session, start, end, 1)`:回非 0 → `capital.kline.rejected`(data:`skCode`、`skMessage`、`symbol`)。
之後只收 `bstrStockNo == code` 的行,以 `OnKLineComplete` 結束;60 秒(`DomesticKLineTimeout`)沒結束 → `capital.kline.timeout`(data:`symbol`、`received`)。
逾時請求的結束事件之後還會來(實測每個請求一則),宿主記一筆「待吃掉」,到時候吃掉,免得它提早結束下一個請求;同一個請求期間已吃掉過一則就不再累加
(當成吃到的是自己的),避免一則真的遺失時之後每個請求都逾時;國內 3002 或重新就緒時歸零。同代碼的逾時請求若還有資料在路上,可能混進下一個請求——這是已知限制。
請求一樣在 STA 上依序處理,一次一個。

0.3.0 起 `ISkQuoteSource` 拆成 partial(`ISkQuoteSource.Live.cs` / `.OverseasOptions.cs` / `.Accounts.cs`),`SkComQuoteSource`、假宿主與測試的
`ScriptedSkQuoteSource` 都同樣拆。新的 COM 呼叫只用 `SkCom*Interop.cs` 既有的真簽章(產生器規格沒有改)。

### 2.2 SKCOM 版本(0.3.0)

`Open` 成功後、登入前呼叫 `SKCenterLib_GetSKAPIVersionAndBit(登入帳號)`(手冊 4-1-10,回傳例 `2.13.30_x64`;probe-full A 段也在登入前讀)。讀到的文字
(經帳號遮罩、去前後空白)附在之後每一則 status 事件的 `skcomVersion`,`status` 回應也帶;讀不到(例外或空字串)只寫一行 stderr,照常啟動。
用戶端 `CapitalSkcomVersion.Tested`(目前只有 `2.13.59_x64`,比較忽略大小寫)決定 `IsTestedSkcomVersion`,不在清單時每個版本記一次事件 4015。

### 2.3 國內期貨 / 選擇權即時報價(0.3.0;格式 2026-11-09 實測,§5.3)

國內連線沿用 §2.1 的狀態機;以下請求在國內首次連線中先存著,有結果再處理。

- **`tw-products {market}`**:`RequestStockList(market)`(手冊 4-4-17:期貨 2、選擇權 3;沒期貨戶 / 沒簽聲明書回 3031)→ `OnNotifyStockList(market, 原文)`
  (4-4-h:一則多筆以 `;` 分隔、類股以換行分隔,`##` 開頭那則是結尾)。只收同市場別的那幾則,切成一筆一行回 `rows`;60 秒(`ProductsTimeout`)沒有 `##`
  → `capital.products.timeout`(`received`),回非 0 → `capital.products.rejected`。用戶端依 `[代碼],[名稱],[最後交易日],[交易所商品代碼],[跳動點],[幣別]` 的
  固定位置解析(實測期貨、選擇權都是 6 欄;選擇權的系列列是 5 欄、沒有幣別;結尾是獨立一則 `##,,;`)。
- **`tw-subscribe {codes}`**:代碼規則同 `kline-tw`。整組最多 **10** 檔(手冊 4-4-1「單一 SKQuoteLib 物件,僅一個訂閱 Tick 報價功能(10 檔商品)」;
  每檔都訂成交,所以整組 10 檔),超過 → `capital.subscribe.limit_exceeded`(`limit`、`requested`),什麼都不送。新代碼先以
  `GetStockByMarketAndNo`(4-4-33,先市場 2 再 3)查索引與 `sDecimal`(沒訂報價時只有基本資料,夠用),兩個市場都查不到 → `capital.subscribe.rejected`
  (`skCode` 是群益代碼,例如 3023)。接著把**完整集合**以逗號串接送一次 `RequestStocks(psPageNo = 1, …)`(4-4-2:自 V2.13.54 起頁碼固定 1、一個物件只有一組即時報價訂閱、
  最多 100 檔),每個新代碼一頁 `RequestTicks(page, code)`(4-4-3:頁碼從 0 起、一頁一檔;頁碼 = 代碼在集合裡的位置)。國內斷線中送的先記住;
  國內 3003(含重連)後清掉索引快取、重查索引、整組重送。
- **事件**(價格一律原始整數,用戶端 ÷ 10^`decimals`;4-4-u 的例子:台指期 4557900 → 45579.00):
  - `tw-quote`:`OnNotifyQuoteLONG(market, idx)` → `GetStockByIndexLONG`(4-4-26)的 `SKSTOCKLONG` 全欄位(5-21;證券專用欄位也照送,用戶端不給具名屬性),
    `code` = 訂閱時的代碼(群益回的 `stockNo` 另外帶)。
  - `tw-tick`:`OnNotifyTicksLONG` / `OnNotifyHistoryTicksLONG`(4-4-m / 4-4-l)的 `ptr、date、time(HHmmss)、micros(nTimemillismicros,前三位毫秒、後三位微秒,5-6)、
    bid、ask、close、qty、simulate(1 = 試算 / 試撮)、history`。(市場別, 索引)先查快取;沒有時以 `GetStockByIndexLONG` 取基本資料並快取
    (手冊 4-4-l 建議不要在成交事件裡呼叫它,訂閱時已先查過索引,這只是保險)。
  - `tw-best5`:`OnNotifyBest5LONG`(4-4-n)五檔 + 衍生一檔(`extendBid`…)+ `simulate`;陣列 `bidPrices / bidQuantities / askPrices / askQuantities`。
  - `tw-live-bar`:`OnNotifyLiveKLineData`(4-4-u)`type`(0 資料重置、價格都給 0;1 分 K 含當日回補;2 該分鐘每筆)、`date`、`time`(HHmm)、OHLC、`qty`;
    `decimals` 依代碼查(訂閱或報價時記下),不知道就不寫,用戶端的價格為 null。
- **時間**:用戶端把 `date + time (+ micros)` 當台北時間(UTC+8)轉 UTC。實測(§5.3):夜盤成交的 `nDate` 是**日曆日**(午夜前;跨午夜未實測)、
  期貨的 `nTimemillismicros` 有值但只有毫秒(後三位 000)、即時分 K 的 HHmm 是**那一分鐘的結束**(用戶端 `OpenTimeUtc` = 結束 − 1 分鐘,同歷史分 K)。
  原始值一併給用戶端。
- **0 → null**(實測):訂閱後第一則 `tw-quote` 可能只填一部分、其餘是 0;成交的試撮回補列可能整筆是 0。國內價格不可能是 0,所以用戶端把報價、成交、
  海外選擇權報價的價格欄位 0 一律給 null(「這則沒有值」),不沿用上一則。宿主照原值送。
- **AM 代碼**(實測):商品清單裡每檔都有一對代碼,結尾多 `AM` 的只有日盤、沒有 `AM` 的是全盤(含夜盤);夜盤行情要訂全盤代碼。

### 2.4 海外五檔 / 十檔(0.3.0;格式 2026-11-09 實測,觸發來源未分出,§5.3)

- `best5`:`OnNotifyBest5NineDigitLONG`(九位擴充整數,5-13「需自行處理小數,參考 sDecimal」;實測 ÷ 10^sDecimal 正確),推定由既有的 `RequestTicks` 觸發
  (比照國內 4-4-3;實測時與 `RequestMarketDepth` 連著送,還分不出來)。
- **`depth {symbols}`**:每檔 `RequestMarketDepth(ref -1, 代碼)`(型別庫有、手冊沒有章節)→ `best10`(`OnNotifyBest10NineDigitLONG`)。回非 0 →
  `capital.subscribe.rejected`。斷線中送的先記住,海外 3001 重連後與報價、成交一起重送。
- 索引 → 代碼沿用 §2 的海外快取;`symbol` 與 `decimals` 由它給。

### 2.5 海外選擇權(0.3.0;手冊沒有 SKOOQuoteLib 的章節;連線、清單、訂閱、報價 2026-11-09 實測,成交未實測,§5.3)

- **建立**:SKOOQuoteLib 與國內一樣在登入前建立並掛事件(probe-full 的順序;施工單原寫「海外就緒後建立」,改成與國內、探針一致,見回報),海外第一次 ready 後
  `SKOOQuoteLib_EnterMonitorLONG`。建不起來 / EnterMonitorLONG 回非 0 / 60 秒(`OverseasOptionsReadyTimeout`)沒連線事件 → `unavailable`(不重試;逾時後才到的連線事件也接受)。
- **狀態機**:與國內同一套階段與退避(`connecting` / `ready` / `disconnected` / `unavailable`),各自一個計時器;`OnConnect` 的 3001 或 3003 都算連上(實測只來 3001,
  Enter 後約 18 秒)、3002 斷線(推定,沒遇到)。任何結果只改 `overseasOptions` 欄位,不影響海外、國內與結束碼。重新就緒時清索引快取並重送全部訂閱。
- **`oo-products`**:`RequestProducts` → `OnProducts` 每筆一則,以 `##` 開頭那筆結尾(實測 `##,##,##,##,##`),60 秒逾時。實測格式
  `交易所,交易所名稱,代碼,名稱,最後交易日`,訂閱代碼 = `交易所,代碼`。
- **`oo-subscribe {symbols}`**:新的那批以 `#` 串接 `RequestStocks(ref -1, …)`,每檔再 `RequestTicks(ref -1, …)`;**`oo-depth`**:每檔 `RequestMarketDepth(ref -1, …)`。
  首次連線中先存著;不可用 → `capital.overseas_options.unavailable`(data:`overseasOptions`、`reason`、`skCode`);斷線中先記住。
- **事件**:`oo-quote`(`GetStockByIndexLONG` 的 `SKFOREIGNLONG`,價格 32 位元整數;另帶 `callPut`、`strike` 原值)、`oo-tick`(`date` / `time` 字串,同海期)、
  `oo-best5`、`oo-best10`。用戶端時間依 `CapitalExchangeClock`;交易所沒有對照時 `TimeUtc` 為 null(事件不丟),`IsTimeZoneVerified` 照時區表。

### 2.6 期貨帳務查詢(0.3.0,唯讀;沒有部位的帳戶 2026-11-09 實測,§5.3)

- **安全**:只呼叫 `SkComOrderInterop.cs` 現有 10 個唯讀真簽章裡的 9 個(`GetOFOpenInterestWithDetails` 只給探針);會改變委託、部位或資金的方法全是
  `Unused*` 佔位,`SkComOrderInteropSafetyTests` 的白名單沒有改。
- **延遲初始化**:SKOrderLib 物件在登入前建立並掛事件(probe-full 的順序;建不起來 → 之後每個帳務請求回 `capital.account.unavailable`,其他功能照常)。
  第一次帳務請求才依序:`SKOrderLib_Initialize`(4-2-1)→ `GetUserAccount`(4-2-2,等 `OnAccount` 結束)→ `ReadCertByID(登入帳號)`(4-2-3)。
  每一步成功一次就不再做;失敗回 `capital.account.init_failed`(`step` = `initialize` / `get_user_account`)或 `capital.account.cert_failed`(`read_cert`),
  附群益代碼,下次請求再試(不是登入,重試無害)。登入帳號只留在宿主記憶體。
- **等待結束**(沿用 probe-full,§5.2):以事件回傳的查詢在送出後輪詢,符合任一條就結束——收過狀態事件且收過 `##`;收過狀態事件或 `##` 之後安靜 2 秒
  (`AccountQuietAfterStatus`);從最後一則事件(沒有就從送出時)起安靜 6 秒(`AccountQuiet`)。60 秒(`AccountQueryLimit`)還沒結束 → `capital.account.query_timeout`
  (`received`)。`OnAccount` 沒有狀態事件,也沒有手冊寫的結尾標記,所以第一次要等 6 秒安靜。狀態事件回 1 → `capital.account.query_failed`(訊息已遮罩);
  查詢函式回非 0 → `capital.account.query_rejected`。只收這次查詢對應的事件名稱;前一個逾時查詢的遲到資料可能混進下一個同類查詢(已知限制)。

| method | 呼叫(手冊) | 結果事件 / 狀態事件 |
| --- | --- | --- |
| `account-rights` | `GetFutureRights(登入帳號, 帳號, sCoinType)`(4-2-41) | `OnFutureRights`(4-2-i,`##` 結尾)/ `OnFutureRightsStatus`(4-2-v) |
| `account-open-interest` | `GetOpenInterestGW(…, nFormat)`(4-2-89,手冊只列 1) | `OnOpenInterest`(4-2-d GW 格式 1)/ `OnOpenInterestGWStatus`(4-2-t) |
| `account-os-rights` | `GetRequestOverSeaFutureRight`(4-2-45) | `OnOverSeaFutureRight`(4-2-k)/ `OnOverseaFutureRightsStatus`(4-2-w) |
| `account-os-open-interest` | `GetOverseaFutureOpenInterestGW(…, 1 彙總 / 2 明細)`(4-2-76) | `OnOFOpenInterestGWReport`(4-2-q)/ `OnOverseaFutureOpenInterestGWStatus`(4-2-u) |
| `account-orders` / `account-fills` | `GetOrderReport` / `GetFulfillReport`(4-2-97 / 4-2-98,阻塞式、直接回字串) | — |

- **回報查詢**:兩次(兩種共用)之間至少 5.5 秒(`ReportInterval`;手冊「每次查詢間需間隔五秒」,多留半秒),宿主在呼叫前等;STA 在阻塞期間不處理別的請求。
  回傳以 `\r\n` 分行回 `rows`;COM 例外 → `capital.account.query_failed`(`hresult`)。`M003` 查無資料、`M999` 查詢錯誤由用戶端判斷(實測沒有委託時就是
  4 個字元 `M003`)。手冊說回報的中文是 UTF-8(實測只拿到 `M003`,未驗證,宿主不轉碼)。
- **同一查詢連續送會被擋**(實測):同一個查詢函式在前一次結束後 1–2 秒再送(這次只量到這麼短的間隔),群益回 1019 `SK_ERROR_QUERY_IN_PROCESSING` → `capital.account.query_rejected`
  (`skCode` 1019)。宿主目前不替事件型查詢排間隔(需要的間隔還沒量到,見 §7 第 23 項)。
- **中文亂碼**(實測):SKOrderLib 事件字串裡的中文是 Big5 位元組被當 Latin-1 再經字碼頁 950 最適對應來回轉一次的結果,一般無法還原。用戶端
  `CapitalSkcomText.Repair` 只還原實測看過的固定字樣(查無資料、查詢成功、基幣),用在幣別欄與錯誤訊息;`RawFields` 保留原樣。詳見 §5.3。
- **隱私**:`OnAccount` 一到就把帳號號碼、`分公司代碼 + 帳號`(第 2、3 欄兩種接法都登記)、身分證字號、戶名登記到 `HostLog` 遮罩(`<ACCT>` / `<ID>` / `<NAME>`),
  stderr 與所有寫出的錯誤訊息都經遮罩;原文只放在 stdout 回應裡。

## 3. 用戶端 `Ozakboy.TradeKit.Capital`

- `CapitalHostOptions`:`HostExecutablePath`(必填)、`LogDirectory`、`StartTimeout`(預設 90 秒)、`MaxRestarts`(預設 5)。
- `CapitalCredentials(LoginId, Password)`:`Validate()` → 身分證字號 1 個英文字母 + 9 個數字(轉大寫後比對);失敗 `capital.credentials.invalid`,訊息不回顯任何字元。`ToString()` 不含內容。
- `CapitalSymbol(Exchange, Code)`:`Parse("FX,SUSDTWD")` / `ToRequestString()`;`ToString()` = `FX,SUSDTWD`。
- `CapitalHostSession : IAsyncDisposable`
  - `StartAsync(CapitalCredentials, CancellationToken)` → `Result`:啟動子行程(帳密只走環境變數)、等 `ready` 或失敗狀態。`login_failed` → `capital.login.failed`(`Error.Data["code"]` = 群益代碼,`["locked"]` = 307),**不重啟**;`faulted` 依 reason 對應 `capital.skcom.not_registered` / `capital.skcom.file_missing` / `capital.connect.timeout`。
  - `State`(`Starting / LoggingIn / Connecting / Ready / Disconnected / LoginFailed / Faulted / Stopped`)+ `StateChanged` 事件。
  - `Events`:`ChannelReader<CapitalMarketEvent>`(有界,滿了丟最舊的並計數 `DroppedEvents`),型別 `CapitalQuote` / `CapitalTick`。
  - `GetProductsAsync`、`SubscribeAsync(IEnumerable<CapitalSymbol>)`、`GetKLinesAsync(CapitalSymbol, CapitalKLineType, DateOnly from, DateOnly to, int decimals)`、`StopAsync`。
  - 子行程意外結束(非 3/4 結束碼)→ 以 1、2、5、10、30 秒退避重啟,最多 `MaxRestarts` 次,重啟後重送訂閱;超過 → `Faulted`。
- **換算**(`CapitalPriceScale`):原始整數 ÷ 10^decimals → decimal(`nDenominator` 實測都是 1;不是 1 時以 `capital.price.fraction_unsupported` 記一次警告並照 decimals 換算);K 線的文字價格 `decimal.Parse(InvariantCulture)` 後 `Math.Round(值, decimals, MidpointRounding.AwayFromZero)`(群益送的是單精度浮點文字,157.889999 → 157.89)。
- **時間**(`CapitalExchangeClock`):交易所 → Windows 時區 ID;已實測:`FX`、`NYM` = `Eastern Standard Time`,`CME`、`CBOT` = `Central Standard Time`。其餘交易所先依交易所所在地填上並標 `IsVerified = false`(呼叫端可據此限制)。成交時間 = 當地 `date + time` 轉 UTC;**分 K 的時間是那一分鐘的結束**,`CapitalBar.OpenTimeUtc` = 結束 − 1 分鐘;日 / 週 / 月 K 只有日期(`TradingDay`)。
- 型別:`CapitalQuote{Symbol, Name, Decimals, TradingDay(DateOnly), Open, High, Low, Close, Settle, Reference, Bid, BidQuantity, Ask, AskQuantity, TotalQuantity, ReceivedUtc}`、`CapitalTick{Symbol, Sequence, TimeUtc, Price, Quantity, IsReplay}`、`CapitalBar{Symbol, Type, OpenTimeUtc?(分 K), TradingDay?(日以上), Open, High, Low, Close, Volume}`、`CapitalProduct{Symbol, ExchangeName, Name, Kind, LastTradeDate?(99991231 → null), FirstNoticeDate?(0 → null), OrderExchange, OrderCode}`。
- 錯誤碼集中在 `CapitalErrorCodes`(`capital.*`)。
- **Job Object**(0.2.0,Windows):第一次啟動宿主時建立一個 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` 的 job(handle 不可繼承),之後每個宿主行程(含重啟)都放進去,
  handle 持有到 `DisposeAsync`。父行程不論怎麼結束,最後一個 handle 關閉時系統都會收掉宿主。建立或放入失敗只記警告(事件 4014),宿主仍有 stdin EOF 與父行程看門狗。
  行程啟動到放進 job 之間有一小段空窗(`Process.Start` 不能先暫停)。P/Invoke 走 `LibraryImport`(`Protocol/NativeMethods.cs`)。用戶端以 `CAPITAL_PARENT_PID`
  把自己的 PID 傳給宿主。

### 3.1 國內期貨歷史 K 線(0.2.0)

- `GetDomesticKLinesAsync(string code, CapitalDomesticKLineType type, CapitalTradeSession session, DateOnly from, DateOnly to, CancellationToken)` →
  `Result<IReadOnlyList<CapitalDomesticBar>>`。`CapitalDomesticKLineType`:`Minute = 0`、`Day = 4`(值即群益 sKLineType);`CapitalTradeSession`:`Full = 0`、`AmOnly = 1`。
  參數在本機先驗證(`capital.argument.invalid`,不送出)。
- `CapitalDomesticBar{Code, Type, OpenTimeUtc?(分 K), TradingDay?(日 K), Open, High, Low, Close(decimal), Volume(long)}`。分 K `OpenTimeUtc` = 台北時間(固定 UTC+8,
  台灣無夏令)的結束時刻 − 1 分鐘轉 UTC;群益 00:00 那根的日期是新的一天,開始時刻是前一天 23:59。價格 `decimal.Parse(InvariantCulture)` 原樣讀入,不四捨五入、不經 double。
  空 K 棒(量 0)照原樣回。解析器 `Protocol/CapitalDomesticKLineParser`:讀不懂的行**記數略過**(事件 4011 一筆警告),不讓整批失敗——與海外的
  `CapitalKLineParser` 整批失敗不同,因為國內一次可能幾萬根,一行壞掉不值得整批重查。
- **國內狀態**:`DomesticState`(`NotStarted / Connecting / Ready / Disconnected / Unavailable`)、`DomesticReason`(`CapitalDomesticReasons`)與**另開的**
  `DomesticStateChanged` 事件(`CapitalDomesticStateChangedEventArgs{Previous, Current, Reason, Code}`)。選擇另開事件而不併進 `StateChanged`:`StateChanged` 的
  參數型別是海外的 `CapitalHostState`,併進去會讓既有呼叫端誤以為海外狀態變了;國內的任何變化都不觸發 `StateChanged`,海外的 `State` 也不受國內影響。
  宿主結束(重啟中)或 `StopAsync` 時回到 `NotStarted`,新宿主就緒後再回報。`CapitalConnectionStatus` 加 `Domestic`、`DomesticReason`。

### 3.2 0.3.0 用戶端(依 2026-11-09 實測修正,§5.3)

- **實測後的修正**:國內報價、國內成交、海外選擇權報價的價格欄位改成可空(0 → null);`CapitalDomesticTick` 加 `Micros`(原值);
  `CapitalDomesticLiveBar.TimeUtc` 改成 `OpenTimeUtc`(結束時刻 − 1 分鐘);國內商品清單固定欄位位置(5 欄的是選擇權系列列、沒有幣別);
  `CapitalOverseasOptionProduct` 加 `LastTradeDate`;海外權益數改 31 欄(加 `IbCode`、`FloatingProfitLoss`,其後欄位順移);海外未平倉 `@@,001` 是查無資料
  (空清單);新增 `CapitalSkcomText`(SKOrderLib 中文亂碼的辨識與固定字樣還原),幣別與錯誤訊息經過它。

- **版本**:`SkcomVersion`(宿主 status 帶的 `skcomVersion`)、`IsTestedSkcomVersion`、`CapitalSkcomVersion.Tested` / `IsTested`;不在清單 → 事件 4015 警告(每版本一次),
  讀到版本記事件 4016(資訊)。`CapitalConnectionStatus` 加 `SkcomVersion`、`OverseasOptions`、`OverseasOptionsReason`。
- **國內即時**:`GetDomesticProductsAsync(CapitalDomesticMarket)` → `CapitalDomesticProduct`(具名屬性可空 + `RawFields`);`SubscribeDomesticAsync(IEnumerable<string>)`
  → 宿主的完整集合,`DomesticSubscriptions` 記住(宿主重啟後重送)。事件型別 `CapitalDomesticQuote`、`CapitalDomesticTick`、`CapitalDomesticLiveBar`(`CapitalLiveBarKind`)、
  `CapitalBest5`;`CapitalMarketEvent.Symbol` 的交易所固定 `CapitalSymbol.DomesticExchange`(`TAIFEX`,只用來標示)。
- **檔位**:`CapitalBest5` / `CapitalBest10` 以 `Market`(`CapitalQuoteMarket.Domestic / Overseas / OverseasOptions`)區分來源,檔位是 `CapitalBookLevel(Price, Quantity)`;
  國內才有 `ExtendedBid` / `ExtendedAsk` / `IsSimulated`,海外為 null(不填 0)。`SubscribeDepthAsync(IEnumerable<CapitalSymbol>)`。
- **海外選擇權**:`OverseasOptionsState`(`CapitalOverseasOptionsState`)、`OverseasOptionsReason`、`OverseasOptionsStateChanged`(另開事件,理由同國內);
  `GetOverseasOptionProductsAsync`、`SubscribeOverseasOptionsAsync`、`SubscribeOverseasOptionDepthAsync`;事件 `CapitalOverseasOptionQuote`、`CapitalOverseasOptionTick`
  (`TimeUtc` 可空、`IsTimeZoneVerified`)。宿主結束或 `StopAsync` 時回到 `NotStarted`。
- **帳務**:`GetAccountsAsync` → `CapitalAccount`(`Kind` 由市場別推定、`FullAccount` = 分公司代碼 + 帳號,查詢都傳它);`GetFutureRightsAsync(account, CapitalCoinType)`、
  `GetOpenInterestAsync(account, format = 1)`、`GetOverseaFutureRightsAsync`、`GetOverseaOpenInterestAsync(account, CapitalOverseaOpenInterestFormat)`、
  `GetOrderReportAsync(account, CapitalOrderReportFormat)`、`GetFulfillReportAsync(account, CapitalFulfillReportFormat)`,都回 `Result<IReadOnlyList<…>>`。
  解析在 `Protocol/CapitalAccountParser`:具名屬性依手冊欄位順序(手冊 1 起算的欄位在索引 n − 1)、`decimal.TryParse(InvariantCulture)`、解析不出為 null;
  `RawFields` 一律保留。格式 2 以後(國內未平倉)、7 / 8(委託回報)、2–4(成交回報)欄位不同,只給 `RawFields`。查無資料(`001,…`、`M003`)回空清單;
  `M999`、海外權益數第一筆 `代碼;訊息`、海外未平倉 `@@,非 0,…` → `capital.account.query_failed`。
- **隱私**:帳號(`GetAccountsAsync` 拿到的與呼叫端傳入的)登記到工作階段的字面值遮罩,宿主 stderr 與宿主錯誤訊息(0.3.0 起 `FromHost` 也經遮罩)都換成 `<ACCT>`;
  帳務型別覆寫 `ToString()`,只露帳號末 3 碼(`CapitalAccountText.Mask`)。
- **重啟重送**:`ResubscribeAsync` 在海外訂閱之後依序重送 `depth`、`tw-subscribe`、`oo-subscribe`、`oo-depth`(國內與海外選擇權的首次連線由宿主等著,用 180 秒時限)。

## 4. 相依

BCL、`Microsoft.Extensions.*`、`Ozakboy.Core.Abstractions`(`Result`)、`Ozakboy.Security`(遮罩)。不引用群益的 interop DLL。

## 5. 實測依據(SKCOM 2.13.59,2026-11-06 探針)

以宿主的 `probe` 指令對真 SKCOM 實測;假宿主的行為以此為準:商品清單與明細以 `##` 結尾、只送 3001、訂閱後先回補當日成交再接即時、`nPtr` 每檔遞增、K 線格式與「分鐘結束時刻」、FX 美東與 CME 芝加哥時間。

### 5.1 國內期貨 K 線(SKCOM 2.13.59 x64,2026-11-07 `probe-tw`,只有期貨戶的帳號)

1. 登入後 `SKQuoteLib_EnterMonitorLONG()` 回 0 → `OnConnection(3001)` → 約 0.5 秒後 `OnConnection(3003)`(商品基本資料完成,之後才能查)。同一行程同時掛
   SKOSQuoteLib 的 `EnterMonitorLONG` 也正常(海外 3001、國內維持連線),國內查完再查海外 K 線(`FX,SUSDTWD`)也正常。
2. `RequestKLineAMByDate(代碼, sKLineType, sOutType=1, sTradeSession, yyyyMMdd, yyyyMMdd, sMinuteNumber=1)`:回 0 表示送出;代碼不存在或沒權限立即回非 0
   (期貨戶查股票 `2330` 回 9999 SK_FAIL;`TX00AM` 回 3023 SK_SUBJECT_STOCKNO_IS_INVALID)。資料以 `OnNotifyKLineData(bstrStockNo, bstrData)` 每根一則送來,
   **結束一定有 `OnKLineComplete("##")`**(每個請求一則;0 筆時也有)。
3. 可用代碼:`TX00` 台指近月、`MTX00` 小台近月、`TM0000` 微台近月(換月方式與永豐連續月相同)。
4. sKLineType:0 分線、4 日線(完整日線)。sTradeSession:0 全盤(含前一晚夜盤)、1 AM 盤(只有日盤)。**起訖日期是交易日**:查 2026-11-02 全盤會從
   2026/10/30 15:01 開始(週五夜盤歸下一個交易日)。
5. 新版格式:分 K `yyyy/MM/dd HH:mm, O, H, L, C, V`;日 K `yyyy/MM/dd, O, H, L, C, V`(逗號後有空白;價格已是小數字串如 `45395.00`)。**分 K 時間是該分鐘的結束時刻、
   台北時間(UTC+8,台灣無夏令)、日期是日曆日**(00:00 那根的日期是新的一天);日盤 08:46–13:45、夜盤 15:01–05:00。
6. 會給量為 0 的空 K 棒(沒有成交的分鐘)。
7. 一個請求最多 60,000 筆,超過時保留最新、截掉最舊(查 8/1–11/7 只回 8/18 21:16 起的 60,000 筆);1 分 K 約只保留最近 1 年,日 K 到 2015 都有。
8. 全盤分 K 的日盤最後一根(13:45;結算日 13:30)的「收盤」與全盤日 K 的收盤其實是**結算價**(期交所官方 11/06:最後成交價 39786、結算價 39652;群益全盤分 K
   最後一根收 39652),AM 盤日 K 的收盤 = 官方最後成交價 39786。**套件不修正**(回原始資料),由呼叫端決定。
9. 見 §6 的群益資料錯誤。

假宿主(`tests/FakeCapitalHostExe/FakeDomesticMarket.cs`)照 1–7 模擬;交易日簡化成週一到週五,價格是決定性的假走勢,不是實測值。

### 5.2 `probe-full`(0.3.0 功能的格式實測;2026-11-09 實跑,結果見 §5.3)

用途:0.3.0 功能的格式實測工具。`serve` 與用戶端的 0.3.0 功能先依手冊做(§2.2–§2.6、§3.2),2026-11-09 跑完後依 §7 逐項核對並修正。
第一次實跑後探針本身的修正:選擇權優先挑全盤代碼(不挑結尾 `AM` 的日盤代碼)、海外選擇權改挑最接近小那斯達克期貨價格的 NQ 買權與賣權(原本照清單順序
挑到履約價 100,三分鐘沒有成交)、帳務查詢回 1019 時以 3 / 5 / 10 / 20 秒遞增再試(順便量出同類查詢需要的間隔;唯讀查詢重送無害)。

```
Ozakboy.TradeKit.Capital.Host probe-full --out <目錄> [--seconds 180] [--accounts-only]
```

| 段 | 內容 | 要開盤 |
|---|---|---|
| A | `SKCenterLib_GetSKAPIVersionAndBit` | 否 |
| B | 期貨帳務(唯讀):`SKOrderLib_Initialize` → `GetUserAccount`(`OnAccount`)→ `ReadCertByID` → 國內期貨帳號(市場別 TF / TO)查 `GetFutureRights`(sCoinType 0 / 1 / 2)、`GetOpenInterestGW`(nFormat 1,另試 2 / 3)、`GetOrderReport`(nFormat 1–9)、`GetFulfillReport`(nFormat 1–5);海外期貨帳號(OF / OO)查 `GetRequestOverSeaFutureRight`、`GetOverseaFutureOpenInterestGW`(nFormat 1 彙總 / 2 明細)、`GetOFOpenInterestWithDetails`,委託 / 成交回報各試格式 1 | 否 |
| C | 國內期權報價:`SKQuoteLib_EnterMonitorLONG` 等 3003 → `RequestStockList(2)`、`(3)` → `TX00,MTX00,TM0000` + 一檔台指選擇權近月、最接近台指期價的買權一起 `RequestStocks`(psPageNo 1,被拒再試 -1)→ 每檔 `RequestTicks`(psPageNo 0、1、2…)。記 `GetStockByIndexLONG` 全欄位(每檔 60 筆)、成交全部、五檔(每檔 200 筆)、即時分 K | 是 |
| D | 海外五檔 / 十檔:`FX,SUSDTWD`、`CME,NQ0000` 訂報價與成交後 `RequestMarketDepth`;記 `OnNotifyBest5NineDigitLONG` / `OnNotifyBest10NineDigitLONG`(每檔各 200 筆) | 是 |
| E | 海外選擇權:`SKOOQuoteLib_EnterMonitorLONG` → `RequestProducts`(原文全記)→ 挑兩檔(優先 CME 小那斯達克 / S&P)`RequestStocks` + `RequestTicks` + `RequestMarketDepth`;記 `GetStockByIndexLONG`(`SKFOREIGNLONG`)、成交、五檔、十檔 | 是 |

`--accounts-only` 只跑 A + B(週末可跑)。登入一次,失敗不重試(結束碼同 `probe`:3 SKCOM 建不起來、4 登入失敗)。以事件回傳的查詢等「收過狀態事件後安靜 2 秒」或「安靜 6 秒」
(最多 60 秒);委託 / 成交回報是阻塞式、直接回字串,手冊要求每次間隔 5 秒。

**輸出**(`--out`):`probe.jsonl`(每個呼叫的回傳碼與訊息、每則事件);`account-<事件或函式名>.txt`(帳務原文,每行前面是當時的步驟名稱);`tw-stocklist-<市場>.txt`、
`tw-ticks-<代碼>.txt`、`tw-best5-<代碼>.txt`、`tw-livek-<代碼>.txt`、`os-best5-<交易所_代碼>.txt`、`os-best10-…`、`oo-products.txt`、`oo-ticks-…`、`oo-best5-…`、
`oo-best10-…`(第一行是欄位名稱)。`sklog\` 是群益自己的紀錄,含帳號,不讀、不給別人。

**遮罩**:每個事件與每行原文寫出前都經 `ProbeRedactor`:登入帳號 → `<ID>`(不分大小寫)、`OnAccount` 解析出的帳號號碼依出現順序 → `<ACCT1>`、`<ACCT2>`…
(完整帳號 `F020000<ACCT1>` 這樣出現)、戶名 → `<NAME>`;對照表只在記憶體。解析不了的 `OnAccount`(帳號沒登記到遮罩)與手冊沒列的多餘欄位只寫「形狀」
(數字 → 9、英文 → A、其他文字 → 字)。金額不遮。已知限制:數字恰好等於某個帳號號碼時也會被換掉。

**只讀保證**:`ISKOrderLib` 上會改變委託、部位或資金的方法全部是 `Unused*` 佔位(參數只有 `IntPtr`),沒有可呼叫的真簽章;產生器對禁用字樣硬擋,
`SkComOrderInteropSafetyTests` 以反射再擋一次並核對真簽章清單。

**COM 定義的產生**:`src/…Host/Interop/SkCom*Interop.cs` 由 `tools/SkComInteropGen/Generate-SkComInterop.ps1`(Windows PowerShell 5.1)讀群益附的
`Interop.SKCOMLib.dll` 中繼資料產生,規格在同目錄 `skcom-interop.psd1`(哪些方法給真簽章、哪些事件要接)。群益的 DLL 不進 repo。

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\SkComInteropGen\Generate-SkComInterop.ps1 -TypeLibInterop '<群益 SDK>\元件\x64\Interop.SKCOMLib.dll'
```

第一次實跑前的推定與結果:`OnAccount` 的市場別字樣(推定 TF / TO / OF / OO;實測期貨戶是 TF 與 OF)、分公司代碼在第 2 欄(實測是)、
台指選擇權代碼格式(`TXO` + 履約價 + 月份字母 + 年尾數,A–L 買權;實測是,另有結尾 `AM` 的日盤代碼)、SKOOQuoteLib 的參數(比照 SKOSQuoteLib;實測頁碼 -1、
`#` 串接可用)、`OnOpenInterestWithDetails` 的觸發來源(實測 `GetOpenInterestGW` 也會觸發它與 `OnOpenInterestJson`,內容同 `OnOpenInterest`)。

### 5.3 probe-full 實測 2026-11-09

條件:2026-11-09(週一)20:05–20:11 台北時間跑 `probe-full --seconds 180`,國內夜盤與海外都在開盤中;SKCOM `2.13.59_x64`;帳號只有期貨戶(TF / OF),
沒有部位、當天沒有委託;Windows 系統 ANSI 字碼頁 950。輸出在 TideTrade 的 `runtime\capital-probe-full\`(帳號已遮罩;`sklog\` 不讀)。

**A. 版本**:登入前 `GetSKAPIVersionAndBit` 回 `2.13.59_x64`。

**B. 帳務(唯讀)**
- `OnAccount` 兩行 `TF,F020000,000,<帳號>,<身分證>,<戶名>,9` 與 `OF,…`:7 欄(多一個手冊沒列的第 7 欄 `9`)、分公司代碼在第 2 欄、第 3 欄是 `000`;沒有結尾標記,
  安靜 6 秒結束(共 6.26 秒)。`ReadCertByID` 回 0。以「分公司代碼 + 帳號」查詢都成功。
- 以事件回傳的查詢,**狀態事件先到**(0 `Success!` 或 0 空訊息),0.4–1.6 毫秒後才是資料列,最後 `##` 加 50 個逗號;「收過狀態事件後安靜 2 秒」約 2.1 秒結束。
- 國內權益數 sCoinType 0:兩列 41 欄 + `##`。第一列幣別 `NTD(基幣)`(亂碼)、第二列 `NTD`(手冊 4-2-i「全幣別回傳第一筆資料為基幣」)。第 39 欄登入帳號、
  第 40 欄完整帳號;沒有部位時維持率(24)/ 風險指標(34)是 `*********`,金額全 0。
- 國內未平倉 GW 格式 1:`001,查無資料,<完整帳號>` + `##`(手冊 4-2-d)。同一個查詢也觸發 `OnOpenInterestWithDetails`、`OnOpenInterestJson`(內容相同),宿主只收 `OnOpenInterest`。
- 海外權益數:第一列 `000,1,查詢成功`(手冊只寫「000 與筆數」,多一個訊息欄),資料列 **31 欄**:期貨商代碼 `F020000`、IB 代號 `000`、帳號、幣別 `NTD(基幣)`、
  兩個空白(風險指標、足額維持率)、25 個數字,第 22 欄參考匯率 = 1。0.3.0 草稿依 29 欄解析(漏了 IB 代號與「未沖銷期貨浮動損益」),已改成手冊 4-2-k 的 31 欄。
- 海外未平倉格式 1:`@@,001,查無資料,<完整帳號>` + `##`。手冊說 `@@` 代碼非 0 都是錯誤,實測 001 是查無資料 → 用戶端回空清單。
- 委託回報格式 1–9、成交回報格式 1–5(國內帳號,海外帳號也各試一次):一律回 4 個字元 `M003`(手冊 4-2-97「M003: 查無資料」)。阻塞 18 毫秒–6.7 秒;5.5 秒間隔沒有被拒。
- **1019**:sCoinType 1(前一個權益數查詢結束後約 1 秒送)、sCoinType 2、國內未平倉格式 2 / 3(前一次結束後 1–2 秒送)、海外未平倉格式 2、`GetOFOpenInterestWithDetails`
  都回 1019 `SK_ERROR_QUERY_IN_PROCESSING`(手冊錯誤表:查詢尚未結束請稍後再試)。換一個查詢函式(權益數 → 未平倉)就不會。需要隔多久還沒量到。
- **中文亂碼**(判斷依據):`查無資料` → `?dμL﹐eRA`、`查詢成功` → `?d﹐s|‥￥\`、`NTD(基幣)` → `NTD(°o1o)`。把中文以 Big5 編碼、每個位元組當 Latin-1 字元,
  再經 Windows 字碼頁 950 的 `WideCharToMultiByte` 最適對應(best fit)來回轉一次,三個樣本逐字重現(UTF-8 位元組被當 Big5 解、或 Big5 被當 UTF-8 解都重現不出來)。
  「基幣」另由手冊 4-2-i 的「第一筆資料為基幣」對上。最適對應把不同位元組併成同一個字(`è é ê ë ð` 都變 `e`、沒有對應的 41 個位元組變 `?`),所以**一般無法還原**,
  只能還原認得的固定字樣(`CapitalSkcomText.Repair`);戶名等任意中文只能判斷「像亂碼」(`LooksGarbled`)。報價端(SKQuoteLib、SKOSQuoteLib、SKOOQuoteLib)的中文
  (台指近、國際外匯、小SP季選2612P)正常。系統字碼頁不是 950 的電腦上亂碼形狀會不同(未實測)。

**C. 國內期權報價**
- `EnterMonitorLONG` → 3001 → 0.54 秒後 3003。`RequestStockList(2)` 8 則、`(3)` 3 則,結尾是獨立一則 `##,,;`,每則是一個類股(以換行分隔)。
  期貨 6,341 筆、選擇權 13,424 筆都是手冊的 6 欄;選擇權另有 120 筆 5 欄的系列列(`TXO11,台選11,0,TXO11,`)。每檔都有一對代碼:結尾 `AM` 的(`TX00AM`、
  `TXO41500K6AM`)只有日盤,沒有 `AM` 的是全盤;交易所代碼相同(近月連續 `TXFK600`)。跳動點文字如 `100000| 1 | `、`10| 0.1 | / 50| 0.5 | / …`。
- `GetStockByMarketAndNo`(探針用的查索引)回的索引(台指 5809、小台 5816、微台 5825、選擇權 337)與報價事件的 (市場別, 索引) 一致;`sDecimal` 期貨與選擇權都是 2;
  `bstrStockNo` 就是訂閱時的代碼(`TX00`、`TXO41500K6AM`)。
- `RequestStocks(頁碼 1, "TX00,MTX00,TM0000,TXO41500K6AM")` 回 0、頁碼仍是 1;`RequestTicks` 頁碼 0–3 各回 0;四檔都有報價與成交。
- 報價:每則是完整快照,但訂閱後第一則只填一部分(小台第一則只有開高低,台指第一則只有成交價與買賣價),約 10 毫秒後補齊,之後 180 則沒有再出現 0。
  `nTradingDay`:全盤代碼夜盤是 20261110(下一個交易日)、`TXO41500K6AM` 是 20261109。`nDealTime` 與成交的 HHmmss 一致。台指 4120100 → 41201.00。
- 成交:訂閱當下先回補 14:50:05 起的當天成交(台指 16,639 筆、小台 50,905、微台 100,496,約 3 分鐘)再接即時。14:50:05–14:59:55 的 119 筆 `nSimulate` = 1(試撮,
  毫秒 0),15:00:00.003 起為 0。`nDate` 是 20261109(日曆日,不是交易日)。`nTimemillismicros` 16.8 萬筆後三位全是 000(毫秒精度,817000 = 817 毫秒)。即時成交
  20:11:07.817 在 UTC 12:11:07.989 收到。`TXO41500K6AM` 只回補日盤(08:30:50 起,第一筆試撮整筆是 0,最後 13:30:39),夜盤沒有成交。
- 五檔:買方由高到低、賣方由低到高;沒有掛單是 0 / 0(選擇權賣 3–5 檔);衍生一檔只有微台有值(200 則中 25 則,賣方,例如 4120000 / 1),台指與小台全是 0 / 0;
  `nSimulate` 全是 0(當時不是試撮時段)。
- 即時分 K:`nType` 1 先回補當日已完成的分 K(夜盤從 1501 起),之後 `nType` 2 是還在形成的那一分鐘(累計開高低收量,量 5 → 6 → 8 → 9),分鐘結束再來一則 1。
  1501 那根的開盤價 = 15:00:00.003 第一筆成交;20:11:07 收到的 `nType` 2 標 2012。所以 **HHmm 是那一分鐘的結束**(與歷史分 K 相同)。`nDate` 是日曆日。沒有看到 `nType` 0。

**D. 海外五檔 / 十檔**:`RequestTicks` 與 `RequestMarketDepth` 是連著送的,分不出五檔由哪個觸發(NQ 五檔與十檔各 78 則)。NQ `sDecimal` 2:2721800 → 27218.00、
2721775 → 27217.75(0.25 跳動點)。十檔前 5 檔與五檔相同;外匯 SUSDTWD 五檔 / 十檔各一則、全是 0。

**E. 海外選擇權**:`SKOOQuoteLib_EnterMonitorLONG` 回 0,約 18 秒後才來 3001(沒有 3003),`IsConnected` = 1。`RequestProducts` 56,156 筆、每筆 5 欄
`交易所,交易所名稱,代碼,名稱,最後交易日`,以 `##,##,##,##,##` 結尾;交易所有 CME、CBOT、NYM、Eurex、HKEx、ICEEU、OSE。代碼 = 根代碼 + 補零的履約價 + 月份字母
(A–L 買權、M–X 賣權)+ 年尾數(`ES00100X6`)。`RequestStocks(-1, "CME,ES00100X6#CME,ES00100L6")` 回 0,報價與五檔 / 十檔都有來;市場別字樣 `7`、`sDecimal` 2,
結算價 730200 → 7302.00、賣權賣價 5 → 0.05;`nStrikePrice` = 100 與名稱「小SP季選2612C 100 C」一致(沒有放大)。挑到的兩檔三分鐘內沒有成交,成交時區未實測。

**探針本身**:寫完 `done` 之後行程沒有結束(還掛著群益登入),見 §6。

## 6. 已知問題

- **群益資料錯誤(國內)**:2026-10-30 週五夜盤(歸屬 11/02)群益分 K 與日 K 的量都是期交所官方的 2 倍(官方 44,305 口、群益 88,442),其他夜盤正常。套件不處理,
  需要正確量的呼叫端請另行比對官方資料。
- **全盤收盤是結算價**(§5.1 第 8 點)。
- **孤兒宿主(0.1.0,0.2.0 修正)**:實機發現父行程(TideTrade.App)結束後宿主還跑了好幾個小時。0.1.0 的程式碼在 stdin EOF 時本來就會 LeaveMonitor 後以 0 結束
  (協定層測試與 0.2.0 直接啟動假宿主的測試都證實這條路可行),所以真環境沒結束的原因只能在協定層之外:STA 卡在 SKCOM 的同步呼叫而處理不到 EOF、結束時的 COM 收尾
  卡住,或父行程的 stdin 寫入端被其他行程繼承而 EOF 根本沒來——沒有真 SKCOM 無法分辨是哪一個。0.2.0 三道都補:Job Object(§3)、父行程看門狗與結束保險(§2)。
- 國內的 `serve` 流程(§2.1)、Job Object 與父行程看門狗只對假宿主測過。
- 0.3.0 的新功能格式已用 `probe-full` 實測(§5.3、§7),但 `serve` 宿主本身仍只對假宿主與協定層測試跑過。
- **SKOrderLib 的中文亂碼**(群益端,§5.3 B):帳務事件字串裡的中文在字碼頁 950 的電腦上不可逆地變成亂碼(戶名、訊息、幣別說明)。套件只還原認得的固定字樣,
  其餘原樣保留在 `RawFields`。
- **probe-full 跑完不結束**(2026-11-09 實跑):寫完 `done` 之後行程一直沒有結束,還掛著群益登入,要手動結束。推定卡在 `RunAsync` 收尾的解除 COM 連接點或 STA 收尾
  (與 0.1.0 孤兒宿主同型)。**已修**:三支探針寫完 `done` 就啟動 `ProbeExitGuard`(背景執行緒計時,不依賴 STA),15 秒內沒結束就以同一個結束碼強制結束;
  卡住的確切位置仍未查明。

## 7. 0.3.0 實測清單(2026-11-09 `probe-full` 逐項結論)

結論分三種:**已實測**(輸出直接證實)、**部分**(格式證實、但有沒涵蓋的情況)、**仍待實測**(這次的條件測不到)。證據的檔名都在 TideTrade 的
`runtime\capital-probe-full\`,詳細數字見 §5.3。

| # | 項目 | 結論 | 證據 | 處理 |
| --- | --- | --- | --- | --- |
| 1 | `GetSKAPIVersionAndBit` 登入前可讀、回傳形式 | 已實測 | `probe.jsonl` 第一筆 `api_version` = `2.13.59_x64`(登入前) | 不用改 |
| 2 | 國內商品清單欄位 | 已實測 | `tw-stocklist-2.txt` 6,341 筆、`-3.txt` 13,424 筆都是 6 欄;選擇權 120 筆 5 欄系列列;AM 代碼成對;結尾 `##,,;` | 解析改固定欄位位置;文件說明 AM 代碼;探針改挑全盤代碼 |
| 3 | 國內訂閱上限與頁碼 | 部分 | `tw_request_stocks` 頁碼 1、4 檔逗號串接回 0;`tw_request_ticks` 頁碼 0–3 各回 0、四檔都有成交 | 10 檔上限與「完整集合重送」沒測到(只訂 4 檔) |
| 4 | 索引、`sDecimal` 與事件一致 | 已實測 | `tw_stock_info` 5809 / 5816 / 5825(市場 2)、337(市場 3)= `tw_quote` 的 `event_market` / `event_index`;`sDecimal` 2 | 不用改 |
| 5 | `bstrStockNo` 是訂閱代碼還是合約代碼 | 已實測 | `tw_quote` 的 `bstrStockNo` = `TX00`、`TXO41500K6AM`(訂閱代碼) | 不用改(兩者相同) |
| 6 | 國內成交時間 | 部分 | `tw-ticks-TX00.txt` 夜盤 `date` 20261109(日曆日);`201107` + `817000` 在 12:11:07.989Z 收到;16.8 萬筆微秒後三位全 000 | 文件改寫;跨午夜未實測 |
| 7 | 試撮旗標 | 已實測 | `tw-ticks-TX00.txt` 14:50:05–14:59:55 共 119 筆 `simulate` 1,15:00:00.003 起 0;選擇權 08:30:50 試撮列整筆 0 | 成交價、買賣價 0 → null |
| 8 | 國內五檔的衍生一檔與空檔 | 部分 | `tw-best5-*.txt`:空檔 0 / 0;衍生一檔只有微台有值(25 / 200,如 `4120000 / 1`);`nSimulate` 全 0 | 文件改寫;試撮時段的五檔沒測到 |
| 9 | 即時分 K 的時間、nType 0、小數位數 | 已實測(nType 0 除外) | `tw-livek-TX00.txt` 第一根 `1501` 開盤 4124000 = 15:00:00.003 第一筆成交;12:11:07.989Z 收到的 nType 2 標 `2012` | `TimeUtc` 改 `OpenTimeUtc` = 結束 − 1 分鐘;nType 0 沒出現 |
| 10 | 海外五檔的觸發來源、小數位數 | 部分 | `os-best5-CME_NQ0000.txt` 2721775 → 27217.75(`sDecimal` 2);`RequestTicks` 與 `RequestMarketDepth` 連著送,五檔與十檔各 78 則 | 觸發來源仍待實測(要只訂成交、不訂十檔再跑一次) |
| 11 | 海外十檔格式 | 已實測 | `os-best10-CME_NQ0000.txt` 10 檔,前 5 檔 = 五檔;買高到低、賣低到高;`os-best10-FX_SUSDTWD.txt` 全 0 | 不用改 |
| 12 | 海外選擇權連線代碼 | 部分 | `oo_on_connect` 只有 3001(Enter 後約 18 秒),`oo_is_connected` 1 | 3001 算連上確認;3002 斷線沒遇到 |
| 13 | 海外選擇權清單格式與結尾、訂閱字串 | 已實測 | `oo-products.txt` 56,156 筆 5 欄 + `##,##,##,##,##`;`oo_request_stocks` `CME,ES00100X6#CME,ES00100L6` 回 0、報價有來 | 加 `LastTradeDate`;假宿主改實測格式 |
| 14 | 海外選擇權價格小數位數、履約價、時區 | 部分 | `oo_quote` 結算 730200(`sDecimal` 2 → 7302.00)、`nStrikePrice` 100 = 名稱「… 100 C」 | 價格 0 → null;履約價照原值(非整數履約價未實測);**時區未實測**(沒有成交) |
| 15 | `OnAccount` 市場別、分公司欄、結尾 | 已實測 | `account-OnAccount.txt` `TF,F020000,000,<ACCT1>,<ID>,<NAME>,9` 與 `OF,…`;沒有結尾,6.26 秒結束 | 文件改寫(多第 7 欄);TO / OO 沒看到 |
| 16 | 查詢帳號是「分公司代碼 + 帳號」 | 已實測 | 權益數、未平倉以 `F020000<ACCT1>` 查,狀態 0 | 不用改 |
| 17 | `ReadCertByID` 是否必需 | 部分 | `read_cert` 回 0 | 是否必需沒測(沒有不讀憑證就查的對照組) |
| 18 | 國內權益數 41 欄 | 部分 | `account-OnFutureRights.txt` 兩列 41 欄 + `##`,第一列幣別 `NTD(基幣)`(亂碼),第 40 欄完整帳號 | **欄位位置已實測、數值語意待有部位時再驗**(當時全 0);sCoinType 1 / 2 被 1019 擋下沒測到 |
| 19 | 國內未平倉 GW 格式 1;格式 2、3 | 部分 | `account-OnOpenInterest.txt` `001,查無資料,F020000<ACCT1>` + `##` | 查無資料形式已實測;有部位的欄位待實測;格式 2 / 3 被 1019 擋下 |
| 20 | 海外權益數第一筆與欄位 | 部分 | `account-OnOverSeaFutureRight.txt` `000,1,查詢成功` + 31 欄(第 22 欄匯率 1) | **改成手冊的 31 欄**(草稿 29 欄是錯的);數值語意待有部位時再驗 |
| 21 | 海外未平倉 `@@` 列、彙總 / 明細、分數價格 | 部分 | `account-OnOFOpenInterestGWReport.txt` `@@,001,查無資料,F020000<ACCT1>` + `##` | `@@,001` 改成空清單(原本當失敗);彙總 / 明細與分數價格待有部位;格式 2 被 1019 擋下 |
| 22 | 委託 / 成交回報欄位、M003 / M999、中文編碼 | 部分 | `account-GetOrderReport.txt` 格式 1–9、`account-GetFulfillReport.txt` 格式 1–5 都只回 `M003` | M003 確認;70 / 52 欄、M999、回報字串的中文編碼待有委託時再驗。**事件字串**的中文確認是 Big5 + 字碼頁 950 最適對應的亂碼(§5.3 B),加 `CapitalSkcomText` |
| 23 | 查詢等待 | 已實測(另發現 1019) | `probe.jsonl` 的 `order_status` 都比資料列早 0.4–1.6 毫秒,`*_done` 約 2.1 秒;`get_user_account_done` 6.26 秒;同一查詢函式在前一次結束後 1–2 秒再送回 1019 | 等待規則不用改;1019 寫進文件與 XML 註解,探針改成遇 1019 遞增等待再試;宿主要不要替同類查詢排間隔待決定 |
