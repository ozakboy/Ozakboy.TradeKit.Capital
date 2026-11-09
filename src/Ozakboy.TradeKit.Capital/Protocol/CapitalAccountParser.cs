using System.Globalization;

namespace Ozakboy.TradeKit.Capital.Protocol;

/// <summary>
/// 期貨帳務查詢原文的解析(宿主原樣轉來的逗號字串)。欄位位置依手冊 V2.13.59(章節號在各方法);2026-11-09 以沒有部位、沒有委託的帳戶實測過
/// 帳號、權益數(國內 / 海外)、未平倉與回報的「查無資料」形式(各方法註明),有部位時的欄位位置與數值語意仍待實測。每個型別同時保留
/// <c>RawFields</c>,具名屬性解析不出就是 <see langword="null"/>(不填 0)。數值一律 <c>decimal.TryParse</c>(InvariantCulture),不經 double。
/// 群益在這些回覆裡的中文會亂碼(<see cref="CapitalSkcomText"/>),幣別與錯誤訊息經 <see cref="CapitalSkcomText.Repair"/>,<c>RawFields</c> 保留原文。
/// Parses the raw comma-separated account replies. Positions follow the manual; the empty-account shapes were measured
/// on 2026-11-09, while the fields of real positions are still to be measured. Each type keeps RawFields and
/// unreadable named properties are null, never zero. Numbers are parsed as decimal with the invariant culture. Capital
/// garbles the Chinese in these replies, so currencies and messages go through <see cref="CapitalSkcomText.Repair"/>.
/// </summary>
internal static class CapitalAccountParser
{
    /// <summary>未平倉「查無資料」的代碼(手冊 4-2-d;海外 4-2-q 實測同一個)。The open-interest "no data" code.</summary>
    private const string NoDataCode = "001";

    private const NumberStyles DecimalStyle =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

    /// <summary>
    /// OnAccount(手冊 4-2-a):<c>市場,分公司代碼,分公司,帳號,身份證字號,姓名</c>。不足 4 欄或「##」開頭的略過。第 2 欄不像代碼而第 3 欄像時
    /// 兩欄互換(手冊曾更正過這兩欄的順序)。實測 2026-11-09:<c>TF,F020000,000,&lt;帳號&gt;,&lt;身分證&gt;,&lt;戶名&gt;,9</c> 與 <c>OF,…</c> 兩行
    /// (多一個手冊沒列的第 7 欄 <c>9</c>,只在 RawFields;分公司欄是 <c>000</c>,代碼在第 2 欄不用互換),沒有結尾標記。
    /// Parses OnAccount rows; the broker fields are swapped when the second one does not look like a code. Measured rows
    /// carry an extra seventh field that is kept in RawFields only.
    /// </summary>
    /// <param name="rows">原文。The raw rows.</param>
    /// <returns>帳號。The accounts.</returns>
    public static IReadOnlyList<CapitalAccount> ParseAccounts(IEnumerable<string> rows)
    {
        List<CapitalAccount> accounts = [];

        foreach (string row in rows)
        {
            string text = row.Trim().TrimEnd(';');

            if (text.Length == 0 || text.StartsWith("##", StringComparison.Ordinal))
            {
                continue;
            }

            string[] fields = Fields(text);

            if (fields.Length < 4)
            {
                continue;
            }

            bool swapped = !LooksLikeCode(fields[1]) && LooksLikeCode(fields[2]);
            string? broker = Text(fields, swapped ? 2 : 1);
            string? account = Text(fields, 3);

            accounts.Add(new CapitalAccount
            {
                Market = Text(fields, 0),
                Kind = KindOf(fields[0]),
                BrokerId = broker,
                BrokerName = Text(fields, swapped ? 1 : 2),
                AccountNo = account,
                FullAccount = broker is not null && account is not null ? broker + account : null,
                RawFields = fields,
            });
        }

        return accounts;
    }

