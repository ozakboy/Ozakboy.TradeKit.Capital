# Ozakboy.TradeKit.Capital

群益證券 SKCOM API **海外報價**(國際外匯 + 海外期貨)的 .NET 10 用戶端,0.2.0 起另可查**國內期貨歷史 K 線**
(台指 / 小台 / 微台近月)。0.3.0(未發佈)加上國內期貨 / 選擇權即時報價、海外五檔 / 十檔、海外選擇權報價與
**期貨帳務查詢(唯讀)**。**沒有任何下單、刪改單、出入金能力**,也不做需要證券戶的功能。

[English](README.md)

SKCOM 是 COM 元件,必須跑在有訊息迴圈的 STA 執行緒上。所以這個套件附了一支 x64 宿主行程
`Ozakboy.TradeKit.Capital.Host` 掛 SKCOM,以 **stdio 換行分隔 JSON** 跟父行程溝通。用戶端 `CapitalHostSession`
負責啟動與監督宿主、送請求、讀報價與成交事件,把整數價格換成 `decimal`、交易所當地時間換成 UTC。SKCOM 當掉或卡住,
不會拖垮你的應用程式。

> **目前狀態:**本套件依賴的 SKCOM 呼叫、格式與時區,都在 2026-11-06 以真帳密對 SKCOM 2.13.59 實測過(見「實測與未實測」)。
> `serve` 協定、監督與重連邏輯目前**只對 `tests/` 的假宿主測過**,尤其 3002 斷線重連從沒在真環境遇過。正式使用前請先看那一節。
> **0.3.0 的新功能依群益手冊 V2.13.59 實作,2026-11-09 以宿主的 `probe-full` 指令對真 SKCOM 實測過格式並據此修正**(只有期貨戶、帳戶沒有部位與委託;
> 下表「實測狀態」欄寫明哪些已實測、哪些只實測了一部分)。

## 這一版做了什麼

| 範圍 | 狀態 | 實測狀態 |
| --- | --- | --- |
| 啟動與監督宿主(`CapitalHostSession`),帳密只走子行程環境變數 | 完成 | 只對假宿主測過 |
| 登入失敗只回報一次、**絕不重試**(`capital.login.failed`,`Data["code"]`、`Data["locked"]`) | 完成 | 代碼取自手冊 |
| 宿主意外結束:以 1、2、5、10、30 秒退避重啟,最多 `MaxRestarts` 次,重啟後重送全部訂閱 | 完成 | 只對假宿主測過 |
| 報價主機斷線(3002):宿主以 5、10、30、60 秒退避重連並重送全部訂閱 | 完成 | 未實測 |
| 海外商品清單併商品明細(`GetProductsAsync`) | 完成 | 已實測 |
| 海外報價與成交(`Events`,有界佇列,滿了丟最舊的並計數 `DroppedEvents`) | 完成 | 已實測 |
| 海外歷史 K 線:分、日、週、月(`GetKLinesAsync`) | 完成 | 已實測 |
| 連線狀態(`GetConnectionStatusAsync`,含國內、海外選擇權狀態與 SKCOM 版本) | 完成 | 海外部分已實測 |
| 國內期貨歷史 K 線:1 分 K、日 K,全盤或 AM 盤(`GetDomesticKLinesAsync`,0.2.0) | 完成 | 格式已實測;`serve` 未實測 |
| 國內狀態獨立於海外(`DomesticState`、`DomesticStateChanged`);國內不可用不影響海外與結束碼(0.2.0) | 完成 | 只對假宿主測過 |
| 宿主不留孤兒:放進 Job Object(KILL_ON_JOB_CLOSE)、stdin 結束或父行程消失即結束(0.2.0) | 完成 | 只對假宿主測過 |
| SKCOM 版本標示與啟動檢查(`SkcomVersion`、`IsTestedSkcomVersion`,0.3.0) | 完成 | 已實測(`2.13.59_x64`,登入前可讀);`serve` 未實測 |
| 國內期貨 / 選擇權商品清單(`GetDomesticProductsAsync`,0.3.0) | 完成 | 已實測(6 欄、AM 日盤代碼成對) |
| 國內期貨 / 選擇權即時報價、成交、五檔、即時分 K(`SubscribeDomesticAsync`,0.3.0) | 完成 | 格式與時間已實測(夜盤);跨午夜、10 檔上限未實測 |
| 海外五檔與市場深度十檔(`SubscribeDepthAsync`,0.3.0) | 完成 | 格式已實測;五檔由哪個訂閱觸發未分出 |
| 海外選擇權商品、報價、成交、五檔、十檔(`OverseasOptionsState` 與 `SubscribeOverseasOptions*Async`,0.3.0) | 完成 | 連線、清單、報價、檔位已實測;**成交與時區未實測** |
| 期貨帳務查詢(唯讀):帳號、權益數、未平倉、委託 / 成交回報(`GetAccountsAsync` 等,0.3.0) | 完成 | 帳號與「沒有部位」的回覆已實測;**有部位時的欄位與數值待實測**;中文會亂碼 |
| 下單、刪改單、減量、改價、智慧單、出入金 | **不提供,也不會提供** | — |
| 需要證券戶的功能:台股報價、上市櫃 K 線、證券下單、庫存、複委託 | **不在範圍內** | — |

