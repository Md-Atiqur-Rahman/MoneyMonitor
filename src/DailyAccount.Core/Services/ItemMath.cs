using System.Globalization;
using System.Text.RegularExpressions;

namespace DailyAccount.Core.Services;

/// <summary>
/// Qty × rate for one item line (ADR 0027): "5 kg × ৳90 = ৳450". Grams and millilitres are priced per
/// kg / litre, as in the market ("250 gm at ৳800 a kg = ৳200"). Quantity is optional everywhere.
/// </summary>
public static partial class ItemMath
{
    /// <summary>Units offered on an item line. Stored as these codes inside <c>Transaction.Quantity</c>.</summary>
    public static readonly string[] Units = ["kg", "gm", "ltr", "ml", "pcs", "dozen", "packet"];

    /// <summary>The unit the rate is given for: gm → kg, ml → ltr, otherwise the unit itself.</summary>
    public static string RateUnit(string? unit) => unit switch
    {
        "gm" => "kg",
        "ml" => "ltr",
        _ => unit ?? ""
    };

    private static decimal Factor(string? unit) => unit is "gm" or "ml" ? 0.001m : 1m;

    /// <summary>Price in poisha of <paramref name="qty"/> units at <paramref name="rate"/> poisha per rate unit.</summary>
    public static long Price(decimal qty, string? unit, long rate) =>
        (long)Math.Round(qty * Factor(unit) * rate, MidpointRounding.AwayFromZero);

    /// <summary>The rate (per rate unit) that gives <paramref name="price"/>; null when qty is not positive.</summary>
    public static long? Rate(decimal qty, string? unit, long price) =>
        qty > 0 ? (long)Math.Round(price / (qty * Factor(unit)), MidpointRounding.AwayFromZero) : null;

    /// <summary>"5 kg", "1.5 kg", "3" — or null when there is no quantity.</summary>
    public static string? Format(decimal? qty, string? unit)
    {
        if (qty is not { } q || q <= 0) return null;
        var number = q.ToString("0.###", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(unit) ? number : $"{number} {unit}";
    }

    /// <summary>
    /// Reads a stored quantity back: "5 kg" / "2kg" / "500 gm" / "3" → (5, "kg") / (2, "kg") / (500, "gm") / (3, null).
    /// Old free text that isn't a number ("half") gives (null, null).
    /// </summary>
    public static (decimal? Qty, string? Unit) Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return (null, null);
        var m = QuantityPattern().Match(stored.Trim());
        if (!m.Success || !decimal.TryParse(m.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty))
            return (null, null);
        var unitText = m.Groups[2].Value.ToLowerInvariant();
        var unit = unitText switch
        {
            "" => null,
            "g" or "gram" or "grams" => "gm",
            "l" or "litre" or "liter" => "ltr",
            "pc" or "piece" or "pieces" => "pcs",
            _ => Units.Contains(unitText) ? unitText : null
        };
        return unitText.Length > 0 && unit is null ? (null, null) : (qty, unit);
    }

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s*([A-Za-z]*)$")]
    private static partial Regex QuantityPattern();
}
