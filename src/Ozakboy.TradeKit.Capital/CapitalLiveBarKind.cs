namespace Ozakboy.TradeKit.Capital;

/// <summary>
/// 國內即時分 K 的種類(<c>OnNotifyLiveKLineData</c> 的 nType,手冊 4-4-u;值即群益的數字)。
/// The kind of a domestic live bar notification (nType; the values are Capital's).
/// </summary>
public enum CapitalLiveBarKind
{
    /// <summary>
    /// 資料重置:捨棄已收到的分 K(清盤通知;這一則的價格都是 0,本套件給 null;實測期間沒有出現)。
    /// Reset: discard the bars received so far; not seen in the measurement.
    /// </summary>
    Reset = 0,

    /// <summary>
    /// 已完成的分 K(訂閱時先回補當日分 K,之後每分鐘結束一則;實測)。A finished minute bar; the day's bars are replayed first (measured).
    /// </summary>
    Minute = 1,

    /// <summary>
    /// 還在形成的那一分鐘,每筆成交後更新一次(開高低收量是這一分鐘到目前的累計;實測)。
    /// The forming minute, updated on every tick with its running values (measured).
    /// </summary>
    Update = 2,
}
