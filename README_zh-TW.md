# Ozakboy.TradeKit.Capital

群益證券 SKCOM API **海外報價**(國際外匯 + 海外期貨)的 .NET 10 用戶端。**只接報價**,不含下單、帳務。

[English](README.md)

SKCOM 是 COM 元件,必須跑在有訊息迴圈的 STA 執行緒上。所以這個套件附了一支 x64 宿主行程
`Ozakboy.TradeKit.Capital.Host` 掛 SKCOM,以 **stdio 換行分隔 JSON** 跟父行程溝通。用戶端 `CapitalHostSession`
負責啟動與監督宿主、送請求、讀報價與成交事件,把整數價格換成 `decimal`、交易所當地時間換成 UTC。SKCOM 當掉或卡住,
不會拖垮你的應用程式。

> **目前狀態:**本套件依賴的 SKCOM 呼叫、格式與時區,都在 2026-11-06 以真帳密對 SKCOM 2.13.59 實測過(見「實測與未實測」)。
> `serve` 協定、監督與重連邏輯目前**只對 `tests/` 的假宿主測過**,尤其 3002 斷線重連從沒在真環境遇過。正式使用前請先看那一節。

## 這一版做了什麼

| 範圍 | 狀態 |
| --- | --- |
| 啟動與監督宿主(`CapitalHostSession`),帳密只走子行程環境變數 | 完成 |
| 登入失敗只回報一次、**絕不重試**(`capital.login.failed`,`Data["code"]`、`Data["locked"]`) | 完成 |
| 宿主意外結束:以 1、2、5、10、30 秒退避重啟,最多 `MaxRestarts` 次,重啟後重送訂閱 | 完成 |
| 報價主機斷線(3002):宿主以 5、10、30、60 秒退避重連並重送全部訂閱 | 完成,未實測 |
| 海外商品清單併商品明細(`GetProductsAsync`) | 完成 |
| 報價與成交(`Events`,有界佇列,滿了丟最舊的並計數 `DroppedEvents`) | 完成 |
| 歷史 K 線:分、日、週、月(`GetKLinesAsync`) | 完成 |
| 連線狀態(`GetConnectionStatusAsync`) | 完成 |
| 下單、帳務、台股報價、選擇權鏈 | **不在範圍內** |

## 需求

- Windows x64,裝 **.NET 10 Desktop Runtime**(宿主用 Windows Forms 的訊息迴圈,framework-dependent)。
- **SKCOM x64 由你自己下載、註冊。**本套件**不含**、也不轉散布任何群益的檔案。請從群益官網下載 SKCOM API,把 `x64`
  資料夾放到固定位置,**以系統管理員身分**執行裡面的 `install.bat`。之後若搬走或刪掉那個資料夾,註冊會指向不存在的 DLL
  (`capital.skcom.file_missing`),重新註冊即可。
- 有海期報價權限的群益帳號,並依群益說明安裝好憑證。

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
| 0 | shutdown 或 stdin 結束 | 無(預期) |
| 1 | 宿主內部缺陷 | 重啟 |
| 2 | 缺 `CAPITAL_LOGIN_ID` / `CAPITAL_PASSWORD`,沒有呼叫 Login | Faulted,不重啟 |
| 3 | 建不起 SKCOM 物件 | Faulted,不重啟 |
| 4 | 登入失敗 | LoginFailed,不重啟 |
| 5 | 60 秒內沒有 `OnConnect(3001)` | 重啟(計入 `MaxRestarts`) |

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

**未實測:**

- 整個 `serve` 模式對真 SKCOM 跑。它用到的每個呼叫都在探針裡以同樣順序實測過,但 `serve` 本身只跑過假的 SKCOM。
- 3002 斷線與重連(不先 `LeaveMonitor` 就再 `EnterMonitorLONG` 行不行、重連後商品索引會不會變;宿主重連時一律清掉索引快取以防萬一)。
- 登入失敗代碼(300、306、307、600、604)取自群益手冊,不是實際遇到的失敗。
- FX、NYM、CME、CBOT 以外交易所的時區。
- 群益每日登入次數上限與訂閱上限(網路經驗談約 100 檔)未確認,本套件沒有限制。
- `nDenominator` 不是 1 的報價,以及 `INDEX` 商品交易日的意義。

## 宿主的 `probe` 指令

`Ozakboy.TradeKit.Capital.Host probe --out <資料夾>` 以互動方式登入(密碼遮罩輸入),把 SKCOM 的原始輸出(商品清單、報價、
成交、K 線)記下來,用來實測格式。只讀行情。輸出資料夾裡 SKCOM 自己的紀錄(`sklog\`)可能含登入帳號,請勿分享。

## 測試

測試完全不碰 SKCOM,也不需要帳密:

- **`tests/FakeCapitalHostExe`** 連結宿主真正的協定層(`Serve/*.cs`),只把 SKCOM 換成依實測行為的模擬:商品以 `##` 結尾、
  只送 3001、先回補再接即時且 `nPtr` 遞增、實測的 K 線文字與分鐘結束時刻、交易所當地時間,外加一筆別的代碼的 K 線行。
  以環境變數讓它登入失敗(300 / 307)、建不起 SKCOM、永遠連不上、崩潰一次或每次都崩潰、斷線後恢復。
  `Ozakboy.TradeKit.Capital.Tests` 以它驅動 `CapitalHostSession`。
- **`Ozakboy.TradeKit.Capital.Host.Tests`** 以可程式化的 `ISkQuoteSource` 在記憶體裡驅動協定層。

```
dotnet test -c Release
```

## 安全

- 沒有預設帳密;帳密不寫檔、不進日誌、不上命令列、不出現在 `ToString`。
- 宿主的 stderr 逐行遮罩後才交給你的 logger;stdout 只放協定訊息。
- SKCOM 自己的紀錄目錄(`LogDirectory`)可能含登入帳號,那是你的資料夾,請勿放進版控。

## 相依政策

只允許 .NET BCL、Microsoft 官方套件與 `Ozakboy.*` 套件,判定看**遞移相依**。不引用、不轉散布任何群益的檔案:
COM 介面是手寫宣告的。

| 套件 | 版本 | 用途 |
| --- | --- | --- |
| `Ozakboy.Core.Abstractions` | 0.3.0 | `Result` / `Error` |
| `Ozakboy.Security` | 0.1.1 | `SecretMasker` |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.14 | 日誌 |

測試專案另外使用 MSTest(Microsoft.Testing.Platform)。

## 授權

MIT,見 [LICENSE](LICENSE)。SKCOM 是群益證券的軟體,依群益自己的條款使用。
