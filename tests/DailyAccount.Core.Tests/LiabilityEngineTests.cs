using DailyAccount.Core;
using DailyAccount.Core.Models;
using DailyAccount.Core.Services;

namespace DailyAccount.Core.Tests;

public class LiabilityEngineTests
{
    private static long Tk(decimal taka) => Money.FromTaka(taka);

    [Fact]
    public void Loan_of_1_lac_in_3_installments_puts_remainder_on_last()
    {
        var loan = new Loan { Id = 1, Lender = "City Bank", TotalPayable = Tk(100_000), InstallmentCount = 3, StartMonth = "2026-08", DueDay = 10 };

        var dues = LiabilityEngine.BuildLoanSchedule(loan);

        Assert.Equal(new[] { Tk(33_333.33m), Tk(33_333.33m), Tk(33_333.34m) }, dues.Select(d => d.Amount));
        Assert.Equal(new[] { "2026-08", "2026-09", "2026-10" }, dues.Select(d => d.DueMonth));
        Assert.Equal(Tk(100_000), dues.Sum(d => d.Amount));
        Assert.Equal("City Bank (3/3)", dues[2].Title);
        Assert.Equal(new DateTime(2026, 10, 10), dues[2].DueDate);
    }

    [Fact]
    public void Loan_due_day_is_clamped_to_month_end()
    {
        var loan = new Loan { Id = 1, TotalPayable = Tk(3000), InstallmentCount = 1, StartMonth = "2026-02", DueDay = 31 };

        Assert.Equal(new DateTime(2026, 2, 28), LiabilityEngine.BuildLoanSchedule(loan)[0].DueDate);
    }

    [Fact]
    public void Card_september_purchases_become_october_bill_once()
    {
        var card = new CreditCard { Id = 7, Name = "Visa", StatementDay = 1, DueDay = 15 };
        var txns = new List<Transaction>
        {
            new() { Type = TransactionType.CardPurchase, CardId = 7, Date = new DateTime(2026, 9, 5), Amount = Tk(2_000) },
            new() { Type = TransactionType.CardPurchase, CardId = 7, Date = new DateTime(2026, 9, 30), Amount = Tk(1_000) },
            new() { Type = TransactionType.CardPurchase, CardId = 7, Date = new DateTime(2026, 10, 1), Amount = Tk(500) }, // next cycle
            new() { Type = TransactionType.CardPurchase, CardId = 8, Date = new DateTime(2026, 9, 10), Amount = Tk(999) }, // other card
        };
        var today = new DateTime(2026, 10, 2);

        var dues = LiabilityEngine.BuildCardStatements(card, txns, [], today);

        var bill = Assert.Single(dues);
        Assert.Equal(Tk(3_000), bill.Amount);
        Assert.Equal("2026-10", bill.DueMonth);
        Assert.Equal(new DateTime(2026, 10, 15), bill.DueDate);
        Assert.Equal("2026-09-01", bill.PeriodKey);

        // Idempotent: running again with the saved due creates nothing.
        Assert.Empty(LiabilityEngine.BuildCardStatements(card, txns, dues, today));

        // October's 500 is unbilled until 1 Nov.
        Assert.Equal(Tk(500), LiabilityEngine.UnbilledAmount(card, txns, today));
    }

    [Fact]
    public void Card_cycle_with_mid_month_statement_day()
    {
        var card = new CreditCard { Id = 1, StatementDay = 20, DueDay = 5 };

        Assert.Equal(new DateTime(2026, 9, 20), LiabilityEngine.CycleStart(card, new DateTime(2026, 10, 19)));
        Assert.Equal(new DateTime(2026, 10, 20), LiabilityEngine.CycleStart(card, new DateTime(2026, 10, 20)));

        var txns = new[] { new Transaction { Type = TransactionType.CardPurchase, CardId = 1, Date = new DateTime(2026, 9, 25), Amount = Tk(100) } };
        var bill = Assert.Single(LiabilityEngine.BuildCardStatements(card, txns, [], new DateTime(2026, 10, 20)));
        // Statement 20 Oct, due day 5 is before that → due 5 Nov.
        Assert.Equal(new DateTime(2026, 11, 5), bill.DueDate);
    }

    [Fact]
    public void Bills_generated_through_month_without_duplicates()
    {
        var bill = new RecurringBill { Id = 3, Name = "Rent", Amount = Tk(15_000), DayOfMonth = 5, StartMonth = "2026-09" };

        var first = LiabilityEngine.BuildBillDues(bill, [], "2026-11");
        Assert.Equal(new[] { "2026-09", "2026-10", "2026-11" }, first.Select(d => d.DueMonth));

        var again = LiabilityEngine.BuildBillDues(bill, first, "2026-12");
        Assert.Equal("2026-12", Assert.Single(again).DueMonth);
    }

    [Fact]
    public void Bill_stops_at_end_month()
    {
        var bill = new RecurringBill { Id = 3, Amount = 1, StartMonth = "2026-09", EndMonth = "2026-10" };

        Assert.Equal(2, LiabilityEngine.BuildBillDues(bill, [], "2027-01").Count);
    }

    [Fact]
    public void Personal_borrow_without_date_is_open_ended()
    {
        var due = LiabilityEngine.BuildPersonalDue(new PersonalDebt { Id = 1, PersonName = "Rahim", Direction = DebtDirection.Borrowed, Amount = Tk(5_000) });

        Assert.Equal("", due.DueMonth);
        Assert.Throws<ArgumentException>(() =>
            LiabilityEngine.BuildPersonalDue(new PersonalDebt { Direction = DebtDirection.Lent, Amount = 1 }));
    }

    [Fact]
    public void Partial_then_full_payment_updates_status()
    {
        var due = new Due { Id = 9, Amount = Tk(3_000), Title = "Visa bill" };

        var t1 = LiabilityEngine.ApplyPayment(due, Tk(1_000), accountId: 1, new DateTime(2026, 10, 5));
        Assert.Equal(DueStatus.Partial, due.Status);
        Assert.Equal(Tk(2_000), due.Remaining);
        Assert.Equal(TransactionType.DuePayment, t1.Type);
        Assert.Equal(9, t1.DueId);

        LiabilityEngine.ApplyPayment(due, Tk(2_000), 1, new DateTime(2026, 10, 6));
        Assert.Equal(DueStatus.Paid, due.Status);

        Assert.Throws<ArgumentException>(() => LiabilityEngine.ApplyPayment(due, 1, 1, DateTime.Today));
    }
}
