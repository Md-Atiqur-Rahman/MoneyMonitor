using System.Globalization;

namespace DailyAccount.Core;

/// <summary>Money is stored as long poisha (1 Tk = 100 poisha) to avoid rounding errors.</summary>
public static class Money
{
    public static long FromTaka(decimal taka) => (long)Math.Round(taka * 100m, MidpointRounding.AwayFromZero);

    public static decimal ToTaka(long poisha) => poisha / 100m;

    /// <summary>Formats with Bangladeshi grouping: 1,00,000.</summary>
    public static string Format(long poisha, bool withSymbol = true)
    {
        var taka = ToTaka(poisha);
        var negative = taka < 0;
        taka = Math.Abs(taka);

        var whole = decimal.Truncate(taka).ToString(CultureInfo.InvariantCulture);
        var fraction = taka - decimal.Truncate(taka);

        string grouped;
        if (whole.Length <= 3)
        {
            grouped = whole;
        }
        else
        {
            var last3 = whole[^3..];
            var rest = whole[..^3];
            var parts = new List<string>();
            while (rest.Length > 2)
            {
                parts.Insert(0, rest[^2..]);
                rest = rest[..^2];
            }
            if (rest.Length > 0) parts.Insert(0, rest);
            grouped = string.Join(",", parts) + "," + last3;
        }

        if (fraction != 0)
            grouped += (fraction).ToString("0.00", CultureInfo.InvariantCulture)[1..];

        return (negative ? "-" : "") + (withSymbol ? "৳" : "") + grouped;
    }
}
