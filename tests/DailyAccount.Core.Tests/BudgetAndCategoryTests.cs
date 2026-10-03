using DailyAccount.Core;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;

namespace DailyAccount.Core.Tests;

public sealed class BudgetAndCategoryTests : IAsyncLifetime
{
    private static long Tk(decimal taka) => Money.FromTaka(taka);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"da-budget-{Guid.NewGuid():N}.db3");
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

    [Fact]
    public async Task Default_categories_include_bajar_with_sub_categories()
    {
        var s = await _svc.LoadAsync();
        var bajar = s.Categories.Single(c => c.Name == "Bajar");
        Assert.Null(bajar.ParentId);
        Assert.Equal(["Grocery", "Meat", "Fish", "Vegetables", "Tiffin", "Cosmetics"], s.Children(bajar.Id).Select(c => c.Name));
    }

    [Fact]
    public async Task Category_rules()
    {
        var baba = await _svc.AddCategoryAsync("Parents", CategoryKind.Expense, null);
        var sub = await _svc.AddCategoryAsync("Medicine for parents", CategoryKind.Income, baba.Id);
        Assert.Equal(CategoryKind.Expense, sub.Kind); // a child always takes its parent's kind

        Assert.Equal("Err_CategoryExists", (await Assert.ThrowsAsync<FinanceException>(() => _svc.AddCategoryAsync("parents", CategoryKind.Expense, null))).Key);
        Assert.Equal("Err_SubOfSub", (await Assert.ThrowsAsync<FinanceException>(() => _svc.AddCategoryAsync("x", CategoryKind.Expense, sub.Id))).Key);

        // Budgets only on top-level expense categories
        Assert.Equal("Err_BudgetCategory", (await Assert.ThrowsAsync<FinanceException>(() => _svc.SetBudgetAsync("2026-10", sub.Id, 100))).Key);

        await _svc.RenameCategoryAsync(baba.Id, "Family support");
        Assert.Contains((await _svc.LoadAsync()).Categories, c => c.Name == "Family support" && c.NameBn == null);
    }

    [Fact]
    public async Task Unused_category_is_deleted_with_its_budget_lines_and_sub_categories()
    {
        var restaurant = await _svc.AddCategoryAsync("Restaurant Bill", CategoryKind.Expense, null);
        var fastFood = await _svc.AddCategoryAsync("Fast food", CategoryKind.Expense, restaurant.Id);
        await _svc.SetBudgetAsync("2026-10", restaurant.Id, Tk(2_000));
        await _svc.SetBudgetAsync("2026-11", restaurant.Id, Tk(2_500));

        await _svc.DeleteCategoryAsync(restaurant.Id);

        var s = await _svc.LoadAsync();
        Assert.DoesNotContain(s.Categories, c => c.Id == restaurant.Id || c.Id == fastFood.Id);
        Assert.Empty(s.Budget);
    }

    [Fact]
    public async Task Main_category_with_entries_cannot_be_deleted()
    {
        var cash = new Account { Name = "Cash", Type = AccountType.Cash };
        await _svc.AddAccountAsync(cash);
        var s = await _svc.LoadAsync();
        var bajar = s.Categories.Single(c => c.Name == "Bajar");
        var fish = s.Categories.Single(c => c.Name == "Fish");
        // An entry in a sub-category also protects the main category
        await _svc.AddTransactionAsync(new() { Type = TransactionType.Expense, AccountId = cash.Id, CategoryId = fish.Id, Amount = Tk(730), Date = DateTime.Today });

        var ex = await Assert.ThrowsAsync<FinanceException>(() => _svc.DeleteCategoryAsync(bajar.Id));
        Assert.Equal("Err_CategoryInUse", ex.Key);
    }

    [Fact]
    public async Task Deleting_a_sub_category_moves_its_entries_to_the_parent()
    {
        var cash = new Account { Name = "Cash", Type = AccountType.Cash };
        await _svc.AddAccountAsync(cash);
        var s = await _svc.LoadAsync();
        var bajar = s.Categories.Single(c => c.Name == "Bajar");
        var fish = s.Categories.Single(c => c.Name == "Fish");
        await _svc.SetBudgetAsync("2026-10", bajar.Id, Tk(15_000));
        await _svc.AddTransactionAsync(new() { Type = TransactionType.Expense, AccountId = cash.Id, CategoryId = fish.Id, ItemName = "Rui", Amount = Tk(730), Date = new DateTime(2026, 10, 2) });

        await _svc.DeleteCategoryAsync(fish.Id);

        s = await _svc.LoadAsync();
        Assert.DoesNotContain(s.Categories, c => c.Id == fish.Id);
        Assert.Equal(bajar.Id, s.Transactions.Single().CategoryId);
        Assert.Equal(Tk(730), s.Plan("2026-10", 0).Lines.Single(l => l.CategoryId == bajar.Id).Spent); // totals unchanged
    }

    [Fact]
    public async Task Last_main_category_of_a_kind_is_kept()
    {
        var s = await _svc.LoadAsync();
        var income = s.TopCategories(CategoryKind.Income).ToList();
        foreach (var c in income.Skip(1)) await _svc.DeleteCategoryAsync(c.Id);

        var ex = await Assert.ThrowsAsync<FinanceException>(() => _svc.DeleteCategoryAsync(income[0].Id));
        Assert.Equal("Err_LastCategory", ex.Key);
    }

    [Fact]
    public async Task Sub_category_spending_rolls_up_into_the_parent_budget_line()
    {
        var cash = new Account { Name = "Cash", Type = AccountType.Cash, OpeningBalance = Tk(5_000) };
        await _svc.AddAccountAsync(cash);
        var s = await _svc.LoadAsync();
        var bajar = s.Categories.Single(c => c.Name == "Bajar");
        var fish = s.Categories.Single(c => c.Name == "Fish");

        await _svc.SetBudgetAsync("2026-10", bajar.Id, Tk(15_000));
        await _svc.AddTransactionsAsync(
        [
            new() { Type = TransactionType.Expense, AccountId = cash.Id, CategoryId = fish.Id, ItemName = "Rui", Quantity = "2kg", Amount = Tk(730), Date = new DateTime(2026, 10, 2) },
            new() { Type = TransactionType.Expense, AccountId = cash.Id, CategoryId = bajar.Id, ItemName = "Misc", Amount = Tk(70), Date = new DateTime(2026, 10, 2) },
        ]);
        // Spending in a category without a budget line still appears
        await _svc.AddTransactionAsync(new() { Type = TransactionType.Expense, AccountId = cash.Id, CategoryId = s.Categories.Single(c => c.Name == "Wifi").Id, Amount = Tk(600), Date = new DateTime(2026, 10, 3) });

        var plan = (await _svc.LoadAsync()).Plan("2026-10", Tk(50_000));
        var line = plan.Lines.Single(l => l.CategoryId == bajar.Id);
        Assert.Equal(Tk(800), line.Spent);
        Assert.Equal(Tk(14_200), line.Left);
        Assert.Contains(plan.Lines, l => !l.InBudget && l.Spent == Tk(600));
        Assert.True(plan.IncomeIsExpected);
        Assert.Equal(Tk(50_000 - 15_000), plan.Save);
    }

    [Fact]
    public async Task Items_of_one_trip_are_saved_together_or_not_at_all()
    {
        var cash = new Account { Name = "Cash", Type = AccountType.Cash };
        await _svc.AddAccountAsync(cash);

        await Assert.ThrowsAsync<FinanceException>(() => _svc.AddTransactionsAsync(
        [
            new() { Type = TransactionType.Expense, AccountId = cash.Id, Amount = Tk(450), ItemName = "Rice", Date = DateTime.Today },
            new() { Type = TransactionType.Expense, AccountId = cash.Id, Amount = 0, ItemName = "Free?", Date = DateTime.Today },
        ]));
        Assert.Empty((await _svc.LoadAsync()).Transactions);
    }

    private async Task<(int Rent, int Bajar, int Eid)> BudgetCategoriesAsync()
    {
        var s = await _svc.LoadAsync();
        var eid = await _svc.AddCategoryAsync("Eid shopping", CategoryKind.Expense, null);
        return (s.Categories.Single(c => c.Name == "Rent").Id, s.Categories.Single(c => c.Name == "Bajar").Id, eid.Id);
    }

    [Fact]
    public async Task Repeating_lines_carry_over_and_only_this_month_lines_do_not()
    {
        var (rent, bajar, eid) = await BudgetCategoriesAsync();
        await _svc.SetBudgetAsync("2026-10", rent, Tk(12_000));                       // every month (default)
        await _svc.SetBudgetAsync("2026-10", bajar, Tk(15_000), onlyThisMonth: false);
        await _svc.SetBudgetAsync("2026-10", eid, Tk(5_000), onlyThisMonth: true);    // one-off

        Assert.True(await _svc.EnsureBudgetAsync("2026-11"));
        var nov = (await _svc.LoadAsync()).Budget.Where(b => b.Month == "2026-11").ToList();
        Assert.Equal(new[] { rent, bajar }.Order(), nov.Select(b => b.CategoryId).Order());
        Assert.DoesNotContain(nov, b => b.CategoryId == eid);

        // The in-memory forecast follows the same rule.
        var (plan, _, _) = (await _svc.LoadAsync()).NextMonthPlan(new DateTime(2026, 10, 2), Tk(100_000));
        Assert.Equal(Tk(12_000 + 15_000), plan.BudgetEstimate);
    }

    [Fact]
    public async Task Changing_a_repeating_line_updates_later_months_already_created()
    {
        var (rent, _, _) = await BudgetCategoriesAsync();
        await _svc.SetBudgetAsync("2026-10", rent, Tk(12_000));
        await _svc.EnsureBudgetAsync("2026-11");
        await _svc.EnsureBudgetAsync("2026-12");

        await _svc.SetBudgetAsync("2026-10", rent, Tk(12_000)); // rent goes up from October on
        var s = await _svc.LoadAsync();
        Assert.All(s.Budget, b => Assert.Equal(Tk(12_000), b.Estimate));
        Assert.Equal(3, s.Budget.Count);
    }

    [Fact]
    public async Task Making_a_line_only_this_month_removes_it_from_later_months()
    {
        var (rent, bajar, _) = await BudgetCategoriesAsync();
        await _svc.SetBudgetAsync("2026-10", rent, Tk(12_000));
        await _svc.SetBudgetAsync("2026-10", bajar, Tk(15_000));
        await _svc.EnsureBudgetAsync("2026-11");

        await _svc.SetBudgetAsync("2026-10", bajar, Tk(15_000), onlyThisMonth: true);

        var s = await _svc.LoadAsync();
        Assert.True(s.Budget.Single(b => b.Month == "2026-10" && b.CategoryId == bajar).OnlyThisMonth);
        Assert.Equal([rent], s.Budget.Where(b => b.Month == "2026-11").Select(b => b.CategoryId).ToArray());
    }

    [Fact]
    public async Task Removing_a_line_removes_it_from_this_and_later_months_only()
    {
        var (rent, _, _) = await BudgetCategoriesAsync();
        await _svc.SetBudgetAsync("2026-09", rent, Tk(11_000));
        await _svc.SetBudgetAsync("2026-10", rent, Tk(12_000));
        await _svc.EnsureBudgetAsync("2026-11");

        await _svc.RemoveBudgetAsync("2026-10", rent);

        Assert.Equal(["2026-09"], (await _svc.LoadAsync()).Budget.Select(b => b.Month).ToArray());
    }

    [Fact]
    public async Task Plan_reports_budget_totals_without_card_payments()
    {
        var cash = new Account { Name = "Cash", Type = AccountType.Cash, OpeningBalance = Tk(10_000) };
        await _svc.AddAccountAsync(cash);
        var (rent, bajar, _) = await BudgetCategoriesAsync();
        await _svc.SetBudgetAsync("2026-10", rent, Tk(12_000));
        await _svc.SetBudgetAsync("2026-10", bajar, Tk(15_000));
        await _svc.AddTransactionAsync(new() { Type = TransactionType.Expense, AccountId = cash.Id, CategoryId = bajar, Amount = Tk(830), Date = new DateTime(2026, 10, 2) });

        var plan = (await _svc.LoadAsync()).Plan("2026-10", 0);
        Assert.Equal(Tk(27_000), plan.BudgetEstimate);
        Assert.Equal(Tk(830), plan.BudgetSpent);
        Assert.Equal(Tk(26_170), plan.BudgetLeft);
    }

    [Fact]
    public async Task Default_budget_is_added_once_without_input_and_repeats_every_month()
    {
        Assert.True(await _svc.ApplyDefaultBudgetAsync("2026-10"));
        Assert.False(await _svc.ApplyDefaultBudgetAsync("2026-10")); // only once

        var s = await _svc.LoadAsync();
        var oct = s.Budget.Where(b => b.Month == "2026-10").ToList();
        Assert.Equal(13, oct.Count);
        Assert.Equal(Tk(63_499), oct.Sum(b => b.Estimate));
        Assert.All(oct, b => Assert.False(b.OnlyThisMonth));
        long Line(string name) => oct.Single(b => b.CategoryId == s.Categories.Single(c => c.Name == name && c.ParentId == null).Id).Estimate;
        Assert.Equal(Tk(11_350), Line("Rent"));
        Assert.Equal(Tk(20_000), Line("DPS"));
        Assert.Equal(Tk(1_500), Line("Education"));

        // Shows in the next month automatically
        await _svc.EnsureBudgetAsync("2026-11");
        Assert.Equal(13, (await _svc.LoadAsync()).Budget.Count(b => b.Month == "2026-11"));
    }

    [Fact]
    public async Task Default_budget_keeps_user_amounts_and_recreates_deleted_categories()
    {
        // The phone's situation: DPS was deleted, some lines already set by the user.
        var s = await _svc.LoadAsync();
        await _svc.DeleteCategoryAsync(s.Categories.Single(c => c.Name == "DPS").Id);
        var bajar = s.Categories.Single(c => c.Name == "Bajar").Id;
        var restaurant = await _svc.AddCategoryAsync("Restaurant Bill", CategoryKind.Expense, null);
        await _svc.SetBudgetAsync("2026-10", bajar, Tk(15_000));
        await _svc.SetBudgetAsync("2026-10", restaurant.Id, Tk(2_000));

        await _svc.ApplyDefaultBudgetAsync("2026-10");

        s = await _svc.LoadAsync();
        var oct = s.Budget.Where(b => b.Month == "2026-10").ToList();
        Assert.Equal(Tk(15_000), oct.Single(b => b.CategoryId == bajar).Estimate);   // user's amount kept
        Assert.Contains(oct, b => b.CategoryId == restaurant.Id);                     // user's own line kept
        Assert.Contains(s.Categories, c => c.Name == "DPS");                          // re-created
        Assert.Equal(14, oct.Count);
    }

    [Fact]
    public async Task Month_due_is_unpaid_budget_plus_non_card_dues_and_ignores_overspending()
    {
        var cash = new Account { Name = "Cash", Type = AccountType.Cash, OpeningBalance = Tk(50_000) };
        await _svc.AddAccountAsync(cash);
        var (rent, bajar, _) = await BudgetCategoriesAsync();
        var gas = (await _svc.LoadAsync()).Categories.Single(c => c.Name == "Gas").Id;
        await _svc.SetBudgetAsync("2026-10", rent, Tk(12_000));
        await _svc.SetBudgetAsync("2026-10", bajar, Tk(15_000));
        await _svc.SetBudgetAsync("2026-10", gas, Tk(1_700));
        async Task Spend(int cat, decimal amount) => await _svc.AddTransactionAsync(new()
            { Type = TransactionType.Expense, AccountId = cash.Id, CategoryId = cat, Amount = Tk(amount), Date = new DateTime(2026, 10, 2) });
        await Spend(rent, 12_000);   // fully paid
        await Spend(bajar, 830);     // 17,170 left
        await Spend(gas, 2_000);     // overspent by 300 → counts as 0, not −300

        // A loan that is not on a card is due too; a card EMI would not be.
        await _svc.AddLoanAsync(new Loan { Lender = "Friend", TotalPayable = Tk(3_000), InstallmentCount = 3, StartMonth = "2026-10", DueDay = 20 });

        var s = await _svc.LoadAsync();
        Assert.Equal(Tk(14_170), s.Plan("2026-10", 0).UnpaidBudget);
        Assert.Equal(Tk(1_000), Assert.Single(s.NonCardDuesUpTo("2026-10")).Remaining);
        Assert.Equal(Tk(15_170), s.MonthDue("2026-10", 0));
    }

    [Fact]
    public async Task Starting_earlier_fills_past_months_from_the_first_budget()
    {
        var (rent, bajar, eid) = await BudgetCategoriesAsync();
        await _svc.SetBudgetAsync("2026-10", rent, Tk(12_000));
        await _svc.SetBudgetAsync("2026-10", bajar, Tk(15_000));
        await _svc.SetBudgetAsync("2026-10", eid, Tk(5_000), onlyThisMonth: true);

        Assert.Equal(2, await _svc.FillBudgetMonthsAsync("2026-08", "2026-10")); // Aug, Sep filled; Oct kept
        Assert.Equal(0, await _svc.FillBudgetMonthsAsync("2026-08", "2026-10")); // idempotent

        var s = await _svc.LoadAsync();
        foreach (var m in new[] { "2026-08", "2026-09" })
        {
            var lines = s.Budget.Where(b => b.Month == m).ToList();
            Assert.Equal(new[] { rent, bajar }.Order(), lines.Select(b => b.CategoryId).Order()); // one-off Eid not copied
            Assert.Equal(Tk(27_000), lines.Sum(b => b.Estimate));
        }
        Assert.Equal(3, s.Budget.Count(b => b.Month == "2026-10"));
    }

    [Fact]
    public async Task Carry_shows_actual_saving_card_purchases_and_unpaid_dues_of_a_month()
    {
        var bank = new Account { Name = "Bank A", Type = AccountType.Bank };
        await _svc.AddAccountAsync(bank);
        var card = new CreditCard { Name = "Card", StatementDay = 1, DueDay = 15 };
        await _svc.AddCardAsync(card, new DateTime(2026, 9, 1));
        var s = await _svc.LoadAsync();
        var bajar = s.Categories.Single(c => c.Name == "Bajar").Id;

        await _svc.AddTransactionAsync(new() { Type = TransactionType.Income, AccountId = bank.Id, Amount = Tk(100_000), Date = new DateTime(2026, 9, 1) });
        await _svc.AddTransactionAsync(new() { Type = TransactionType.Expense, AccountId = bank.Id, CategoryId = bajar, Amount = Tk(30_000), Date = new DateTime(2026, 9, 5) });
        await _svc.AddTransactionAsync(new() { Type = TransactionType.CardPurchase, CardId = card.Id, CategoryId = bajar, Amount = Tk(2_559), Date = new DateTime(2026, 9, 18) });
        await _svc.AddLoanAsync(new Loan { Lender = "Friend", TotalPayable = Tk(2_000), InstallmentCount = 2, StartMonth = "2026-09", DueDay = 20 });
        var firstDue = (await _svc.LoadAsync()).Dues.Single(d => d.SourceType == DueSource.Loan && d.DueMonth == "2026-09");
        await _svc.PayDueAsync(firstDue.Id, Tk(400), bank.Id, new DateTime(2026, 9, 20)); // partly paid

        var carry = (await _svc.LoadAsync()).Carry("2026-09");
        Assert.Equal(Tk(100_000 - 30_000 - 400), carry.Saved);   // actual: income − spent − bills paid
        Assert.Equal(Tk(2_559), carry.CardToNextBill);            // on October's card bill
        Assert.Equal(Tk(600), carry.CarriedDues);                 // unpaid, overdue in October
    }

    [Fact]
    public async Task Budget_edit_and_remove()
    {
        var s = await _svc.LoadAsync();
        var rent = s.Categories.Single(c => c.Name == "Rent");
        await _svc.SetBudgetAsync("2026-10", rent.Id, Tk(11_000));
        await _svc.SetBudgetAsync("2026-10", rent.Id, Tk(12_000));
        Assert.Equal(Tk(12_000), Assert.Single((await _svc.LoadAsync()).Budget).Estimate);

        await _svc.RemoveBudgetAsync("2026-10", rent.Id);
        Assert.Empty((await _svc.LoadAsync()).Budget);
    }

    [Fact]
    public async Task Loan_paid_before_must_leave_at_least_one_installment()
    {
        var ex = await Assert.ThrowsAsync<FinanceException>(() => _svc.AddLoanAsync(new Loan
        {
            Lender = "Done", TotalPayable = Tk(600), InstallmentCount = 6, InstallmentsPaidBefore = 6, StartMonth = "2026-04"
        }));
        Assert.Equal("Err_PaidBefore", ex.Key);
    }

    [Fact]
    public async Task Account_and_card_can_be_edited()
    {
        var bank = new Account { Name = "Prime", OpeningBalance = Tk(100) };
        await _svc.AddAccountAsync(bank);
        bank.OpeningBalance = Tk(70_000);
        bank.Name = "Bank A";
        await _svc.UpdateAccountAsync(bank);

        var card = new CreditCard { Name = "SCB", CreditLimit = Tk(1_000), StatementDay = 1, DueDay = 15 };
        await _svc.AddCardAsync(card, DateTime.Today);
        card.CreditLimit = Tk(300_000);
        card.DueDay = 20;
        await _svc.UpdateCardAsync(card);

        var s = await _svc.LoadAsync();
        Assert.Equal(Tk(70_000), s.Balance(s.Accounts.Single()));
        Assert.Equal(20, s.Cards.Single().DueDay);
    }
}
