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

/// <summary>One line of a cash-flow breakdown; tapping opens its details when there are any.</summary>
public sealed record FlowRow(string Title, string Subtitle, string Amount, ICommand? Tap = null)
{
    public bool HasSubtitle => Subtitle.Length > 0;
    public bool CanTap => Tap is not null;
}

/// <summary>A group of rows with its own total, e.g. "You lent" / "You borrowed" (ADR 0043).</summary>
public sealed record FlowSection(string Title, List<FlowRow> Rows, string TotalLabel, string Total, string Note);

/// <summary>
/// Reports → Cash flow → Money in / Dues paid / Cash expenses, in detail (ADR 0032):
/// "Salary 80,000 · Bonus 20,000 · Borrowed from Friend 10,000 · Total 1,10,000".
/// </summary>
public sealed partial class FlowViewModel(FinanceService finance, MonthState months) : ViewModelBase, IQueryAttributable
{
    private string _kind = "in";
    private int? _dueId; // kind=bill: the card statement whose purchases are listed (ADR 0039)
    private int? _debtId; // kind=debt: one personal debt, step by step (ADR 0040)
    private int? _personId; // kind=person: everything with one person (ADR 0042)
    private string? _billMonth;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _monthTitle = "";
    [ObservableProperty] private string _hint = "";
    [ObservableProperty] private List<FlowRow> _rows = [];
    /// <summary>When set, the page shows these groups instead of one list (Lent &amp; borrowed, ADR 0043).</summary>
    [ObservableProperty] private List<FlowSection> _sections = [];
    public bool HasSections => Sections.Count > 0;
    public bool HasOneList => Sections.Count == 0;
    partial void OnSectionsChanged(List<FlowSection> value)
    {
        OnPropertyChanged(nameof(HasSections));
        OnPropertyChanged(nameof(HasOneList));
    }
    [ObservableProperty] private string _total = "";
    [ObservableProperty] private bool _isEmpty;
    /// <summary>‹ › only for a month's lists; a bill's purchases belong to that bill.</summary>
    [ObservableProperty] private bool _showMonthNav = true;
    public bool IsBill => !ShowMonthNav;
    /// <summary>The label of the bottom line: "Total", "Left", "Owed to you" (ADR 0040).</summary>
    [ObservableProperty] private string _totalLabel = Loc.T("Card_NextTotal");
    partial void OnShowMonthNavChanged(bool value) => OnPropertyChanged(nameof(IsBill));

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("kind", out var k) && k?.ToString() is { } kind) _kind = kind;
        if (query.TryGetValue("dueId", out var d) && int.TryParse(d?.ToString(), out var dueId)) _dueId = dueId;
        if (query.TryGetValue("debtId", out var b) && int.TryParse(b?.ToString(), out var debtId)) _debtId = debtId;
        if (query.TryGetValue("personId", out var pp) && int.TryParse(pp?.ToString(), out var personId)) _personId = personId;
        if (query.TryGetValue("month", out var m) && m?.ToString() is { Length: 7 } paidIn) _billMonth = paidIn;
    }

    public override async Task LoadAsync()
    {
        var s = await finance.LoadAsync();
        var month = months.Month;
        var flow = s.CashFlow(month);
        MonthTitle = Fmt.Month(month);

        switch (_kind)
        {
            case "people":
            {
                // Reports → Lent & borrowed: one line per person and direction, adding up all their debts (ADR 0042).
                ShowMonthNav = false;
                var people = s.PeopleLedgers();
                Title = Loc.T("People_Title");
                MonthTitle = Title;
                var owedToMe = people.Sum(p => p.LentLeft);
                var iOwe = people.Sum(p => p.BorrowedLeft);
                Hint = Loc.F("People_Hint", Fmt.Money(owedToMe), Fmt.Money(iOwe));
                // Two groups (ADR 0043): first everyone you lent to and its total, then everyone you borrowed from.
                FlowRow Line(PersonLedger p, bool lent)
                {
                    var debts = lent ? p.Lent : p.Borrowed;
                    var total = lent ? p.LentTotal : p.BorrowedTotal;
                    var back = lent ? p.LentBack : p.Repaid;
                    var left = lent ? p.LentLeft : p.BorrowedLeft;
                    return new FlowRow(
                        p.Person.Name + (debts.Count > 1 ? " (" + Fmt.Number(debts.Count) + ")" : ""),
                        Loc.F(lent ? "People_LentRow" : "People_BorrowedRow", Fmt.Money(total)) + " · "
                            + Display.DebtProgress(new DebtLedger(debts[0].Debt, total, back, [.. debts.SelectMany(l => l.Steps)])),
                        left == 0 ? "✓" : Fmt.Money(left),
                        new AsyncRelayCommand(() => Ui.Go($"{AppShell.Flow}?kind=person&personId={p.Person.Id}")));
                }
                // Open ones in their group; settled ones for 12 months under "Settled", then off the report (ADR 0044).
                var cutoff = DateTime.Today.AddMonths(-12);
                var lentTo = people.Where(p => p.Lent.Count > 0 && p.LentLeft > 0).ToList();
                var borrowedFrom = people.Where(p => p.Borrowed.Count > 0 && p.BorrowedLeft > 0).ToList();
                var settledLent = people.Where(p => p.LentSettledOn >= cutoff).ToList();
                var settledBorrowed = people.Where(p => p.BorrowedSettledOn >= cutoff).ToList();
                Sections =
                [
                    new FlowSection(Loc.T("Lent_Section"), [.. lentTo.Select(p => Line(p, true))],
                        Loc.T("People_OwedToYou"), Fmt.Money(owedToMe),
                        Loc.F("People_SectionNote", Fmt.Money(lentTo.Sum(p => p.LentTotal)), Fmt.Money(lentTo.Sum(p => p.LentBack)))),
                    new FlowSection(Loc.T("Borrowed_Section"), [.. borrowedFrom.Select(p => Line(p, false))],
                        Loc.T("People_YouOwe"), Fmt.Money(iOwe),
                        Loc.F("People_SectionNoteBorrowed", Fmt.Money(borrowedFrom.Sum(p => p.BorrowedTotal)), Fmt.Money(borrowedFrom.Sum(p => p.Repaid)))),
                ];
                if (settledLent.Count + settledBorrowed.Count > 0)
                    Sections = [.. Sections, new FlowSection(Loc.T("People_Settled"),
                        [.. settledLent.Select(p => Line(p, true) with { Subtitle = Line(p, true).Subtitle + " · " + Fmt.Date(p.LentSettledOn!.Value) }),
                         .. settledBorrowed.Select(p => Line(p, false) with { Subtitle = Line(p, false).Subtitle + " · " + Fmt.Date(p.BorrowedSettledOn!.Value) })],
                        Loc.T("People_SettledTotal"),
                        Fmt.Money(settledLent.Sum(p => p.LentTotal) + settledBorrowed.Sum(p => p.BorrowedTotal)),
                        Loc.T("People_SettledNote"))];
                Rows = [];
                TotalLabel = Loc.T("People_OwedToYou");
                Total = Fmt.Money(owedToMe);
                break;
            }
            case "person" when s.PeopleLedgers().FirstOrDefault(x => x.Person.Id == _personId) is { } person:
            {
                // Everything with one person, step by step (ADR 0042).
                ShowMonthNav = false;
                Title = person.Person.Name;
                MonthTitle = Title;
                var parts = new List<string>();
                if (person.Lent.Count > 0) parts.Add(Loc.F("Person_LentLine", Fmt.Money(person.LentTotal), Fmt.Money(person.LentBack), Fmt.Money(person.LentLeft)));
                if (person.Borrowed.Count > 0) parts.Add(Loc.F("Person_BorrowedLine", Fmt.Money(person.BorrowedTotal), Fmt.Money(person.Repaid), Fmt.Money(person.BorrowedLeft)));
                Hint = string.Join("\n", parts);
                Rows = person.Lent.Concat(person.Borrowed)
                    .SelectMany(l => l.Steps.Select(x => (Lent: l.Debt.Direction == DebtDirection.Lent, Step: x)))
                    .OrderBy(x => x.Step.Date).ThenBy(x => !x.Step.IsGiven)
                    .Select(x => new FlowRow(
                        Loc.T(x.Step.IsGiven ? (x.Lent ? "Step_Lent" : "Step_Borrowed") : x.Step.IsGift ? "Step_Gifted" : (x.Lent ? "Step_GotBack" : "Step_Repaid")),
                        Fmt.Date(x.Step.Date) + (x.Step.CardId is { } c ? " · " + Loc.F("Debt_ByCard", s.Cards.FirstOrDefault(k => k.Id == c)?.Name ?? "")
                            : Display.AccountName(x.Step.AccountId, s) is { Length: > 0 } a ? " · " + a : ""),
                        x.Step.IsGift ? "🎁 " + Fmt.Money(x.Step.Amount) : (x.Step.IsGiven ? "" : "−") + Fmt.Money(x.Step.Amount))).ToList();
                var left = person.LentLeft + person.BorrowedLeft;
                TotalLabel = Loc.T(left == 0 ? "Debt_FullyPaid" : "People_Left");
                Total = left == 0 ? "✓" : Fmt.Money(left);
                break;
            }
            case "debt" when s.Debts.FirstOrDefault(x => x.Id == _debtId) is { } debt:
            {
                // One person's debt, step by step: given, each repayment, what is left.
                ShowMonthNav = false;
                var l = s.Ledger(debt);
                var lent = debt.Direction == DebtDirection.Lent;
                Title = Loc.F(lent ? "People_LentTitle" : "People_BorrowedTitle", debt.PersonName);
                MonthTitle = Title;
                Hint = Display.DebtProgress(l);
                Rows = l.Steps.Select(x => new FlowRow(
                    Loc.T(x.IsGiven ? (lent ? "Step_Lent" : "Step_Borrowed") : (lent ? "Step_GotBack" : "Step_Repaid")),
                    Fmt.Date(x.Date) + (x.CardId is { } c ? " · " + Loc.F("Debt_ByCard", s.Cards.FirstOrDefault(k => k.Id == c)?.Name ?? "")
                        : Display.AccountName(x.AccountId, s) is { Length: > 0 } a ? " · " + a : ""),
                    (x.IsGiven ? "" : "−") + Fmt.Money(x.Amount))).ToList();
                TotalLabel = Loc.T(l.IsFullyPaid ? "Debt_FullyPaid" : "People_Left");
                Total = l.IsFullyPaid ? "✓" : Fmt.Money(l.Left);
                break;
            }
            case "bill" when s.Dues.FirstOrDefault(x => x.Id == _dueId) is { } statement:
                // The purchases a card bill is made of (ADR 0039).
                ShowMonthNav = false;
                var purchases = s.StatementPurchases(statement);
                var card = s.Cards.FirstOrDefault(c => c.Id == statement.SourceId)?.Name ?? "";
                var cycle = purchases.FirstOrDefault()?.Date ?? DateTime.Today;
                Title = Loc.F("Flow_CardPurchases", card, Fmt.MonthName(MonthKey.Of(cycle)));
                MonthTitle = Title;
                Hint = Loc.F("Flow_BillHint", Fmt.Month(_billMonth ?? statement.DueMonth), Fmt.Money(statement.PaidAmount), Fmt.Money(statement.Amount));
                Rows = purchases.Select(t => new FlowRow(
                    t.ItemName ?? t.Note ?? Display.CategoryName(t.CategoryId, s),
                    Fmt.Date(t.Date) + " · " + Display.CategoryName(t.CategoryId, s),
                    Fmt.Money(t.Amount))).ToList();
                Total = Fmt.Money(purchases.Sum(t => t.Amount));
                break;
            case "dues":
                Title = Loc.T("DuesPaid");
                Hint = Loc.T("Flow_DuesHint");
                Rows = flow.DuesPaid.Select(l => DueRow(l, s)).ToList();
                Total = Fmt.Money(flow.DuesPaidTotal);
                break;
            case "spent":
                Title = Loc.T("CashExpenses");
                Hint = Loc.T("Flow_SpentHint");
                Rows = flow.Spent.Select(g => new FlowRow(
                    Display.CategoryName(g.CategoryId, s),
                    Loc.F(g.Entries.Count == 1 ? "Flow_Entry1" : "Flow_Entries", Fmt.Number(g.Entries.Count)),
                    Fmt.Money(g.Amount),
                    g.CategoryId is { } id ? new AsyncRelayCommand(() => Ui.Go($"{AppShell.CategoryReport}?id={id}&month={month}")) : null))
                    .ToList();
                Total = Fmt.Money(flow.CashSpent);
                break;
            default:
                Title = Loc.T("Flow_MoneyIn");
                Hint = Loc.T("Flow_InHint");
                Rows = flow.In.Select(l => InRow(l, s)).ToList();
                Total = Fmt.Money(flow.MoneyIn);
                break;
        }
        IsEmpty = Rows.Count == 0 && Sections.Count == 0;
    }

    private static FlowRow InRow(MoneyInLine line, FinanceSnapshot s)
    {
        var person = s.Debts.FirstOrDefault(d => d.Id == line.DebtId)?.PersonName ?? "";
        var lender = s.Loans.FirstOrDefault(l => l.Id == line.LoanId)?.Lender;
        var title = line.Kind switch
        {
            MoneyInKind.Borrowed when lender is not null => Loc.F("Flow_LoanFrom", lender),
            MoneyInKind.Borrowed => Loc.F("Flow_BorrowedFrom", person),
            MoneyInKind.LendReturned => Loc.F("Flow_ReturnedBy", person),
            _ => Display.CategoryName(line.CategoryId, s)
        };
        var sub = line.Kind switch
        {
            MoneyInKind.Borrowed when lender is not null => Loc.T("Flow_LoanNote"),
            MoneyInKind.Borrowed => Loc.T("Flow_BorrowedNote"),
            _ => line.Count > 1 ? Loc.F("Flow_Entries", Fmt.Number(line.Count)) : ""
        };
        return new FlowRow(title, sub, Fmt.Money(line.Amount));
    }

    /// <summary>"Loan-1 · installment 1 of 6", "Credit Card · August purchases", "Repaid Friend".</summary>
    private static FlowRow DueRow(DuePaidLine line, FinanceSnapshot s)
    {
        var due = line.Due;
        string title;
        System.Windows.Input.ICommand? tap = null; // a card bill opens its purchases (ADR 0039)
        switch (due.SourceType)
        {
            case DueSource.Loan when s.Loans.FirstOrDefault(l => l.Id == due.SourceId) is { } loan:
                var card = s.Cards.FirstOrDefault(c => c.Id == loan.CardId);
                title = Loc.F("Flow_Installment", loan.Lender, Fmt.Number(due.Sequence), Fmt.Number(loan.InstallmentCount))
                        + (card is null ? "" : " · " + Loc.F("Loan_OnCard", card.Name));
                break;
            case DueSource.Card when s.Cards.FirstOrDefault(c => c.Id == due.SourceId) is { } c
                                     && DateTime.TryParse(due.PeriodKey, System.Globalization.CultureInfo.InvariantCulture,
                                         System.Globalization.DateTimeStyles.None, out var cycle):
                title = Loc.F("Flow_CardPurchases", c.Name, Fmt.MonthName(MonthKey.Of(cycle)));
                tap = new AsyncRelayCommand(() => Ui.Go($"{AppShell.Flow}?kind=bill&dueId={due.Id}&month={MonthKey.Of(line.LastDate)}"));
                break;
            case DueSource.Personal:
                title = Loc.F("Flow_Repaid", due.Title);
                break;
            default:
                title = due.Title;
                break;
        }
        var from = Display.AccountName(line.AccountId, s);
        var sub = Fmt.Date(line.LastDate) + (from.Length > 0 ? " · " + Loc.F("Flow_From", from) : "")
                  + (line.Amount < due.Amount ? " · " + Loc.F("Flow_PartOf", Fmt.Money(due.Amount)) : "");
        return new FlowRow(title, sub, Fmt.Money(line.Amount), tap);
    }

    [RelayCommand]
    private Task Previous()
    {
        months.Previous();
        return LoadAsync();
    }

    [RelayCommand]
    private Task Next()
    {
        months.Next();
        return LoadAsync();
    }
}
