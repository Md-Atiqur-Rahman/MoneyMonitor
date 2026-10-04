using DailyAccount.Core;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;
using DailyAccount.Core.Services;

namespace DailyAccount.Core.Tests;

/// <summary>ADR 0030/0031: an earlier month's forecast, card parts, loans, borrowing and skipped budget items.</summary>
public sealed class MonthViewTests : IAsyncLifetime
{
    private static long Tk(decimal taka) => Money.FromTaka(taka);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"da-monthview-{Guid.NewGuid():N}.db3");
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

    private async Task<(Account Bank, CreditCard Card, Loan Loan)> SeptemberAsync()
    {
        var bank = new Account { Name = "Bank", Type = AccountType.Bank, OpeningBalance = Tk(100_000) };
        await _svc.AddAccountAsync(bank);
        var card = new CreditCard { Name = "Card", CreditLimit = Tk(300_000), StatementDay = 1, DueDay = 15, PayFromAccountId = bank.Id };
        await _svc.AddCardAsync(card, new DateTime(2026, 9, 1));
        // 50,000 in 6, first installment in September.
        var loan = new Loan { Lender = "Loan-1", Principal = Tk(50_000), TotalPayable = Tk(50_000), InstallmentCount = 6, StartMonth = "2026-09", DueDay = 15, CardId = card.Id };
        await _svc.AddLoanAsync(loan);
        await _svc.AddTransactionAsync(new() { Type = TransactionType.CardPurchase, CardId = card.Id, Amount = Tk(10_000), Date = new DateTime(2026, 9, 20) });
        await _svc.GenerateDuesAsync(new DateTime(2026, 10, 3)); // statement of 1 Oct for September's purchases
        return (bank, card, loan);
    }

    [Fact]
    public async Task Septembers_forecast_counts_septembers_purchases_once()
    {
        var (_, card, _) = await SeptemberAsync();
        var s = await _svc.LoadAsync();

        // Seen from September (as of 30 Sept) the September cycle is already on October's statement.
        Assert.Equal(0, s.Unbilled(card, new DateTime(2026, 9, 30)));
        var (october, _, _) = s.NextMonthPlan(new DateTime(2026, 9, 30), 0);
        Assert.Equal(Tk(8_333.33m) + Tk(10_000), october.DueLines.Sum(l => l.Estimate));

        // Seen from October (today) the October cycle is not billed yet: it is added to November.
        await _svc.AddTransactionAsync(new() { Type = TransactionType.CardPurchase, CardId = card.Id, Amount = Tk(3_000), Date = new DateTime(2026, 10, 2) });
        s = await _svc.LoadAsync();
        Assert.Equal(Tk(3_000), s.Unbilled(card, new DateTime(2026, 10, 3)));
        var (november, _, _) = s.NextMonthPlan(new DateTime(2026, 10, 3), 0);
        Assert.Equal(Tk(8_333.33m) + Tk(3_000), november.DueLines.Sum(l => l.Estimate));
    }

    [Fact]
    public async Task A_loan_is_seen_as_it_was_in_each_month()
    {
        var (bank, card, loan) = await SeptemberAsync();
        await _svc.PayCardBillAsync(card.Id, "2026-09", Tk(8_333.33m), bank.Id, new DateTime(2026, 9, 15));
        var s = await _svc.LoadAsync();

        var sep = s.LoanIn(loan, "2026-09");
        Assert.Equal(1, sep.PaidCount);
        Assert.Equal(1, sep.Installment!.Sequence);
        Assert.Equal(DueStatus.Paid, sep.Installment.Status);
        Assert.Equal(Tk(50_000) - Tk(8_333.33m), sep.Remaining);

        var aug = s.LoanIn(loan, "2026-08");
        Assert.Equal(0, aug.PaidCount);
        Assert.Null(aug.Installment);
        Assert.Equal(Tk(50_000), aug.Remaining);

        var oct = s.LoanIn(loan, "2026-10");
        Assert.Equal(2, oct.Installment!.Sequence);
        Assert.Equal(DueStatus.Pending, oct.Installment.Status);
    }

    [Fact]
    public async Task Borrowing_counts_only_from_its_pay_month_and_the_month_can_change()
    {
        var (bank, _, _) = await SeptemberAsync();
        await _svc.AddPersonalDebtAsync(new PersonalDebt
        {
            PersonName = "Friend", Direction = DebtDirection.Borrowed, Amount = Tk(30_000), Date = new DateTime(2026, 9, 1), AccountId = bank.Id
        });
        var s = await _svc.LoadAsync();
        var debt = s.Debts.Single();
        Assert.Equal(Tk(130_000), s.TotalBalanceOn(new DateTime(2026, 9, 1))); // the money is in the bank
        Assert.Empty(s.PersonalDueBy("2026-12"));                               // no pay month yet

        await _svc.SetDebtPayMonthAsync(debt.Id, "2026-10");
        s = await _svc.LoadAsync();
        var due = s.DueFor(DueSource.Personal, debt.Id)!;
        Assert.Equal("2026-10", due.DueMonth);
        Assert.Equal(new DateTime(2026, 10, 31), due.DueDate);
        Assert.Empty(s.PersonalDueBy("2026-09"));
        Assert.Single(s.PersonalDueBy("2026-10"));
        Assert.Single(s.PersonalDueBy("2026-11")); // still unpaid later on
        Assert.Equal(s.OwedIn("2026-09", new DateTime(2026, 9, 30)) + Tk(30_000), s.OwedIn("2026-10", new DateTime(2026, 9, 30)));
        Assert.Equal(Tk(30_000), s.MonthDue("2026-10", 0) - s.MonthDue("2026-09", 0) + s.Plan("2026-09", 0).UnpaidBudget - s.Plan("2026-10", 0).UnpaidBudget);

        // Moved to December: no longer counted in October.
        await _svc.SetDebtPayMonthAsync(debt.Id, "2026-12");
        s = await _svc.LoadAsync();
        Assert.Empty(s.PersonalDueBy("2026-10"));
        Assert.Single(s.PersonalDueBy("2026-12"));

        // Paid back: nothing left to count.
        await _svc.PayDueAsync(s.DueFor(DueSource.Personal, debt.Id)!.Id, Tk(30_000), bank.Id, new DateTime(2026, 12, 5));
        s = await _svc.LoadAsync();
        Assert.Empty(s.PersonalDueBy("2026-12"));
    }

    [Fact]
    public async Task Card_parts_add_up_in_september_and_october()
    {
        var (_, card, _) = await SeptemberAsync();
        await _svc.AddTransactionAsync(new() { Type = TransactionType.CardPurchase, CardId = card.Id, Amount = Tk(3_000), Date = new DateTime(2026, 10, 2) });
        var s = await _svc.LoadAsync();
        var sep30 = new DateTime(2026, 9, 30);
        var oct3 = new DateTime(2026, 10, 3);

        // September: its purchases are "this month" (already on the 1 Oct statement); EMIs 41,666.67 left.
        var sep = s.CardOwedOn(card, sep30);
        Assert.Equal(Tk(10_000), sep.ThisMonth);
        Assert.Equal(0, sep.LastMonth);
        Assert.Equal(Tk(50_000), sep.Emi); // nothing paid yet in this test
        Assert.Equal(sep.Total + 0, s.OwedIn("2026-09", sep30));
        Assert.Equal(new CardNext(Tk(8_333.33m), Tk(10_000)), s.CardNextMonth(card, "2026-09", sep30));
        Assert.Equal(s.CyclePurchases(card, sep30).Sum(t => t.Amount), sep.ThisMonth);

        // October: September's statement is now "last month"; October's purchases are this month.
        var oct = s.CardOwedOn(card, oct3);
        Assert.Equal(Tk(3_000), oct.ThisMonth);
        Assert.Equal(Tk(10_000), oct.LastMonth);
        Assert.Equal(new CardNext(Tk(8_333.33m), Tk(3_000)), s.CardNextMonth(card, "2026-10", oct3));
        Assert.Equal(s.CardLimitUsed(card, oct3), oct.Total);
    }

    [Fact]
    public async Task A_skipped_budget_item_leaves_the_budget_and_the_due()
    {
        var bank = new Account { Name = "Bank", Type = AccountType.Bank };
        await _svc.AddAccountAsync(bank);
        var s = await _svc.LoadAsync();
        var family = s.Categories.Single(c => c.Name == "Family").Id;
        var fruits = s.Categories.Single(c => c.Name == "Fruits").Id;
        await _svc.SetBudgetAsync("2026-09", family, Tk(3_000));
        await _svc.SetBudgetAsync("2026-09", fruits, Tk(1_500));
        await _svc.AddTransactionAsync(new() { Type = TransactionType.Expense, AccountId = bank.Id, CategoryId = fruits, Amount = Tk(500), Date = new DateTime(2026, 9, 5) });
        await _svc.FillBudgetMonthsAsync("2026-09", "2026-10");

        var before = (await _svc.LoadAsync()).Plan("2026-09", 0);
        Assert.Equal(Tk(4_500), before.BudgetEstimate);
        Assert.Equal(Tk(4_000), before.UnpaidBudget);

        await _svc.SetBudgetSkippedAsync("2026-09", family, true);
        await _svc.SetBudgetSkippedAsync("2026-09", fruits, true); // 500 already spent stays
        s = await _svc.LoadAsync();
        var plan = s.Plan("2026-09", 0);
        Assert.Equal(Tk(500), plan.BudgetEstimate);
        Assert.Equal(0, plan.UnpaidBudget);
        Assert.Equal(0, s.MonthDue("2026-09", 0));
        var line = plan.Lines.Single(l => l.CategoryId == family);
        Assert.True(line.Skipped);
        Assert.Equal(Tk(3_000), line.Planned);

        // Only September: October still plans both.
        Assert.Equal(Tk(4_500), s.Plan("2026-10", 0).BudgetEstimate);

        // Undo.
        await _svc.SetBudgetSkippedAsync("2026-09", family, false);
        Assert.Equal(Tk(3_500), (await _svc.LoadAsync()).Plan("2026-09", 0).BudgetEstimate);
    }

    [Fact]
    public async Task Cash_flow_shows_what_came_in_what_was_paid_and_spent()
    {
        var (bank, card, loan) = await SeptemberAsync();
        var s = await _svc.LoadAsync();
        int Cat(string name) => s.Categories.Single(c => c.Name == name && c.ParentId == null).Id;
        var sep1 = new DateTime(2026, 9, 1);
        await _svc.AddTransactionsAsync(
        [
            new() { Type = TransactionType.Income, AccountId = bank.Id, CategoryId = Cat("Salary"), Amount = Tk(80_000), Date = sep1 },
            new() { Type = TransactionType.Income, AccountId = bank.Id, CategoryId = Cat("Bonus"), Amount = Tk(20_000), Date = sep1 },
            new() { Type = TransactionType.Expense, AccountId = bank.Id, CategoryId = Cat("Rent"), Amount = Tk(12_000), Date = sep1 },
            new() { Type = TransactionType.Expense, AccountId = bank.Id, CategoryId = s.Categories.Single(c => c.Name == "Fish").Id, Amount = Tk(730), Date = sep1 },
            new() { Type = TransactionType.Expense, AccountId = bank.Id, CategoryId = Cat("Bajar"), Amount = Tk(270), Date = sep1 },
        ]);
        await _svc.AddPersonalDebtAsync(new PersonalDebt { PersonName = "Friend", Direction = DebtDirection.Borrowed, Amount = Tk(10_000), Date = sep1, AccountId = bank.Id });
        await _svc.PayCardBillAsync(card.Id, "2026-09", Tk(8_333.33m), bank.Id, new DateTime(2026, 9, 15));

        var flow = (await _svc.LoadAsync()).CashFlow("2026-09");
        Assert.Equal(Tk(110_000), flow.MoneyIn);
        Assert.Equal([MoneyInKind.Income, MoneyInKind.Income, MoneyInKind.Borrowed], flow.In.Select(l => l.Kind));
        Assert.Equal(Tk(80_000), flow.In[0].Amount); // Salary before Bonus (category order)

        var installment = Assert.Single(flow.DuesPaid);
        Assert.Equal(loan.Id, installment.Due.SourceId);
        Assert.Equal(1, installment.Due.Sequence);
        Assert.Equal(Tk(8_333.33m), flow.DuesPaidTotal);

        // Fish (a Bajar sub-category) adds into Bajar; card purchases are not cash spending.
        var bajar = flow.Spent.Single(g => g.CategoryId == Cat("Bajar"));
        Assert.Equal(Tk(1_000), bajar.Amount);
        Assert.Equal(2, bajar.Entries.Count);
        Assert.Equal(Tk(13_000), flow.CashSpent);
        Assert.Equal(Tk(110_000) - Tk(8_333.33m) - Tk(13_000), flow.Net);

        // The budget counts the borrowed money as money to spend too (ADR 0033).
        var plan = (await _svc.LoadAsync()).Plan("2026-09", Tk(80_000));
        Assert.Equal(Tk(110_000), plan.Income);
        Assert.Equal(Tk(10_000), plan.Borrowed);
        Assert.Equal(Tk(100_000), plan.Earned);
        Assert.False(plan.IncomeIsExpected);
    }

    [Fact]
    public async Task A_paid_card_bill_keeps_its_amount_and_date()
    {
        var (bank, card, _) = await SeptemberAsync();
        var s = await _svc.LoadAsync();
        Assert.False(s.CardBillIn(card, "2026-09").IsPaid);

        await _svc.PayCardBillAsync(card.Id, "2026-09", Tk(8_333.33m), bank.Id, new DateTime(2026, 9, 15));
        var bill = (await _svc.LoadAsync()).CardBillIn(card, "2026-09");
        Assert.True(bill.IsPaid);
        Assert.Equal(Tk(8_333.33m), bill.Billed);
        Assert.Equal(Tk(8_333.33m), bill.Paid);
        Assert.Equal(new DateTime(2026, 9, 15), bill.PaidOn);
        Assert.Empty((await _svc.LoadAsync()).CardBillDues(card.Id, "2026-09")); // the old view lost it
    }

    [Fact]
    public async Task A_finished_loan_shows_in_its_last_month_only()
    {
        var (_, card, _) = await SeptemberAsync();
        // 2 installments, both paid before: July and August.
        var done = await _svc.AddLoanAsync(new Loan { Lender = "Old", TotalPayable = Tk(2_000), InstallmentCount = 2, InstallmentsPaidBefore = 1, StartMonth = "2026-08", DueDay = 15, CardId = card.Id });
        var s = await _svc.LoadAsync();
        await _svc.PayDueAsync(s.Dues.Single(d => d.SourceType == DueSource.Loan && d.SourceId == done.Id && d.Sequence == 2).Id, Tk(1_000), s.Accounts[0].Id, new DateTime(2026, 9, 15));
        var later = await _svc.AddLoanAsync(new Loan { Lender = "Later", TotalPayable = Tk(3_000), InstallmentCount = 3, StartMonth = "2026-12", DueDay = 15 });
        s = await _svc.LoadAsync();

        Assert.True(s.LoanIn(done, "2026-09").ShowsIn);   // its last installment is in September
        Assert.False(s.LoanIn(done, "2026-10").ShowsIn);  // finished: not in October's list
        Assert.True(s.LoanIn(later, "2026-10").ShowsIn);  // not started yet: pending, shown
        Assert.True(s.Loans.Any(l => l.Id == done.Id));    // hidden, never deleted
    }

    [Fact]
    public async Task Last_month_is_only_the_month_before()
    {
        var (_, card, _) = await SeptemberAsync(); // a 10,000 purchase on 20 Sept
        await _svc.AddTransactionAsync(new() { Type = TransactionType.CardPurchase, CardId = card.Id, Amount = Tk(500), Date = new DateTime(2026, 8, 5) });
        await _svc.AddTransactionAsync(new() { Type = TransactionType.CardPurchase, CardId = card.Id, Amount = Tk(700), Date = new DateTime(2026, 10, 2) });
        var s = await _svc.LoadAsync();

        var last = s.LastCyclePurchases(card, new DateTime(2026, 10, 3));
        Assert.All(last, t => Assert.Equal(9, t.Date.Month)); // September only, no August
        Assert.Equal(Tk(10_000), last.Sum(t => t.Amount));
        Assert.Equal(Tk(500), s.LastCyclePurchases(card, new DateTime(2026, 9, 30)).Sum(t => t.Amount)); // from September: August
    }

    [Fact]
    public async Task A_card_statement_lists_the_purchases_it_is_made_of()
    {
        var (_, card, _) = await SeptemberAsync(); // 10,000 on 20 Sept → the 1 Oct statement
        await _svc.AddTransactionAsync(new() { Type = TransactionType.CardPurchase, CardId = card.Id, Amount = Tk(500), Date = new DateTime(2026, 9, 2) });
        await _svc.AddTransactionAsync(new() { Type = TransactionType.CardPurchase, CardId = card.Id, Amount = Tk(700), Date = new DateTime(2026, 10, 2) });
        await _svc.GenerateDuesAsync(new DateTime(2026, 10, 3));
        var s = await _svc.LoadAsync();

        var statement = s.Dues.Single(d => d.SourceType == DueSource.Card && d.DueMonth == "2026-10");
        var purchases = s.StatementPurchases(statement);
        Assert.Equal([Tk(500), Tk(10_000)], purchases.Select(t => t.Amount)); // September's two, oldest first
        Assert.Equal(statement.Amount, purchases.Sum(t => t.Amount));
        Assert.Empty(s.StatementPurchases(s.Dues.First(d => d.SourceType == DueSource.Loan)));
    }
}