## 相容的 SKCOM 版本

本套件以 **SKCOM 2.13.59 x64** 實測。宿主的 COM 介面是依該版型別庫產生的(`tools/SkComInteropGen`):COM 雙重介面是照**方法順序**呼叫的,
群益改版若動到方法順序,宿主會呼叫到錯的方法。所以:

- 宿主啟動時讀 `SKCenterLib_GetSKAPIVersionAndBit`,`CapitalHostSession.SkcomVersion` 是讀到的版本(例如 `2.13.59_x64`),
  `IsTestedSkcomVersion` 表示它在 `CapitalSkcomVersion.Tested` 清單裡。
- 不是實測過的版本時,工作階段記一筆警告日誌(`capital.skcom.version_untested`,事件 4015)並**照常運作**。
- 升級 SKCOM 前,請先以新版跑宿主的 `probe-full` 探針,並以新版的 `Interop.SKCOMLib.dll` 重跑產生器,確認方法順序沒變。

## 需求

- Windows x64。宿主隨套件以 self-contained 出貨(自帶 .NET 與 Windows Desktop runtime),**不用為宿主另裝 .NET runtime**;
  你自己的程式照常需要 .NET 10 runtime。
- **SKCOM x64 由你自己下載、註冊。**本套件**不含**、也不轉散布任何群益的檔案。請從群益官網下載 SKCOM API,把 `x64`
  資料夾放到固定位置,**以系統管理員身分**執行裡面的 `install.bat`。之後若搬走或刪掉那個資料夾,註冊會指向不存在的 DLL
  (`capital.skcom.file_missing`),重新註冊即可。
- 有海期報價權限的群益帳號,並依群益說明安裝好憑證。
- 國內期貨 K 線需要群益**期貨戶**;**沒有證券戶拿不到上市櫃資料**(實測期貨戶查 `2330` 回 9999)。本套件只承諾期貨。

## 安裝

```
dotnet add package Ozakboy.TradeKit.Capital
```

套件把宿主放在 `tools/capital-host/`,`buildTransitive` 的 targets 會在建置時把它複製到參照本套件(直接或間接)的專案的
`$(OutDir)capital-host\`,發佈時也一併帶上。`CapitalHostOptions.DefaultHostPath(AppContext.BaseDirectory)` 就指到那裡。

## 快速開始

```csharp
using Ozakboy.TradeKit.Capital;

var options = new CapitalHostOptions
{
    HostExecutablePath = CapitalHostOptions.DefaultHostPath(AppContext.BaseDirectory),
    // 選填:SKCOM 自己的紀錄目錄。它的紀錄可能含登入帳號,請放在私人資料夾。
    LogDirectory = null,
};

await using var session = new CapitalHostSession(options, logger);

// 身分證字號會轉大寫;Validate() 檢查「1 個英文字母 + 9 個數字」,失敗訊息不回顯任何字元。
var credentials = new CapitalCredentials(loginId, password);

