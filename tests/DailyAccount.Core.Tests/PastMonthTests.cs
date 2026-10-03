using DailyAccount.Core;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;

namespace DailyAccount.Core.Tests;

/// <summary>ADR 0028/0029: any earlier month — balance at its end, months to choose from, the day it is seen as of.</summary>
public class PastMonthTests
{
    private static long Tk(decimal taka) => Money.FromTaka(taka);

    private static FinanceSnapshot Snapshot(List<Transaction> txns, List<BudgetItem>? budget = null) => new(
        [new Account { Id = 1, Name = "Bank", OpeningBalance = Tk(10_000), IsActive = true },
         new Account { Id = 2, Name = "Cash", OpeningBalance = 0, IsActive = true }],
        [], txns, [], [], [], [], [], budget ?? []);

    [Fact]
    public void Balance_at_the_end_of_a_month_counts_only_entries_up_to_that_day()
    {
        var s = Snapshot([
            new() { Type = TransactionType.Income, AccountId = 1, Amount = Tk(100_000), Date = new DateTime(2026, 1, 1) },
            new() { Type = TransactionType.Expense, AccountId = 1, Amount = Tk(30_000), Date = new DateTime(2026, 1, 31, 18, 30, 0) },
            new() { Type = TransactionType.Transfer, AccountId = 1, ToAccountId = 2, Amount = Tk(5_000), Date = new DateTime(2026, 1, 20) },
            new() { Type = TransactionType.Expense, AccountId = 1, Amount = Tk(40_000), Date = new DateTime(2026, 2, 1) },
            new() { Type = TransactionType.CardPurchase, CardId = 1, Amount = Tk(9_999), Date = new DateTime(2026, 1, 5) },
        ]);

        var endOfJan = new DateTime(2026, 1, 31);
        Assert.Equal(Tk(75_000), s.BalanceOn(s.Accounts[0], endOfJan));   // 10k + 100k − 30k − 5k
        Assert.Equal(Tk(5_000), s.BalanceOn(s.Accounts[1], endOfJan));
        Assert.Equal(Tk(80_000), s.TotalBalanceOn(endOfJan));
        Assert.Equal(Tk(10_000), s.TotalBalanceOn(new DateTime(2025, 12, 31))); // only the opening balance
        Assert.Equal(s.TotalBalance, s.TotalBalanceOn(new DateTime(2026, 2, 28)));
    }

    [Fact]
    public void Months_go_back_to_the_first_entry_or_start_month()
    {
        var s = Snapshot(
            [new() { Type = TransactionType.Expense, AccountId = 1, Amount = Tk(100), Date = new DateTime(2025, 11, 3) }],
            [new BudgetItem { Month = "2026-01", CategoryId = 1, Estimate = Tk(1) }]);

        Assert.Equal(["2026-02", "2026-01", "2025-12", "2025-11"], s.MonthsUpTo("2026-02", null));
        Assert.Equal(["2026-02", "2026-01", "2025-12", "2025-11", "2025-10"], s.MonthsUpTo("2026-02", "2025-10"));
        Assert.Equal(["2026-02"], Snapshot([]).MonthsUpTo("2026-02", null));
    }

    [Fact]
    public void An_earlier_month_is_seen_as_of_its_last_day()
    {
        var today = new DateTime(2026, 10, 3);
        Assert.Equal(new DateTime(2026, 9, 30), MonthKey.AsOf("2026-09", today));
        Assert.Equal(new DateTime(2026, 2, 28), MonthKey.AsOf("2026-02", today));
        Assert.Equal(today, MonthKey.AsOf("2026-10", today));
        Assert.Equal(today, MonthKey.AsOf("2026-11", today)); // a later month: as of today
        Assert.Equal(new DateTime(2024, 2, 29), MonthKey.LastDay("2024-02"));
    }
}
