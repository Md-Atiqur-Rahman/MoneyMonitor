using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DailyAccount.App.Localization;
using DailyAccount.App.Services;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;

namespace DailyAccount.App.ViewModels;

/// <summary>One line of a shopping trip: type (sub-category), item, qty, price (ADR 0013).</summary>
public sealed partial class ItemLine : ObservableObject
{
    private readonly Action _changed;
    private readonly Action<ItemLine> _remove;

    public ItemLine(List<Option> subCategories, Action changed, Action<ItemLine> remove)
    {
        _subCategories = subCategories;
        _selectedSub = subCategories.FirstOrDefault();
        _changed = changed;
        _remove = remove;
    }

    [ObservableProperty] private List<Option> _subCategories;
    [ObservableProperty] private Option? _selectedSub;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _quantity = "";
    [ObservableProperty] private string _priceText = "";

    partial void OnPriceTextChanged(string value) => _changed();

    [RelayCommand]
    private void Remove() => _remove(this);
}

/// <summary>
/// One form for all six everyday entries. Income/Expense/Card/Transfer become Transactions (one per
/// item line when entering item by item); Borrow/Lend become a PersonalDebt.
/// </summary>
public sealed partial class AddTransactionViewModel : ViewModelBase, IQueryAttributable
{
    private readonly FinanceService _finance;
    private FinanceSnapshot? _snapshot;
    private string _type = "expense";

    public AddTransactionViewModel(FinanceService finance)
    {
        _finance = finance;
        Types =
        [
            new("income", Loc.T("Type_Income"), Select),
            new("expense", Loc.T("Type_Expense"), Select),
            new("card", Loc.T("Type_Card"), Select),
            new("transfer", Loc.T("Type_Transfer"), Select),
            new("borrow", Loc.T("Type_Borrow"), Select),
            new("lend", Loc.T("Type_Lend"), Select),
        ];
    }

    public List<ChipOption> Types { get; }
    public ObservableCollection<ItemLine> Lines { get; } = [];

    [ObservableProperty] private string _hint = "";
    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private List<Option> _categories = [];
    [ObservableProperty] private Option? _selectedCategory;
    [ObservableProperty] private List<Option> _accounts = [];
    [ObservableProperty] private Option? _selectedAccount;
    [ObservableProperty] private Option? _selectedToAccount;
    [ObservableProperty] private List<Option> _cards = [];
    [ObservableProperty] private Option? _selectedCard;
    [ObservableProperty] private string _accountLabel = "";
    [ObservableProperty] private string _personName = "";
    [ObservableProperty] private bool _hasReturnDate;
    [ObservableProperty] private DateTime _returnDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private DateTime _date = DateTime.Today;
    [ObservableProperty] private string _note = "";

    [ObservableProperty] private bool _showCategory;
    [ObservableProperty] private bool _showAccount;
    [ObservableProperty] private bool _showToAccount;
    [ObservableProperty] private bool _showCard;
    [ObservableProperty] private bool _showPerson;
    [ObservableProperty] private bool _showReturnDate;

    /// <summary>Item-by-item entry is offered for Expense and Card purchase.</summary>
    [ObservableProperty] private bool _canUseItems;
    [ObservableProperty] private bool _useItems;
    [ObservableProperty] private string _itemsTotal = "";

    public bool ShowAmount => !(CanUseItems && UseItems);
    public bool ShowItems => CanUseItems && UseItems;

    partial void OnUseItemsChanged(bool value)
    {
        if (value && Lines.Count == 0) AddLine();
        OnPropertyChanged(nameof(ShowAmount));
        OnPropertyChanged(nameof(ShowItems));
    }

