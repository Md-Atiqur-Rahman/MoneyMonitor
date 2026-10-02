using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DailyAccount.App.Localization;
using DailyAccount.App.Services;
using DailyAccount.Core;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;

namespace DailyAccount.App.ViewModels;

/// <summary>Every entry of a month in one list, newest first; tap to edit, 🗑 to delete (ADR 0026).</summary>
public sealed partial class EntriesViewModel(FinanceService finance) : ViewModelBase, IQueryAttributable
{
    private string _month = MonthKey.Of(DateTime.Today);

    [ObservableProperty] private string _monthTitle = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private List<TxRow> _rows = [];
    [ObservableProperty] private bool _isEmpty;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("month", out var m) && m?.ToString() is { Length: 7 } month) _month = month;
    }

    public override async Task LoadAsync()
    {
        var s = await finance.LoadAsync();
        var list = s.Transactions.Where(t => MonthKey.Contains(_month, t.Date))
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .ToList();
        long In(TransactionType ty) => list.Where(t => t.Type == ty).Sum(t => t.Amount);

        MonthTitle = Fmt.Month(_month);
        Summary = Loc.F("Entries_Summary", Fmt.Number(list.Count),
            Fmt.Money(In(TransactionType.Income) + In(TransactionType.BorrowIn) + In(TransactionType.LendReturn)),
            Fmt.Money(In(TransactionType.Expense) + In(TransactionType.CardPurchase) + In(TransactionType.DuePayment) + In(TransactionType.LendOut)));
        Rows = list.Select(t => Display.ToRow(t, s, null, DeleteAsync)).ToList();
        IsEmpty = Rows.Count == 0;
    }

    private async Task DeleteAsync(Transaction t)
    {
        if (!await Ui.Confirm(Loc.T("Confirm_Delete"))) return;
        if (await Ui.Try(() => finance.DeleteTransactionAsync(t.Id))) await LoadAsync();
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