var started = await session.StartAsync(credentials, cancellationToken);

if (started.IsFailure)
{
    // capital.login.failed       -> 不會重試;Data["code"] = 300/306/307/600/604,307 時 Data["locked"] = true
    // capital.skcom.not_registered / capital.skcom.file_missing -> 以系統管理員身分註冊 SKCOM x64
    // capital.connect.timeout    -> 60 秒內沒有 OnConnect(3001)
    return;
}

await session.SubscribeAsync([CapitalSymbol.Parse("FX,SUSDTWD"), CapitalSymbol.Parse("CME,NQ0000")]);

await foreach (var item in session.Events.ReadAllAsync(cancellationToken))
{
    switch (item)
    {
        case CapitalQuote quote:
            // quote.Close 是 decimal;quote.TradingDay 是群益的交易日
            break;
        case CapitalTick tick:
            // tick.TimeUtc 是 UTC;tick.IsReplay 表示訂閱當下回補的當日成交;
            // 請以 (Symbol, Sequence) 去重 —— 重啟後回補會再送一次
            break;
    }
}
```

K 線:

```csharp
var bars = await session.GetKLinesAsync(
    CapitalSymbol.Parse("FX,SUSDJPY"), CapitalKLineType.Minute,
    new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 5), decimals: 2);
// 分 K:OpenTimeUtc 是那一分鐘的開始(群益給的是結束,這裡已減 1 分鐘)。
// 日 / 週 / 月 K:只有 TradingDay。
```

### 國內期貨歷史 K 線(0.2.0)

```csharp
// 代碼:TX00 台指近月、MTX00 小台近月、TM0000 微台近月(換月方式與永豐連續月相同)。
// 起訖是「交易日」:Full 會含前一個交易日 15:00 起的夜盤,查週一會從上週五 15:01 開始。
var tx = await session.GetDomesticKLinesAsync(
    "TX00", CapitalDomesticKLineType.Minute, CapitalTradeSession.Full,
    new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 6));

if (tx.IsFailure)
{
    // capital.domestic.unavailable -> 國內不可用或斷線;Data["reason"] 見 CapitalDomesticReasons(海外照常)
    // capital.kline.rejected       -> 代碼不存在或沒權限;Data["code"] = 群益代碼(實測 9999、3023)
    // capital.kline.timeout        -> 60 秒內沒有 OnKLineComplete
    return;
}

foreach (CapitalDomesticBar bar in tx.GetValueOrThrow())
{
    // bar.OpenTimeUtc = 台北時間的結束時刻 − 1 分鐘,轉 UTC;日 K 只有 bar.TradingDay。
    // Volume == 0 是沒有成交的空 K 棒,照原樣回。
}

session.DomesticStateChanged += (_, e) => { /* e.Current、e.Reason、e.Code */ };
```

要知道的事:

- 國內在海外就緒之後才連(`DomesticState`:`NotStarted` → `Connecting` → `Ready`,約 0.5 秒)。`Connecting` 時送的請求由宿主等到有結果再處理。
  國內 `EnterMonitorLONG` 被拒、60 秒內沒收到 3003 → `Unavailable`(不重試),**海外照常 ready、`StartAsync` 照常成功**;國內斷線(3002)以 5、10、30、60 秒退避重連。
  國內的變化只觸發 `DomesticStateChanged`,不觸發 `StateChanged`。
- **收盤價可能是結算價。**全盤分 K 的日盤最後一根(13:45;結算日 13:30)與全盤日 K 的收盤,實測是期交所的結算價;AM 盤日 K 的收盤才是最後成交價
  (2026-11-06 台指:最後成交 39786、結算 39652)。本套件回原始資料、不修正。
- 一個請求最多 60,000 筆,超過時群益**保留最新、截掉最舊,不報錯**;1 分 K 約只保留最近 1 年,日 K 到 2015 年都有。長區間請分段查,並檢查第一根。
- 讀不懂的行記數略過(寫一筆警告日誌),不讓整批失敗。
- 已知群益資料錯誤:2026-10-30 週五夜盤(歸屬 11/02)的量是期交所官方的 2 倍,見 [`docs/DESIGN.md`](docs/DESIGN.md) §6。

### 國內即時報價、海外十檔、海外選擇權(0.3.0)

```csharp
// 國內期貨 / 選擇權:報價、成交(先回補當日)、五檔(含衍生一檔)、即時分 K;整組最多 10 檔(手冊 4-4-1)。
// 清單裡每檔都有一對代碼:結尾 AM 的(TX00AM)只有日盤,要夜盤行情請訂全盤代碼(TX00)。
IReadOnlyList<CapitalDomesticProduct> futures = (await session.GetDomesticProductsAsync(CapitalDomesticMarket.Futures)).GetValueOrThrow();
await session.SubscribeDomesticAsync(["TX00", "MTX00"]);