    /// <summary>
    /// 國內權益數(手冊 4-2-i,41 欄,0 起算;實測 41 欄、全幣別第一筆是基幣,幣別欄經 <see cref="CapitalSkcomText.Repair"/>)。
    /// Domestic rights rows, 41 fields; the currency goes through <see cref="CapitalSkcomText.Repair"/>.
    /// </summary>
    /// <param name="rows">原文(不含「##」)。The raw rows.</param>
    /// <returns>權益數。The rights.</returns>
    public static IReadOnlyList<CapitalFutureRights> ParseFutureRights(IEnumerable<string> rows) =>
        [.. DataRows(rows).Select(fields => new CapitalFutureRights
        {
            AccountBalance = Decimal(fields, 0),
            FloatingProfitLoss = Decimal(fields, 1),
            RealizedFees = Decimal(fields, 2),
            TransactionTax = Decimal(fields, 3),
            ReservedPremium = Decimal(fields, 4),
            PremiumReceivedPaid = Decimal(fields, 5),
            Equity = Decimal(fields, 6),
            ExcessMargin = Decimal(fields, 7),
            DepositWithdrawal = Decimal(fields, 8),
            LongMarketValue = Decimal(fields, 9),
            ShortMarketValue = Decimal(fields, 10),
            FuturesRealizedProfitLoss = Decimal(fields, 11),
            UnrealizedProfitLoss = Decimal(fields, 12),
            InitialMargin = Decimal(fields, 13),
            MaintenanceMargin = Decimal(fields, 14),
            PositionInitialMargin = Decimal(fields, 15),
            PositionMaintenanceMargin = Decimal(fields, 16),
            OrderMargin = Decimal(fields, 17),
            ExcessOptimalMargin = Decimal(fields, 18),
            TotalOptionValue = Decimal(fields, 19),
            ReservedFees = Decimal(fields, 20),
            PreviousBalance = Decimal(fields, 22),
            OptionCombinationMarginFlag = Text(fields, 23),
            MaintenanceRatio = Decimal(fields, 24),
            Currency = CapitalSkcomText.Repair(Text(fields, 25)),
            FullInitialMargin = Decimal(fields, 26),
            FullMaintenanceMargin = Decimal(fields, 27),
            FullAvailable = Decimal(fields, 28),
            CollateralAmount = Decimal(fields, 29),
            SecuritiesAvailable = Decimal(fields, 30),
            AvailableBalance = Decimal(fields, 31),
            FullCashAvailable = Decimal(fields, 32),
            SecuritiesValue = Decimal(fields, 33),
            RiskIndicator = Decimal(fields, 34),
            OptionExpiryDifference = Decimal(fields, 35),
            OptionExpiryLoss = Decimal(fields, 36),
            FuturesExpiryProfitLoss = Decimal(fields, 37),
            AdditionalMargin = Decimal(fields, 38),
            AccountNo = Text(fields, 40),
            RawFields = fields,
        })];

    /// <summary>
    /// 國內未平倉(手冊 4-2-d GW 格式 1,11 欄);「001,查無資料,帳號」略過(實測 2026-11-09 沒有部位時就是這一行加 <c>##</c>,中文亂碼)。
    /// 其他格式只有 RawFields。有部位時的欄位待實測。
    /// Domestic open interest; the "no data" row (measured) is skipped and other formats only carry RawFields.
    /// </summary>
    /// <param name="rows">原文。The raw rows.</param>
    /// <param name="format">請求的格式。The requested format.</param>
    /// <returns>未平倉。The open interest.</returns>
    public static IReadOnlyList<CapitalOpenInterest> ParseOpenInterest(IEnumerable<string> rows, int format) =>
        [.. DataRows(rows)
            .Where(fields => fields[0] != NoDataCode)
            .Select(fields => format == 1
                ? new CapitalOpenInterest
                {
                    Format = format,
                    Market = Text(fields, 0),
                    AccountNo = Text(fields, 1),
                    Product = Text(fields, 2),
                    Side = Text(fields, 3),
                    Quantity = Long(fields, 4),
                    DayTradeQuantity = Long(fields, 5),
                    AverageCost = Decimal(fields, 6),
                    FeePerContract = Decimal(fields, 7),
                    TaxRate = Decimal(fields, 8),
                    OrderCode = Text(fields, 10),
                    RawFields = fields,
                }
                : new CapitalOpenInterest { Format = format, RawFields = fields })];

