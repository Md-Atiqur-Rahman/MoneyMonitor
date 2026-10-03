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

/// <summary>
/// Reports → Cash flow → Money in / Dues paid / Cash expenses, in detail (ADR 0032):
/// "Salary 80,000 · Bonus 20,000 · Borrowed from Friend 10,000 · Total 1,10,000".
/// </summary>
public sealed partial class FlowViewModel(FinanceService finance, MonthState months) : ViewModelBase, IQueryAttributable
{
    private string _kind = "in";

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _monthTitle = "";
    [ObservableProperty] private string _hint = "";
    [ObservableProperty] private List<FlowRow> _rows = [];
    [ObservableProperty] private string _total = "";
    [ObservableProperty] private bool _isEmpty;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("kind", out var k) && k?.ToString() is { } kind) _kind = kind;
    }

    public override async Task LoadAsync()
    {
        var s = await finance.LoadAsync();
        var month = months.Month;
        var flow = s.CashFlow(month);
        MonthTitle = Fmt.Month(month);

        switch (_kind)
        {
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
        IsEmpty = Rows.Count == 0;
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
        return new FlowRow(title, sub, Fmt.Money(line.Amount));
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
