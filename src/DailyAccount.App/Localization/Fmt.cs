using System.Globalization;
using DailyAccount.Core;

namespace DailyAccount.App.Localization;

/// <summary>Display formatting. In Bangla mode digits become ০–৯; input accepts both.</summary>
public static class Fmt
{
    private const string BnDigits = "০১২৩৪৫৬৭৮৯";

    public static string Digits(string s)
    {
        if (!Loc.IsBangla) return s;
        return string.Create(s.Length, s, (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
                span[i] = src[i] is >= '0' and <= '9' ? BnDigits[src[i] - '0'] : src[i];
        });
    }

    public static string Money(long poisha) => Digits(Core.Money.Format(poisha));

    public static string Number(int n) => Digits(n.ToString(CultureInfo.InvariantCulture));

    public static string Date(DateTime d) => Digits(d.ToString("d MMM yyyy", Loc.Culture));

    public static string DayMonth(DateTime d) => Digits(d.ToString("d MMM", Loc.Culture));

    public static string Month(string monthKey) => Digits(MonthKey.FirstDay(monthKey).ToString("MMMM yyyy", Loc.Culture));

    public static string MonthName(string monthKey) => MonthKey.FirstDay(monthKey).ToString("MMMM", Loc.Culture);

    public static string ShortMonth(string monthKey) => MonthKey.FirstDay(monthKey).ToString("MMM", Loc.Culture);

    /// <summary>Text for an amount Entry (no symbol, no grouping): "33334" or "850.5".</summary>
    public static string EditableAmount(long poisha) =>
        Core.Money.ToTaka(poisha).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Accepts "1,00,000", "৳ 850.50", "১০০০". Returns null when empty or invalid.</summary>
    public static long? ParseMoney(string? text)
    {
        var normalized = Normalize(text);
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var taka)
            ? Core.Money.FromTaka(taka)
            : null;
    }

    /// <summary>A plain number such as a quantity ("1.5", "২"); null when empty or invalid.</summary>
    public static decimal? ParseNumber(string? text) =>
        decimal.TryParse(Normalize(text), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null;

    public static int? ParseInt(string? text) =>
        int.TryParse(Normalize(text), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var chars = text.Where(c => c is not (',' or ' ' or '৳'))
            .Select(c => BnDigits.IndexOf(c) is var i and >= 0 ? (char)('0' + i) : c);
        return new string(chars.ToArray());
    }
}
