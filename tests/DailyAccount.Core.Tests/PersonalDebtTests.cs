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

    [Fact]
    public async Task One_person_adds_up_all_their_lends_and_pays_back_oldest_first()
    {
        var father = await _svc.AddPersonAsync("Father");
        Assert.Equal("Err_PersonExists", (await Assert.ThrowsAsync<FinanceException>(() => _svc.AddPersonAsync(" father "))).Key);

        await _svc.AddPersonalDebtAsync(new PersonalDebt { PersonId = father.Id, Direction = DebtDirection.Lent, Amount = Tk(3_000), Date = new DateTime(2026, 9, 29), CardId = _card.Id });
        await _svc.AddPersonalDebtAsync(new PersonalDebt { PersonId = father.Id, Direction = DebtDirection.Lent, Amount = Tk(2_000), Date = new DateTime(2026, 9, 26), CardId = _card.Id });
        var s = await _svc.LoadAsync();
        var p = Assert.Single(s.PeopleLedgers());
        Assert.Equal("Father", p.Person.Name);
        Assert.Equal(Tk(5_000), p.LentTotal);
        Assert.Equal(2, p.Lent.Count);
        Assert.All(s.Debts, d => Assert.Equal("Father", d.PersonName));

        // He pays back 2,500: the older lend (26 Sept, 2,000) is settled first, 500 off the other.
        await _svc.ReceiveFromPersonAsync(father.Id, Tk(2_500), _bank.Id, new DateTime(2026, 10, 4));
        s = await _svc.LoadAsync();
        p = s.PeopleLedgers().Single();
        Assert.Equal(Tk(2_500), p.LentBack);
        Assert.Equal(Tk(2_500), p.LentLeft);
        Assert.True(p.Lent.Single(l => l.Debt.Amount == Tk(2_000)).IsFullyPaid);
        Assert.Equal(Tk(2_500), p.Lent.Single(l => l.Debt.Amount == Tk(3_000)).Left);
        Assert.Equal("Err_TooMuch", (await Assert.ThrowsAsync<FinanceException>(
            () => _svc.ReceiveFromPersonAsync(father.Id, Tk(3_000), _bank.Id, new DateTime(2026, 10, 5)))).Key);
    }

    [Fact]
    public async Task Older_debts_are_linked_to_one_person_by_name()
    {
        // Two debts typed with the same name in another letter case (older data / older backup).
        await _db.Connection.InsertAsync(new PersonalDebt { PersonName = "Uncle", Direction = DebtDirection.Lent, Amount = Tk(100), Date = new DateTime(2026, 9, 1) });
        await _db.Connection.InsertAsync(new PersonalDebt { PersonName = "uncle ", Direction = DebtDirection.Lent, Amount = Tk(200), Date = new DateTime(2026, 9, 2) });
        await _db.Connection.RunInTransactionAsync(FinanceDatabase.LinkPeople);

        var s = await _svc.LoadAsync();
        var person = Assert.Single(s.People!);
        Assert.All(s.Debts, d => Assert.Equal(person.Id, d.PersonId));
        Assert.Equal(Tk(300), s.PeopleLedgers().Single().LentTotal);

        // Backup and restore keep them together.
        await _svc.ImportAsync(BackupData.FromJson((await _svc.ExportAsync(DateTime.Now)).ToJson()));
        Assert.Single((await _svc.LoadAsync()).PeopleLedgers());
    }

    [Fact]
    public async Task Gifting_closes_what_is_left_without_income_or_spending()
    {
        var father = await _svc.AddPersonAsync("Father");
        await _svc.AddPersonalDebtAsync(new PersonalDebt { PersonId = father.Id, Direction = DebtDirection.Lent, Amount = Tk(2_000), Date = new DateTime(2026, 9, 26), AccountId = _cash.Id });
        await _svc.AddPersonalDebtAsync(new PersonalDebt { PersonId = father.Id, Direction = DebtDirection.Lent, Amount = Tk(3_000), Date = new DateTime(2026, 9, 29), AccountId = _cash.Id });
        await _svc.ReceiveFromPersonAsync(father.Id, Tk(1_000), _bank.Id, new DateTime(2026, 10, 2));
        var before = await _svc.LoadAsync();

        // The 4,000 still owed is given as a gift.
        await _svc.GiftToPersonAsync(father.Id, Tk(4_000), new DateTime(2026, 10, 5));
        var s = await _svc.LoadAsync();
        var p = s.PeopleLedgers().Single();
        Assert.Equal(0, p.LentLeft);
        Assert.Equal(Tk(4_000), p.LentGifted);
        Assert.Equal(new DateTime(2026, 10, 5), p.LentSettledOn);
        Assert.Equal(0, s.Receivables);

        // No account moved, nothing became income or spending.
        Assert.Equal(before.TotalBalance, s.TotalBalance);
        Assert.Equal(before.Plan("2026-10", 0).Income, s.Plan("2026-10", 0).Income);
        Assert.Equal(before.Summary("2026-10").TotalSpending, s.Summary("2026-10").TotalSpending);
        Assert.Equal(before.CashFlow("2026-10").Net, s.CashFlow("2026-10").Net);

        Assert.Equal("Err_TooMuch", (await Assert.ThrowsAsync<FinanceException>(
            () => _svc.GiftToPersonAsync(father.Id, Tk(1), new DateTime(2026, 10, 6)))).Key);
    }

    [Fact]
    public async Task A_repaid_borrowing_knows_when_it_was_settled()
    {
        var friend = await _svc.AddPersonAsync("Friend");
        await _svc.AddPersonalDebtAsync(new PersonalDebt { PersonId = friend.Id, Direction = DebtDirection.Borrowed, Amount = Tk(1_000), Date = new DateTime(2026, 9, 1), AccountId = _bank.Id });
        var s = await _svc.LoadAsync();
        Assert.Null(s.PeopleLedgers().Single().BorrowedSettledOn);
        await _svc.PayDueAsync(s.DueFor(DueSource.Personal, s.Debts.Single().Id)!.Id, Tk(1_000), _bank.Id, new DateTime(2026, 10, 3));
        Assert.Equal(new DateTime(2026, 10, 3), (await _svc.LoadAsync()).PeopleLedgers().Single().BorrowedSettledOn);
    }
}
