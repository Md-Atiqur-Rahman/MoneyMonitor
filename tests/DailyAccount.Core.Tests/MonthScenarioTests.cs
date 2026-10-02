using DailyAccount.Core;
using DailyAccount.Core.Models;
using DailyAccount.Core.Services;

namespace DailyAccount.Core.Tests;

/// <summary>
/// The scenario from the requirements: October 2026, salary 1 lac,
/// last installment of a 1 lac / 3-month loan + September card bill of 3k.
/// </summary>
public class MonthScenarioTests
{
    private static long Tk(decimal taka) => Money.FromTaka(taka);

    private readonly Account _bank = new() { Id = 1, Name = "DBBL", OpeningBalance = Money.FromTaka(20_000) };
    private readonly Account _cash = new() { Id = 2, Name = "Cash", OpeningBalance = 0 };
    private readonly CreditCard _card = new() { Id = 1, Name = "Visa", StatementDay = 1, DueDay = 15 };
    private readonly List<Due> _dues = [];
    private readonly List<Transaction> _txns = [];

    public MonthScenarioTests()
    {
        var loan = new Loan { Id = 1, Lender = "City Bank", TotalPayable = Tk(100_000), InstallmentCount = 3, StartMonth = "2026-08" };
        var schedule = LiabilityEngine.BuildLoanSchedule(loan);
        for (var i = 0; i < schedule.Count; i++) schedule[i].Id = i + 1;
        // Aug + Sep installments already paid.
        schedule[0].PaidAmount = schedule[0].Amount; schedule[0].Status = DueStatus.Paid;
        schedule[1].PaidAmount = schedule[1].Amount; schedule[1].Status = DueStatus.Paid;
        _dues.AddRange(schedule);

        _txns.Add(new() { Type = TransactionType.CardPurchase, CardId = 1, Date = new DateTime(2026, 9, 12), Amount = Tk(3_000), Note = "Super shop" });
        var cardDues = LiabilityEngine.BuildCardStatements(_card, _txns, _dues, new DateTime(2026, 10, 1));
        cardDues[0].Id = 100;
        _dues.AddRange(cardDues);

        _txns.Add(new() { Type = TransactionType.Income, AccountId = 1, Date = new DateTime(2026, 10, 1), Amount = Tk(100_000) });
    }

    [Fact]
    public void October_dues_are_installment_plus_card_bill()
    {
        var s = MonthSummaryService.Summarize("2026-10", _txns, _dues);

        Assert.Equal(Tk(100_000), s.Income);
        Assert.Equal(Tk(33_333.34m + 3_000), s.DueThisMonth); // 36,333.34
        Assert.Equal(0, s.Overdue);
        Assert.Equal(Tk(36_333.34m), s.DueRemaining);
    }

    [Fact]
    public void Savings_after_paying_dues_and_expenses()
    {
        var loanDue = _dues.Single(d => d.SourceType == DueSource.Loan && d.DueMonth == "2026-10");
        var cardDue = _dues.Single(d => d.SourceType == DueSource.Card);
        _txns.Add(LiabilityEngine.ApplyPayment(loanDue, loanDue.Remaining, 1, new DateTime(2026, 10, 10)));
        _txns.Add(LiabilityEngine.ApplyPayment(cardDue, cardDue.Remaining, 1, new DateTime(2026, 10, 15)));
        _txns.Add(new() { Type = TransactionType.Transfer, AccountId = 1, ToAccountId = 2, Date = new DateTime(2026, 10, 2), Amount = Tk(10_000) });
        _txns.Add(new() { Type = TransactionType.Expense, AccountId = 2, Date = new DateTime(2026, 10, 3), Amount = Tk(8_000) });
        _txns.Add(new() { Type = TransactionType.CardPurchase, CardId = 1, Date = new DateTime(2026, 10, 4), Amount = Tk(2_500) });

        var s = MonthSummaryService.Summarize("2026-10", _txns, _dues);

        Assert.Equal(Tk(36_333.34m), s.DuePaid);
        Assert.Equal(0, s.DueRemaining);
        Assert.Equal(Tk(8_000), s.CashExpenses);
        Assert.Equal(Tk(10_500), s.TotalSpending);
        // 1,00,000 − 36,333.34 − 8,000; the Sep card purchase isn't counted twice.
        Assert.Equal(Tk(55_666.66m), s.Savings);

        // Balances: bank 20,000 + 1,00,000 − 36,333.34 − 10,000 = 73,666.66; cash 10,000 − 8,000 = 2,000
        Assert.Equal(Tk(73_666.66m), BalanceService.AccountBalance(_bank, _txns));
        Assert.Equal(Tk(2_000), BalanceService.AccountBalance(_cash, _txns));
        Assert.Equal(Tk(75_666.66m), BalanceService.TotalBalance([_bank, _cash], _txns));

        // Only October's card purchase is still owed.
        var unbilled = LiabilityEngine.UnbilledAmount(_card, _txns, new DateTime(2026, 10, 20));
        Assert.Equal(Tk(2_500), BalanceService.OutstandingLiabilities(_dues, unbilled));
    }

    [Fact]
    public void Forecast_shows_need_to_borrow_when_short()
    {
        // Big rent bill starts in November; balance is low.
        var rent = new RecurringBill { Id = 1, Name = "Rent", Amount = Tk(40_000), StartMonth = "2026-11" };
        _dues.AddRange(LiabilityEngine.BuildBillDues(rent, _dues, "2026-11"));

        var f = ForecastService.ForNextMonth(
            currentMonth: "2026-10",
            dues: _dues,
            unbilledCard: Tk(5_000),
            expectedSpending: Tk(30_000),
            currentBalance: Tk(40_000),
            expectedIncome: Tk(60_000));

        Assert.Equal("2026-11", f.Month);
        Assert.Equal(Tk(36_333.34m), f.CurrentRemaining);
        Assert.Equal(Tk(40_000), f.NextMonthDues);
        // Required 36,333.34 + 40,000 + 5,000 + 30,000 = 1,11,333.34; available 1,00,000
        Assert.Equal(Tk(11_333.34m), f.NeedToBorrow);
        Assert.Equal(Tk(-11_333.34m), f.ProjectedEndBalance);
    }

    [Fact]
    public void Receivables_and_net_worth()
    {
        var debts = new[] { new PersonalDebt { Id = 5, Direction = DebtDirection.Lent, Amount = Tk(10_000) } };
        _txns.Add(new() { Type = TransactionType.LendReturn, DebtId = 5, AccountId = 1, Date = new DateTime(2026, 10, 9), Amount = Tk(4_000) });

        var receivable = BalanceService.Receivables(debts, _txns);
        Assert.Equal(Tk(6_000), receivable);
        Assert.Equal(Tk(100), BalanceService.NetWorth(Tk(1_000), Tk(600), Tk(1_500)));
    }

    [Theory]
    [InlineData(100_000, "৳1,00,000")]
    [InlineData(33_333.5, "৳33,333.50")]
    [InlineData(12_34_56_789, "৳12,34,56,789")]
    [InlineData(999, "৳999")]
    [InlineData(-1500, "-৳1,500")]
    public void Money_formats_with_bangladeshi_grouping(decimal taka, string expected)
    {
        Assert.Equal(expected, Money.Format(Money.FromTaka(taka)));
    }
}
