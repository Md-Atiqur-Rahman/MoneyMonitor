using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DailyAccount.App.Localization;
using DailyAccount.App.Services;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;

namespace DailyAccount.App.ViewModels;

public sealed record AccountRow(string Name, string TypeText, string Badge, Color BadgeBg, Color BadgeFg, string Balance, ICommand Open);

/// <summary>Accounts with their balance at the end of the month shown (ADR 0029): today for this month.</summary>
public sealed partial class AccountsViewModel(FinanceService finance, MonthState months) : ViewModelBase
{
    // ‹ month › shared by every page (ADR 0029).
    [ObservableProperty] private string _monthTitle = "";
    [ObservableProperty] private bool _isPastMonth;

    [RelayCommand]
    private Task PreviousMonth()
    {
        months.Previous();
        return LoadAsync();
    }

    [RelayCommand]
    private Task NextMonth()
    {
        months.Next();
        return LoadAsync();
    }

    [RelayCommand]
    private Task ThisMonth()
    {
        months.Reset();
        return LoadAsync();
    }

    [ObservableProperty] private string _totalTitle = "";
    [ObservableProperty] private string _total = "";
    [ObservableProperty] private List<AccountRow> _accounts = [];
    [ObservableProperty] private bool _isEmpty;

    public override async Task LoadAsync()
    {
        var s = await finance.LoadAsync();
        var asOf = months.AsOf;
        MonthTitle = Fmt.Month(months.Month);
        IsPastMonth = months.IsPast;
        TotalTitle = months.IsPast ? Loc.F("Home_AccountsOn", Fmt.Date(asOf)) : Loc.T("TotalSavings");
        Total = Fmt.Money(s.TotalBalanceOn(asOf));
        Accounts = s.Accounts.Where(a => a.IsActive).Select(a => new AccountRow(
            a.Name,
            Loc.T($"AccountType_{a.Type}"),
            a.Type switch { AccountType.Bank => "BANK", AccountType.Cash => "CASH", _ => "MOB" },
            Color.FromArgb(a.Type switch { AccountType.Bank => "#E6EDF8", AccountType.Cash => "#E3F0E8", _ => "#F7E6EF" }),
            Color.FromArgb(a.Type switch { AccountType.Bank => "#1D3F75", AccountType.Cash => "#14503A", _ => "#7A1F4E" }),
            Fmt.Money(s.BalanceOn(a, asOf)),
            new AsyncRelayCommand(() => Ui.Go($"{AppShell.AccountDetail}?id={a.Id}")))).ToList();
        IsEmpty = Accounts.Count == 0;
    }

    [RelayCommand]
    private Task Add() => Ui.Go(AppShell.AddAccount);
}

/// <summary>New account, or edit one (?id=) to rename it or correct its opening balance.</summary>
public sealed partial class AddAccountViewModel : ViewModelBase, IQueryAttributable
{
    private readonly FinanceService _finance;
    private Account? _editing;
    private int? _editId;

    public AddAccountViewModel(FinanceService finance, AppSettings settings)
    {
        _finance = finance;
        // With a start month, the opening balance is the balance on its 1st day (ADR 0025).
        if (settings.StartMonth is { } start)
            OpeningLabel = Loc.F("OpeningOn", Fmt.Date(Core.MonthKey.FirstDay(start)));
        Types =
        [
            new(nameof(AccountType.Bank), Loc.T("AccountType_Bank"), Select),
            new(nameof(AccountType.Cash), Loc.T("AccountType_Cash"), Select),
            new(nameof(AccountType.MobileWallet), Loc.T("AccountType_MobileWallet"), Select),
        ];
        Select(Types[0]);
    }

    public List<ChipOption> Types { get; }

