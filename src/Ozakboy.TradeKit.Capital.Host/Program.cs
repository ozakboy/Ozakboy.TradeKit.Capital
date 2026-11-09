using System.Text;
using Ozakboy.TradeKit.Capital.Host;
using Ozakboy.TradeKit.Capital.Host.Probe;

// 結束碼:0 成功、1 宿主內部錯誤、2 參數錯誤或缺帳密、3 SKCOM 未註冊、4 登入失敗、5 連不上報價主機
// Exit codes: 0 ok, 1 internal error, 2 bad arguments or missing credentials, 3 SKCOM not registered, 4 login failed,
// 5 quote server unreachable.

// serve:stdio 換行分隔 JSON 協定(見 docs/DESIGN.md §2);帳密只從環境變數讀。不碰 Console.Out,stdout 只放協定訊息。
// serve: the newline-delimited JSON protocol over stdio; credentials come only from the environment. Console.Out is
// never touched, so stdout carries protocol messages only.
if (args.Length > 0 && args[0] == "serve")
{
    return await ServeCommand.RunAsync().ConfigureAwait(false);
}

Console.OutputEncoding = Encoding.UTF8;

if (args.Length > 0 && args[0] == "probe-full")
{
    return await RunFullProbeAsync(args.Skip(1).ToArray()).ConfigureAwait(false);
}

if (args.Length == 0 || (args[0] != "probe" && args[0] != "probe-tw"))
{
    Console.WriteLine("用法:Ozakboy.TradeKit.Capital.Host serve | probe --out <輸出目錄> [--seconds 120] [--kline-days 3] [--symbol 交易所,代碼]... | probe-tw --out <輸出目錄> | probe-full --out <輸出目錄> [--seconds 180] [--accounts-only]");
    return 2;
}

bool domestic = args[0] == "probe-tw";

ProbeOptions? options = ProbeOptions.Parse(args.Skip(1).ToArray(), out string error);

if (options is null)
{
    Console.WriteLine(error);
    return 2;
}

Console.WriteLine(domestic
    ? "群益國內報價探針(歷史 K 線):只讀行情、不下單。帳號密碼只在記憶體使用,不寫進任何檔案。請先關掉 TideTrade,避免同一個帳號同時登入。"
    : "群益海外報價探針:只讀行情、不下單。帳號密碼只在記憶體使用,不寫進任何檔案。");
string userId = ConsoleSecret.Read("身分證字號:").ToUpperInvariant();
string password = ConsoleSecret.Read("登入密碼:");

if (userId.Length == 0 || password.Length == 0)
{
    Console.WriteLine("帳號或密碼是空的,結束。");
    return 2;
}

using ProbeLog log = new(options.OutputDirectory);
using StaComThread sta = new();
if (domestic)
{
    DomesticProbeRunner domesticRunner = new(options, log);
    return await sta.InvokeAsync(() => domesticRunner.RunAsync(userId, password)).ConfigureAwait(false);
}

ProbeRunner runner = new(options, log);
return await sta.InvokeAsync(() => runner.RunAsync(userId, password)).ConfigureAwait(false);

// probe-full:版本、期貨帳務查詢(唯讀)、國內期權 / 海外五檔十檔 / 海外選擇權報價(見 docs/DESIGN.md §5.2)。
// 輸出一律經遮罩器:登入帳號 → <ID>、帳號號碼 → <ACCTn>、戶名 → <NAME>,對照表只在記憶體。
// probe-full: version, read-only futures account queries and the new quote feeds; all output is redacted.
static async Task<int> RunFullProbeAsync(string[] arguments)
{
    FullProbeOptions? fullOptions = FullProbeOptions.Parse(arguments, out string fullError);

    if (fullOptions is null)
    {
        Console.WriteLine(fullError);
        return 2;
    }

    Console.WriteLine(fullOptions.AccountsOnly
        ? "群益完整探針(只跑 SKCOM 版本與期貨帳務查詢):只查詢、不下單,這支程式沒有任何下單能力。帳號密碼只在記憶體使用,不寫進任何檔案。請先關掉 TideTrade,避免同一個帳號同時登入。"
        : "群益完整探針(SKCOM 版本、期貨帳務查詢、國內期權 / 海外五檔十檔 / 海外選擇權報價;報價部分要開盤時跑):只查詢、不下單,這支程式沒有任何下單能力。帳號密碼只在記憶體使用,不寫進任何檔案。請先關掉 TideTrade,避免同一個帳號同時登入。");
    string fullUserId = ConsoleSecret.Read("身分證字號:").ToUpperInvariant();
    string fullPassword = ConsoleSecret.Read("登入密碼:");

    if (fullUserId.Length == 0 || fullPassword.Length == 0)
    {
        Console.WriteLine("帳號或密碼是空的,結束。");
        return 2;
    }

    ProbeRedactor redactor = new();
    redactor.AddLoginId(fullUserId);
    using ProbeLog fullLog = new(fullOptions.OutputDirectory, redactor);
    using StaComThread fullSta = new();
    FullProbeRunner fullRunner = new(fullOptions, fullLog, redactor);
    return await fullSta.InvokeAsync(() => fullRunner.RunAsync(fullUserId, fullPassword)).ConfigureAwait(false);
}
