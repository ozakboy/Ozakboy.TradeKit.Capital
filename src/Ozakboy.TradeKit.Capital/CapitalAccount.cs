namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 一個群益帳號(<c>GetUserAccount</c> → <c>OnAccount</c>,手冊 4-2-a:<c>市場,分公司代碼,分公司,帳號,身份證字號,姓名</c>;期貨的分公司代碼是
/// Broker id)。實測 2026-11-09(只有期貨戶):<c>TF,F020000,000,&lt;帳號&gt;,&lt;身分證&gt;,&lt;戶名&gt;,9</c> 與 <c>OF,…</c> 各一行,
/// 多一個手冊沒列的第 7 欄(只在 <see cref="RawFields"/>);分公司欄是 <c>000</c>;以 <see cref="FullAccount"/> 查權益數成功。
/// 第 2 欄不像代碼而第 3 欄像時兩欄互換(手冊曾更正過這兩欄的順序;實測不用互換)。解析不出的具名屬性為 null,
/// <see cref="RawFields"/> 是原文(含身分證字號與戶名,請自行保管;戶名的中文會亂碼,見 <see cref="CapitalSkcomText"/>)。
/// <see cref="ToString"/> 只露帳號末 3 碼。
/// One Capital account, measured on 2026-11-09 (TF and OF rows with an extra seventh field). Unreadable named
/// properties are null and RawFields holds the raw text, the holder's ID and (garbled) name included. ToString shows
/// only the last 3 characters of the account number.
/// </summary>
public sealed record CapitalAccount
{
    /// <summary>市場別原文(實測期貨戶是 TF 與 OF;TO / OO / TS / OS 依手冊推定)。The raw market string; TF and OF measured.</summary>
    public string? Market { get; init; }

    /// <summary>由 <see cref="Market"/> 推定的類別。The inferred category.</summary>
    public required CapitalAccountKind Kind { get; init; }

    /// <summary>分公司代碼 / Broker id(例如 <c>F020000</c>)。The branch code or broker id.</summary>
    public string? BrokerId { get; init; }

    /// <summary>分公司(第 3 欄原文;實測是 <c>000</c>,不是名稱)。The branch field, <c>000</c> as measured.</summary>
    public string? BrokerName { get; init; }

    /// <summary>帳號號碼。The account number.</summary>
    public string? AccountNo { get; init; }

    /// <summary>
    /// 查詢用的完整帳號 = <see cref="BrokerId"/> + <see cref="AccountNo"/>(手冊:委託帳號 IB + 帳號);任一個是 null 時為 null。
    /// 帳務查詢方法的 <c>account</c> 參數就傳這個。
    /// The full account used by the queries, broker id followed by the number; null if either is null. Pass it as the
    /// account argument of the query methods.
    /// </summary>
    public string? FullAccount { get; init; }

    /// <summary>原始欄位(以逗號切開、去掉前後空白;含身分證字號與戶名)。The raw fields, the holder's ID and name included.</summary>
    public required IReadOnlyList<string> RawFields { get; init; }

    /// <summary>
    /// 不含帳號全碼、身分證字號與戶名的描述。
    /// A description without the full account number, the holder's ID or name.
    /// </summary>
    /// <returns>描述。The description.</returns>
    public override string ToString() =>
        $"{nameof(CapitalAccount)} {{ Market = {Market}, Kind = {Kind}, BrokerId = {BrokerId}, Account = {CapitalAccountText.Mask(AccountNo)} }}";
}
