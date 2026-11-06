using System.Globalization;

namespace Ozakboy.TradeKit.Capital.Host.Probe;

/// <summary>
/// 探針參數:<c>probe --out &lt;目錄&gt; [--seconds 120] [--kline-days 3] [--symbol 交易所,代碼]...</c>
/// Probe arguments.
/// </summary>
internal sealed class ProbeOptions
{
    /// <summary>輸出目錄。Output directory.</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>訂閱後收集即時報價的秒數。Seconds to collect live quotes after subscribing.</summary>
    public int Seconds { get; init; } = 120;

    /// <summary>歷史 K 往回查幾天。Days of history K-lines to request.</summary>
    public int KLineDays { get; init; } = 3;

    /// <summary>指定要訂的商品(空 = 從商品清單自動挑外匯)。Explicit symbols; empty picks FX from the product list.</summary>
    public IReadOnlyList<string> Symbols { get; init; } = [];

    /// <summary>
    /// 解析參數;不合法回 <see langword="null"/> 並給錯誤訊息。
    /// Parses the arguments; returns null with an error message when invalid.
    /// </summary>
    /// <param name="args">probe 之後的參數。Arguments after "probe".</param>
    /// <param name="error">錯誤訊息。Error message.</param>
    /// <returns>選項或 null。Options or null.</returns>
    public static ProbeOptions? Parse(IReadOnlyList<string> args, out string error)
    {
        string? output = null;
        int seconds = 120;
        int days = 3;
        List<string> symbols = [];

        for (int i = 0; i < args.Count; i++)
        {
            string name = args[i];
            string? value = i + 1 < args.Count ? args[i + 1] : null;

            if (value is null)
            {
                error = $"參數 {name} 缺少值。";
                return null;
            }

            switch (name)
            {
                case "--out":
                    output = value;
                    break;
                case "--seconds" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int s) && s is > 0 and <= 3600:
                    seconds = s;
                    break;
                case "--kline-days" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int d) && d is > 0 and <= 60:
                    days = d;
                    break;
                case "--symbol":
                    symbols.Add(value);
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
        return new ProbeOptions { OutputDirectory = Path.GetFullPath(output), Seconds = seconds, KLineDays = days, Symbols = symbols };
    }
}
