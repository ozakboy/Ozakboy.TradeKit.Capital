namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 帳號類別,由 <see cref="CapitalAccount.Market"/> 推定(手冊 4-2-4 的市場別:TF 國內期貨、TO 國內選擇權、OF 海外期貨、OO 海外選擇權;
/// 實測 2026-11-09 期貨戶送的是 TF 與 OF,TO / OO 沒看到)。
/// The account category inferred from the market string; a futures account sent TF and OF as measured.
/// </summary>
public enum CapitalAccountKind
{
    /// <summary>不是期貨帳號(證券 TS、複委託 OS)或認不得的市場別。Not a futures account, or an unknown market.</summary>
    Other = 0,

    /// <summary>國內期貨 / 選擇權(TF、TO)。Domestic futures or options.</summary>
    DomesticFutures = 1,

    /// <summary>海外期貨 / 選擇權(OF、OO)。Overseas futures or options.</summary>
    OverseasFutures = 2,
}
