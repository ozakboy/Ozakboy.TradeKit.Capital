using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Ozakboy.TradeKit.Capital.Tests.Support;

/// <summary>
/// 把每一行日誌留下來的 logger(含例外訊息),用來斷言帳密沒有出現在任何紀錄裡。
/// A logger that keeps every line, exception text included, so tests can assert credentials never appear.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly Lock _gate = new();
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public string AllText => string.Join('\n', Entries.Select(entry => entry.Message));

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        string message = formatter(state, exception) + (exception is null ? string.Empty : " | " + exception);

        lock (_gate)
        {
            _entries.Add((logLevel, message));
        }
    }
}

/// <summary>
/// 斷言結果是失敗並取出錯誤。
/// Asserts a failure and returns its error.
/// </summary>
internal static class ResultAssert
{
    public static Error Failure(this Result result)
    {
        Assert.IsTrue(result.IsFailure, "expected a failure");
        return result.Error;
    }

    public static Error Failure<T>(this Result<T> result)
    {
        Assert.IsTrue(result.IsFailure, "expected a failure");
        return result.Error;
    }
}

/// <summary>
/// 假宿主的位置與殘留行程清理。
/// Where the fake host lives, and cleanup of leftover processes.
/// </summary>
internal static class FakeHost
{
    public const string ProcessName = "FakeCapitalHostExe";

    /// <summary>
    /// 測試用的帳密(假值,格式合法)。Fake credentials in a valid format.
    /// </summary>
    public const string LoginId = "A123456789";

    public const string Password = "Pw-Fake-2026";

    /// <summary>
    /// 與測試專案同層的 FakeCapitalHostExe 專案輸出。The output of the FakeCapitalHostExe project next to this one.
    /// </summary>
    public static string ExecutablePath()
    {
        DirectoryInfo targetFramework = new(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        DirectoryInfo configuration = targetFramework.Parent!;
        DirectoryInfo testsRoot = configuration.Parent!.Parent!.Parent!;
        string path = Path.Combine(testsRoot.FullName, ProcessName, "bin", configuration.Name, targetFramework.Name, ProcessName + ".exe");

        Assert.IsTrue(File.Exists(path), $"the fake host is missing: {path}");
        return path;
    }

    public static void KillLeftovers()
    {
        foreach (Process process in Process.GetProcessesByName(ProcessName))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // 已經結束。Already gone.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    /// <summary>
    /// 等條件成立(輪詢),逾時回 false。Polls until the condition holds; false on timeout.
    /// </summary>
    public static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        Stopwatch watch = Stopwatch.StartNew();

        while (watch.Elapsed < timeout)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(25);
        }

        return condition();
    }
}
