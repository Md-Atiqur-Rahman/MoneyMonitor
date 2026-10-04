using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DailyAccount.App.Localization;
using DailyAccount.App.Services;
using DailyAccount.Core;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;

namespace DailyAccount.App.ViewModels;

/// <summary>One credit card's payment for this month with a coloured status chip.</summary>
public sealed record CardStatusRow(string Name, string Amount, string Status, Color StatusBg, Color StatusFg, string Detail)
{
    /// <summary>Opens Liabilities, whose Cards section is shown first.</summary>
    public System.Windows.Input.ICommand Open { get; } = new AsyncRelayCommand(() => Shell.Current.GoToAsync("//liabilities"));
}

public sealed record DashboardModel(
    string MonthTitle,
    bool IsCurrentMonth,
    bool CanGoNext,
    string BankTitle,
    string SavingsTitle,
    string BudgetTitle,
    string PrevCardLabel,
    string PrevCarriedLabel,
    string PrevSavedLabel,
    string NextSaveLabel,
    string Savings,
    bool SavingsIsNegative,
    string SavingsSub,
    string NextTitle,
    string NextPayments,
    string NextSave,
    bool NextIsShort,
    string NextSub,
    string Budget,
    string BudgetSub,
    string PrevTitle,
    string PrevSaved,
    bool PrevSavedNegative,
    string PrevSub,
    string PrevCard,
    string PrevCarried,
    bool HasPrev,
    string Expenses,
    string ExpensesSub,
    string Due,
    string DueSub,
    List<CardStatusRow> Cards,
    string BankTotal,
    string BankSub,
    bool HasNoAccounts)
{
    public bool HasCards => Cards.Count > 0;
}

/// <summary>
/// Home: five numbers, each opening its details page (ADR 0018) — savings, expenses, dues,
/// credit-card payment status and the money in all accounts. Income is deliberately not shown.
/// </summary>
public sealed partial class DashboardViewModel(FinanceService finance, AppSettings settings, MonthState months) : ViewModelBase
{
    /// <summary>The month shown (ADR 0028), shared by every page (ADR 0029).</summary>
    private string SelectedMonth { get => months.Month; set => months.Month = value; }

    private static readonly Color GreenBg = Color.FromArgb("#E3F0E8"), GreenFg = Color.FromArgb("#14503A");
    private static readonly Color OrangeBg = Color.FromArgb("#FBE9E1"), OrangeFg = Color.FromArgb("#8F3313");
    private static readonly Color RedBg = Color.FromArgb("#B4441A"), RedFg = Colors.White;
    private static readonly Color GreyBg = Color.FromArgb("#EEF0EC"), GreyFg = Color.FromArgb("#3F4943");

    [ObservableProperty]
    private DashboardModel? _model;