// 海外十檔(市場深度);海外五檔推定隨 SubscribeAsync 的成交訂閱一起來。
await session.SubscribeDepthAsync([CapitalSymbol.Parse("CME,NQ0000")]);

// 海外選擇權(SKOOQuoteLib):自己的狀態,不可用時不影響其他報價。
if (session.OverseasOptionsState == CapitalOverseasOptionsState.Ready)
{
    var products = (await session.GetOverseasOptionProductsAsync()).GetValueOrThrow();
    // products[i].Symbol 是訂閱代碼「交易所,代碼」(例如 CME,ES00100X6);另有 Name、LastTradeDate
}

await foreach (var item in session.Events.ReadAllAsync(cancellationToken))
{
    switch (item)
    {
        case CapitalDomesticQuote quote: break;        // 價格 = 原始整數 ÷ 10^Decimals;0(這則沒有值)是 null
        case CapitalDomesticTick tick: break;          // tick.IsSimulated = 試撮;nDate 是日曆日;期貨只有毫秒
        case CapitalDomesticLiveBar bar: break;        // bar.OpenTimeUtc = 群益標示的結束時刻 − 1 分鐘;Reset 時價格為 null
        case CapitalBest5 best5: break;                // best5.Market 區分國內 / 海外 / 海外選擇權
        case CapitalBest10 best10: break;
        case CapitalOverseasOptionQuote optionQuote: break;
        case CapitalOverseasOptionTick optionTick: break;  // 交易所時區不明時 TimeUtc 為 null
    }
}
```

- 國內事件的 `Symbol` 交易所固定是 `CapitalSymbol.DomesticExchange`(`TAIFEX`),只用來標示,不能拿去 `SubscribeAsync`。
- 訂閱都是與已訂集合取聯集;宿主斷線重連後整組重送,宿主重啟後工作階段再送一次。國內與海外選擇權第一次連線時的請求由宿主等到有結果。
- 拿不到的值不填 0:國內報價 / 成交與海外選擇權報價的價格是 0 時給 `null`(實測訂閱後第一則報價只填一部分),小數位數不知道時即時分 K 的價格是 `null`;
  檔位裡群益送 0 / 0 的空檔照原樣保留,使用前請排除價格 0。

### 期貨帳務查詢(唯讀,0.3.0)

```csharp
// 第一次帳務請求時宿主才初始化 SKOrderLib(Initialize → GetUserAccount → ReadCertByID);第一次較慢。
IReadOnlyList<CapitalAccount> accounts = (await session.GetAccountsAsync()).GetValueOrThrow();
string futuresAccount = accounts.First(a => a.Kind == CapitalAccountKind.DomesticFutures).FullAccount!;