    /// <summary>
    /// 海外權益數(手冊 4-2-k):第一筆成功「000,筆數」(實測 2026-11-09 是 <c>000,1,查詢成功</c>,多一個訊息欄)、失敗「代碼;訊息」(→ 失敗回傳),
    /// 之後每筆 31 欄(實測)。第一筆兩種都不是時,當成資料照解析。
    /// Overseas rights: the first row is the query result ("000,count,message" as measured, or "code;message", a
    /// failure); then 31 fields per row as measured.
    /// </summary>
    /// <param name="rows">原文。The raw rows.</param>
    /// <param name="mask">錯誤訊息的遮罩。Masks the failure message.</param>
    /// <returns>權益數或失敗。The rights or a failure.</returns>
    public static Result<IReadOnlyList<CapitalOverseaFutureRights>> ParseOverseaFutureRights(IReadOnlyList<string> rows, Func<string, string> mask)
    {
        IEnumerable<string> data = rows;

        if (rows.Count > 0)
        {
            string first = rows[0].Trim();

            if (first.StartsWith("000", StringComparison.Ordinal))
            {
                data = rows.Skip(1);
            }
            else if (first.Contains(';', StringComparison.Ordinal))
            {
                int separator = first.IndexOf(';', StringComparison.Ordinal);
                return CapitalErrors.AccountQueryFailed(first[..separator].Trim(), mask(CapitalSkcomText.Repair(first[(separator + 1)..].Trim())));
            }
        }

        return Result.Success<IReadOnlyList<CapitalOverseaFutureRights>>([.. DataRows(data).Select(fields => new CapitalOverseaFutureRights
        {
            BrokerId = Text(fields, 0),
            IbCode = Text(fields, 1),
            AccountNo = Text(fields, 2),
            Currency = CapitalSkcomText.Repair(Text(fields, 3)),
            RiskIndicator = Decimal(fields, 4),
            FullMaintenanceRatio = Decimal(fields, 5),
            AvailableMargin = Decimal(fields, 6),
            OrderMargin = Decimal(fields, 7),
            MaintenanceMargin = Decimal(fields, 8),
            InitialMargin = Decimal(fields, 9),
            NetEquity = Decimal(fields, 10),
            ReservedPremium = Decimal(fields, 11),
            LongOptionValue = Decimal(fields, 12),
            ShortOptionValue = Decimal(fields, 13),
            Equity = Decimal(fields, 14),
            UnrealizedProfitLoss = Decimal(fields, 15),
            FloatingProfitLoss = Decimal(fields, 16),
            PremiumToday = Decimal(fields, 17),
            RealizedProfitLossToday = Decimal(fields, 18),
            Fees = Decimal(fields, 19),
            DepositWithdrawalToday = Decimal(fields, 20),
            PreviousBalance = Decimal(fields, 21),
            ExchangeRate = Decimal(fields, 22),
            OptionExpiryGain = Decimal(fields, 23),
            OptionExpiryLoss = Decimal(fields, 24),
            FuturesDeliveryProfitLoss = Decimal(fields, 25),
            ExcessMargin = Decimal(fields, 26),
            ExerciseProfitLoss = Decimal(fields, 27),
            AccountBalance = Decimal(fields, 28),
            FixedMargin = Decimal(fields, 29),
            VariableMargin = Decimal(fields, 30),
            RawFields = fields,
        })]);
    }

