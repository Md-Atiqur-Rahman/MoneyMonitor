using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DailyAccount.App.Localization;
using DailyAccount.App.Services;
using DailyAccount.Core;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;
using DailyAccount.Core.Services;

namespace DailyAccount.App.ViewModels;

/// <summary>One line of the budget table: Estimate / Spent / Left, like the sheet.</summary>
/// <param name="Struck">"Won't pay this month": shown struck through (ADR 0031).</param>
public sealed record BudgetRow(string Name, string Subtitle, string Estimate, string Spent, string Left, Color LeftColor, ICommand Tap, ICommand? Due = null,
    bool Struck = false)
{
    public bool HasSubtitle => Subtitle.Length > 0;
    public bool HasDue => Due is not null;
}

/// <summary>Next month's plan, shown under the current month's budget (moved here from Home, ADR 0018).</summary>
public sealed record ForecastModel(
    string Title, string Payments, string BudgetLabel, string Budget, string Needed,
    string IncomeLabel, string Income, string Save, string Result, bool IsShort, bool IncomeFromThisMonth)
{
    public bool IsOk => !IsShort;
}

public sealed record BudgetModel(
    string MonthTitle,
    string IncomeLabel,
    string Income,
    string IncomeParts,
    string Payments,
    string BudgetItems,
    string Planned,
    string Spent,
    string Left,
    string Save,
    bool SaveIsNegative,
    string Banner,
    List<BudgetRow> DueRows,
    List<BudgetRow> ItemRows,
    ForecastModel? Forecast)
{
    public bool HasForecast => Forecast is not null;
    public bool HasIncomeParts => IncomeParts.Length > 0;
    public bool SaveIsOk => !SaveIsNegative;
    public bool HasDues => DueRows.Count > 0;
    public bool IsEmpty => ItemRows.Count == 0;
}

/// <summary>The monthly plan from the user's sheet (ADR 0011).</summary>
public sealed partial class BudgetViewModel(FinanceService finance, AppSettings settings, MonthState months) : ViewModelBase, IQueryAttributable
{
    /// <summary>The month shown, shared by every page (ADR 0029).</summary>
    private string SelectedMonth { get => months.Month; set => months.Month = value; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        // From Home of an earlier month (ADR 0028).
        if (query.TryGetValue("month", out var m) && m?.ToString() is { Length: 7 } month) SelectedMonth = month;
        query.Clear();
    }
    private FinanceSnapshot? _snapshot;

    [ObservableProperty] private BudgetModel? _model;

    public override async Task LoadAsync()
    {
        var today = DateTime.Today;
        await finance.GenerateDuesAsync(today);
        var current = MonthKey.Of(today);
        await finance.ApplyDefaultBudgetAsync(current); // one time only (ADR 0020)
        // Every month from the start month to the viewed one has a budget (ADR 0025).
        var start = settings.StartMonth ?? current;
        if (string.CompareOrdinal(SelectedMonth, start) >= 0)
            await finance.FillBudgetMonthsAsync(start, string.CompareOrdinal(SelectedMonth, current) > 0 ? SelectedMonth : current);

        var s = _snapshot = await finance.LoadAsync();
        var plan = s.Plan(SelectedMonth, settings.ExpectedIncome);

        Model = new BudgetModel(
            MonthTitle: Fmt.Month(SelectedMonth),
            IncomeLabel: Loc.T(plan.IncomeIsExpected ? "Budget_IncomeExpected" : "Budget_Income"),
            Income: Fmt.Money(plan.Income),
            // "income ৳1,00,000 + borrowed ৳10,000" when something was borrowed (ADR 0033).
            IncomeParts: plan.Borrowed > 0 ? Loc.F("Budget_IncomeParts", Fmt.Money(plan.Earned), Fmt.Money(plan.Borrowed)) : "",
            Payments: Fmt.Money(plan.DueLines.Sum(l => l.Estimate)),
            BudgetItems: Fmt.Money(plan.BudgetEstimate),
            Planned: Fmt.Money(plan.TotalEstimate),
            Spent: Fmt.Money(plan.TotalSpent),
            Left: Fmt.Money(plan.TotalLeft),
            Save: Fmt.Money(plan.Save),
            SaveIsNegative: plan.Save < 0,
            Banner: plan.Save < 0 ? Loc.F("Budget_NeedBorrow", Fmt.Money(plan.NeedToBorrow)) : Loc.F("Budget_Ok", Fmt.Money(plan.Save)),
            DueRows: plan.DueLines.Select(l => DueRow(l, s)).ToList(),
            ItemRows: plan.Lines
                .OrderByDescending(l => l.InBudget)
                .ThenBy(l => s.Categories.FirstOrDefault(c => c.Id == l.CategoryId)?.SortOrder ?? int.MaxValue)
                .Select(l => ItemRow(l, s)).ToList(),
            SelectedMonth == MonthKey.Of(today) ? BuildForecast(s, today) : null);
    }