var rights = await session.GetFutureRightsAsync(futuresAccount, CapitalCoinType.All);       // 每個幣別一筆,第一筆是基幣
var positions = await session.GetOpenInterestAsync(futuresAccount);                         // 查無資料回空清單
var orders = await session.GetOrderReportAsync(futuresAccount, CapitalOrderReportFormat.All); // 宿主強制兩次回報查詢間隔 5.5 秒
```

- **只有查詢。**宿主的 `ISKOrderLib` COM 定義上,會改變委託、部位或資金的方法全部是呼叫不到的佔位,單元測試以反射核對;本套件不會加入任何下單功能。
- 每個記錄型別都有依手冊欄位解析的具名屬性(可空;解析不出就是 `null`,絕不填 0)與 `RawFields`(原始欄位)。2026-11-09 以沒有部位的帳戶實測過
  帳號、權益數(國內 41 欄、海外 31 欄)與「查無資料」的形式;**有部位 / 委託時的欄位與數值語意還沒實測**,有疑問時可從 `RawFields` 自己取。
- **中文會亂碼**:群益的帳務回覆(SKOrderLib)把中文弄成不可逆的亂碼(例如「查無資料」變成 `?dμL﹐eRA`)。套件只還原實測看過的固定字樣
  (`CapitalSkcomText.Repair`,幣別與錯誤訊息已經過它),其他中文(例如戶名)請用 `CapitalSkcomText.LooksGarbled` 判斷;報價的中文正常。
- **同一個查詢不要連著送**:同一個查詢函式在前一次結束後 1–2 秒再送,群益回 1019(`capital.account.query_rejected`,`Data["code"]` = 1019),請隔幾秒再查。
- 失敗:`capital.account.unavailable`(建不起 SKOrderLib)、`capital.account.init_failed` / `cert_failed`(附群益代碼與步驟,下次請求會再試)、
  `capital.account.query_rejected` / `query_failed` / `query_timeout`。
- 帳號、戶名、身分證字號不進宿主 stderr 與用戶端日誌(拿到就登記遮罩,換成 `<ACCT>` / `<NAME>` / `<ID>`);記錄型別的 `ToString()` 只露帳號末 3 碼。
  `CapitalAccount.RawFields` 含身分證字號與戶名,請自行保管。

## 幾個值得知道的設計決定

### 為什麼用子行程 stdio,不用 HTTP

宿主只有一個呼叫端,就是啟動它的父行程。stdio 天生只有父行程拿得到:不必開 port、不必發 token,本機其他行程也連不上。
協定形狀同 MCP 的 stdio 傳輸,好測。完整協定見 [`docs/DESIGN.md`](docs/DESIGN.md) §2。

### 帳密只走子行程的環境變數

`StartAsync` 把身分證字號與密碼放進 `ProcessStartInfo.Environment`(`CAPITAL_LOGIN_ID`、`CAPITAL_PASSWORD`),
其他地方一律沒有 —— 不上命令列、不寫檔、不進日誌。宿主讀完立刻從自己的環境清掉。兩者都會登記到 `SecretMasker`;
此外宿主 stderr 的每一行都會把帳密字面值換掉(`<ID>` / `***`),因為遮罩器不收短於 8 個字元的值。宿主自己寫出一行之前
也會先把帳號換成 `<ID>`。`CapitalCredentials.ToString()` 不含內容,型別刻意不用 record。

### 登入絕不重試

群益密碼錯太多次會鎖帳號(307),而且反覆登入解不開。登入失敗就停在 `LoginFailed`,沒有任何東西會重試,包括重啟迴圈。
只有連線類的失敗會重試:宿主崩潰(重啟,新行程會重新登入)與報價主機斷線(宿主重新呼叫 `EnterMonitorLONG`,絕不重新登入)。

### 宿主的結束碼

| 結束碼 | 意義 | 用戶端行為 |
| --- | --- | --- |
| 0 | shutdown、stdin 結束,或父行程不在了 | 無(預期) |
| 1 | 宿主內部缺陷 | 重啟 |
| 2 | 缺 `CAPITAL_LOGIN_ID` / `CAPITAL_PASSWORD`,沒有呼叫 Login | Faulted,不重啟 |
| 3 | 建不起 SKCOM 物件 | Faulted,不重啟 |
| 4 | 登入失敗 | LoginFailed,不重啟 |
| 5 | 60 秒內沒有 `OnConnect(3001)` | 重啟(計入 `MaxRestarts`) |

國內報價的任何失敗都不影響結束碼。

### 宿主不會變成孤兒(0.2.0)

宿主若在父行程結束後還活著,會一直掛著群益登入。所以有三道保險:用戶端啟動宿主後把它放進 Windows **Job Object**
(`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`,handle 由 `CapitalHostSession` 持有到 `DisposeAsync`),父行程不論正常結束或被強制結束,系統都會收掉宿主;
宿主讀到 stdin EOF 就 LeaveMonitor(海外與國內)後以 0 結束;另外每 5 秒檢查用戶端以 `CAPITAL_PARENT_PID` 傳入的父行程,不在了也一樣結束。
決定結束後 15 秒內沒能有序收尾(例如 STA 卡在 SKCOM 的同步呼叫),宿主強制結束。

### 數字與時間

- 報價與成交的價格是原始整數,`CapitalPriceScale.FromRaw` 以 `decimal` 除以 10^小數位數,任何地方都不經過 `double` / `float`。
- K 線價格是單精度浮點印出來的文字(`157.889999`),以 `decimal` 讀進來,再依你傳的小數位數四捨五入(`157.89`)。
- `nDenominator` 實測都是 1;不是 1 的報價照小數位數換算,每個商品記一次 `capital.price.fraction_unsupported` 警告。
- 時間是**交易所當地時間**,由 `CapitalExchangeClock` 轉換(含夏令時間)。實測:`FX`、`NYM` 為美東,`CME`、`CBOT`
  為芝加哥。其他交易所只依所在地推定,標 `IsVerified = false`;`INDEX` 與 `ICS` 沒有對照,回
  `capital.clock.unknown_exchange`。

## 實測與未實測

**已實測**(SKCOM 2.13.59 x64、真帳密、2026-11-06,以宿主的 `probe` 指令):

- 登入前必須掛 `OnReplyMessage` 並回 -1;登入成功回 0。
- `EnterMonitorLONG` 之後只收到 `OnConnect(3001)`,不會有 3003。`GetQuoteStatus` 回連線數 0、未超限。
- 商品清單(`交易所,交易所名稱,代碼,名稱,最後交易日,第一通知日`,1,345 筆)與明細
  (`…,下單交易所,下單代碼,最後交易日,第一通知日,商品種類`)都以 `##` 開頭那筆結尾。外匯最後交易日 `99991231`、
  第一通知日 `0`;期貨熱月 = 根代碼 + `0000`(`CME,NQ0000`)。