    public override async Task LoadAsync()
    {
        var today = DateTime.Today;
        var current = MonthKey.Of(today);
        await finance.GenerateDuesAsync(today);
        await finance.ApplyDefaultBudgetAsync(current); // one time only (ADR 0020)
        await finance.FillBudgetMonthsAsync(settings.StartMonth ?? current, current); // ADR 0025
        var s = await finance.LoadAsync();

        // An earlier month is shown as it stood at its end (ADR 0028): balance on its last day,
        // its own plan, and the forecast it gave for the month after it.
        // Home shows no future month: coming here from a later month (Budget/Dues) means this month everywhere.
        if (string.CompareOrdinal(SelectedMonth, current) > 0) SelectedMonth = current;
        var month = SelectedMonth;
        var isCurrent = month == current;
        var lastDay = MonthKey.FirstDay(month).AddMonths(1).AddDays(-1);
        var asOf = isCurrent ? today : lastDay;

        var plan = s.Plan(month, settings.ExpectedIncome);
        var prev = s.Carry(MonthKey.Add(month, -1)); // what the month before left for this one (ADR 0025)
        var (next, _, _) = s.NextMonthPlan(asOf, settings.ExpectedIncome);
        var sum = s.Summary(month);

        // Due = unpaid budget items + unpaid non-card dues; the same Core calculation as the Dues page (ADR 0024).
        var openItems = plan.Lines.Count(l => l.InBudget && l.Left > 0);
        var otherDue = s.NonCardDuesUpTo(month).Sum(d => d.Remaining);
        var dueSub = openItems == 0 && otherDue == 0
            ? Loc.T(plan.Lines.Any(l => l.InBudget) ? "Home_DueAllPaid" : "Home_DueNone")
            : Loc.F("Home_DueBudget", Fmt.Number(openItems))
              + (otherDue > 0 ? " · " + Loc.F("Home_DueLoans", Fmt.Money(otherDue)) : "");

        var accounts = s.Accounts.Where(a => a.IsActive).ToList();

        var monthName = Fmt.MonthName(month);
        Model = new DashboardModel(
            MonthTitle: Fmt.Month(month),
            IsCurrentMonth: isCurrent,
            CanGoNext: !isCurrent,
            BankTitle: isCurrent ? Loc.T("Home_Accounts") : Loc.F("Home_AccountsOn", Fmt.Date(lastDay)),
            SavingsTitle: plan.Save < 0
                ? (isCurrent ? Loc.T("Home_Short") : Loc.F("Home_ShortIn", monthName))
                : (isCurrent ? Loc.T("Home_Savings") : Loc.F("Home_SavingsIn", monthName)),
            PrevSavedLabel: Loc.T(prev.Saved < 0 ? "Home_PrevShort" : "Home_PrevSaved"),
            NextSaveLabel: Loc.T(next.Save < 0 ? "Home_AssumedShort" : "Home_AssumedSavings"),
            BudgetTitle: isCurrent ? Loc.T("Home_Budget") : Loc.F("Home_BudgetIn", monthName),
            PrevCardLabel: Loc.F("Home_PrevCardIn", monthName),
            PrevCarriedLabel: Loc.F("Home_PrevCarriedIn", monthName),
            Savings: Fmt.Money(Math.Abs(plan.Save)), // a shortfall is named so, without a minus (ADR 0049)
            SavingsIsNegative: plan.Save < 0,
            SavingsSub: plan.Save < 0
                ? Loc.F("Budget_NeedBorrow", Fmt.Money(plan.NeedToBorrow))
                : Loc.T("Home_SavingsSub"),
            NextTitle: Loc.F("Home_NextMonth", Fmt.MonthName(next.Month)),
            NextPayments: Fmt.Money(next.DueLines.Sum(l => l.Estimate)),
            NextSave: Fmt.Money(Math.Abs(next.Save)),
            NextIsShort: next.Save < 0,
            NextSub: next.Save < 0
                ? Loc.F("Budget_NeedBorrow", Fmt.Money(next.NeedToBorrow))
                : Loc.F("Home_NextSub", Fmt.Money(next.Lines.Sum(l => l.Estimate)), Fmt.Money(next.Income)),
            Budget: Fmt.Money(plan.BudgetEstimate),
            BudgetSub: Loc.F("Home_BudgetSub", Fmt.Money(plan.BudgetSpent), Fmt.Money(plan.BudgetLeft)),
            PrevTitle: Loc.F("Home_PrevTitle", Fmt.MonthName(prev.Month)),
            PrevSaved: Fmt.Money(Math.Abs(prev.Saved)),
            PrevSavedNegative: prev.Saved < 0,
            PrevSub: Loc.F("Home_PrevSub", Fmt.Money(prev.Income), Fmt.Money(prev.CashExpenses), Fmt.Money(prev.BillsPaid)),
            PrevCard: Fmt.Money(prev.CardToNextBill),
            PrevCarried: Fmt.Money(prev.CarriedDues),
            // Shown from the start month on, or whenever last month has any activity.
            HasPrev: string.CompareOrdinal(prev.Month, settings.StartMonth ?? current) >= 0
                     || prev.Income + prev.CashExpenses + prev.BillsPaid + prev.CardToNextBill + prev.CarriedDues > 0,
            Expenses: Fmt.Money(sum.TotalSpending),
            ExpensesSub: Loc.F("CatReport_CashCard", Fmt.Money(sum.CashExpenses), Fmt.Money(sum.CardSpending)),
            Due: Fmt.Money(s.MonthDue(month, settings.ExpectedIncome)),
            DueSub: dueSub,
            Cards: s.Cards.Select(c => CardStatus(c, s, today, month)).ToList(),
            BankTotal: Fmt.Money(s.TotalBalanceOn(asOf)),
            BankSub: string.Join(" · ", accounts.Select(a => $"{a.Name} {Fmt.Money(s.BalanceOn(a, asOf))}")),
            HasNoAccounts: accounts.Count == 0);
    }

