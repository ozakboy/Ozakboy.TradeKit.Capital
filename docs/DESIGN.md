# Ozakboy.TradeKit.Capital 設計(0.1.0)

群益證券 SKCOM 海外報價(外匯 + 海外期貨)的 .NET 10 用戶端。**只接報價**,不含下單、帳務。

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

**輸出**:stdout 只放協定訊息(每行一個 JSON 物件,UTF-8、無 BOM);人看的紀錄走 stderr(不得含帳密;遇到登入帳號字面值一律換成 `<ID>`)。

**啟動流程**(在 STA 執行緒上):
1. 建立 SKCenterLib、SKReplyLib、SKOSQuoteLib。失敗 → 事件 `status{state:"faulted", reason:"skcom_not_registered"|"skcom_file_missing"|"skcom_create_failed", hresult}`,結束碼 3。(0x80040154 = 沒註冊;0x8007007E = 登錄檔指向的 DLL 不存在)
2. 掛 `OnReplyMessage`(回 -1,手冊必要)與海外報價事件。
3. `status{state:"logging_in"}` → `SKCenterLib_Login`。非 0 → `status{state:"login_failed", code, message}`,結束碼 4,**不重試**(300 密碼錯 / 306 身分證錯 / 307 鎖定 / 600 憑證錯 / 604 憑證過期)。
4. `status{state:"connecting"}` → `SKOSQuoteLib_EnterMonitorLONG`;等 `OnConnect(3001)`(實測只會有 3001,沒有 3003),60 秒沒來 → `status{state:"faulted", reason:"connect_timeout"}`,結束碼 5。
5. `status{state:"ready"}`。
6. 之後收到 `OnConnect(3002)` → `status{state:"disconnected"}`,以 5、10、30、60 秒退避重新 `EnterMonitorLONG`;回到 3001 後**重送全部訂閱**再發 `ready`。這是連線類錯誤,可以重試;登入不重登。

**請求**(stdin,每行一個):`{"id":<整數>,"method":"<名稱>","params":{...}}`;**回應**:`{"id":<同>,"result":{...}}` 或 `{"id":<同>,"error":{"code":"capital.*","message":"…"}}`。請求在 STA 上依序處理,一次一個。

| method | params | result | 說明 |
|---|---|---|---|
| `products` | — | `{items:[{exchange, exchangeName, code, name, lastTradeDate, firstNoticeDate, kind, orderExchange, orderCode}]}` | `RequestOverseaProducts` 收到 `##` 開頭那筆為止;再 `GetOverseaProductDetail(1)` 收到 `##` 為止,依 (exchange, code) 併入 `kind` 等欄位;各 60 秒逾時 → `capital.products.timeout` |
| `subscribe` | `{symbols:["FX,SUSDTWD", ...]}` | `{subscribed:[...]}` | 與已訂集合取聯集;新的那批以 `RequestStocks(ref -1, 以 # 串接)`,每檔再 `RequestTicks(ref -1, 代碼)`;宿主記住集合以便重連後重送。回傳非 0 → `capital.subscribe.rejected`(附代碼與訊息) |
| `kline` | `{symbol, type: 0\|1\|2\|3, start:"yyyyMMdd", end:"yyyyMMdd"}` | `{lines:["2026/11/02 16:01, 157.889999, …, 141", ...]}` | `RequestKLineByDate(symbol, type, start, end, 1)`;沒有結束事件,以「最後一筆後 1.5 秒沒新資料」或「10 秒都沒資料」結束;只收 `bstrStockNo` 等於這次代碼(去掉交易所)的那些行 |
| `status` | — | `{connected, connections, outLimit}` | `IsConnected` + `GetQuoteStatus` |
| `shutdown` | — | `{}` | `LeaveMonitor` 後結束,結束碼 0 |

**事件**(stdout):`{"event":"<名稱>","data":{...}}`
- `status`:如上。
- `quote`:`{symbol:"CME,NQ0000", name, decimals, denominator, tradingDay, open, high, low, close, settle, ref, bid, bidQty, ask, askQty, totalQty, tickQty}`,價格欄位是**原始整數**(long)。來源:`OnNotifyQuoteLONG(idx)` → `GetStockByIndexNineDigitLONG(idx)`。
- `tick`:`{symbol, ptr, date, time, price, qty, history}`,`price` 原始整數、`date` = yyyyMMdd、`time` = HHmmss(交易所當地時間)、`history` = 訂閱當下的回補。
- 索引 → 代碼的對照:第一次看到某個索引(報價或成交都可能先到;實測成交先到)就 `GetStockByIndexNineDigitLONG` 取 `bstrExchangeNo + "," + bstrStockNo` 並快取;小數位數同時快取,成交事件的 `price` 一樣是原始整數,由用戶端依該代碼的 `decimals` 換算(宿主在 tick 事件也帶 `decimals`,省得用戶端等報價)。

**COM 抽象**:`ISkQuoteSource`(登入、連線、商品、訂閱、K 線、事件)隔開 COM;`SkComQuoteSource` 是真實作,協定層只認介面,測試以假實作驅動。

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

## 4. 相依

BCL、`Microsoft.Extensions.*`、`Ozakboy.Core.Abstractions`(`Result`)、`Ozakboy.Security`(遮罩)。不引用群益的 interop DLL。

## 5. 實測依據(SKCOM 2.13.59,2026-11-06 探針)

以宿主的 `probe` 指令對真 SKCOM 實測;假宿主的行為以此為準:商品清單與明細以 `##` 結尾、只送 3001、訂閱後先回補當日成交再接即時、`nPtr` 每檔遞增、K 線格式與「分鐘結束時刻」、FX 美東與 CME 芝加哥時間。