    /// <summary>
    /// 海外未平倉(手冊 4-2-q):第一筆「@@,代碼,訊息」,代碼 0 是成功;實測 2026-11-09 沒有部位時是 <c>@@,001,查無資料,帳號</c> 加 <c>##</c>
    /// (手冊說非 0 都是錯誤,但 001 與國內未平倉的「001,查無資料」同義)→ 空清單;其他非 0 代碼 → 失敗。彙總 21 欄 / 明細 17 欄,
    /// 選擇權另有履約價(與 C/P),有部位時的欄位待實測。
    /// Overseas open interest: an "@@,code,message" first row. Code 0 succeeds; 001 means no data as measured (an empty
    /// list); any other code fails. Then summary or detail rows, still to be measured.
    /// </summary>
    /// <param name="rows">原文。The raw rows.</param>
    /// <param name="format">請求的格式。The requested format.</param>
    /// <param name="mask">錯誤訊息的遮罩。Masks the failure message.</param>
    /// <returns>未平倉或失敗。The open interest or a failure.</returns>
    public static Result<IReadOnlyList<CapitalOverseaOpenInterest>> ParseOverseaOpenInterest(
        IReadOnlyList<string> rows,
        CapitalOverseaOpenInterestFormat format,
        Func<string, string> mask)
    {
        List<CapitalOverseaOpenInterest> items = [];

        foreach (string[] fields in DataRows(rows))
        {
            if (fields[0] == "@@")
            {
                string code = Text(fields, 1) ?? string.Empty;

                if (code is not ("0" or "" or NoDataCode))
                {
                    return CapitalErrors.AccountQueryFailed(code, mask(CapitalSkcomText.Repair(string.Join(',', fields.Skip(2)))));
                }

                continue;
            }

            if (fields[0] == NoDataCode)
            {
                continue;
            }

            items.Add(format == CapitalOverseaOpenInterestFormat.Summary ? Summary(fields) : Detail(fields));
        }

        return items;

        static CapitalOverseaOpenInterest Summary(string[] fields)
        {
            bool? option = Flag(fields, 20);
            return new CapitalOverseaOpenInterest
            {
                Format = CapitalOverseaOpenInterestFormat.Summary,
                AccountNo = Text(fields, 0),
                ExchangeCode = Text(fields, 1),
                ExchangeName = Text(fields, 2),
                ProductWithMonth = Text(fields, 3),
                Product = Text(fields, 4),
                YearMonth = Text(fields, 5),
                Name = Text(fields, 6),
                Side = Text(fields, 7),
                Quantity = Long(fields, 8),
                AveragePrice = Decimal(fields, 9),
                MarketPrice = Decimal(fields, 10),
                UnrealizedProfitLoss = Decimal(fields, 11),
                PreviousSettlementText = Text(fields, 17),
                PreviousSettlement = Decimal(fields, 17),
                DayTradeQuantity = Long(fields, 18),
                InitialMargin = Decimal(fields, 19),
                IsOption = option,
                StrikePrice = option == true ? Decimal(fields, 21) : null,
                CallPut = option == true ? Text(fields, 22) : null,
                RawFields = fields,
            };
        }

        static CapitalOverseaOpenInterest Detail(string[] fields)
        {
            bool? option = Flag(fields, 16);
            return new CapitalOverseaOpenInterest
            {
                Format = CapitalOverseaOpenInterestFormat.Detail,
                AccountNo = Text(fields, 0),
                TradeDate = Date(fields, 1),
                ExchangeCode = Text(fields, 2),
                ExchangeName = Text(fields, 3),
                Product = Text(fields, 4),
                YearMonth = Text(fields, 5),
                Name = Text(fields, 6),
                Side = Text(fields, 7),
                FilledQuantity = Long(fields, 8),
                Quantity = Long(fields, 9),
                TradePrice = Decimal(fields, 10),
                MarketPrice = Decimal(fields, 11),
                UnrealizedProfitLoss = Decimal(fields, 12),
                BookNo = Text(fields, 13),
                DayTradeFlag = Text(fields, 14),
                InitialMargin = Decimal(fields, 15),
                IsOption = option,
                StrikePrice = option == true ? Decimal(fields, 17) : null,
                RawFields = fields,
            };
        }
    }

