using System.Globalization;

namespace Ozakboy.TradeKit.Capital.Host.Probe;

/// <summary>
/// 探針輸出的遮罩器:登入帳號換成 <c>&lt;ID&gt;</c>(不分大小寫)、帳號號碼依登記順序換成 <c>&lt;ACCT1&gt;</c>、<c>&lt;ACCT2&gt;</c>…、
/// 戶名換成 <c>&lt;NAME&gt;</c>。對照表只在記憶體,不落檔;金額等其他內容不動(要看欄位意義)。
/// Redactor for probe output: the login ID becomes &lt;ID&gt; (case-insensitive), account numbers become &lt;ACCT1&gt;,
/// &lt;ACCT2&gt;… in registration order and the account holder's name becomes &lt;NAME&gt;. The mapping lives in memory
/// only; amounts and everything else stay untouched.
/// </summary>
internal sealed class ProbeRedactor
{
    private const string IdToken = "<ID>";
    private const string NameToken = "<NAME>";

    private readonly Lock _gate = new();
    private readonly List<(string Literal, string Replacement, StringComparison Comparison)> _rules = [];
    private readonly Dictionary<string, string> _accounts = new(StringComparer.Ordinal);

    /// <summary>已登記的帳號數。Number of registered accounts.</summary>
    public int AccountCount
    {
        get
        {
            lock (_gate)
            {
                return _accounts.Count;
            }
        }
    }

    /// <summary>
    /// 登記登入帳號(身分證字號)。
    /// Registers the login ID.
    /// </summary>
    /// <param name="loginId">登入帳號。The login ID.</param>
    public void AddLoginId(string? loginId) => Add(loginId, IdToken, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 登記戶名。
    /// Registers the account holder's name.
    /// </summary>
    /// <param name="name">戶名。The name.</param>
    public void AddName(string? name) => Add(name, NameToken, StringComparison.Ordinal);

    /// <summary>
    /// 登記帳號號碼,回傳它的代號;同一個號碼登記兩次拿到同一個代號。空字串回 <see langword="null"/>。
    /// Registers an account number and returns its token; the same number always gets the same token.
    /// </summary>
    /// <param name="account">帳號號碼(前後空白會去掉)。The account number.</param>
    /// <returns>代號,例如 <c>&lt;ACCT1&gt;</c>。The token, for example &lt;ACCT1&gt;.</returns>
    public string? AddAccount(string? account)
    {
        string trimmed = account?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return null;
        }

        lock (_gate)
        {
            if (_accounts.TryGetValue(trimmed, out string? existing))
            {
                return existing;
            }

            string token = string.Create(CultureInfo.InvariantCulture, $"<ACCT{_accounts.Count + 1}>");
            _accounts[trimmed] = token;
            AddRule(trimmed, token, StringComparison.Ordinal);
            return token;
        }
    }

    /// <summary>
    /// 把登記過的字面值換掉(長的先換,短值是長值一部分時不會只換一半)。
    /// Replaces every registered literal, longest first.
    /// </summary>
    /// <param name="text">原文。The text.</param>
    /// <returns>遮過的文字。The redacted text.</returns>
    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        lock (_gate)
        {
            string result = text;

            foreach ((string literal, string replacement, StringComparison comparison) in _rules)
            {
                result = result.Replace(literal, replacement, comparison);
            }

            return result;
        }
    }

    /// <summary>
    /// 只留「形狀」:數字換成 9、英文字母換成 A、其他非標點字元換成 字,標點與空白照留。給解析不了、不知道含什麼的原文用,
    /// 看得出欄位結構但看不到內容。
    /// Keeps only the shape: digits become 9, ASCII letters A, other non-punctuation characters 字. Used for raw text that
    /// could not be parsed, so its structure is visible but its content is not.
    /// </summary>
    /// <param name="text">原文。The text.</param>
    /// <returns>形狀。The shape.</returns>
    public static string Shape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return string.Create(text.Length, text, static (span, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                span[i] = char.IsAsciiDigit(c) ? '9'
                    : char.IsAsciiLetter(c) ? 'A'
                    : char.IsPunctuation(c) || char.IsSymbol(c) || char.IsWhiteSpace(c) ? c
                    : '字';
            }
        });
    }

    private void Add(string? literal, string replacement, StringComparison comparison)
    {
        string trimmed = literal?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            AddRule(trimmed, replacement, comparison);
        }
    }

    private void AddRule(string literal, string replacement, StringComparison comparison)
    {
        if (_rules.Exists(rule => string.Equals(rule.Literal, literal, StringComparison.Ordinal)))
        {
            return;
        }

        _rules.Add((literal, replacement, comparison));
        _rules.Sort((left, right) => right.Literal.Length.CompareTo(left.Literal.Length));
    }
}
