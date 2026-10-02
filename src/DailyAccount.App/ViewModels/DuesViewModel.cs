using CommunityToolkit.Mvvm.ComponentModel;
using DailyAccount.App.Localization;
using DailyAccount.Core;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;

namespace DailyAccount.App.ViewModels;

public sealed record DuesModel(
    string MonthTitle,
    string Total,
    string Paid,
    string Remaining,
    double Progress,
    string OverdueText,
    bool HasOverdue,
    List<DueRow> Pending,
    List<DueRow> NoDate,
    List<DueRow> PaidRows,
    List<DueRow> NextMonth,
    string NextMonthTitle)
{
    public bool PendingEmpty => Pending.Count == 0;
    public bool HasNoDate => NoDate.Count > 0;
    public bool HasPaid => PaidRows.Count > 0;
    public bool HasNextMonth => NextMonth.Count > 0;
}

public sealed partial class DuesViewModel(FinanceService finance) : ViewModelBase
{
    [ObservableProperty]
    private DuesModel? _model;

    public override async Task LoadAsync()
    {
        var today = DateTime.Today;
        await finance.GenerateDuesAsync(today);
        var s = await finance.LoadAsync();
        var month = MonthKey.Of(today);
        var next = MonthKey.Add(month, 1);
        var sum = s.Summary(month);

        List<DueRow> Rows(Func<Due, bool> filter, bool canPay = true) => Display.DueRows(s.Dues.Where(filter), s, canPay);

        var paid = sum.DuePaid;
        var remaining = sum.DueRemaining;

        Model = new DuesModel(
            MonthTitle: Fmt.Month(month),
            Total: Fmt.Money(paid + remaining),
            Paid: Fmt.Money(paid),
            Remaining: Fmt.Money(remaining),
            Progress: paid + remaining == 0 ? 0 : (double)paid / (paid + remaining),
            OverdueText: Loc.F("Dues_Overdue", Fmt.Money(sum.Overdue)),
            HasOverdue: sum.Overdue > 0,
            Pending: Rows(d => d.DueMonth.Length > 0 && string.CompareOrdinal(d.DueMonth, month) <= 0 && d.Status != DueStatus.Paid),
            NoDate: Rows(d => d.DueMonth.Length == 0 && d.Status != DueStatus.Paid),
            PaidRows: Rows(d => d.DueMonth == month && d.Status == DueStatus.Paid),
            // Next month's dues are shown for planning only: no Pay button (user: "I never pay next month's dues").
            NextMonth: Rows(d => d.DueMonth == next && d.Status != DueStatus.Paid, canPay: false),
            NextMonthTitle: Loc.T("Dues_NextMonth") + " · " + Fmt.Month(next));
    }
}
