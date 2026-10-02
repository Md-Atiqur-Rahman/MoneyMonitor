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
public sealed partial class DashboardViewModel(FinanceService finance, AppSettings settings) : ViewModelBase
{
    private static readonly Color GreenBg = Color.FromArgb("#E3F0E8"), GreenFg = Color.FromArgb("#14503A");
    private static readonly Color OrangeBg = Color.FromArgb("#FBE9E1"), OrangeFg = Color.FromArgb("#8F3313");
    private static readonly Color RedBg = Color.FromArgb("#B4441A"), RedFg = Colors.White;
    private static readonly Color GreyBg = Color.FromArgb("#EEF0EC"), GreyFg = Color.FromArgb("#3F4943");

    [ObservableProperty]
    private DashboardModel? _model;

    public override async Task LoadAsync()
    {
        var today = DateTime.Today;
        var month = MonthKey.Of(today);
        await finance.GenerateDuesAsync(today);
        await finance.EnsureBudgetAsync(month);
        await finance.ApplyDefaultBudgetAsync(month); // one time only (ADR 0020)
        var s = await finance.LoadAsync();

        var plan = s.Plan(month, settings.ExpectedIncome);
        var (next, _, _) = s.NextMonthPlan(today, settings.ExpectedIncome);
        var sum = s.Summary(month);

        // Due: everything payable up to this month that is NOT on a credit card (cards have their own row).
        var cardDueIds = s.Cards.SelectMany(c => s.CardDues(c.Id)).Select(d => d.Id).ToHashSet();
        var otherDues = s.Dues.Where(d => !cardDueIds.Contains(d.Id) && d.DueMonth.Length > 0
                                          && string.CompareOrdinal(d.DueMonth, month) <= 0).ToList();
        var unpaid = otherDues.Where(d => d.Status != DueStatus.Paid).ToList();

        // Due = what is still unpaid in the budget, like the sheet's "Due" column (estimate − paid per
        // item; an overspent item counts as 0), plus loans/personal dues that are not on a card.
        // The card payment has its own row, so it is not included (ADR 0018 note).
        var openItems = plan.Lines.Where(l => l.InBudget && l.Left > 0).ToList();
        var budgetDue = openItems.Sum(l => l.Left);
        var otherDue = unpaid.Sum(d => d.Remaining);
        var dueSub = openItems.Count == 0 && otherDue == 0
            ? Loc.T(plan.Lines.Any(l => l.InBudget) ? "Home_DueAllPaid" : "Home_DueNone")
            : Loc.F("Home_DueBudget", Fmt.Number(openItems.Count))
              + (otherDue > 0 ? " · " + Loc.F("Home_DueLoans", Fmt.Money(otherDue)) : "");

        var accounts = s.Accounts.Where(a => a.IsActive).ToList();

        Model = new DashboardModel(
            MonthTitle: Fmt.Month(month),
            Savings: Fmt.Money(plan.Save),
            SavingsIsNegative: plan.Save < 0,
            SavingsSub: plan.Save < 0
                ? Loc.F("Budget_NeedBorrow", Fmt.Money(plan.NeedToBorrow))
                : Loc.T("Home_SavingsSub"),
            NextTitle: Loc.F("Home_NextMonth", Fmt.MonthName(next.Month)),
            NextPayments: Fmt.Money(next.DueLines.Sum(l => l.Estimate)),
            NextSave: Fmt.Money(next.Save),
            NextIsShort: next.Save < 0,
            NextSub: next.Save < 0
                ? Loc.F("Budget_NeedBorrow", Fmt.Money(next.NeedToBorrow))
                : Loc.F("Home_NextSub", Fmt.Money(next.Lines.Sum(l => l.Estimate)), Fmt.Money(next.Income)),
            Budget: Fmt.Money(plan.BudgetEstimate),
            BudgetSub: Loc.F("Home_BudgetSub", Fmt.Money(plan.BudgetSpent), Fmt.Money(plan.BudgetLeft)),
            Expenses: Fmt.Money(sum.TotalSpending),
            ExpensesSub: Loc.F("CatReport_CashCard", Fmt.Money(sum.CashExpenses), Fmt.Money(sum.CardSpending)),
            Due: Fmt.Money(budgetDue + otherDue),
            DueSub: dueSub,
            Cards: s.Cards.Select(c => CardStatus(c, s, today, month)).ToList(),
            BankTotal: Fmt.Money(s.TotalBalance),
            BankSub: string.Join(" · ", accounts.Select(a => $"{a.Name} {Fmt.Money(s.Balance(a))}")),
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

    [RelayCommand] private Task AddTransaction() => Ui.Go(AppShell.AddTransaction);
    [RelayCommand] private Task AddAccount() => Ui.Go(AppShell.AddAccount);
    [RelayCommand] private Task OpenSettings() => Ui.Go(AppShell.Settings);
    [RelayCommand] private Task OpenReports() => Ui.Go(AppShell.Reports);
    [RelayCommand] private Task OpenBudget() => Shell.Current.GoToAsync("//budget");
    [RelayCommand] private Task OpenAccounts() => Shell.Current.GoToAsync("//accounts");
}
