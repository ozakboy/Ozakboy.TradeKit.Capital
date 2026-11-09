using System.Diagnostics.CodeAnalysis;

namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 群益海外商品:交易所 + 代碼(例如 <c>FX</c> + <c>SUSDTWD</c>、<c>CME</c> + <c>NQ0000</c>)。字串形式是群益訂閱用的
/// <c>交易所,代碼</c>;大小寫照群益原樣(<c>Eurex</c>、<c>Cboe</c> 是混合大小寫),比較區分大小寫。
/// A Capital overseas symbol: exchange plus code. Its string form is Capital's subscription form <c>exchange,code</c>;
/// case is kept as Capital sends it and comparisons are case-sensitive.
/// </summary>
/// <param name="Exchange">交易所代碼(<c>FX</c>、<c>CME</c>、<c>CBOT</c>、<c>NYM</c>…)。The exchange code.</param>
/// <param name="Code">商品代碼(期貨熱月 = 根代碼 + <c>0000</c>)。The product code; a front-month future is root + 0000.</param>
public readonly record struct CapitalSymbol(string Exchange, string Code)
{
    /// <summary>
    /// 0.3.0:國內期貨 / 選擇權事件(<see cref="CapitalDomesticQuote"/> 等)的 <see cref="CapitalMarketEvent.Symbol"/> 用的交易所代碼
    /// (期交所;群益回報的「交易所別」也是這個字)。只用來標示,<b>不能</b>拿去 <see cref="CapitalHostSession.SubscribeAsync"/> 訂海外報價。
    /// The exchange code on the symbol of domestic futures and options events (TAIFEX). It only labels them; it does
    /// <b>not</b> work with SubscribeAsync, which is for overseas quotes.
    /// </summary>
    public const string DomesticExchange = "TAIFEX";

    /// <summary>
    /// 解析 <c>交易所,代碼</c>。
    /// Parses <c>exchange,code</c>.
    /// </summary>
    /// <param name="text">例如 <c>FX,SUSDTWD</c>。For example <c>FX,SUSDTWD</c>.</param>
    /// <returns>商品。The symbol.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> 為 null。<paramref name="text"/> is null.</exception>
    /// <exception cref="FormatException">格式不對。The format is wrong.</exception>
    public static CapitalSymbol Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!TryParse(text, out CapitalSymbol symbol))
        {
            throw new FormatException($"商品代碼必須是「交易所,代碼」:{text}。A symbol must be \"exchange,code\": {text}.");
        }

        return symbol;
    }

    /// <summary>
    /// 嘗試解析 <c>交易所,代碼</c>:恰好一個逗號、兩邊非空、不含 <c>#</c>、空白或控制字元。
    /// Tries to parse <c>exchange,code</c>: exactly one comma, both sides non-empty, no '#', whitespace or control
    /// characters.
    /// </summary>
    /// <param name="text">文字。The text.</param>
    /// <param name="symbol">結果。The result.</param>
    /// <returns>格式正確時為 <see langword="true"/>。True when well-formed.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, out CapitalSymbol symbol)
    {
        symbol = default;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        int comma = text.IndexOf(',', StringComparison.Ordinal);

        if (comma <= 0 || comma == text.Length - 1 || text.IndexOf(',', comma + 1) >= 0)
        {
            return false;
        }

        string exchange = text[..comma];
        string code = text[(comma + 1)..];

        if (!IsValidPart(exchange) || !IsValidPart(code))
        {
            return false;
        }

        symbol = new CapitalSymbol(exchange, code);
        return true;
    }

    /// <summary>
    /// 是否是合法的商品(兩部分都非空且不含分隔字元)。<c>default</c> 不合法。
    /// Whether this is a valid symbol (both parts non-empty without separators); <c>default</c> is not.
    /// </summary>
    public bool IsValid => IsValidPart(Exchange) && IsValidPart(Code);

    /// <summary>
    /// 群益請求用的字串 <c>交易所,代碼</c>。
    /// The request string Capital expects, <c>exchange,code</c>.
    /// </summary>
    /// <returns>請求字串。The request string.</returns>
    public string ToRequestString() => Exchange + "," + Code;

    /// <summary>
    /// 等同 <see cref="ToRequestString"/>,例如 <c>FX,SUSDTWD</c>。
    /// Same as <see cref="ToRequestString"/>, for example <c>FX,SUSDTWD</c>.
    /// </summary>
    /// <returns>字串形式。The string form.</returns>
    public override string ToString() => ToRequestString();

    private static bool IsValidPart(string? part)
    {
        if (string.IsNullOrEmpty(part))
        {
            return false;
        }

        foreach (char c in part)
        {
            if (c is ',' or '#' || char.IsWhiteSpace(c) || char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }
}
