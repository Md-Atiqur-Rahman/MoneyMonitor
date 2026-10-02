using DailyAccount.Core.Models;

namespace DailyAccount.Core.Services;

/// <summary>
/// One budget line for a top-level expense category.
/// Spent = cash/bank expenses this month in the category and its sub-categories.
/// OnCard = card purchases this month: shown for information only, because they are paid next month
/// through the card payment line (that's how the sheet works, and it avoids counting them twice).
/// </summary>
public sealed record BudgetLine(int CategoryId, long Estimate, long Spent, long OnCard, bool InBudget, bool OnlyThisMonth = false)
{
    public long Left => Estimate - Spent;
}

/// <summary>A liability payable this month, as one budget line: a whole card bill (statement + EMIs),
/// one non-card loan, or one personal debt.</summary>
public sealed record BudgetDueLine(DueSource Source, int SourceId, long Estimate, long Paid, List<int> DueIds)
{
    public long Left => Estimate - Paid;
}

public sealed record BudgetPlan(
    string Month,
    long Income,
    bool IncomeIsExpected,
    List<BudgetDueLine> DueLines,
    List<BudgetLine> Lines)
{
    public long TotalEstimate => DueLines.Sum(d => d.Estimate) + Lines.Sum(l => l.Estimate);
    public long TotalSpent => DueLines.Sum(d => d.Paid) + Lines.Sum(l => l.Spent);
    public long TotalLeft => TotalEstimate - TotalSpent;
    public long TotalOnCard => Lines.Sum(l => l.OnCard);

    /// <summary>The budget items only (Bajar, Rent…), without card/loan payments.</summary>
    public long BudgetEstimate => Lines.Sum(l => l.Estimate);
    public long BudgetSpent => Lines.Sum(l => l.Spent);
    public long BudgetLeft => BudgetEstimate - BudgetSpent;

    /// <summary>The sheet's "Save" = Income − everything planned (dues + estimates). Negative = shortfall.</summary>
    public long Save => Income - TotalEstimate;
    public long NeedToBorrow => Math.Max(0, -Save);
}

public static class BudgetService
{
    /// <param name="expectedIncome">Used when no income has been recorded for the month yet.</param>
    public static BudgetPlan Build(
        string month,
        IReadOnlyCollection<Category> categories,
        IReadOnlyCollection<Transaction> transactions,
        IReadOnlyCollection<BudgetItem> budget,
        IReadOnlyCollection<Due> dues,
        IReadOnlyCollection<Loan> loans,
        long expectedIncome)
    {
        var inMonth = transactions.Where(t => MonthKey.Contains(month, t.Date)).ToList();
        var actualIncome = inMonth.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);

        // Map every category to its top-level parent; uncategorized → 0.
        var topOf = categories.ToDictionary(c => c.Id, c => c.ParentId ?? c.Id);
        int Top(int? categoryId) => categoryId is { } id && topOf.TryGetValue(id, out var top) ? top : 0;

        var spent = inMonth.Where(t => t.Type == TransactionType.Expense)
            .GroupBy(t => Top(t.CategoryId)).ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
        var onCard = inMonth.Where(t => t.Type == TransactionType.CardPurchase)
            .GroupBy(t => Top(t.CategoryId)).ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var monthBudget = budget.Where(b => b.Month == month).ToList();
        var lines = monthBudget
            .Select(b => new BudgetLine(b.CategoryId, b.Estimate, spent.GetValueOrDefault(b.CategoryId), onCard.GetValueOrDefault(b.CategoryId), true, b.OnlyThisMonth))
            .ToList();

        // Spending in categories that have no budget line still shows up (estimate 0), so totals are honest.
        var budgeted = monthBudget.Select(b => b.CategoryId).ToHashSet();
        lines.AddRange(spent.Keys.Union(onCard.Keys)
            .Where(id => !budgeted.Contains(id))
            .Select(id => new BudgetLine(id, 0, spent.GetValueOrDefault(id), onCard.GetValueOrDefault(id), false)));

        return new BudgetPlan(
            month,
            actualIncome > 0 ? actualIncome : expectedIncome,
            actualIncome == 0 && expectedIncome > 0,
            DueLinesFor(month, dues, loans),
            lines);
    }

    /// <summary>
    /// Next month's plan, built the same way as the sheet (ADR 0015): its dues (e.g. card EMIs), plus
    /// card purchases not billed yet (they become next month's statement), plus its budget. When the
    /// month has no budget yet, the latest earlier budget is used in memory, nothing is saved.
    /// </summary>
    public static (BudgetPlan Plan, bool BudgetCopied) BuildForecast(
        string month,
        IReadOnlyCollection<Category> categories,
        IReadOnlyCollection<Transaction> transactions,
        IReadOnlyCollection<BudgetItem> budget,
        IReadOnlyCollection<Due> dues,
        IReadOnlyCollection<Loan> loans,
        IReadOnlyDictionary<int, long> unbilledByCard,
        long expectedIncome)
    {
        var items = budget.Where(b => b.Month == month).ToList();
        var copied = false;
        if (items.Count == 0)
        {
            var source = budget.Where(b => string.CompareOrdinal(b.Month, month) < 0)
                .GroupBy(b => b.Month).OrderByDescending(g => g.Key).FirstOrDefault();
            if (source is not null)
            {
                // Only repeating lines carry over; "only this month" lines don't (ADR 0019).
                items = source.Where(b => !b.OnlyThisMonth)
                    .Select(b => new BudgetItem { Month = month, CategoryId = b.CategoryId, Estimate = b.Estimate }).ToList();
                copied = items.Count > 0;
            }
        }

        var plan = Build(month, categories, transactions, items, dues, loans, expectedIncome);

        var dueLines = plan.DueLines.ToList();
        foreach (var (cardId, unbilled) in unbilledByCard.Where(kv => kv.Value > 0))
        {
            var i = dueLines.FindIndex(l => l.Source == DueSource.Card && l.SourceId == cardId);
            if (i >= 0)
                dueLines[i] = dueLines[i] with { Estimate = dueLines[i].Estimate + unbilled };
            else
                dueLines.Add(new BudgetDueLine(DueSource.Card, cardId, unbilled, 0, []));
        }

        return (plan with { DueLines = dueLines.OrderBy(l => l.Source).ThenBy(l => l.SourceId).ToList() }, copied);
    }

    /// <summary>
    /// Dues of this month grouped the way they're paid: everything billed on one card (statements +
    /// card EMIs) is one line; other loans, bills and personal debts are a line each.
    /// </summary>
    public static List<BudgetDueLine> DueLinesFor(string month, IEnumerable<Due> dues, IEnumerable<Loan> loans)
    {
        var loanCard = loans.ToDictionary(l => l.Id, l => l.CardId);

        (DueSource, int) GroupKey(Due d) =>
            d.SourceType == DueSource.Loan && loanCard.GetValueOrDefault(d.SourceId) is { } cardId
                ? (DueSource.Card, cardId)
                : (d.SourceType, d.SourceId);

        return dues.Where(d => d.DueMonth == month)
            .GroupBy(GroupKey)
            .Select(g => new BudgetDueLine(g.Key.Item1, g.Key.Item2, g.Sum(d => d.Amount), g.Sum(d => d.PaidAmount), g.Select(d => d.Id).ToList()))
            .OrderBy(l => l.Source).ThenBy(l => l.SourceId)
            .ToList();
    }
}
