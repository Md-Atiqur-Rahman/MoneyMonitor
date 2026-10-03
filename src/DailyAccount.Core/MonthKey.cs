using System.Globalization;

namespace DailyAccount.Core;

/// <summary>Helpers for "yyyy-MM" month keys. String comparison of keys equals chronological order.</summary>
public static class MonthKey
{
    public static string Of(DateTime date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static DateTime FirstDay(string key) =>
        DateTime.ParseExact(key, "yyyy-MM", CultureInfo.InvariantCulture);

    public static string Add(string key, int months) => Of(FirstDay(key).AddMonths(months));

    public static bool Contains(string key, DateTime date) => Of(date) == key;

    public static DateTime LastDay(string key) => FirstDay(key).AddMonths(1).AddDays(-1);

    /// <summary>
    /// The day a month is looked at (ADR 0029): an earlier month as it stood on its last day; this month
    /// and later months as of today.
    /// </summary>
    public static DateTime AsOf(string key, DateTime today) =>
        string.CompareOrdinal(key, Of(today)) < 0 ? LastDay(key) : today;

    /// <summary>The given day in that month, clamped to the month's last day (e.g. 31 → 30 Sep).</summary>
    public static DateTime DayIn(string key, int day)
    {
        var first = FirstDay(key);
        var clamped = Math.Clamp(day, 1, DateTime.DaysInMonth(first.Year, first.Month));
        return new DateTime(first.Year, first.Month, clamped);
    }
}