    partial void OnCanUseItemsChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAmount));
        OnPropertyChanged(nameof(ShowItems));
    }

    partial void OnSelectedCategoryChanged(Option? value)
    {
        var subs = SubOptions();
        foreach (var line in Lines)
        {
            line.SubCategories = subs;
            line.SelectedSub = subs.FirstOrDefault();
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("type", out var t) && t is string type) _type = type;
    }

    public override async Task LoadAsync()
    {
        _snapshot = await _finance.LoadAsync();
        Accounts = Display.AccountOptions(_snapshot);
        Cards = _snapshot.Cards.Select(c => new Option(c.Id, c.Name)).ToList();
        SelectedAccount ??= Accounts.FirstOrDefault();
        SelectedToAccount ??= Accounts.Skip(1).FirstOrDefault();
        SelectedCard ??= Cards.FirstOrDefault();
        Select(Types.First(x => x.Key == _type));
    }

    private void Select(ChipOption option)
    {
        _type = option.Key;
        foreach (var t in Types) t.IsSelected = t == option;

        Hint = Loc.T(_type switch
        {
            "income" => "Hint_Income",
            "card" => "Hint_Card",
            "transfer" => "Hint_Transfer",
            "borrow" => "Hint_Borrow",
            "lend" => "Hint_Lend",
            _ => "Hint_Expense"
        });

        ShowCategory = _type is "income" or "expense" or "card";
        ShowCard = _type == "card";
        ShowAccount = _type != "card";
        ShowToAccount = _type == "transfer";
        ShowPerson = _type is "borrow" or "lend";
        ShowReturnDate = _type == "borrow";
        CanUseItems = _type is "expense" or "card";
        AccountLabel = Loc.T(_type switch
        {
            "income" => "ToAccount",
            "borrow" => "ReceivedIn",
            _ => "FromAccount"
        });

        if (_snapshot is not null)
        {
            var kind = _type == "income" ? CategoryKind.Income : CategoryKind.Expense;
            Categories = _snapshot.TopCategories(kind)
                .Select(c => new Option(c.Id, Display.CategoryName(c.Id, _snapshot)))
                .ToList();
            SelectedCategory = Categories.FirstOrDefault();
        }
    }

    /// <summary>The chosen category itself, then its sub-categories (Bajar, Grocery, Meat, Fish…).</summary>
    private List<Option> SubOptions()
    {
        if (_snapshot is null || SelectedCategory is null) return [];
        return [SelectedCategory, .. _snapshot.Children(SelectedCategory.Id).Select(c => new Option(c.Id, Display.CategoryName(c.Id, _snapshot)))];
    }

    /// <summary>Creates a new top-level category (e.g. "Restaurant Bill") and selects it right away.</summary>
    [RelayCommand]
    private async Task NewCategory()
    {
        var name = await Ui.PromptText(Loc.T("NewCategory_Prompt"));
        if (name is null) return;

        var kind = _type == "income" ? CategoryKind.Income : CategoryKind.Expense;
        Category? created = null;
        if (!await Ui.Try(async () => created = await _finance.AddCategoryAsync(name, kind, null))) return;

        _snapshot = await _finance.LoadAsync();
        Categories = _snapshot.TopCategories(kind)
            .Select(c => new Option(c.Id, Display.CategoryName(c.Id, _snapshot)))
            .ToList();
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == created!.Id);
    }

    [RelayCommand]
    private void AddLine() => Lines.Add(new ItemLine(SubOptions(), UpdateTotal, RemoveLine));

    private void RemoveLine(ItemLine line)
    {
        Lines.Remove(line);
        UpdateTotal();
    }

    private void UpdateTotal() =>
        ItemsTotal = Loc.F("ItemsTotal", Fmt.Money(Lines.Sum(l => Fmt.ParseMoney(l.PriceText) ?? 0)));

    [RelayCommand]
    private async Task Save()
    {
        if (_snapshot is null) return;
        if (_type != "card" && _snapshot.Accounts.Count == 0 && _type != "borrow")
        {
            await Ui.Alert(Loc.T("NeedAccountFirst"));
            await Ui.Go(AppShell.AddAccount);
            return;
        }
        if (_type == "card" && _snapshot.Cards.Count == 0)
        {
            await Ui.Alert(Loc.T("NeedCardFirst"));
            return;
        }

        var note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();
        var type = _type switch
        {
            "income" => TransactionType.Income,
            "card" => TransactionType.CardPurchase,
            "transfer" => TransactionType.Transfer,
            _ => TransactionType.Expense
        };
        Transaction Make(long amount, int? categoryId, string? item, string? qty) => new()
        {
            Type = type, Amount = amount, Date = Date,
            AccountId = SelectedAccount?.Id, ToAccountId = SelectedToAccount?.Id, CardId = SelectedCard?.Id,
            CategoryId = categoryId, ItemName = item, Quantity = qty, Note = note
        };

        var ok = await Ui.Try(async () =>
        {
            if (_type is "borrow" or "lend")
            {
                await _finance.AddPersonalDebtAsync(new PersonalDebt
                {
                    PersonName = PersonName,
                    Direction = _type == "borrow" ? DebtDirection.Borrowed : DebtDirection.Lent,
                    Amount = Fmt.ParseMoney(AmountText) ?? 0,
                    Date = Date,
                    ExpectedReturnDate = _type == "borrow" && HasReturnDate ? ReturnDate : null,
                    AccountId = SelectedAccount?.Id,
                    Note = note
                });
            }
            else if (ShowItems)
            {
                // Blank lines are skipped; a line with a name but no price is an error (amount 0).
                var lines = Lines.Where(l => !string.IsNullOrWhiteSpace(l.Name) || !string.IsNullOrWhiteSpace(l.PriceText))
                    .Select(l => Make(Fmt.ParseMoney(l.PriceText) ?? 0, l.SelectedSub?.Id ?? SelectedCategory?.Id, l.Name, l.Quantity))
                    .ToList();
                await _finance.AddTransactionsAsync(lines);
            }
            else
            {
                await _finance.AddTransactionAsync(Make(Fmt.ParseMoney(AmountText) ?? 0, SelectedCategory?.Id, null, null));
            }
        });

        if (ok) await Ui.Back();
    }
}