    /// <summary>Paid / Partial / Pending (due date) / Overdue / No bill, for this month's card payment.</summary>
    private static CardStatusRow CardStatus(CreditCard card, FinanceSnapshot s, DateTime today, string month)
    {
        var thisMonth = s.CardDues(card.Id).Where(d => d.DueMonth == month).ToList();
        var unpaid = s.CardBillDues(card.Id, month);
        var remaining = unpaid.Sum(d => d.Remaining);
        var paid = thisMonth.Sum(d => d.PaidAmount) + unpaid.Where(d => d.DueMonth != month).Sum(d => d.PaidAmount);
        var breakdown = Loc.F("Due_CardBreakdown",
            Fmt.Money(thisMonth.Where(d => d.SourceType == DueSource.Loan).Sum(d => d.Amount)),
            Fmt.Money(thisMonth.Where(d => d.SourceType == DueSource.Card).Sum(d => d.Amount)));

        if (thisMonth.Count == 0 && unpaid.Count == 0)
            return new CardStatusRow(card.Name, Fmt.Money(0), Loc.T("Home_CardNoBill"), GreyBg, GreyFg, "");
        if (unpaid.Count == 0)
            return new CardStatusRow(card.Name, Fmt.Money(thisMonth.Sum(d => d.Amount)), "✓ " + Loc.T("Status_Paid"), GreenBg, GreenFg, breakdown);

        var dueDate = unpaid.Min(d => d.DueDate) ?? today;
        if (dueDate < today)
            return new CardStatusRow(card.Name, Fmt.Money(remaining), Loc.F("Home_CardOverdue", Fmt.DayMonth(dueDate)), RedBg, RedFg, breakdown);
        if (paid > 0)
            return new CardStatusRow(card.Name, Fmt.Money(remaining), Loc.F("Home_CardPartial", Fmt.Money(paid), Fmt.DayMonth(dueDate)), OrangeBg, OrangeFg, breakdown);
        return new CardStatusRow(card.Name, Fmt.Money(remaining), Loc.F("Home_CardPending", Fmt.DayMonth(dueDate)), OrangeBg, OrangeFg, breakdown);
    }

    // Every card opens its page for the month shown (ADR 0028).
    private bool IsPast => months.IsPast;

    /// <summary>In an earlier month a new entry is dated on that month's last day (it can be changed).</summary>
    [RelayCommand]
    private Task AddTransaction() => Ui.Go(IsPast
        ? $"{AppShell.AddTransaction}?date={MonthKey.FirstDay(SelectedMonth).AddMonths(1).AddDays(-1):yyyy-MM-dd}"
        : AppShell.AddTransaction);

    [RelayCommand] private Task AddAccount() => Ui.Go(AppShell.AddAccount);
    [RelayCommand] private Task OpenSettings() => Ui.Go(AppShell.Settings);
    [RelayCommand] private Task OpenReports() => Ui.Go($"{AppShell.Reports}?month={SelectedMonth}");
    [RelayCommand] private Task OpenBudget() => Shell.Current.GoToAsync($"//budget?month={SelectedMonth}");
    [RelayCommand] private Task OpenDues() => Shell.Current.GoToAsync($"//dues?month={SelectedMonth}");
    [RelayCommand] private Task OpenPrevDues() => Shell.Current.GoToAsync($"//dues?month={MonthKey.Add(SelectedMonth, -1)}");
    [RelayCommand] private Task OpenAccounts() => Shell.Current.GoToAsync("//accounts");

    [RelayCommand]
    private Task PreviousMonth()
    {
        SelectedMonth = MonthKey.Add(SelectedMonth, -1);
        return LoadAsync();
    }

    [RelayCommand]
    private Task NextMonth()
    {
        if (!IsPast) return Task.CompletedTask; // no future months on Home
        SelectedMonth = MonthKey.Add(SelectedMonth, 1);
        return LoadAsync();
    }

    /// <summary>Tap on the month: pick any month with data, newest first (e.g. last January).</summary>
    [RelayCommand]
    private async Task ChooseMonth()
    {
        var s = await finance.LoadAsync();
        var months = s.MonthsUpTo(MonthKey.Of(DateTime.Today), settings.StartMonth);
        var labels = months.ToDictionary(Fmt.Month, m => m);
        var choice = await Ui.Choose(Loc.T("Home_ChooseMonth"), [.. labels.Keys]);
        if (choice is null || !labels.TryGetValue(choice, out var month)) return;
        SelectedMonth = month;
        await LoadAsync();
    }

    [RelayCommand]
    private Task ThisMonth()
    {
        months.Reset();
        return LoadAsync();
    }
}
