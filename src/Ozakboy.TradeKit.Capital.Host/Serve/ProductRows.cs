using System.Globalization;

namespace Ozakboy.TradeKit.Capital.Host.Serve;

/// <summary>
/// 合併後的一筆商品(協定 <c>products</c> 的一個 item)。日期維持群益的原始文字(<c>99991231</c>、<c>0</c> 照送,由用戶端解讀)。
/// One merged product, an item of the products result. Dates stay Capital's raw text; the client interprets them.
/// </summary>
internal sealed record ProductRow(
    string Exchange,
    string ExchangeName,
    string Code,
    string Name,
    string LastTradeDate,
    string FirstNoticeDate,
    int? Kind,
    string? OrderExchange,
    string? OrderCode);

/// <summary>
/// 解析並合併商品清單與商品明細。
/// Parses and merges the product list with the product details.
/// </summary>
/// <remarks>
/// 實測格式(SKCOM 2.13.59,2026-11-06):清單 <c>交易所,交易所名稱,代碼,名稱,最後交易日,第一通知日</c>;
/// 明細 <c>交易所,交易所名稱,代碼,名稱,下單交易所,下單代碼,最後交易日,第一通知日,商品種類</c>。
/// 名稱理論上可能含逗號,所以前面固定欄位從左取、後面固定欄位從右取,中間的才是名稱。
/// Measured formats: list <c>exchange,exchangeName,code,name,lastTradeDate,firstNoticeDate</c>; details
/// <c>exchange,exchangeName,code,name,orderExchange,orderCode,lastTradeDate,firstNoticeDate,kind</c>. A name could in
/// theory contain commas, so the fixed leading fields are taken from the left, the fixed trailing ones from the right,
/// and whatever is in between is the name.
/// </remarks>
internal static class ProductRows
{
    private const int ListLeading = 3;
    private const int ListTrailing = 2;
    private const int DetailLeading = 3;
    private const int DetailTrailing = 5;

    /// <summary>
    /// 依 (交易所, 代碼) 把明細併進清單;清單的順序保留,沒有明細的商品 kind 等欄位為 null。
    /// Merges the details into the list by (exchange, code), keeping the list's order; a product without details has
    /// null kind and order fields.
    /// </summary>
    /// <param name="listRows">清單原始行(不含 ## 那筆)。The raw list rows without the ## row.</param>
    /// <param name="detailRows">明細原始行(不含 ## 那筆)。The raw detail rows without the ## row.</param>
    /// <param name="skipped">欄位數不足而略過的筆數。Rows skipped for too few fields.</param>
    /// <returns>合併結果。The merged rows.</returns>
    public static IReadOnlyList<ProductRow> Merge(IReadOnlyList<string> listRows, IReadOnlyList<string> detailRows, out int skipped)
    {
        skipped = 0;
        Dictionary<string, (int? Kind, string OrderExchange, string OrderCode)> details = new(StringComparer.Ordinal);

        foreach (string row in detailRows)
        {
            string[] fields = Split(row);

            if (fields.Length < DetailLeading + DetailTrailing + 1)
            {
                skipped++;
                continue;
            }

            int tail = fields.Length - DetailTrailing;
            int? kind = int.TryParse(fields[tail + 4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
            details[Key(fields[0], fields[2])] = (kind, fields[tail], fields[tail + 1]);
        }

        List<ProductRow> merged = new(listRows.Count);

        foreach (string row in listRows)
        {
            string[] fields = Split(row);

            if (fields.Length < ListLeading + ListTrailing + 1)
            {
                skipped++;
                continue;
            }

            int tail = fields.Length - ListTrailing;
            string name = string.Join(',', fields[ListLeading..tail]);
            bool hasDetail = details.TryGetValue(Key(fields[0], fields[2]), out var detail);

            merged.Add(new ProductRow(
                fields[0],
                fields[1],
                fields[2],
                name,
                fields[tail],
                fields[tail + 1],
                hasDetail ? detail.Kind : null,
                hasDetail ? detail.OrderExchange : null,
                hasDetail ? detail.OrderCode : null));
        }

        return merged;
    }

    private static string[] Split(string row) => row.Split(',', StringSplitOptions.TrimEntries);

    private static string Key(string exchange, string code) => exchange + "\u0001" + code;
}
