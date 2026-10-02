using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DailyAccount.App.Localization;
using DailyAccount.Core;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;
using DailyAccount.Core.Services;

namespace DailyAccount.App.ViewModels;

public sealed record ItemRow(string Title, string Subtitle, string Amount);

public sealed record GroupRow(string Name, string Total, string CashCard, double Progress, List<ItemRow> Items);

public sealed record CategoryReportModel(
    string Title,
    string MonthTitle,
    string Total,
    string CashCard,
    string BudgetText,
    List<GroupRow> Groups)
{
    public bool HasBudget => BudgetText.Length > 0;
    public bool IsEmpty => Groups.Count == 0;
}

/// <summary>One category's spending in a month, group by group with every item (ADR 0017).</summary>
public sealed partial class CategoryReportViewModel(FinanceService finance) : ViewModelBase, IQueryAttributable
{
    private int _categoryId;
    private string _month = MonthKey.Of(DateTime.Today);

    [ObservableProperty] private CategoryReportModel? _model;
    [ObservableProperty] private string _title = "";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var v) && int.TryParse(v?.ToString(), out var id)) _categoryId = id;
        if (query.TryGetValue("month", out var m) && m?.ToString() is { Length: 7 } month) _month = month;
    }

    public override async Task LoadAsync()
    {
        var s = await finance.LoadAsync();
        var report = ReportService.Breakdown(_categoryId, _month, s.Categories, s.Transactions);
        var max = Math.Max(1, report.Groups.Select(g => g.Total).DefaultIfEmpty(0).Max());

        // Budget line for this category, if any (Spent counts cash/bank only, as in the Budget tab).
        var budgetText = "";
        if (s.Plan(_month, 0).Lines.FirstOrDefault(l => l.CategoryId == _categoryId && l.InBudget) is { } line)
            budgetText = Loc.F("CatReport_Budget", Fmt.Money(line.Estimate), Fmt.Money(line.Spent), Fmt.Money(line.Left));

        Title = Display.CategoryName(_categoryId, s);
        Model = new CategoryReportModel(
            Title,
            Fmt.Month(_month),
            Fmt.Money(report.Total),
            Loc.F("CatReport_CashCard", Fmt.Money(report.Cash), Fmt.Money(report.Card)),
            budgetText,
            report.Groups.Select(g => new GroupRow(
                Display.CategoryName(g.CategoryId, s),
                Fmt.Money(g.Total),
                Loc.F("CatReport_CashCard", Fmt.Money(g.Cash), Fmt.Money(g.Card)),
                (double)g.Total / max,
                g.Items.Select(t => Item(t, s)).ToList())).ToList());
    }

    private static ItemRow Item(Transaction t, FinanceSnapshot s)
    {
        var title = t.ItemName is null
            ? t.Note ?? Display.CategoryName(t.CategoryId, s)
            : t.Quantity is null ? t.ItemName : $"{t.ItemName} · {t.Quantity}";
        var paidWith = t.Type == TransactionType.CardPurchase
            ? s.Cards.FirstOrDefault(c => c.Id == t.CardId)?.Name ?? Loc.T("CatReport_OnCard")
            : Display.AccountName(t.AccountId, s);
        return new ItemRow(title, Fmt.DayMonth(t.Date) + " · " + paidWith, Fmt.Money(t.Amount));
    }

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