- 訂閱:`RequestStocks(-1, "FX,SUSDTWD#CME,NQ0000")`,每檔再一個 `RequestTicks`。
- 報價價格 ÷ 10^`sDecimal`(USD/TWD 3、JPY 2、EUR 與 CNY 4、NQ 2、GC 1、YM 0);外匯的 `nTQty` 是報價次數,不是成交量。
- 成交:訂閱當下先回補當個交易日的全部成交(7 檔約 20.8 萬筆),之後接即時;`nPtr` 每檔遞增;成交可能比第一筆報價先到。
- FX、NYM、CME、CBOT 的時區(成交時間對照接收時間,延遲 < 1 秒)。
- K 線:類型 0 分、1 日、2 週、3 月(4–7 回 1042);格式 `yyyy/MM/dd HH:mm, O, H, L, C, V`(日 K 沒有時間);分 K 的時間是那一分鐘的
  **結束**;只到上一個交易日;沒有結束事件。

**國內期貨 K 線已實測**(SKCOM 2.13.59 x64、只有期貨戶的帳號、2026-11-07,以宿主的 `probe-tw` 指令):

- `SKQuoteLib_EnterMonitorLONG` 回 0 → `OnConnection(3001)` → 約 0.5 秒後 3003(商品基本資料完成,之後才能查)。同一行程同時掛海外的 `EnterMonitorLONG` 正常。
- `RequestKLineAMByDate(代碼, 類型, 1, 盤別, 起, 迄, 1)`:回 0 表示送出;代碼不存在或沒權限立即回非 0(`2330` 回 9999、`TX00AM` 回 3023)。
  資料每根一則 `OnNotifyKLineData`,**結束一定有一則 `OnKLineComplete("##")`**(0 筆時也有)。
