using DailyAccount.Core;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;

namespace DailyAccount.Core.Tests;

/// <summary>End-to-end through a real SQLite file: the same code path the phone uses.</summary>
public sealed class FinanceServiceTests : IAsyncLifetime
{
    private static long Tk(decimal taka) => Money.FromTaka(taka);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"da-test-{Guid.NewGuid():N}.db3");
    private FinanceDatabase _db = null!;
    private FinanceService _svc = null!;

    public Task InitializeAsync()
    {
        _db = new FinanceDatabase(_path);
        _svc = new FinanceService(_db);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _db.CloseAsync();
        File.Delete(_path);
    }

    private async Task<(Account bank, CreditCard card)> SetUpOctoberAsync()
    {
        var bank = new Account { Name = "DBBL", Type = AccountType.Bank, OpeningBalance = Tk(20_000) };
        await _svc.AddAccountAsync(bank);

        await _svc.AddLoanAsync(new Loan { Lender = "City Bank", TotalPayable = Tk(100_000), InstallmentCount = 3, StartMonth = "2026-08", DueDay = 10 });

        var card = new CreditCard { Name = "Visa", StatementDay = 1, DueDay = 15 };
        await _svc.AddCardAsync(card, new DateTime(2026, 9, 1));
        await _svc.AddTransactionAsync(new Transaction { Type = TransactionType.CardPurchase, CardId = card.Id, Amount = Tk(3_000), Date = new DateTime(2026, 9, 12) });

        await _svc.AddTransactionAsync(new Transaction { Type = TransactionType.Income, AccountId = bank.Id, Amount = Tk(100_000), Date = new DateTime(2026, 10, 1) });
        return (bank, card);
    }

    [Fact]
    public async Task Seeds_default_categories_once()
    {
        var first = await _svc.LoadAsync();
        var second = await _svc.LoadAsync();
        Assert.Equal(23, first.Categories.Count);
        Assert.Equal(23, second.Categories.Count);
        Assert.DoesNotContain(first.Categories, c => c.Name == "Certificate");
        Assert.Contains(first.Categories, c => c.Name == "DPS");
        Assert.Contains(first.Categories, c => c.Name == "Education");
        Assert.Contains(first.Categories, c => c.NameBn == "বেতন");
    }

    [Fact]
    public async Task October_scenario_end_to_end()
    {
        var (bank, _) = await SetUpOctoberAsync();

        // App start on 2 Oct: the Sep card statement is generated, once.
        Assert.Equal(1, await _svc.GenerateDuesAsync(new DateTime(2026, 10, 2)));
        Assert.Equal(0, await _svc.GenerateDuesAsync(new DateTime(2026, 10, 2)));

        var s = await _svc.LoadAsync();
        var october = s.Summary("2026-10");
        // Aug and Sep installments were never paid in this test, so they show as overdue.
        Assert.Equal(Tk(36_333.34m), october.DueThisMonth);
        Assert.Equal(Tk(66_666.66m), october.Overdue);

        foreach (var due in s.Dues.Where(d => d.DueMonth == "2026-10"))
            await _svc.PayDueAsync(due.Id, due.Remaining, bank.Id, new DateTime(2026, 10, 10));

        s = await _svc.LoadAsync();
        Assert.Equal(Tk(36_333.34m), s.Summary("2026-10").DuePaid);
        Assert.Equal(Tk(20_000 + 100_000 - 36_333.34m), s.Balance(s.Accounts.Single()));
        Assert.All(s.Dues.Where(d => d.DueMonth == "2026-10"), d => Assert.Equal(DueStatus.Paid, d.Status));
    }

    [Fact]
    public async Task Deleting_a_payment_reopens_the_due()
    {
        var (bank, _) = await SetUpOctoberAsync();
        var due = (await _svc.LoadAsync()).Dues.First(d => d.DueMonth == "2026-10");

        await _svc.PayDueAsync(due.Id, Tk(10_000), bank.Id, new DateTime(2026, 10, 5));
        var s = await _svc.LoadAsync();
        Assert.Equal(DueStatus.Partial, s.Dues.Single(d => d.Id == due.Id).Status);

        var payment = s.Transactions.Single(t => t.Type == TransactionType.DuePayment);
        await _svc.DeleteTransactionAsync(payment.Id);

        s = await _svc.LoadAsync();
        var reopened = s.Dues.Single(d => d.Id == due.Id);
        Assert.Equal(DueStatus.Pending, reopened.Status);
        Assert.Equal(0, reopened.PaidAmount);
    }

    [Fact]
    public async Task Deleting_the_only_purchase_on_an_unpaid_bill_removes_the_bill()
    {
        await SetUpOctoberAsync();
        await _svc.GenerateDuesAsync(new DateTime(2026, 10, 2));
        var purchase = (await _svc.LoadAsync()).Transactions.Single(t => t.Type == TransactionType.CardPurchase);

        await _svc.DeleteTransactionAsync(purchase.Id);
        await _svc.GenerateDuesAsync(new DateTime(2026, 10, 3)); // must not come back

        var s = await _svc.LoadAsync();
        Assert.DoesNotContain(s.Transactions, t => t.Type == TransactionType.CardPurchase);
        Assert.DoesNotContain(s.Dues, d => d.SourceType == DueSource.Card);
    }

    [Fact]
    public async Task Deleting_one_purchase_reduces_the_unpaid_bill()
    {
        var (_, card) = await SetUpOctoberAsync(); // Sep purchase 3,000
        await _svc.AddTransactionAsync(new Transaction { Type = TransactionType.CardPurchase, CardId = card.Id, Amount = Tk(1_500), Date = new DateTime(2026, 9, 18) });
        await _svc.GenerateDuesAsync(new DateTime(2026, 10, 2));
        var s = await _svc.LoadAsync();
        Assert.Equal(Tk(4_500), s.Dues.Single(d => d.SourceType == DueSource.Card).Amount);

        await _svc.DeleteTransactionAsync(s.Transactions.Single(t => t.Amount == Tk(1_500)).Id);

        s = await _svc.LoadAsync();
        var bill = s.Dues.Single(d => d.SourceType == DueSource.Card);
        Assert.Equal(Tk(3_000), bill.Amount);
        Assert.Equal(DueStatus.Pending, bill.Status);
    }

    [Fact]
    public async Task Purchase_entered_after_the_statement_is_added_to_it()
    {
        var (_, card) = await SetUpOctoberAsync(); // Sep purchase 3,000
        await _svc.GenerateDuesAsync(new DateTime(2026, 10, 2)); // statement 3,000

        // The user enters more September purchases on 3 Oct (from the sheet).
        await _svc.AddTransactionAsync(new Transaction { Type = TransactionType.CardPurchase, CardId = card.Id, Amount = Tk(800), Date = new DateTime(2026, 9, 19) });
        await _svc.AddTransactionAsync(new Transaction { Type = TransactionType.CardPurchase, CardId = card.Id, Amount = Tk(2_930), Date = new DateTime(2026, 9, 20) });
        await _svc.GenerateDuesAsync(new DateTime(2026, 10, 3));

        var bill = Assert.Single((await _svc.LoadAsync()).Dues, d => d.SourceType == DueSource.Card);
        Assert.Equal(Tk(3_000 + 800 + 2_930), bill.Amount);
    }

    [Fact]
    public async Task Statement_never_drops_below_what_was_paid()
    {
        var (bank, card) = await SetUpOctoberAsync();
        await _svc.AddTransactionAsync(new Transaction { Type = TransactionType.CardPurchase, CardId = card.Id, Amount = Tk(1_000), Date = new DateTime(2026, 9, 20) });
        await _svc.GenerateDuesAsync(new DateTime(2026, 10, 2)); // 4,000
        var bill = (await _svc.LoadAsync()).Dues.Single(d => d.SourceType == DueSource.Card);
        await _svc.PayDueAsync(bill.Id, Tk(3_500), bank.Id, new DateTime(2026, 10, 5));

        // Deleting the 1,000 purchase would take the bill to 3,000 < 3,500 paid: refused.
        var small = (await _svc.LoadAsync()).Transactions.Single(t => t.Amount == Tk(1_000));
        await Assert.ThrowsAsync<FinanceException>(() => _svc.DeleteTransactionAsync(small.Id));
        await _svc.GenerateDuesAsync(new DateTime(2026, 10, 6));
        Assert.Equal(Tk(4_000), (await _svc.LoadAsync()).Dues.Single(d => d.SourceType == DueSource.Card).Amount);
    }

    [Fact]
    public async Task Editing_an_expense_changes_amount_category_and_balance()
    {
        var (bank, _) = await SetUpOctoberAsync();
        var s = await _svc.LoadAsync();
        var food = s.Categories.Single(c => c.Name == "Bajar").Id;
        var fish = s.Categories.Single(c => c.Name == "Fish").Id;
        await _svc.AddTransactionAsync(new Transaction { Type = TransactionType.Expense, AccountId = bank.Id, CategoryId = food, Amount = Tk(500), Date = new DateTime(2026, 10, 3) });
        var expense = (await _svc.LoadAsync()).Transactions.Single(t => t.Type == TransactionType.Expense);

        expense.Amount = Tk(730);
        expense.CategoryId = fish;
        expense.ItemName = "Rui";
        await _svc.UpdateTransactionAsync(expense);

        s = await _svc.LoadAsync();
        var saved = s.Transactions.Single(t => t.Id == expense.Id);
        Assert.Equal(Tk(730), saved.Amount);
        Assert.Equal(fish, saved.CategoryId);
        Assert.Equal("Rui", saved.ItemName);
        Assert.Equal(Tk(20_000 + 100_000 - 730), s.Balance(s.Accounts.Single()));
    }

    [Fact]
    public async Task Editing_a_billed_card_purchase_updates_the_unpaid_bill()
    {
        await SetUpOctoberAsync(); // Sep purchase 3,000
        await _svc.GenerateDuesAsync(new DateTime(2026, 10, 2));
        var purchase = (await _svc.LoadAsync()).Transactions.Single(t => t.Type == TransactionType.CardPurchase);

        purchase.Amount = Tk(2_500);
        await _svc.UpdateTransactionAsync(purchase);

        Assert.Equal(Tk(2_500), (await _svc.LoadAsync()).Dues.Single(d => d.SourceType == DueSource.Card).Amount);
    }

    [Fact]
    public async Task Card_purchase_cannot_be_edited_below_what_its_bill_already_paid()
    {
        var (bank, _) = await SetUpOctoberAsync();
        await _svc.GenerateDuesAsync(new DateTime(2026, 10, 2));
        var s = await _svc.LoadAsync();
        var bill = s.Dues.Single(d => d.SourceType == DueSource.Card);
        await _svc.PayDueAsync(bill.Id, Tk(3_000), bank.Id, new DateTime(2026, 10, 10));

        var purchase = s.Transactions.Single(t => t.Type == TransactionType.CardPurchase);
        purchase.Amount = Tk(2_000);
        Assert.Equal("Err_CardBilled", (await Assert.ThrowsAsync<FinanceException>(() => _svc.UpdateTransactionAsync(purchase))).Key);

        purchase.Amount = Tk(3_200); // raising it is fine: the extra 200 becomes due
        await _svc.UpdateTransactionAsync(purchase);
        var updated = (await _svc.LoadAsync()).Dues.Single(d => d.SourceType == DueSource.Card);
        Assert.Equal(Tk(200), updated.Remaining);
        Assert.Equal(DueStatus.Partial, updated.Status);
    }

    [Fact]
    public async Task Payments_and_kind_changes_cannot_be_edited()
    {
        var (bank, _) = await SetUpOctoberAsync();
        var due = (await _svc.LoadAsync()).Dues.First(d => d.DueMonth == "2026-10");
        await _svc.PayDueAsync(due.Id, Tk(100), bank.Id, new DateTime(2026, 10, 5));
        var s = await _svc.LoadAsync();

        var payment = s.Transactions.Single(t => t.Type == TransactionType.DuePayment);
        payment.Amount = Tk(50);
        Assert.Equal("Err_EditNotAllowed", (await Assert.ThrowsAsync<FinanceException>(() => _svc.UpdateTransactionAsync(payment))).Key);

        var income = s.Transactions.Single(t => t.Type == TransactionType.Income);
        income.Type = TransactionType.BorrowIn; // borrow/lend can't be made by editing
        Assert.Equal("Err_EditNotAllowed", (await Assert.ThrowsAsync<FinanceException>(() => _svc.UpdateTransactionAsync(income))).Key);
    }

    [Fact]
    public async Task An_expense_can_be_changed_into_a_card_purchase()
    {
        var (bank, card) = await SetUpOctoberAsync();
        await _svc.AddTransactionAsync(new Transaction { Type = TransactionType.Expense, AccountId = bank.Id, Amount = Tk(950), Date = new DateTime(2026, 9, 25) });
        await _svc.GenerateDuesAsync(new DateTime(2026, 10, 2)); // Sep statement: the 3,000 card purchase
        var expense = (await _svc.LoadAsync()).Transactions.Single(t => t.Type == TransactionType.Expense);

        expense.Type = TransactionType.CardPurchase;
        expense.CardId = card.Id;
        await _svc.UpdateTransactionAsync(expense);

        var s = await _svc.LoadAsync();
        Assert.Equal(Tk(3_950), s.Dues.Single(d => d.SourceType == DueSource.Card).Amount); // added to Sep bill
        Assert.Equal(Tk(20_000 + 100_000), s.Balance(s.Accounts.Single()));                // no longer from the bank
    }

    [Fact]
    public async Task Purchase_on_a_paid_bill_cannot_be_deleted()
    {
        var (bank, _) = await SetUpOctoberAsync();
        await _svc.GenerateDuesAsync(new DateTime(2026, 10, 2));
        var s = await _svc.LoadAsync();
        var bill = s.Dues.Single(d => d.SourceType == DueSource.Card);
        await _svc.PayDueAsync(bill.Id, bill.Amount, bank.Id, new DateTime(2026, 10, 10));

        var purchase = s.Transactions.Single(t => t.Type == TransactionType.CardPurchase);
        var ex = await Assert.ThrowsAsync<FinanceException>(() => _svc.DeleteTransactionAsync(purchase.Id));
        Assert.Equal("Err_CardBilled", ex.Key);
    }

    [Fact]
    public async Task Overpaying_a_due_is_rejected()
    {
        var (bank, _) = await SetUpOctoberAsync();
        var due = (await _svc.LoadAsync()).Dues.First();

        var ex = await Assert.ThrowsAsync<FinanceException>(() => _svc.PayDueAsync(due.Id, due.Amount + 1, bank.Id, DateTime.Today));
        Assert.Equal("Err_TooMuch", ex.Key);
    }

    [Fact]
    public async Task Borrow_and_lend_flows()
    {
        var bank = new Account { Name = "DBBL", OpeningBalance = Tk(10_000) };
        await _svc.AddAccountAsync(bank);

        await _svc.AddPersonalDebtAsync(new PersonalDebt { PersonName = "Rahim", Direction = DebtDirection.Borrowed, Amount = Tk(5_000), Date = new DateTime(2026, 9, 20), AccountId = bank.Id });
        await _svc.AddPersonalDebtAsync(new PersonalDebt { PersonName = "Karim", Direction = DebtDirection.Lent, Amount = Tk(3_000), Date = new DateTime(2026, 9, 21), AccountId = bank.Id });

        var s = await _svc.LoadAsync();
        Assert.Equal(Tk(12_000), s.Balance(s.Accounts.Single()));
        var rahimDue = Assert.Single(s.Dues);
        Assert.Equal("", rahimDue.DueMonth);
        Assert.Equal(Tk(5_000), s.OutstandingLiabilities(new DateTime(2026, 10, 1)));
        Assert.Equal(Tk(3_000), s.Receivables);

        var karim = s.Debts.Single(d => d.Direction == DebtDirection.Lent);
        await _svc.ReceiveLendReturnAsync(karim.Id, Tk(1_000), bank.Id, new DateTime(2026, 10, 1));
        await Assert.ThrowsAsync<FinanceException>(() => _svc.ReceiveLendReturnAsync(karim.Id, Tk(2_001), bank.Id, DateTime.Today));

        await _svc.PayDueAsync(rahimDue.Id, Tk(5_000), bank.Id, new DateTime(2026, 10, 2));

        s = await _svc.LoadAsync();
        Assert.Equal(Tk(2_000), s.Receivables);
        Assert.Equal(0, s.OutstandingLiabilities(new DateTime(2026, 10, 3)));
        Assert.Equal(Tk(10_000 + 5_000 - 3_000 + 1_000 - 5_000), s.TotalBalance);
    }

    [Fact]
    public async Task Stopping_a_bill_removes_future_unpaid_dues()
    {
        var bill = new RecurringBill { Name = "Rent", Amount = Tk(15_000), DayOfMonth = 5, StartMonth = "2026-09" };
        await _svc.AddBillAsync(bill, new DateTime(2026, 10, 2)); // Sep, Oct, Nov
        Assert.Equal(3, (await _svc.LoadAsync()).Dues.Count);

        await _svc.StopBillAsync(bill.Id, "2026-11");
        await _svc.GenerateDuesAsync(new DateTime(2026, 12, 2));

        var s = await _svc.LoadAsync();
        Assert.Equal(new[] { "2026-09", "2026-10" }, s.Dues.Select(d => d.DueMonth).Order());
    }

    [Fact]
    public async Task Loan_with_payments_cannot_be_deleted_but_fresh_one_can()
    {
        var bank = new Account { Name = "DBBL" };
        await _svc.AddAccountAsync(bank);
        var loan = await _svc.AddLoanAsync(new Loan { Lender = "Typo Bank", TotalPayable = Tk(1_000), InstallmentCount = 2, StartMonth = "2026-10" });

        await _svc.DeleteLoanAsync(loan.Id);
        var s = await _svc.LoadAsync();
        Assert.Empty(s.Loans);
        Assert.Empty(s.Dues);

        var loan2 = await _svc.AddLoanAsync(new Loan { Lender = "City", TotalPayable = Tk(1_000), InstallmentCount = 2, StartMonth = "2026-10" });
        var due = (await _svc.LoadAsync()).Dues.First();
        await _svc.PayDueAsync(due.Id, Tk(100), bank.Id, DateTime.Today);
        var ex = await Assert.ThrowsAsync<FinanceException>(() => _svc.DeleteLoanAsync(loan2.Id));
        Assert.Equal("Err_LoanHasPayments", ex.Key);
    }

    [Fact]
    public async Task Validation_errors_use_resource_keys()
    {
        await _svc.AddAccountAsync(new Account { Name = "A" });
        var ex = await Assert.ThrowsAsync<FinanceException>(() =>
            _svc.AddTransactionAsync(new Transaction { Type = TransactionType.Expense, AccountId = 1, Amount = 0 }));
        Assert.Equal("Err_Amount", ex.Key);

        ex = await Assert.ThrowsAsync<FinanceException>(() =>
            _svc.AddTransactionAsync(new Transaction { Type = TransactionType.Transfer, AccountId = 1, ToAccountId = 1, Amount = 5 }));
        Assert.Equal("Err_SameAccount", ex.Key);
    }

    [Fact]
    public async Task Backup_round_trip_keeps_ids_and_totals()
    {
        var (bank, _) = await SetUpOctoberAsync();
        await _svc.GenerateDuesAsync(new DateTime(2026, 10, 2));
        var due = (await _svc.LoadAsync()).Dues.First(d => d.DueMonth == "2026-10");
        await _svc.PayDueAsync(due.Id, Tk(1_000), bank.Id, new DateTime(2026, 10, 3));

        var before = await _svc.LoadAsync();
        var json = (await _svc.ExportAsync(new DateTime(2026, 10, 3))).ToJson();

        // Wipe by importing an empty backup, then restore.
        await _svc.ImportAsync(new BackupData());
        Assert.Empty((await _svc.LoadAsync()).Accounts);

        await _svc.ImportAsync(BackupData.FromJson(json));
        var after = await _svc.LoadAsync();

        Assert.Equal(before.TotalBalance, after.TotalBalance);
        Assert.Equal(before.Dues.Select(d => (d.Id, d.PaidAmount)), after.Dues.OrderBy(d => d.Id).Select(d => (d.Id, d.PaidAmount)));
        Assert.Equal(before.Transactions.Count, after.Transactions.Count);
        Assert.Equal(before.Summary("2026-10"), after.Summary("2026-10"));
    }

    [Fact]
    public void Corrupt_backup_file_is_rejected()
    {
        Assert.Equal("Err_BackupInvalid", Assert.Throws<FinanceException>(() => BackupData.FromJson("not json")).Key);
        Assert.Equal("Err_BackupNewer", Assert.Throws<FinanceException>(() => BackupData.FromJson("{\"Version\": 99}")).Key);
    }
}
