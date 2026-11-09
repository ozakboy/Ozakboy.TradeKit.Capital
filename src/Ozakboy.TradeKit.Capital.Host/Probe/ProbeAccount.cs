namespace Ozakboy.TradeKit.Capital.Host.Probe;

/// <summary>探針推定的帳號類別。The account category the probe infers.</summary>
internal enum ProbeAccountKind
{
    /// <summary>不是期貨帳號(證券 TS、複委託 OS)或認不得的市場別。Not a futures account, or an unknown market.</summary>
    Other,

    /// <summary>國內期貨 / 選擇權(TF、TO)。Domestic futures or options.</summary>
    DomesticFutures,

    /// <summary>海外期貨 / 選擇權(OF、OO)。Overseas futures or options.</summary>
    OverseasFutures,
}

/// <summary>
/// <c>OnAccount</c> 的一筆帳號。手冊 4-2-a:<c>市場,分公司代碼,分公司,帳號,身份證字號,姓名</c>(期貨的分公司代碼是 Broker id);
/// 市場別的字樣手冊沒列,依 4-2-4 SetMaxQty 的 nMarketType 說明(TS 證券、TF 期貨、TO 選擇權、OS 複委託、OF 海外期貨、OO 海外選擇權)推定,待實測。
/// One account from OnAccount. The market strings are inferred from the SetMaxQty documentation and still need measuring.
/// </summary>
/// <param name="Market">市場別原文。The raw market.</param>
/// <param name="BrokerId">分公司代碼 / Broker id。</param>
/// <param name="BrokerName">分公司名稱。The branch name.</param>
/// <param name="AccountNo">帳號號碼。The account number.</param>
/// <param name="OwnerId">身分證字號(可能是空的)。The holder's ID, possibly empty.</param>
/// <param name="OwnerName">戶名(可能是空的)。The holder's name, possibly empty.</param>
/// <param name="BrokerFieldSwapped">
/// 第 2 欄不像代碼、改用第 3 欄當分公司代碼(手冊曾更正過這兩欄的順序)。The second field did not look like a code, so the
/// third one was used (the manual once corrected the order of these two).
/// </param>
internal sealed record ProbeAccount(
    string Market,
    string BrokerId,
    string BrokerName,
    string AccountNo,
    string OwnerId,
    string OwnerName,
    bool BrokerFieldSwapped)
{
    /// <summary>查詢用的完整帳號(手冊:IB + 帳號)。The full account used by queries: broker id followed by the number.</summary>
    public string FullAccount => BrokerId + AccountNo;

    /// <summary>推定的類別。The inferred category.</summary>
    public ProbeAccountKind Kind => Market.Trim().ToUpperInvariant() switch
    {
        "TF" or "TO" => ProbeAccountKind.DomesticFutures,
        "OF" or "OO" => ProbeAccountKind.OverseasFutures,
        _ => ProbeAccountKind.Other,
    };

    /// <summary>
    /// 解析一則 <c>OnAccount</c> 的 bstrAccountData;欄位不足四欄或是 <c>##</c> 結尾標記回 <see langword="null"/>。
    /// Parses one OnAccount payload; returns null for fewer than four fields or a ## end marker.
    /// </summary>
    /// <param name="data">原文。The raw payload.</param>
    /// <returns>帳號或 null。The account or null.</returns>
    public static ProbeAccount? Parse(string? data)
    {
        string text = (data ?? string.Empty).Trim().TrimEnd(';');

        if (text.Length == 0 || text.StartsWith("##", StringComparison.Ordinal))
        {
            return null;
        }

        string[] fields = text.Split(',');

        if (fields.Length < 4)
        {
            return null;
        }

        string second = fields[1].Trim();
        string third = fields[2].Trim();
        bool swapped = !LooksLikeCode(second) && LooksLikeCode(third);

        return new ProbeAccount(
            fields[0].Trim(),
            swapped ? third : second,
            swapped ? second : third,
            fields[3].Trim(),
            fields.Length > 4 ? fields[4].Trim() : string.Empty,
            fields.Length > 5 ? fields[5].Trim() : string.Empty,
            swapped);
    }

    /// <summary>
    /// 寫出 <c>OnAccount</c> 原文前的保險:解析不了的只留形狀(<see cref="ProbeRedactor.Shape"/>),因為裡面的帳號沒登記到遮罩;
    /// 解析得了但比手冊多出的欄位(第 7 欄以後,不知道是什麼)也只留形狀。前 6 欄照原文,交給遮罩器換掉帳號、身分證字號、戶名。
    /// Safety net before writing an OnAccount payload: an unparsable one keeps only its shape because its account was never
    /// registered; extra fields beyond the documented six keep only their shape too.
    /// </summary>
    /// <param name="data">原文。The raw payload.</param>
    /// <returns>可以交給遮罩器寫出的文字。Text that is safe to hand to the redactor.</returns>
    public static string ToSafeRaw(string? data)
    {
        string text = data ?? string.Empty;
        string trimmed = text.Trim();

        if (trimmed.Length == 0 || trimmed.StartsWith("##", StringComparison.Ordinal))
        {
            return text;
        }

        if (Parse(text) is null)
        {
            return "UNPARSED " + ProbeRedactor.Shape(text);
        }

        string[] fields = text.Split(',');

        return fields.Length <= 6
            ? text
            : string.Join(',', fields.Take(6)) + "," + string.Join(',', fields.Skip(6).Select(ProbeRedactor.Shape));
    }

    private static bool LooksLikeCode(string value) => value.Length > 0 && value.All(char.IsAsciiLetterOrDigit);
}
