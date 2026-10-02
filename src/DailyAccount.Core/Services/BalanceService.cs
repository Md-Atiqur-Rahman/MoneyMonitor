using DailyAccount.Core.Models;

namespace DailyAccount.Core.Services;

public static class BalanceService
{
    /// <summary>How a transaction changes the given account's balance.</summary>
    public static long EffectOn(int accountId, Transaction t) => t.Type switch
    {
        TransactionType.Income or TransactionType.BorrowIn or TransactionType.LendReturn
            => t.AccountId == accountId ? t.Amount : 0,
        TransactionType.Expense or TransactionType.DuePayment or TransactionType.LendOut
            => t.AccountId == accountId ? -t.Amount : 0,
        TransactionType.Transfer
            => (t.ToAccountId == accountId ? t.Amount : 0) - (t.AccountId == accountId ? t.Amount : 0),
        TransactionType.CardPurchase => 0,
        _ => 0
    };

    public static long AccountBalance(Account account, IEnumerable<Transaction> transactions) =>
        account.OpeningBalance + transactions.Sum(t => EffectOn(account.Id, t));

    /// <summary>Total money in all accounts: your "total savings".</summary>
    public static long TotalBalance(IEnumerable<Account> accounts, IReadOnlyCollection<Transaction> transactions) =>
        accounts.Where(a => a.IsActive).Sum(a => AccountBalance(a, transactions));

    /// <summary>Everything still owed: unpaid Dues + card purchases not yet billed.</summary>
    public static long OutstandingLiabilities(IEnumerable<Due> dues, long unbilledCardTotal) =>
        dues.Sum(d => d.Remaining) + unbilledCardTotal;

    /// <summary>Money others still owe you: lent − returned.</summary>
    public static long Receivables(IEnumerable<PersonalDebt> debts, IEnumerable<Transaction> transactions)
    {
        var returned = transactions
            .Where(t => t.Type == TransactionType.LendReturn && t.DebtId is not null)
            .GroupBy(t => t.DebtId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        return debts
            .Where(d => d.Direction == DebtDirection.Lent)
            .Sum(d => Math.Max(0, d.Amount - returned.GetValueOrDefault(d.Id)));
    }

    public static long NetWorth(long totalBalance, long receivables, long outstandingLiabilities) =>
        totalBalance + receivables - outstandingLiabilities;
}
