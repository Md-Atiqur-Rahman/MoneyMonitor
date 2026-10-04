using DailyAccount.Core;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;

namespace DailyAccount.Core.Tests;

/// <summary>ADR 0040: lending through a card, and who owes what (given, paid back, left, fully paid).</summary>
public sealed class PersonalDebtTests : IAsyncLifetime
{
    private static long Tk(decimal taka) => Money.FromTaka(taka);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"da-debts-{Guid.NewGuid():N}.db3");
    private FinanceDatabase _db = null!;
    private FinanceService _svc = null!;
    private Account _bank = null!, _cash = null!;
    private CreditCard _card = null!;

    public async Task InitializeAsync()
    {
        _db = new FinanceDatabase(_path);
        _svc = new FinanceService(_db);
        _bank = new Account { Name = "Bank", Type = AccountType.Bank, OpeningBalance = Tk(50_000) };
        _cash = new Account { Name = "Cash", Type = AccountType.Cash, OpeningBalance = Tk(10_000) };
        await _svc.AddAccountAsync(_bank);
        await _svc.AddAccountAsync(_cash);
        _card = new CreditCard { Name = "Card", CreditLimit = Tk(100_000), StatementDay = 1, DueDay = 15, PayFromAccountId = _bank.Id };
        await _svc.AddCardAsync(_card, new DateTime(2026, 8, 1));
    }

    public async Task DisposeAsync()
    {
        await _db.CloseAsync();
        File.Delete(_path);
    }

    [Fact]
    public async Task Money_lent_through_a_card_is_on_the_bill_but_not_spending()
    {
        await _svc.AddPersonalDebtAsync(new PersonalDebt
        {
            PersonName = "Father", Direction = DebtDirection.Lent, Amount = Tk(3_000), Date = new DateTime(2026, 8, 12), CardId = _card.Id
        });
        await _svc.GenerateDuesAsync(new DateTime(2026, 9, 3));
        var s = await _svc.LoadAsync();

        Assert.Equal(Tk(3_000), s.CardBillDues(_card.Id, "2026-09").Sum(d => d.Remaining)); // on September's card bill
        Assert.Equal(0, s.Summary("2026-08").CardSpending);                                 // not spending
        Assert.DoesNotContain(s.Plan("2026-08", 0).Lines, l => l.OnCard > 0);
        Assert.Equal(Tk(50_000), s.Balance(_bank));                                         // no account moved

        // It belongs to the person's record: no deleting or editing it alone.
        var entry = s.Transactions.Single(t => t.Type == TransactionType.CardPurchase);
        Assert.Equal("Err_DeleteDebtTx", (await Assert.ThrowsAsync<FinanceException>(() => _svc.DeleteTransactionAsync(entry.Id))).Key);
        Assert.Equal("Err_EditNotAllowed", (await Assert.ThrowsAsync<FinanceException>(() => _svc.UpdateTransactionAsync(entry))).Key);
    }

    [Fact]
    public async Task Ledger_shows_given_paid_back_left_and_fully_paid()
    {
        await _svc.AddPersonalDebtAsync(new PersonalDebt
        {
            PersonName = "Brother", Direction = DebtDirection.Lent, Amount = Tk(5_000), Date = new DateTime(2026, 9, 5), AccountId = _cash.Id
        });
        await _svc.AddPersonalDebtAsync(new PersonalDebt
        {
            PersonName = "Friend", Direction = DebtDirection.Borrowed, Amount = Tk(10_000), Date = new DateTime(2026, 9, 1), AccountId = _bank.Id
        });
        var s = await _svc.LoadAsync();
        var brother = s.Debts.Single(d => d.PersonName == "Brother");
        var friend = s.Debts.Single(d => d.PersonName == "Friend");

        // Brother pays 3,000 back, then 2,000: fully paid. I repay the friend 4,000: 6,000 left.
        await _svc.ReceiveLendReturnAsync(brother.Id, Tk(3_000), _bank.Id, new DateTime(2026, 10, 2));
        await _svc.ReceiveLendReturnAsync(brother.Id, Tk(2_000), _cash.Id, new DateTime(2026, 10, 9));
        await _svc.PayDueAsync(s.DueFor(DueSource.Personal, friend.Id)!.Id, Tk(4_000), _bank.Id, new DateTime(2026, 10, 3));
        s = await _svc.LoadAsync();

        var b = s.Ledger(brother);
        Assert.Equal(Tk(5_000), b.Total);
        Assert.Equal(Tk(5_000), b.PaidBack);
        Assert.True(b.IsFullyPaid);
        Assert.Equal([true, false, false], b.Steps.Select(x => x.IsGiven));
        Assert.Equal(new DateTime(2026, 10, 9), b.LastPayment);

        var f = s.Ledger(friend);
        Assert.Equal(Tk(4_000), f.PaidBack);
        Assert.Equal(Tk(6_000), f.Left);
        Assert.False(f.IsFullyPaid);

        // Open ones first in the list.
        Assert.Equal(["Friend", "Brother"], s.Ledgers().Select(l => l.Debt.PersonName));
    }

    [Fact]
    public async Task An_expense_or_card_purchase_that_was_lending_becomes_a_lend()
    {
        var s = await _svc.LoadAsync();
        var others = s.Categories.Single(c => c.Name == "Others").Id;
        await _svc.AddTransactionAsync(new() { Type = TransactionType.Expense, AccountId = _cash.Id, CategoryId = others, Amount = Tk(5_000), Date = new DateTime(2026, 9, 5) });
        await _svc.AddTransactionAsync(new() { Type = TransactionType.CardPurchase, CardId = _card.Id, CategoryId = others, Amount = Tk(3_000), Date = new DateTime(2026, 8, 12) });
        await _svc.GenerateDuesAsync(new DateTime(2026, 9, 6));
        s = await _svc.LoadAsync();
        var expense = s.Transactions.Single(t => t.Type == TransactionType.Expense);
        var purchase = s.Transactions.Single(t => t.Type == TransactionType.CardPurchase);
        var bill = s.CardBillDues(_card.Id, "2026-09").Sum(d => d.Amount);

        await _svc.ConvertToDebtAsync(expense.Id, new PersonalDebt { PersonName = "Brother", Direction = DebtDirection.Lent });
        await _svc.ConvertToDebtAsync(purchase.Id, new PersonalDebt { PersonName = "Father", Direction = DebtDirection.Lent });
        s = await _svc.LoadAsync();

        // Cash balance unchanged (still 5,000 out), but no longer an expense.
        Assert.Equal(Tk(5_000), s.Balance(_cash));
        Assert.Equal(0, s.Summary("2026-09").CashExpenses);
        var brother = s.Ledger(s.Debts.Single(d => d.PersonName == "Brother"));
        Assert.Equal(Tk(5_000), brother.Left);
        Assert.Equal(new DateTime(2026, 9, 5), brother.Debt.Date);

        // The card purchase is the same entry, on the same bill, now Father's and not spending.
        var lentByCard = s.Transactions.Single(t => t.Id == purchase.Id);
        Assert.Equal(s.Debts.Single(d => d.PersonName == "Father").Id, lentByCard.DebtId);
        Assert.Equal(bill, s.CardBillDues(_card.Id, "2026-09").Sum(d => d.Amount));
        Assert.Equal(0, s.Summary("2026-08").CardSpending);

        // Only once, and only the right kind.
        Assert.Equal("Err_ConvertNotAllowed", (await Assert.ThrowsAsync<FinanceException>(() =>
            _svc.ConvertToDebtAsync(purchase.Id, new PersonalDebt { PersonName = "X", Direction = DebtDirection.Lent }))).Key);
    }
}