    /// <summary>Same formula as the sheet: next month's payments + budget vs income (ADR 0015).</summary>
    private ForecastModel BuildForecast(FinanceSnapshot s, DateTime today)
    {
        var (next, copied, incomeFromThisMonth) = s.NextMonthPlan(today, settings.ExpectedIncome);
        return new ForecastModel(
            Loc.F("Fc_Title", Fmt.MonthName(next.Month)),
            Fmt.Money(next.DueLines.Sum(l => l.Estimate)),
            Loc.T(copied ? "Fc_BudgetCopied" : "Fc_Budget"),
            Fmt.Money(next.Lines.Sum(l => l.Estimate)),
            Fmt.Money(next.TotalEstimate),
            Loc.T(incomeFromThisMonth ? "Fc_IncomeThisMonth" : "Fc_Income"),
            Fmt.Money(next.Income),
            Fmt.Money(next.Save),
            next.Save < 0 ? Loc.F("Budget_NeedBorrow", Fmt.Money(next.NeedToBorrow)) : Loc.F("Budget_Ok", Fmt.Money(next.Save)),
            next.Save < 0,
            incomeFromThisMonth);
    }

    private BudgetRow DueRow(BudgetDueLine line, FinanceSnapshot s)
    {
        var dues = s.Dues.Where(d => line.DueIds.Contains(d.Id)).ToList();
        var payable = string.CompareOrdinal(SelectedMonth, MonthKey.Of(DateTime.Today)) <= 0 && line.Left > 0;
        string name, subtitle = "";
        ICommand tap;

        if (line.Source == DueSource.Card && s.Cards.FirstOrDefault(c => c.Id == line.SourceId) is { } card)
        {
            name = Loc.F("Due_CardPayment", card.Name);
            subtitle = Loc.F("Due_CardBreakdown",
                Fmt.Money(dues.Where(d => d.SourceType == DueSource.Loan).Sum(d => d.Amount)),
                Fmt.Money(dues.Where(d => d.SourceType == DueSource.Card).Sum(d => d.Amount)));
            tap = new AsyncRelayCommand(() => payable ? Ui.Go($"{AppShell.Pay}?cardId={card.Id}&month={SelectedMonth}") : Task.CompletedTask);
        }
        else
        {
            var due = dues.First();
            name = Display.DueTitle(due, s);
            tap = new AsyncRelayCommand(() => payable ? Ui.Go($"{AppShell.Pay}?dueId={due.Id}") : Task.CompletedTask);
        }

        // Paid: ✓ like the Dues page's "Fully paid" (ADR 0046).
        return new BudgetRow(name, subtitle, Fmt.Money(line.Estimate), Fmt.Money(line.Paid),
            line.Left > 0 ? Fmt.Money(line.Left) : "✓",
            line.Left > 0 ? Display.Warn : Display.Positive, tap);
    }

    private BudgetRow ItemRow(BudgetLine line, FinanceSnapshot s)
    {
        var notes = new List<string>();
        if (line.Skipped) notes.Add(Loc.F("Budget_Skipped", Fmt.Money(line.Planned)));
        notes.Add(Loc.T(!line.InBudget ? "Budget_NotInBudget" : line.OnlyThisMonth ? "Budget_TagOnce" : "Budget_TagEvery"));
        if (line.OnCard > 0) notes.Add(Loc.F("Budget_OnCard", Fmt.Money(line.OnCard)));
        // A budget item fully paid shows ✓ like the Dues page (ADR 0046); overspending is said in words.
        var paid = line.InBudget && !line.Skipped && line.Estimate > 0 && line.Left <= 0;
        if (paid && line.Left < 0) notes.Add(Loc.F("Dues_Over", Fmt.Money(-line.Left)));

        return new BudgetRow(
            Display.CategoryName(line.CategoryId == 0 ? null : line.CategoryId, s),
            string.Join(" · ", notes),
            Fmt.Money(line.Estimate), Fmt.Money(line.Spent), paid ? "✓" : Fmt.Money(line.Left),
            paid ? Display.Positive : line.Left < 0 ? Display.Warn : Display.Negative,
            new AsyncRelayCommand(() => EditLineAsync(line)),
            // Due → the Dues page, scrolled to this item's row (current month only; ADR 0024 note).
            line.InBudget && line.Left > 0 && string.CompareOrdinal(SelectedMonth, MonthKey.Of(DateTime.Today)) <= 0
                ? new AsyncRelayCommand(() => Shell.Current.GoToAsync($"//dues?focus={line.CategoryId}&month={SelectedMonth}"))
                : null,
            line.Skipped);
    }