- 類型 0 分線、4 日線;盤別 0 全盤(含前一晚夜盤)、1 AM 盤;起訖是交易日。
- 格式:分 K `yyyy/MM/dd HH:mm, O, H, L, C, V`、日 K `yyyy/MM/dd, O, H, L, C, V`;分 K 時間是那一分鐘的結束、台北時間、日期是日曆日;
  日盤 08:46–13:45、夜盤 15:01–05:00;會給量為 0 的空 K 棒。

**未實測:**

- 整個 `serve` 模式對真 SKCOM 跑(含 0.2.0 的國內連線狀態機、`kline-tw`、國內 3002 重連、Job Object 與父行程看門狗)。它用到的每個呼叫都在探針裡以同樣順序實測過,但 `serve` 本身只跑過假的 SKCOM。
- 3002 斷線與重連(不先 `LeaveMonitor` 就再 `EnterMonitorLONG` 行不行、重連後商品索引會不會變;宿主重連時一律清掉索引快取以防萬一)。
- 登入失敗代碼(300、306、307、600、604)取自群益手冊,不是實際遇到的失敗。
- FX、NYM、CME、CBOT 以外交易所的時區。
- 群益每日登入次數上限與訂閱上限(網路經驗談約 100 檔)未確認,本套件沒有限制。
- `nDenominator` 不是 1 的報價,以及 `INDEX` 商品交易日的意義。
- **0.3.0 還沒實測到的部分**(2026-11-09 的 `probe-full` 測不到):有部位 / 有委託時的帳務欄位與數值語意、權益數 sCoinType 1 / 2 與國內未平倉格式 2 / 3
  (當時被 1019 擋下)、`ReadCertByID` 是否必需、夜盤跨午夜的成交與分 K 日期、國內訂閱 10 檔上限、海外五檔由哪個訂閱觸發、海外選擇權的成交與時區、
  回報查詢回傳字串的中文編碼(手冊說是 UTF-8)。逐項結論見 [`docs/DESIGN.md`](docs/DESIGN.md) §7。

**0.3.0 新功能已實測**(SKCOM 2.13.59 x64、只有期貨戶、2026-11-09 夜盤,以宿主的 `probe-full` 指令;細節見 DESIGN §5.3):

- 版本字串 `2.13.59_x64`,登入前可讀。
- 國內商品清單 6 欄(選擇權的系列列 5 欄),每檔一對代碼:結尾 `AM` 的只有日盤;報價的 `sDecimal` 期貨與選擇權都是 2;訂閱後第一則報價只填一部分。
- 國內成交 `nDate` 是日曆日、期貨只有毫秒、回補從開盤前試撮開始(`nSimulate` = 1);即時分 K 的時間是分鐘結束;五檔空檔 0 / 0、衍生一檔只有微台有值。
- 海外五檔 / 十檔的價格 ÷ 10^小數位數、十檔前 5 檔 = 五檔;海外選擇權只送 3001、清單 5 欄以 `##,##,##,##,##` 結尾、`#` 串接訂閱可用、履約價不放大。
- 帳號是 `TF` / `OF`;查詢用「分公司代碼 + 帳號」;狀態事件比資料列先到;沒有部位時國內未平倉回 `001,查無資料`、海外回 `@@,001,查無資料`、回報回 `M003`;
  海外權益數是 31 欄。

## 宿主的 `probe` 指令

