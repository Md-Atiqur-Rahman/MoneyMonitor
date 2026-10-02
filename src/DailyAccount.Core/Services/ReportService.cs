using DailyAccount.Core.Models;

namespace DailyAccount.Core.Services;

/// <summary>One group (sub-category) inside a category breakdown. CategoryId = the parent itself
/// for entries that were put directly on the parent ("Bajar" without a type).</summary>
public sealed record BreakdownGroup(int CategoryId, long Cash, long Card, List<Transaction> Items)
{
    public long Total => Cash + Card;
}

public sealed record CategoryBreakdown(int CategoryId, string Month, List<BreakdownGroup> Groups)
{
    public long Cash => Groups.Sum(g => g.Cash);
    public long Card => Groups.Sum(g => g.Card);
    public long Total => Cash + Card;
}

public static class ReportService
{
    /// <summary>
    /// Spending of one main category in one month, grouped by its sub-categories (ADR 0017).
    /// Counts cash/bank expenses and card purchases (the spending view of ADR 0009), largest group first.
    /// </summary>
    public static CategoryBreakdown Breakdown(
        int categoryId, string month, IReadOnlyCollection<Category> categories, IReadOnlyCollection<Transaction> transactions)
    {
        var ids = categories.Where(c => c.ParentId == categoryId).Select(c => c.Id).Append(categoryId).ToHashSet();

        var groups = transactions
            .Where(t => t.Type is TransactionType.Expense or TransactionType.CardPurchase
                        && t.CategoryId is { } id && ids.Contains(id)
                        && MonthKey.Contains(month, t.Date))
            .GroupBy(t => t.CategoryId!.Value)
            .Select(g => new BreakdownGroup(
                g.Key,
                g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount),
                g.Where(t => t.Type == TransactionType.CardPurchase).Sum(t => t.Amount),
                g.OrderBy(t => t.Date).ThenBy(t => t.Id).ToList()))
            .OrderByDescending(g => g.Total)
            .ToList();

        return new CategoryBreakdown(categoryId, month, groups);
    }
}
