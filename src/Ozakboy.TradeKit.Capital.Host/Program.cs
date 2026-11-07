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

if (args.Length == 0 || args[0] != "probe")
{
    Console.WriteLine("用法:Ozakboy.TradeKit.Capital.Host serve | probe --out <輸出目錄> [--seconds 120] [--kline-days 3] [--symbol 交易所,代碼]...");
    return 2;
}

ProbeOptions? options = ProbeOptions.Parse(args.Skip(1).ToArray(), out string error);

if (options is null)
{
    Console.WriteLine(error);
    return 2;
}

Console.WriteLine("群益海外報價探針:只讀行情、不下單。帳號密碼只在記憶體使用,不寫進任何檔案。");
string userId = ConsoleSecret.Read("身分證字號:").ToUpperInvariant();
string password = ConsoleSecret.Read("登入密碼:");

if (userId.Length == 0 || password.Length == 0)
{
    Console.WriteLine("帳號或密碼是空的,結束。");
    return 2;
}

using ProbeLog log = new(options.OutputDirectory);
using StaComThread sta = new();
ProbeRunner runner = new(options, log);
return await sta.InvokeAsync(() => runner.RunAsync(userId, password)).ConfigureAwait(false);
