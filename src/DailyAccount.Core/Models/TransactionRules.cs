namespace DailyAccount.Core.Models;

public static class TransactionRules
{
    /// <summary>
    /// Own spending: cash/bank expenses and card purchases — but not money lent to someone through a card
    /// (a card purchase with a <see cref="Transaction.DebtId"/>, ADR 0040): that comes back, so it isn't spent.
    /// </summary>
    public static bool IsSpending(this Transaction t) =>
        t.Type == TransactionType.Expense || (t.Type == TransactionType.CardPurchase && t.DebtId is null);
}
