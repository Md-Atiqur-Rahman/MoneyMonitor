using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DailyAccount.App.Localization;
using DailyAccount.App.Services;
using DailyAccount.Core;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;

namespace DailyAccount.App.ViewModels;

public sealed record ReportModel(
    string MonthTitle,
    string Income,
    string DuesPaid,
    string CashExpenses,
    string SavedSoFar,
    string DuesStill,
    string ProjectedSavings,
    string SpendingTotal,
    string SpendingSub,
    List<BarRow> Categories,
    List<BarRow> TopItems,
    List<BarRow> Trend)
{
    public bool NoSpending => Categories.Count == 0;
    public bool NoItems => TopItems.Count == 0;
}

public sealed partial class ReportsViewModel(FinanceService finance) : ViewModelBase
{
    private string _month = MonthKey.Of(DateTime.Today);

    [ObservableProperty] private ReportModel? _model;

    public override async Task LoadAsync()
    {
        var s = await finance.LoadAsync();
        var sum = s.Summary(_month);

        var spent = s.Transactions
            .Where(t => t.Type is TransactionType.Expense or TransactionType.CardPurchase && MonthKey.Contains(_month, t.Date))
            .ToList();

        // By top-level category: sub-categories (Fish, Meat…) add up into their parent (Bajar).
        int? Top(int? id) => s.Categories.FirstOrDefault(c => c.Id == id) is { } c ? c.ParentId ?? c.Id : null;
        var spending = spent
            .GroupBy(t => Top(t.CategoryId))
            .Select(g => (Id: g.Key, Name: Display.CategoryName(g.Key, s), Amount: g.Sum(t => t.Amount)))
            .OrderByDescending(x => x.Amount)
            .ToList();
        var maxSpend = spending.Count == 0 ? 1 : spending.Max(x => x.Amount);

        // Items (Rice, Chicken…) by total spent, case-insensitive.
        var items = spent.Where(t => t.ItemName is not null)
            .GroupBy(t => t.ItemName!.Trim().ToLowerInvariant())
            .Select(g => (Name: g.First().ItemName!, Amount: g.Sum(t => t.Amount)))
            .OrderByDescending(x => x.Amount)
            .Take(10)
            .ToList();
        var maxItem = items.Count == 0 ? 1 : items.Max(x => x.Amount);

        // Savings trend: the selected month and the five before it.
        var months = Enumerable.Range(0, 6).Select(i => MonthKey.Add(_month, i - 5)).ToList();
        var savings = months.Select(m => (Month: m, Value: s.Summary(m).ProjectedSavings)).ToList();
        var maxAbs = Math.Max(1, savings.Max(x => Math.Abs(x.Value)));

        Model = new ReportModel(
            MonthTitle: Fmt.Month(_month),
            Income: "+" + Fmt.Money(sum.Income),
            DuesPaid: "−" + Fmt.Money(sum.DuePaid),
            CashExpenses: "−" + Fmt.Money(sum.CashExpenses),
            SavedSoFar: Fmt.Money(sum.Savings),
            DuesStill: "−" + Fmt.Money(sum.DueRemaining),
            ProjectedSavings: Fmt.Money(sum.ProjectedSavings),
            SpendingTotal: Fmt.Money(sum.TotalSpending),
            SpendingSub: Loc.F("SpendingSub", Fmt.Money(sum.CashExpenses), Fmt.Money(sum.CardSpending)),
            // Tap a category to see its groups (Bajar → Grocery, Meat, Fish…) and items (ADR 0017).
            Categories: spending.Select(x => new BarRow(x.Name, Fmt.Money(x.Amount), (double)x.Amount / maxSpend, Display.Positive,
                x.Id is { } id ? new AsyncRelayCommand(() => Ui.Go($"{AppShell.CategoryReport}?id={id}&month={_month}")) : null)).ToList(),
            TopItems: items.Select(x => new BarRow(x.Name, Fmt.Money(x.Amount), (double)x.Amount / maxItem, Color.FromArgb("#2B5BA8"))).ToList(),
            Trend: savings.Select(x => new BarRow(
                Fmt.ShortMonth(x.Month),
                Fmt.Money(x.Value),
                (double)Math.Abs(x.Value) / maxAbs,
                x.Value < 0 ? Display.Warn : Display.Positive)).ToList());
    }

    [RelayCommand]
    private Task OpenEntries() => Services.Ui.Go($"{AppShell.Entries}?month={_month}");

    [RelayCommand]
    private Task Previous()
    {
        _month = MonthKey.Add(_month, -1);
        return LoadAsync();
    }

    [RelayCommand]
    private Task Next()
    {
        _month = MonthKey.Add(_month, 1);
        return LoadAsync();
    }
}
