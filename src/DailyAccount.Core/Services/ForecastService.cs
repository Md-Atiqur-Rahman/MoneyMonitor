using DailyAccount.Core.Models;

namespace DailyAccount.Core.Services;

public sealed record Forecast(
    string Month,
    /// <summary>Still unpaid for this month and earlier; must also come out of today's balance.</summary>
    long CurrentRemaining,
    /// <summary>Installments, bills and personal dues scheduled for next month.</summary>
    long NextMonthDues,
    /// <summary>Running card bill for the open cycle, which becomes next month's statement.</summary>
    long UnbilledCard,
    long ExpectedSpending,
    long CurrentBalance,
    long ExpectedIncome)
{
    public long Required => CurrentRemaining + NextMonthDues + UnbilledCard + ExpectedSpending;
    public long Available => CurrentBalance + ExpectedIncome;
    public long ProjectedEndBalance => Available - Required;
    /// <summary>How much you'd need to borrow to cover next month. 0 = no shortfall.</summary>
    public long NeedToBorrow => Math.Max(0, Required - Available);
}

public static class ForecastService
{
    /// <param name="currentMonth">"yyyy-MM" for today; the forecast is for the following month.</param>
    /// <param name="expectedSpending">Usually <see cref="MonthSummaryService.AverageMonthlySpending"/>.
    /// Card purchases in it are counted again next cycle; this is deliberately conservative.</param>
    public static Forecast ForNextMonth(
        string currentMonth,
        IEnumerable<Due> dues,
        long unbilledCard,
        long expectedSpending,
        long currentBalance,
        long expectedIncome)
    {
        var next = MonthKey.Add(currentMonth, 1);
        var dueList = dues.Where(d => d.DueMonth.Length > 0 && d.Status != DueStatus.Paid).ToList();

        return new Forecast(
            Month: next,
            CurrentRemaining: dueList.Where(d => string.CompareOrdinal(d.DueMonth, currentMonth) <= 0).Sum(d => d.Remaining),
            NextMonthDues: dueList.Where(d => d.DueMonth == next).Sum(d => d.Remaining),
            UnbilledCard: unbilledCard,
            ExpectedSpending: expectedSpending,
            CurrentBalance: currentBalance,
            ExpectedIncome: expectedIncome);
    }
}