    /// <summary>Change amount, switch "every month" / "only this month", or remove (ADR 0019).</summary>
    private async Task EditLineAsync(BudgetLine line)
    {
        if (line.CategoryId == 0 || _snapshot is null) return;
        var name = Display.CategoryName(line.CategoryId, _snapshot);

        if (!line.InBudget)
        {
            // Spending without a budget line: give it an estimate.
            await AskEstimateAndRepeatAsync(line.CategoryId, name, null);
            return;
        }

        var change = Loc.T("Budget_ChangeEstimate");
        var toggle = Loc.T(line.OnlyThisMonth ? "Budget_MakeEvery" : "Budget_MakeOnce");
        var skip = Loc.T(line.Skipped ? "Budget_Unskip" : "Budget_Skip");
        var remove = Loc.T("Budget_RemoveFuture");
        var choice = await Ui.Choose(name, skip, change, toggle, remove);

        if (choice == skip)
        {
            // Strike through for this month only, or undo (ADR 0031).
            if (await Ui.Try(() => finance.SetBudgetSkippedAsync(SelectedMonth, line.CategoryId, !line.Skipped))) await LoadAsync();
        }
        else if (choice == remove)
        {
            if (await Ui.Confirm(Loc.F("Budget_ConfirmDelete", name))
                && await Ui.Try(() => finance.RemoveBudgetAsync(SelectedMonth, line.CategoryId)))
                await LoadAsync();
        }
        else if (choice == toggle)
        {
            if (await Ui.Try(() => finance.SetBudgetAsync(SelectedMonth, line.CategoryId, line.Estimate, !line.OnlyThisMonth))) await LoadAsync();
        }
        else if (choice == change)
        {
            var estimate = await Ui.PromptMoney(Loc.F("Budget_EstimateFor", name), line.Estimate);
            if (estimate is not null && await Ui.Try(() => finance.SetBudgetAsync(SelectedMonth, line.CategoryId, estimate.Value)))
                await LoadAsync();
        }
    }

    /// <summary>Asks the amount, then "every month" or "only this month", and saves.</summary>
    private async Task AskEstimateAndRepeatAsync(int categoryId, string name, long? current)
    {
        var estimate = await Ui.PromptMoney(Loc.F("Budget_EstimateFor", name), current);
        if (estimate is null) return;

        var every = Loc.T("Budget_EveryMonth");
        var repeat = await Ui.Choose(Loc.F("Budget_RepeatQuestion", name), every, Loc.T("Budget_OnlyThisMonth"));
        if (repeat is null) return;

        if (await Ui.Try(() => finance.SetBudgetAsync(SelectedMonth, categoryId, estimate.Value, onlyThisMonth: repeat != every)))
            await LoadAsync();
    }

    [RelayCommand]
    private async Task AddItem()
    {
        if (_snapshot is null) return;
        var used = _snapshot.Budget.Where(b => b.Month == SelectedMonth).Select(b => b.CategoryId).ToHashSet();
        // Every expense category that is not in this month's spending plan yet, in category order (ADR 0027).
        var available = _snapshot.TopCategories(CategoryKind.Expense).Where(c => !used.Contains(c.Id))
            .ToDictionary(c => Display.CategoryName(c.Id, _snapshot), c => c.Id);

        // "+ New category…" first: the list of existing categories is long and would push it off-screen.
        var choice = await Ui.Choose(Loc.T("Budget_ChooseCategory"), [Loc.T("Budget_NewCategory"), .. available.Keys]);
        if (choice is null) return;

        int categoryId;
        var name = choice;
        if (available.TryGetValue(choice, out var existing))
            categoryId = existing;
        else
        {
            var newName = await Ui.PromptText(Loc.T("NewCategory_Prompt"));
            if (newName is null) return;

            // No duplicates (ADR 0027): an existing category is used, or refused if already planned.
            if (_snapshot.FindCategory(newName, CategoryKind.Expense) is { } existingCategory)
            {
                if (existingCategory.ParentId is not null)
                    await Ui.Alert(Display.CategoryExists(existingCategory, _snapshot));
                else if (used.Contains(existingCategory.Id))
                    await Ui.Alert(Loc.F("Budget_AlreadyInPlan", Display.CategoryName(existingCategory.Id, _snapshot)));
                else
                    await AskEstimateAndRepeatAsync(existingCategory.Id, Display.CategoryName(existingCategory.Id, _snapshot), null);
                return;
            }

            Category? created = null;
            if (!await Ui.Try(async () => created = await finance.AddCategoryAsync(newName, CategoryKind.Expense, null))) return;
            categoryId = created!.Id;
            name = created.Name;
        }

        await AskEstimateAndRepeatAsync(categoryId, name, null);
    }

    [RelayCommand]
    private Task Previous()
    {
        SelectedMonth = MonthKey.Add(SelectedMonth, -1);
        return LoadAsync();
    }

    [RelayCommand]
    private Task Next()
    {
        SelectedMonth = MonthKey.Add(SelectedMonth, 1);
        return LoadAsync();
    }
}