`Ozakboy.TradeKit.Capital.Host probe --out <資料夾>` 以互動方式登入(密碼遮罩輸入),把 SKCOM 的原始輸出(商品清單、報價、
成交、K 線)記下來,用來實測格式。只讀行情。輸出資料夾裡 SKCOM 自己的紀錄(`sklog\`)可能含登入帳號,請勿分享。
`probe-tw --out <資料夾>` 是國內版,對 `RequestKLineAMByDate` 跑一組固定查詢(0.2.0 的國內實測就是它做的)。
`probe-full --out <資料夾> [--seconds 180] [--accounts-only]` 量 0.3.0 功能的格式(版本、帳務查詢、國內期權、海外五檔十檔、海外選擇權);
輸出的帳號、戶名、登入帳號都已遮罩(`sklog\` 除外),帳務部分只呼叫唯讀查詢。0.3.0 就是拿它 2026-11-09 的輸出逐項核對的;還沒實測到的點
(見上)要換條件(有部位、跨午夜、海外選擇權有成交時)再跑一次。已知問題:它寫完 `done` 之後行程不會自己結束,請手動關掉(DESIGN §6)。

## 測試

測試完全不碰 SKCOM,也不需要帳密:

- **`tests/FakeCapitalHostExe`** 連結宿主真正的協定層(`Serve/*.cs`),只把 SKCOM 換成依實測行為的模擬:商品以 `##` 結尾、
  只送 3001、先回補再接即時且 `nPtr` 遞增、實測的 K 線文字與分鐘結束時刻、交易所當地時間,外加一筆別的代碼的 K 線行。
  以環境變數讓它登入失敗(300 / 307)、建不起 SKCOM、永遠連不上、崩潰一次或每次都崩潰、斷線後恢復。
  `Ozakboy.TradeKit.Capital.Tests` 以它驅動 `CapitalHostSession`。0.2.0 起也模擬國內報價(2026-11-07 實測):3001 → 3003、
  交易日含前一晚夜盤、分鐘結束時刻、空 K 棒、60,000 筆上限截最舊、未知代碼回 9999,並可設成國內不可用、斷線、不送結束事件;
  另有直接啟動假宿主的測試,驗證 stdin EOF、父行程消失與 Job Object 關閉時宿主都會結束。
  0.3.0 起也模擬國內即時報價、海外五檔 / 十檔、海外選擇權與帳務查詢:2026-11-09 實測過的部分照實測(商品清單與 AM 代碼、第一則報價只填一部分、
  成交毫秒、帳務的「查無資料」形式與中文亂碼、海外權益數 31 欄、海外選擇權清單格式),沒實測到的(有部位的帳務列、海外選擇權成交時區等)照手冊推定。
  可設成海外選擇權不可用、SKOrderLib 建不起來、初始化或讀憑證失敗、權益數查詢失敗(訊息含帳號,驗證遮罩)、不同的 SKCOM 版本。
- **`Ozakboy.TradeKit.Capital.Host.Tests`** 以可程式化的 `ISkQuoteSource` 在記憶體裡驅動協定層。

```
dotnet test -c Release
```

## 安全

- 沒有預設帳密;帳密不寫檔、不進日誌、不上命令列、不出現在 `ToString`。
- 宿主的 stderr 逐行遮罩後才交給你的 logger;stdout 只放協定訊息。
- SKCOM 自己的紀錄目錄(`LogDirectory`)可能含登入帳號,那是你的資料夾,請勿放進版控。
- 0.3.0:帳號、戶名、身分證字號一拿到就登記遮罩(宿主與用戶端兩邊都做),不進 stderr 與日誌;帳務記錄型別的 `ToString()` 只露帳號末 3 碼。
- 0.3.0:**沒有下單能力。**宿主的 `ISKOrderLib` 上會改變委託、部位或資金的方法都是只收 `IntPtr` 的佔位,產生器對禁用字樣硬擋,
  `SkComOrderInteropSafetyTests` 以反射核對可呼叫的方法恰好是審過的唯讀查詢。

## 相依政策

只允許 .NET BCL、Microsoft 官方套件與 `Ozakboy.*` 套件,判定看**遞移相依**。不引用、不轉散布任何群益的檔案:
COM 介面由 `tools/SkComInteropGen` 從使用者本機的群益型別庫中繼資料產生(群益的 DLL 不進 repo)。

| 套件 | 版本 | 用途 |
| --- | --- | --- |
| `Ozakboy.Core.Abstractions` | 0.3.0 | `Result` / `Error` |
| `Ozakboy.Security` | 0.1.1 | `SecretMasker` |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.14 | 日誌 |

測試專案另外使用 MSTest(Microsoft.Testing.Platform)。

## 授權

MIT,見 [LICENSE](LICENSE)。SKCOM 是群益證券的軟體,依群益自己的條款使用。