    /// <summary>
    /// 委託回報(手冊 4-2-97、5-4-4):「M003」開頭(查無資料)回空清單,「M999」開頭(查詢錯誤)→ 失敗;格式 1–6、9 依 70 欄解析,7、8 只有 RawFields。
    /// 實測 2026-11-09(當天沒有委託):格式 1–9 都只回 4 個字元 <c>M003</c>(手冊 4-2-97「M003: 查無資料」);有委託時的 70 欄待實測。
    /// Order reports: M003 means no data (all nine formats returned exactly "M003" for an account without orders), M999 a
    /// failure; formats 1-6 and 9 are parsed, 7 and 8 only carry RawFields.
    /// </summary>
    /// <param name="rows">原文。The raw rows.</param>
    /// <param name="format">請求的種類。The requested kind.</param>
    /// <param name="mask">錯誤訊息的遮罩。Masks the failure message.</param>
    /// <returns>回報或失敗。The reports or a failure.</returns>
    public static Result<IReadOnlyList<CapitalOrderReport>> ParseOrderReports(IReadOnlyList<string> rows, CapitalOrderReportFormat format, Func<string, string> mask)
    {
        if (ReportFailure(rows, mask) is { } error)
        {
            return error;
        }

        bool named = format is not (CapitalOrderReportFormat.MergedByPrice or CapitalOrderReportFormat.MergedByProduct);

        return Result.Success<IReadOnlyList<CapitalOrderReport>>([.. ReportRows(rows).Select(fields => named
            ? new CapitalOrderReport
            {
                Format = format,
                Market = Number(fields, 1),
                ProductKind = Number(fields, 2),
                Exchange = Number(fields, 3),
                BrokerId = Number(fields, 4),
                AccountNo = Number(fields, 6),
                BookNo = Number(fields, 8),
                SequenceNo = Number(fields, 9),
                Status = Number(fields, 11),
                OrderDate = Date(fields, 11),
                OrderTime = Time(fields, 12),
                TradeDate = Date(fields, 13),
                Symbol = Number(fields, 16),
                Side = Number(fields, 23),
                TimeInForce = Number(fields, 26),
                PriceType = Number(fields, 27),
                Price = Decimal(fields, 27),
                ValidQuantity = Long(fields, 29),
                OriginalQuantity = Long(fields, 30),
                FilledQuantity = Long(fields, 31),
                RemainingQuantity = Long(fields, 32),
                DayTradeFlag = Number(fields, 34),
                SessionFlag = Number(fields, 35),
                IsErrorReport = YesNo(fields, 35),
                CallPut = Number(fields, 41),
                ExchangeMessage = Number(fields, 50),
                RawFields = fields,
            }
            : new CapitalOrderReport { Format = format, RawFields = fields })]);
    }

    /// <summary>
    /// 成交回報(手冊 4-2-98、5-4-5):M003 / M999 同委託回報(實測格式 1–5 沒有成交時都回 <c>M003</c>);格式 1、5 依 52 欄解析,2–4 只有 RawFields。
    /// Fill reports: as order reports (all five formats returned "M003" without fills); formats 1 and 5 are parsed, 2-4
    /// only carry RawFields.
    /// </summary>
    /// <param name="rows">原文。The raw rows.</param>
    /// <param name="format">請求的種類。The requested kind.</param>
    /// <param name="mask">錯誤訊息的遮罩。Masks the failure message.</param>
    /// <returns>回報或失敗。The reports or a failure.</returns>
    public static Result<IReadOnlyList<CapitalFulfillReport>> ParseFulfillReports(IReadOnlyList<string> rows, CapitalFulfillReportFormat format, Func<string, string> mask)
    {
        if (ReportFailure(rows, mask) is { } error)
        {
            return error;
        }

        bool named = format is CapitalFulfillReportFormat.Full or CapitalFulfillReportFormat.NextSession;

        return Result.Success<IReadOnlyList<CapitalFulfillReport>>([.. ReportRows(rows).Select(fields => named
            ? new CapitalFulfillReport
            {
                Format = format,
                Market = Number(fields, 1),
                ProductKind = Number(fields, 2),
                Exchange = Number(fields, 3),
                BrokerId = Number(fields, 4),
                AccountNo = Number(fields, 6),
                BookNo = Number(fields, 8),
                FillSequenceNo = Number(fields, 9),
                FillDate = Date(fields, 9),
                FillTime = Time(fields, 10),
                TradeDate = Date(fields, 11),
                Symbol = Number(fields, 13),
                Name = Number(fields, 14),
                Side = Number(fields, 23),
                Price = Decimal(fields, 26),
                Quantity = Long(fields, 27),
                DayTradeFlag = Number(fields, 29),
                SessionFlag = Number(fields, 30),
                OrderDate = Date(fields, 37),
                OrderTime = Time(fields, 38),
                RawFields = fields,
            }
            : new CapitalFulfillReport { Format = format, RawFields = fields })]);
    }

