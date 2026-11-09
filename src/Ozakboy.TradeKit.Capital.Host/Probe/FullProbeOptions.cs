using System.Globalization;

namespace Ozakboy.TradeKit.Capital.Host.Probe;

/// <summary>
/// <c>probe-full</c> 的參數:<c>probe-full --out &lt;目錄&gt; [--seconds 180] [--accounts-only]</c>。
/// Arguments of probe-full.
/// </summary>
internal sealed class FullProbeOptions
{
    /// <summary>預設收集秒數。Default collection seconds.</summary>
    public const int DefaultSeconds = 180;

    private const int MaxSeconds = 3600;

    /// <summary>輸出目錄(絕對路徑)。Output directory, absolute.</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>訂閱報價後收集的秒數。Seconds to collect quotes after subscribing.</summary>
    public int Seconds { get; init; } = DefaultSeconds;

    /// <summary>只跑 SKCOM 版本與帳務查詢(不連報價主機;週末可跑)。Only the version and account queries; no quote servers.</summary>
    public bool AccountsOnly { get; init; }

    /// <summary>
    /// 解析參數;不合法回 <see langword="null"/> 並給錯誤訊息。
    /// Parses the arguments; returns null with an error message when invalid.
    /// </summary>
    /// <param name="args">probe-full 之後的參數。Arguments after "probe-full".</param>
    /// <param name="error">錯誤訊息。Error message.</param>
    /// <returns>選項或 null。Options or null.</returns>
    public static FullProbeOptions? Parse(IReadOnlyList<string> args, out string error)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? output = null;
        int seconds = DefaultSeconds;
        bool accountsOnly = false;

        for (int i = 0; i < args.Count; i++)
        {
            string name = args[i];

            if (name == "--accounts-only")
            {
                accountsOnly = true;
                continue;
            }

            string? value = i + 1 < args.Count ? args[i + 1] : null;

            if (value is null || value.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"參數 {name} 缺少值。";
                return null;
            }

            switch (name)
            {
                case "--out" when !string.IsNullOrWhiteSpace(value):
                    output = value;
                    break;
                case "--seconds" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int s) && s is > 0 and <= MaxSeconds:
                    seconds = s;
                    break;
                default:
                    error = $"不認得的參數或值:{name} {value}";
                    return null;
            }

            i++;
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            error = "必須指定 --out <輸出目錄>。";
            return null;
        }

        error = string.Empty;
        return new FullProbeOptions { OutputDirectory = Path.GetFullPath(output), Seconds = seconds, AccountsOnly = accountsOnly };
    }
}
