using DailyAccount.Core;

namespace DailyAccount.App.Services;

/// <summary>
/// The month the whole app is showing (ADR 0029). Moving to September on any page (Home, Budget, Dues,
/// Reports, Accounts, Liabilities) moves every page there. Starts at this month when the app starts.
/// </summary>
public sealed class MonthState
{
    public string Month { get; set; } = MonthKey.Of(DateTime.Today);

    public string Current => MonthKey.Of(DateTime.Today);
    public bool IsCurrent => Month == Current;
    public bool IsPast => string.CompareOrdinal(Month, Current) < 0;

    /// <summary>Last day of an earlier month, otherwise today.</summary>
    public DateTime AsOf => MonthKey.AsOf(Month, DateTime.Today);

    public void Previous() => Month = MonthKey.Add(Month, -1);
    public void Next() => Month = MonthKey.Add(Month, 1);
    public void Reset() => Month = Current;
}
