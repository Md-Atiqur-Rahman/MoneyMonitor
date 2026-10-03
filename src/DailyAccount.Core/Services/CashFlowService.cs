using DailyAccount.Core.Models;

namespace DailyAccount.Core.Services;

public enum MoneyInKind { Income, Borrowed, LendReturned }

/// <summary>One kind of money that came in: an income category (Salary), money borrowed from a person or a
/// bank loan (<paramref name="LoanId"/>, ADR 0034), or money returned by a person.</summary>
public sealed record MoneyInLine(MoneyInKind Kind, int? CategoryId, int? DebtId, long Amount, int Count, int? LoanId = null);

/// <summary>What was paid on one due in the month (an installment, a card statement, a personal repayment).</summary>
public sealed record DuePaidLine(Due Due, long Amount, DateTime LastDate, int? AccountId);

/// <summary>Cash/bank spending of one top-level category in the month, with its entries.</summary>
public sealed record SpentGroup(int? CategoryId, long Amount, List<Transaction> Entries);

/// <summary>
/// A month's cash flow in detail (ADR 0032), the way the sheet reads: money in (income + borrowed + lent
/// money returned) − dues paid − cash expenses = what is left in the accounts from this month.
/// </summary>
public sealed record CashFlow(string Month, List<MoneyInLine> In, List<DuePaidLine> DuesPaid, List<SpentGroup> Spent)
{
    public long MoneyIn => In.Sum(l => l.Amount);
    public long DuesPaidTotal => DuesPaid.Sum(l => l.Amount);
    public long CashSpent => Spent.Sum(g => g.Amount);
    public long Net => MoneyIn - DuesPaidTotal - CashSpent;
}

public static class CashFlowService
{
    public static CashFlow Build(string month, IEnumerable<Transaction> transactions, IReadOnlyCollection<Due> dues,
        IReadOnlyCollection<Category> categories)
    {
        var inMonth = transactions.Where(t => MonthKey.Contains(month, t.Date)).ToList();
        var topOf = categories.ToDictionary(c => c.Id, c => c.ParentId ?? c.Id);
        int? Top(int? id) => id is { } i && topOf.TryGetValue(i, out var top) ? top : null;
        int Order(int? categoryId) => categories.FirstOrDefault(c => c.Id == categoryId)?.SortOrder ?? int.MaxValue;

        var moneyIn = inMonth.Where(t => t.Type == TransactionType.Income)
            .GroupBy(t => t.CategoryId)
            .OrderBy(g => Order(g.Key))
            .Select(g => new MoneyInLine(MoneyInKind.Income, g.Key, null, g.Sum(t => t.Amount), g.Count()))
            .Concat(inMonth.Where(t => t.Type == TransactionType.BorrowIn)
                .GroupBy(t => (t.DebtId, t.LoanId))
                .Select(g => new MoneyInLine(MoneyInKind.Borrowed, null, g.Key.DebtId, g.Sum(t => t.Amount), g.Count(), g.Key.LoanId)))
            .Concat(inMonth.Where(t => t.Type == TransactionType.LendReturn)
                .GroupBy(t => t.DebtId)
                .Select(g => new MoneyInLine(MoneyInKind.LendReturned, null, g.Key, g.Sum(t => t.Amount), g.Count())))
            .ToList();

        var dueById = dues.ToDictionary(d => d.Id);
        var paid = inMonth.Where(t => t.Type == TransactionType.DuePayment && t.DueId is { } id && dueById.ContainsKey(id))
            .GroupBy(t => t.DueId!.Value)
            .Select(g => new DuePaidLine(dueById[g.Key], g.Sum(t => t.Amount), g.Max(t => t.Date), g.First().AccountId))
            .OrderBy(l => l.LastDate).ThenBy(l => l.Due.SourceType).ThenBy(l => l.Due.SourceId).ThenBy(l => l.Due.Sequence)
            .ToList();

        var spent = inMonth.Where(t => t.Type == TransactionType.Expense)
            .GroupBy(t => Top(t.CategoryId))
            .Select(g => new SpentGroup(g.Key, g.Sum(t => t.Amount), g.OrderBy(t => t.Date).ThenBy(t => t.Id).ToList()))
            .OrderBy(g => Order(g.CategoryId))
            .ToList();

        return new CashFlow(month, moneyIn, paid, spent);
    }
}
