using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DailyAccount.App.Localization;
using DailyAccount.App.Services;
using DailyAccount.Core;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;

namespace DailyAccount.App.ViewModels;

/// <summary>One budget item on the Dues page: what is left to pay, with a shortcut to record the expense.</summary>
public sealed record BudgetDueRow(string Name, string Subtitle, string Amount, Color AmountColor, string ActionText, ICommand? Action, bool Highlight = false)
{
    public bool HasAction => Action is not null;
}

public sealed record DuesModel(
    string MonthTitle,
    string DueLabel,
    string Due,
    string BudgetLine,
    double Progress,
    List<BudgetDueRow> NotPaid,
    List<DueRow> OtherDues,
    List<BudgetDueRow> FullyPaid,
    List<DueRow> CardThisMonth,
    List<DueRow> NoDate,
    List<DueRow> CardPaid,
    List<DueRow> NextMonth,
    string NextMonthTitle)
{
    public bool NothingDue => NotPaid.Count == 0 && OtherDues.Count == 0;
    public bool HasOtherDues => OtherDues.Count > 0;
    public bool HasFullyPaid => FullyPaid.Count > 0;
    public bool HasCardThisMonth => CardThisMonth.Count > 0;
    public bool HasNoDate => NoDate.Count > 0;
    public bool HasCardPaid => CardPaid.Count > 0;
    public bool HasNextMonth => NextMonth.Count > 0;
    public bool HasCardSection => HasCardThisMonth || HasNoDate || HasCardPaid || HasNextMonth;
}

/// <summary>
/// Dues page (ADR 0024): first the month's "Due" — the budget items not paid yet, the sheet's "Due"
/// column, same number as on Home — then card &amp; loan payments as a separate section, not in that total.
/// </summary>
public sealed partial class DuesViewModel(FinanceService finance, AppSettings settings) : ViewModelBase, IQueryAttributable
{
    /// <summary>Category whose row should be highlighted and scrolled to (from Budget → Due), used once.</summary>
    private int? _focus;

    /// <summary>The month shown (ADR 0025): current by default, ‹ › or ?month= to change it.</summary>
    private string _month = MonthKey.Of(DateTime.Today);

    [ObservableProperty]
    private DuesModel? _model;

    /// <summary>Set after a load when a row was asked for; the page scrolls to it.</summary>
    public BudgetDueRow? FocusRow { get; private set; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("focus", out var f) && int.TryParse(f?.ToString(), out var id))
        {
            _focus = id;
            _month = MonthKey.Of(DateTime.Today); // unless ?month= below says which month (Budget → Due)
        }
        if (query.TryGetValue("month", out var m) && m?.ToString() is { Length: 7 } month) _month = month;
        query.Clear(); // don't re-apply on the next visit
    }

    public override async Task LoadAsync()
    {
        var today = DateTime.Today;
        var current = MonthKey.Of(today);
        var month = _month;
        var next = MonthKey.Add(month, 1);
        await finance.GenerateDuesAsync(today);
        await finance.ApplyDefaultBudgetAsync(current);
        var start = settings.StartMonth ?? current;
        if (string.CompareOrdinal(month, start) >= 0)
            await finance.FillBudgetMonthsAsync(start, string.CompareOrdinal(month, current) > 0 ? month : current);

        // + Expense: this month → today; a past month → its last day; a future month → none.
        var isPast = string.CompareOrdinal(month, current) < 0;
        var isFuture = string.CompareOrdinal(month, current) > 0;
        var dateParam = isPast ? "&date=" + MonthKey.DayIn(month, 31).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "";
        var s = await finance.LoadAsync();

        var plan = s.Plan(month, settings.ExpectedIncome);
        var items = plan.Lines.Where(l => l.InBudget)
            .OrderBy(l => s.Categories.FirstOrDefault(c => c.Id == l.CategoryId)?.SortOrder ?? int.MaxValue)
            .ToList();

        var focus = _focus;
        _focus = null;
        BudgetDueRow Row(Core.Services.BudgetLine l, bool open) => new(
            Display.CategoryName(l.CategoryId, s),
            Loc.F("Dues_ItemSub", Fmt.Money(l.Estimate), Fmt.Money(l.Spent))
                + (l.Left < 0 ? " · " + Loc.F("Dues_Over", Fmt.Money(-l.Left)) : ""),
            open ? Fmt.Money(l.Left) : "✓",
            open ? Display.Warn : Display.Positive,
            Loc.T("Dues_AddExpense"),
            open && !isFuture
                ? new AsyncRelayCommand(() => Ui.Go($"{AppShell.AddTransaction}?type=expense&categoryId={l.CategoryId}&amount={l.Left}{dateParam}"))
                : null,
            l.CategoryId == focus);

        // Card & loan payments (not in the Due total, like the sheet).
        var cardDueIds = s.Cards.SelectMany(c => s.CardDues(c.Id)).Select(d => d.Id).ToHashSet();
        List<DueRow> CardRows(Func<Due, bool> filter, bool canPay = true) =>
            Display.DueRows(s.Dues.Where(d => cardDueIds.Contains(d.Id) && filter(d)), s, canPay);

        Model = new DuesModel(
            MonthTitle: Fmt.Month(month),
            DueLabel: month == current ? Loc.T("Dues_DueTotal") : Loc.F("Dues_DueIn", Fmt.MonthName(month)),
            Due: Fmt.Money(s.MonthDue(month, settings.ExpectedIncome)),
            BudgetLine: Loc.F("Dues_BudgetLine", Fmt.Money(plan.BudgetEstimate), Fmt.Money(plan.BudgetSpent)),
            Progress: plan.BudgetEstimate == 0 ? 0 : Math.Min(1, (double)plan.BudgetSpent / plan.BudgetEstimate),
            NotPaid: items.Where(l => l.Left > 0).Select(l => Row(l, true)).ToList(),
            OtherDues: Display.DueRows(s.NonCardDuesUpTo(month), s),
            FullyPaid: items.Where(l => l.Left <= 0).Select(l => Row(l, false)).ToList(),
            CardThisMonth: CardRows(d => d.DueMonth.Length > 0 && string.CompareOrdinal(d.DueMonth, month) <= 0 && d.Status != DueStatus.Paid),
            NoDate: Display.DueRows(s.Dues.Where(d => d.DueMonth.Length == 0 && d.Status != DueStatus.Paid), s),
            CardPaid: CardRows(d => d.DueMonth == month && d.Status == DueStatus.Paid),
            // Next month's dues are shown for planning only: no Pay button.
            NextMonth: Display.DueRows(s.Dues.Where(d => d.DueMonth == next && d.Status != DueStatus.Paid), s, canPay: false),
            NextMonthTitle: Loc.T("Dues_NextMonth") + " · " + Fmt.Month(next));
        FocusRow = Model.NotPaid.FirstOrDefault(r => r.Highlight);
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