    [ObservableProperty] private string _title = Loc.T("AddAccount_Title");
    [ObservableProperty] private string _openingLabel = Loc.T("OpeningBalance");
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _openingText = "";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var v) && int.TryParse(v?.ToString(), out var id)) _editId = id;
    }

    public override async Task LoadAsync()
    {
        if (_editId is not { } id || _editing is not null) return;
        _editing = (await _finance.LoadAsync()).Accounts.FirstOrDefault(a => a.Id == id);
        if (_editing is null) return;

        Title = Loc.T("EditAccount_Title");
        if (OpeningLabel == Loc.T("OpeningBalance")) OpeningLabel = Loc.T("Opening");
        Name = _editing.Name;
        OpeningText = Fmt.EditableAmount(_editing.OpeningBalance);
        Select(Types.First(t => t.Key == _editing.Type.ToString()));
    }

    private void Select(ChipOption option)
    {
        foreach (var t in Types) t.IsSelected = t == option;
    }

    [RelayCommand]
    private async Task Save()
    {
        var type = Enum.Parse<AccountType>(Types.First(t => t.IsSelected).Key);
        var opening = Fmt.ParseMoney(OpeningText) ?? 0;
        var ok = await Ui.Try(() =>
        {
            if (_editing is null)
                return _finance.AddAccountAsync(new Account { Name = Name, Type = type, OpeningBalance = opening });

            _editing.Name = Name;
            _editing.Type = type;
            _editing.OpeningBalance = opening;
            return _finance.UpdateAccountAsync(_editing);
        });
        if (ok) await Ui.Back();
    }
}

/// <summary>One account in the month shown (ADR 0029): balance at its start and end, and that month's history.</summary>
public sealed partial class AccountDetailViewModel(FinanceService finance, AppSettings settings, MonthState months) : ViewModelBase, IQueryAttributable
{
    private int _accountId;

    // ‹ month › shared by every page (ADR 0029).
    [ObservableProperty] private string _monthTitle = "";
    [ObservableProperty] private bool _isPastMonth;

    [RelayCommand]
    private Task PreviousMonth()
    {
        months.Previous();
        return LoadAsync();
    }

    [RelayCommand]
    private Task NextMonth()
    {
        months.Next();
        return LoadAsync();
    }

    [RelayCommand]
    private Task ThisMonth()
    {
        months.Reset();
        return LoadAsync();
    }

    [ObservableProperty] private string _balanceTitle = "";
    [ObservableProperty] private string _monthSummary = "";

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _balance = "";
    [ObservableProperty] private string _opening = "";
    [ObservableProperty] private List<TxRow> _rows = [];
    [ObservableProperty] private bool _isEmpty;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var v) && int.TryParse(v?.ToString(), out var id)) _accountId = id;
    }

    public override async Task LoadAsync()
    {
        var s = await finance.LoadAsync();
        var account = s.Accounts.FirstOrDefault(a => a.Id == _accountId);
        if (account is null) return;

        var month = months.Month;
        var asOf = months.AsOf;
        var first = Core.MonthKey.FirstDay(month);
        var mine = s.Transactions.Where(t => t.AccountId == _accountId || t.ToAccountId == _accountId).ToList();
        var inMonth = mine.Where(t => Core.MonthKey.Contains(month, t.Date)).ToList();
        var moneyIn = inMonth.Sum(t => Math.Max(0, Core.Services.BalanceService.EffectOn(_accountId, t)));
        var moneyOut = inMonth.Sum(t => Math.Max(0, -Core.Services.BalanceService.EffectOn(_accountId, t)));

        Name = account.Name;
        MonthTitle = Fmt.Month(month);
        IsPastMonth = months.IsPast;
        BalanceTitle = months.IsPast ? Loc.F("Home_AccountsOn", Fmt.Date(asOf)) : Loc.T("Acc_Balance");
        Balance = Fmt.Money(s.BalanceOn(account, asOf));
        Opening = (settings.StartMonth is { } start ? Loc.F("OpeningOn", Fmt.Date(Core.MonthKey.FirstDay(start))) : Loc.T("Opening"))
                  + ": " + Fmt.Money(account.OpeningBalance);
        MonthSummary = Loc.F("Acc_MonthSummary", Fmt.Date(first), Fmt.Money(s.BalanceOn(account, first.AddDays(-1))),
            Fmt.Money(moneyIn), Fmt.Money(moneyOut));
        Rows = inMonth
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Select(t => Display.ToRow(t, s, _accountId, DeleteAsync))
            .ToList();
        IsEmpty = Rows.Count == 0;
    }

    private async Task DeleteAsync(Transaction t)
    {
        if (!await Ui.Confirm(Loc.T("Confirm_Delete"))) return;
        if (await Ui.Try(() => finance.DeleteTransactionAsync(t.Id)))
            await LoadAsync();
    }

    [RelayCommand]
    private Task Edit() => Ui.Go($"{AppShell.AddAccount}?id={_accountId}");
}
