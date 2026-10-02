using DailyAccount.Core.Models;

namespace DailyAccount.Core.Services;

public sealed record MonthSummary(
    string Month,
    long Income,
    /// <summary>Dues scheduled for this month (installments, last cycle's card bill, bills).</summary>
    long DueThisMonth,
    /// <summary>Unpaid dues from earlier months still outstanding.</summary>
    long Overdue,
    /// <summary>Payments made against dues during this month.</summary>
    long DuePaid,
    /// <summary>What's still to pay for this month and earlier.</summary>
    long DueRemaining,
    long CashExpenses,
    long CardSpending,
    long Borrowed,
    long Lent)
{
    /// <summary>Spending view: everything spent this month, cash + card.</summary>
    public long TotalSpending => CashExpenses + CardSpending;

    /// <summary>Cash flow view: Income − dues paid − cash expenses.</summary>
    public long Savings => Income - DuePaid - CashExpenses;

    /// <summary>Savings after the remaining dues are also paid.</summary>
    public long ProjectedSavings => Savings - DueRemaining;
}

public static class MonthSummaryService
{
    public static MonthSummary Summarize(string month, IEnumerable<Transaction> transactions, IEnumerable<Due> dues)
    {
        var inMonth = transactions.Where(t => MonthKey.Contains(month, t.Date)).ToList();
        long Sum(TransactionType type) => inMonth.Where(t => t.Type == type).Sum(t => t.Amount);

        var dueList = dues.Where(d => d.DueMonth.Length > 0).ToList();
        var thisMonth = dueList.Where(d => d.DueMonth == month).ToList();
        var earlierUnpaid = dueList
            .Where(d => string.CompareOrdinal(d.DueMonth, month) < 0 && d.Status != DueStatus.Paid)
            .ToList();

        return new MonthSummary(
            Month: month,
            Income: Sum(TransactionType.Income),
            DueThisMonth: thisMonth.Sum(d => d.Amount),
            Overdue: earlierUnpaid.Sum(d => d.Remaining),
            DuePaid: Sum(TransactionType.DuePayment),
            DueRemaining: thisMonth.Sum(d => d.Remaining) + earlierUnpaid.Sum(d => d.Remaining),
            CashExpenses: Sum(TransactionType.Expense),
            CardSpending: Sum(TransactionType.CardPurchase),
            Borrowed: Sum(TransactionType.BorrowIn),
            Lent: Sum(TransactionType.LendOut));
    }

    /// <summary>Average monthly cash + card spending over the last <paramref name="months"/> full months that have data.</summary>
    public static long AverageMonthlySpending(IEnumerable<Transaction> transactions, string currentMonth, int months = 3)
    {
        var totals = transactions
            .Where(t => t.Type is TransactionType.Expense or TransactionType.CardPurchase)
            .GroupBy(t => MonthKey.Of(t.Date))
            .Where(g => string.CompareOrdinal(g.Key, currentMonth) < 0)
            .OrderByDescending(g => g.Key)
            .Take(months)
            .Select(g => g.Sum(t => t.Amount))
            .ToList();

        return totals.Count == 0 ? 0 : totals.Sum() / totals.Count;
    }
}
