# Ozakboy.TradeKit.Capital 設計(0.2.0)

群益證券 SKCOM 海外報價(外匯 + 海外期貨)的 .NET 10 用戶端,0.2.0 起另可查國內期貨歷史 K 線(§2.1、§3.1)。**只接報價**,不含下單、帳務。

## 1. 分層

| 專案 | 型態 | 責任 |
|---|---|---|
| `Ozakboy.TradeKit.Capital.Host` | x64 exe(`net10.0-windows`、WinForms 訊息迴圈) | 掛 SKCOM(STA + 訊息迴圈),以 **stdio 換行分隔 JSON** 對外提供服務;`probe` 指令保留 |
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
| `status` | — | `{connected, connections, outLimit, domestic, domesticReason?, domesticCode?}` | `IsConnected` + `GetQuoteStatus`(海外);國內欄位 0.2.0 起 |
| `shutdown` | — | `{}` | `LeaveMonitor` 後結束,結束碼 0 |

**事件**(stdout):`{"event":"<名稱>","data":{...}}`
- `status`:如上。`state` 永遠是**海外**狀態。0.2.0 起國內開始後(或確定不可用後)每則 status 都帶 `domestic`(`connecting` / `ready` / `disconnected` /
  `unavailable`)、`domesticReason`(`enter_rejected` / `ready_timeout` / `connection_lost` / `skcom_*`)、`domesticCode`(國內 EnterMonitorLONG 被拒的群益代碼);
  國內狀態改變時再送一則 status,`state` 照舊是海外目前的狀態(舊版用戶端看到的只是重複的同一個狀態,無害)。要看海外的轉換就把連續重複的 state 合併。
- `quote`:`{symbol:"CME,NQ0000", name, decimals, denominator, tradingDay, open, high, low, close, settle, ref, bid, bidQty, ask, askQty, totalQty, tickQty}`,價格欄位是**原始整數**(long)。來源:`OnNotifyQuoteLONG(idx)` → `GetStockByIndexNineDigitLONG(idx)`。
- `tick`:`{symbol, ptr, date, time, price, qty, history}`,`price` 原始整數、`date` = yyyyMMdd、`time` = HHmmss(交易所當地時間)、`history` = 訂閱當下的回補。
- 索引 → 代碼的對照:第一次看到某個索引(報價或成交都可能先到;實測成交先到)就 `GetStockByIndexNineDigitLONG` 取 `bstrExchangeNo + "," + bstrStockNo` 並快取;小數位數同時快取,成交事件的 `price` 一樣是原始整數,由用戶端依該代碼的 `decimals` 換算(宿主在 tick 事件也帶 `decimals`,省得用戶端等報價)。

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

## 6. 已知問題

- **群益資料錯誤(國內)**:2026-10-30 週五夜盤(歸屬 11/02)群益分 K 與日 K 的量都是期交所官方的 2 倍(官方 44,305 口、群益 88,442),其他夜盤正常。套件不處理,
  需要正確量的呼叫端請另行比對官方資料。
- **全盤收盤是結算價**(§5.1 第 8 點)。
- **孤兒宿主(0.1.0,0.2.0 修正)**:實機發現父行程(TideTrade.App)結束後宿主還跑了好幾個小時。0.1.0 的程式碼在 stdin EOF 時本來就會 LeaveMonitor 後以 0 結束
  (協定層測試與 0.2.0 直接啟動假宿主的測試都證實這條路可行),所以真環境沒結束的原因只能在協定層之外:STA 卡在 SKCOM 的同步呼叫而處理不到 EOF、結束時的 COM 收尾
  卡住,或父行程的 stdin 寫入端被其他行程繼承而 EOF 根本沒來——沒有真 SKCOM 無法分辨是哪一個。0.2.0 三道都補:Job Object(§3)、父行程看門狗與結束保險(§2)。
- 國內的 `serve` 流程(§2.1)、Job Object 與父行程看門狗只對假宿主測過。
