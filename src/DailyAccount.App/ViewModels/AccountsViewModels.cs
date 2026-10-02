using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DailyAccount.App.Localization;
using DailyAccount.App.Services;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;

namespace DailyAccount.App.ViewModels;

public sealed record AccountRow(string Name, string TypeText, string Badge, Color BadgeBg, Color BadgeFg, string Balance, ICommand Open);

public sealed partial class AccountsViewModel(FinanceService finance) : ViewModelBase
{
    [ObservableProperty] private string _total = "";
    [ObservableProperty] private List<AccountRow> _accounts = [];
    [ObservableProperty] private bool _isEmpty;

    public override async Task LoadAsync()
    {
        var s = await finance.LoadAsync();
        Total = Fmt.Money(s.TotalBalance);
        Accounts = s.Accounts.Where(a => a.IsActive).Select(a => new AccountRow(
            a.Name,
            Loc.T($"AccountType_{a.Type}"),
            a.Type switch { AccountType.Bank => "BANK", AccountType.Cash => "CASH", _ => "MOB" },
            Color.FromArgb(a.Type switch { AccountType.Bank => "#E6EDF8", AccountType.Cash => "#E3F0E8", _ => "#F7E6EF" }),
            Color.FromArgb(a.Type switch { AccountType.Bank => "#1D3F75", AccountType.Cash => "#14503A", _ => "#7A1F4E" }),
            Fmt.Money(s.Balance(a)),
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

    public AddAccountViewModel(FinanceService finance)
    {
        _finance = finance;
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
        OpeningLabel = Loc.T("Opening");
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

public sealed partial class AccountDetailViewModel(FinanceService finance) : ViewModelBase, IQueryAttributable
{
    private int _accountId;

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

        Name = account.Name;
        Balance = Fmt.Money(s.Balance(account));
        Opening = Loc.T("Opening") + ": " + Fmt.Money(account.OpeningBalance);
        Rows = s.Transactions
            .Where(t => t.AccountId == _accountId || t.ToAccountId == _accountId)
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