    /// <summary>以逗號切開並去掉前後空白。Splits on commas and trims.</summary>
    /// <param name="row">原文。The raw row.</param>
    /// <returns>欄位。The fields.</returns>
    public static string[] Fields(string row) => row.Split(',', StringSplitOptions.TrimEntries);

    /// <summary>第 <paramref name="index"/> 欄(0 起算)的文字;超出範圍或空字串為 null。The text of a field, null when missing or empty.</summary>
    public static string? Text(string[] fields, int index) =>
        index < fields.Length && fields[index].Length > 0 ? fields[index] : null;

    /// <summary>第 <paramref name="index"/> 欄的 decimal;讀不懂為 null(不填 0)。A decimal field; null when unreadable.</summary>
    public static decimal? Decimal(string[] fields, int index) =>
        Text(fields, index) is { } text && decimal.TryParse(text, DecimalStyle, CultureInfo.InvariantCulture, out decimal value) ? value : null;

    /// <summary>第 <paramref name="index"/> 欄的整數;讀不懂為 null。An integer field; null when unreadable.</summary>
    public static long? Long(string[] fields, int index) =>
        Text(fields, index) is { } text && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value) ? value : null;

    /// <summary>第 <paramref name="index"/> 欄的 yyyyMMdd 日期;<c>0</c> 或讀不懂為 null。A yyyyMMdd field; null for 0 or unreadable.</summary>
    public static DateOnly? Date(string[] fields, int index) =>
        Text(fields, index) is { } text && DateOnly.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date) ? date : null;

    /// <summary>
    /// 第 <paramref name="index"/> 欄的時間:<c>hhmmss</c>(前導 0 可省)或 <c>hhmmssfff</c>;讀不懂為 null。A hhmmss or hhmmssfff field.
    /// </summary>
    public static TimeOnly? Time(string[] fields, int index)
    {
        if (Text(fields, index) is not { } text || !text.All(char.IsAsciiDigit))
        {
            return null;
        }

        string padded = text.Length <= 6 ? text.PadLeft(6, '0') : text.PadLeft(9, '0');
        string pattern = padded.Length == 6 ? "HHmmss" : "HHmmssfff";
        return TimeOnly.TryParseExact(padded, pattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time) ? time : null;
    }

    private static string? Number(string[] fields, int manualNumber) => Text(fields, manualNumber - 1);

    private static bool? Flag(string[] fields, int index) => Text(fields, index) switch
    {
        "0" => false,
        "1" => true,
        _ => null,
    };

    private static bool? YesNo(string[] fields, int index) => Text(fields, index) switch
    {
        "Y" => true,
        "N" => false,
        _ => null,
    };

    private static IEnumerable<string[]> DataRows(IEnumerable<string> rows)
    {
        foreach (string row in rows)
        {
            string text = row.Trim();

            if (text.Length == 0 || text.StartsWith("##", StringComparison.Ordinal))
            {
                continue;
            }

            yield return Fields(text);
        }
    }

    private static IEnumerable<string[]> ReportRows(IEnumerable<string> rows) =>
        DataRows(rows).Where(fields => !fields[0].StartsWith("M003", StringComparison.Ordinal));

    private static Error? ReportFailure(IReadOnlyList<string> rows, Func<string, string> mask)
    {
        string? failure = rows.Select(row => row.Trim()).FirstOrDefault(row => row.StartsWith("M999", StringComparison.Ordinal));
        return failure is null ? null : CapitalErrors.AccountQueryFailed("M999", mask(CapitalSkcomText.Repair(failure)));
    }

    private static CapitalAccountKind KindOf(string market) => market.Trim().ToUpperInvariant() switch
    {
        "TF" or "TO" => CapitalAccountKind.DomesticFutures,
        "OF" or "OO" => CapitalAccountKind.OverseasFutures,
        _ => CapitalAccountKind.Other,
    };

    private static bool LooksLikeCode(string value) => value.Length > 0 && value.All(char.IsAsciiLetterOrDigit);
}
