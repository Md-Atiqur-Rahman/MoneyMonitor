using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DailyAccount.App.Localization;
using DailyAccount.App.Services;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;
using DailyAccount.Core.Services;

namespace DailyAccount.App.ViewModels;

/// <summary>
/// One line of a shopping trip: type (sub-category), item, qty + unit, rate, price (ADR 0013, 0027).
/// Qty and rate are optional; when both are given the price is qty × rate ("5 kg × ৳90 = ৳450").
/// </summary>
public sealed partial class ItemLine : ObservableObject
{
    private readonly Action _changed;
    private readonly Action<ItemLine> _remove;
    private readonly Func<ItemLine, Task> _newSub;
    private bool _calculating;

    /// <summary>"—" (no unit) then kg, gm, ltr, ml, pcs, dozen, packet; Id = index in <see cref="ItemMath.Units"/>.</summary>
    public static List<Option> UnitOptions() =>
        [new(-1, "—"), .. ItemMath.Units.Select((u, i) => new Option(i, Loc.T("Unit_" + u)))];

    public ItemLine(List<Option> subCategories, Action changed, Action<ItemLine> remove, Func<ItemLine, Task> newSub)
    {
        _subCategories = subCategories;
        _selectedSub = subCategories.FirstOrDefault();
        _changed = changed;
        _remove = remove;
        _newSub = newSub;
        Units = UnitOptions();
        _selectedUnit = Units[1]; // kg: the usual unit at the bazar
    }

    public List<Option> Units { get; }

    [ObservableProperty] private List<Option> _subCategories;
    [ObservableProperty] private Option? _selectedSub;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _quantityText = "";
    [ObservableProperty] private Option? _selectedUnit;
    [ObservableProperty] private string _rateText = "";
    [ObservableProperty] private string _priceText = "";
    [ObservableProperty] private string _calculation = "";

    /// <summary>Unit code ("kg") or null for "—".</summary>
    public string? UnitCode => SelectedUnit is { Id: >= 0 } u ? ItemMath.Units[u.Id] : null;

    /// <summary>"Rate/kg" — for gm and ml the rate is per kg / litre.</summary>
    public string RatePlaceholder => UnitCode is { } code
        ? Loc.F("Item_RatePer", Loc.T("Unit_" + ItemMath.RateUnit(code)))
        : Loc.T("Item_Rate");

    public bool HasCalculation => Calculation.Length > 0;

    partial void OnCalculationChanged(string value) => OnPropertyChanged(nameof(HasCalculation));
    partial void OnQuantityTextChanged(string value) => Recalculate();
    partial void OnRateTextChanged(string value) => Recalculate();

    partial void OnSelectedUnitChanged(Option? value)
    {
        OnPropertyChanged(nameof(RatePlaceholder));
        Recalculate();
    }

    partial void OnPriceTextChanged(string value)
    {
        if (!_calculating) ShowCalculation();
        _changed();
    }

    /// <summary>The typed quantity as a number, or null when blank / not a number.</summary>
    public decimal? Qty => Fmt.ParseNumber(QuantityText) is { } q && q > 0 ? q : null;

    /// <summary>What is stored: "5 kg", "3", older free text as typed, or null.</summary>
    public string? StoredQuantity => Qty is { } q
        ? ItemMath.Format(q, UnitCode)
        : string.IsNullOrWhiteSpace(QuantityText) ? null : QuantityText.Trim();

    /// <summary>Qty and rate both given → price = qty × rate. Otherwise the typed price stays.</summary>
    private void Recalculate()
    {
        if (_calculating) return;
        if (Qty is { } q && Fmt.ParseMoney(RateText) is { } rate and > 0)
        {
            _calculating = true;
            PriceText = Fmt.EditableAmount(ItemMath.Price(q, UnitCode, rate));
            _calculating = false;
        }
        ShowCalculation();
    }

    private void ShowCalculation()
    {
        var price = Fmt.ParseMoney(PriceText);
        if (Qty is not { } q || price is null)
        {
            Calculation = "";
            return;
        }
        var qty = Fmt.Digits(ItemMath.Format(q, null)!) + (UnitCode is { } u ? " " + Loc.T("Unit_" + u) : "");
        Calculation = Fmt.ParseMoney(RateText) is { } rate and > 0
            ? Loc.F("Item_Calc", qty, Fmt.Money(rate) + (UnitCode is { } c ? "/" + Loc.T("Unit_" + ItemMath.RateUnit(c)) : ""), Fmt.Money(price.Value))
            : Loc.F("Item_QtyPrice", qty, Fmt.Money(price.Value));
    }

    /// <summary>Fills the line from a saved entry (edit mode): "5 kg" + ৳450 → qty 5, kg, rate 90.</summary>
    public void Load(string? name, string? quantity, long amount)
    {
        Name = name ?? "";
        var (qty, unit) = ItemMath.Parse(quantity);
        _calculating = true;
        SelectedUnit = qty is null ? Units[1] : Units.First(u => u.Id == (unit is null ? -1 : Array.IndexOf(ItemMath.Units, unit)));
        QuantityText = qty is { } q ? ItemMath.Format(q, null)! : quantity ?? "";
        RateText = qty is { } q2 && ItemMath.Rate(q2, unit, amount) is { } r && ItemMath.Price(q2, unit, r) == amount
            ? Fmt.EditableAmount(r)
            : "";
        PriceText = Fmt.EditableAmount(amount);
        _calculating = false;
        ShowCalculation();
    }

    [RelayCommand]
    private void Remove() => _remove(this);

    /// <summary>"+ Sub": a new sub-category of the chosen category, selected on this line (ADR 0027).</summary>
    [RelayCommand]
    private Task NewSub() => _newSub(this);
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
    private int? _categoryId;
    private long? _presetAmount;

    // Edit mode (ADR 0026): ?id= opens an existing entry, filled in.
    private int? _editId;
    private bool _editLoaded;
    private Transaction? _editing;

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
    /// <summary>Borrow: the month it is to be paid back, no day (ADR 0030). First option = not decided.</summary>
    [ObservableProperty] private List<Option> _payMonths = [];
    [ObservableProperty] private Option? _selectedPayMonth;
    private List<string?> _payMonthKeys = [];

    partial void OnDateChanged(DateTime value) => FillPayMonths();

    private void FillPayMonths()
    {
        var options = Display.PayMonthOptions(Date);
        _payMonthKeys = [.. options.Values];
        var keep = SelectedPayMonth is { } o && o.Id < _payMonthKeys.Count ? _payMonthKeys[o.Id] : null;
        PayMonths = options.Keys.Select((label, i) => new Option(i, label)).ToList();
        SelectedPayMonth = PayMonths.ElementAtOrDefault(Math.Max(0, _payMonthKeys.IndexOf(keep)));
    }
    [ObservableProperty] private DateTime _date = DateTime.Today;
    [ObservableProperty] private string _note = "";

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _pageTitle = Loc.T("AddTx_Title");
    public bool NotEditing => !IsEditing;
    public bool ShowItemSwitch => CanUseItems;

    /// <summary>All six types when adding; in edit mode the four an entry can be (no Borrow/Lend).</summary>
    /// <remarks>ADR 0041: an expense or card purchase can become a Lend, an income a Borrow.</remarks>
    public List<ChipOption> VisibleTypes => IsEditing
        ? Types.Where(t => t.Key is "income" or "expense" or "card" or "transfer"
                           || (t.Key == "lend" && _editing?.Type is TransactionType.Expense or TransactionType.CardPurchase)
                           || (t.Key == "borrow" && _editing?.Type == TransactionType.Income)).ToList()
        : Types;

    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(NotEditing));
        OnPropertyChanged(nameof(VisibleTypes));
    }

    [ObservableProperty] private bool _showCategory;
    [ObservableProperty] private bool _showAccount;
    [ObservableProperty] private bool _showToAccount;
    [ObservableProperty] private bool _showCard;
    [ObservableProperty] private bool _showPerson;
    [ObservableProperty] private bool _showReturnDate;

    /// <summary>Lend: paid with a credit card instead of from an account (ADR 0040).</summary>
    [ObservableProperty] private bool _lendByCard;
    [ObservableProperty] private bool _showLendByCard;

    partial void OnLendByCardChanged(bool value) => UpdatePaidWith();

    private void UpdatePaidWith()
    {
        var byCard = _type == "lend" && LendByCard;
        ShowCard = _type == "card" || byCard;
        ShowAccount = _type != "card" && !byCard;
        // Turning an entry into a lend/borrow (ADR 0041): its own amount, date and account/card are used, so
        // those boxes are hidden; the hint names them instead.
        if (Converting) ShowCard = ShowAccount = false;
        OnPropertyChanged(nameof(ShowAmount));
        OnPropertyChanged(nameof(ShowDate));
    }

    /// <summary>Edit mode, Lend or Borrow chosen: the entry becomes a personal debt (ADR 0041).</summary>
    private bool Converting => IsEditing && _type is "borrow" or "lend";
    public bool ShowDate => !Converting;

    /// <summary>Item-by-item entry is offered for Expense and Card purchase.</summary>
    [ObservableProperty] private bool _canUseItems;
    [ObservableProperty] private bool _useItems;
    [ObservableProperty] private string _itemsTotal = "";

    public bool ShowAmount => !(CanUseItems && UseItems) && !Converting;
    public bool ShowItems => CanUseItems && UseItems;

    partial void OnUseItemsChanged(bool value)
    {
        if (value && Lines.Count == 0) AddLine();
        var first = Lines.FirstOrDefault();
        if (value && first is not null && string.IsNullOrWhiteSpace(first.PriceText) && !string.IsNullOrWhiteSpace(AmountText))
            first.PriceText = AmountText;
        if (!value && string.IsNullOrWhiteSpace(AmountText) && first is not null && !string.IsNullOrWhiteSpace(first.PriceText))
            AmountText = first.PriceText;
        OnPropertyChanged(nameof(ShowAmount));
        OnPropertyChanged(nameof(ShowItems));
    }

    partial void OnCanUseItemsChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAmount));
        OnPropertyChanged(nameof(ShowItems));
        OnPropertyChanged(nameof(ShowItemSwitch));
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
        if (query.TryGetValue("id", out var e) && int.TryParse(e?.ToString(), out var editId)) _editId = editId;
        if (query.TryGetValue("categoryId", out var c) && int.TryParse(c?.ToString(), out var categoryId)) _categoryId = categoryId;
        if (query.TryGetValue("amount", out var a) && long.TryParse(a?.ToString(), out var amount) && amount > 0) _presetAmount = amount;
        if (query.TryGetValue("date", out var d) && DateTime.TryParseExact(d?.ToString(), "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
            Date = date; // a past month's expense from Dues (ADR 0025)
    }

    public override async Task LoadAsync()
    {
        // Edit form already filled: keep what the user has changed when the page re-appears.
        if (_editLoaded) return;
        _snapshot = await _finance.LoadAsync();
        if (PayMonths.Count == 0) FillPayMonths();
        Accounts = Display.AccountOptions(_snapshot);
        Cards = _snapshot.Cards.Select(c => new Option(c.Id, c.Name)).ToList();
        SelectedAccount ??= Accounts.FirstOrDefault();
        SelectedToAccount ??= Accounts.Skip(1).FirstOrDefault();
        SelectedCard ??= Cards.FirstOrDefault();
        Select(Types.First(x => x.Key == _type));
        if (_categoryId is { } id && Categories.FirstOrDefault(c => c.Id == id) is { } preset)
        {
            SelectedCategory = preset;
            _categoryId = null; // only on first load, so the user's own choice is kept afterwards
        }
        // From Dues → "+ Expense": the amount left on that budget item, editable (ADR 0024 note).
        if (_presetAmount is { } preset2)
        {
            AmountText = Fmt.EditableAmount(preset2);
            _presetAmount = null;
        }

        if (_editId is { } id2 && !_editLoaded)
        {
            _editLoaded = true;
            await LoadForEditAsync(id2);
        }
    }

    private static string? KeyOf(TransactionType type) => type switch
    {
        TransactionType.Income => "income",
        TransactionType.Expense => "expense",
        TransactionType.CardPurchase => "card",
        TransactionType.Transfer => "transfer",
        _ => null
    };

    /// <summary>Fills the form with an existing entry (ADR 0026). Items/sub-categories use one item line.</summary>
    private async Task LoadForEditAsync(int id)
    {
        var t = _snapshot!.Transactions.FirstOrDefault(x => x.Id == id);
        if (t is null || KeyOf(t.Type) is not { } key)
        {
            await Ui.Alert(Loc.T("Err_EditNotAllowed"));
            await Ui.Back();
            return;
        }

        _editing = t;
        IsEditing = true;
        PageTitle = Loc.T("AddTx_EditTitle");
        Select(Types.First(x => x.Key == key));

        var category = _snapshot.Categories.FirstOrDefault(c => c.Id == t.CategoryId);
        var topId = category?.ParentId ?? category?.Id;
        SelectedCategory = Categories.FirstOrDefault(o => o.Id == topId) ?? SelectedCategory;
        SelectedAccount = Accounts.FirstOrDefault(o => o.Id == t.AccountId) ?? SelectedAccount;
        SelectedToAccount = Accounts.FirstOrDefault(o => o.Id == t.ToAccountId) ?? SelectedToAccount;
        SelectedCard = Cards.FirstOrDefault(o => o.Id == t.CardId) ?? SelectedCard;
        Date = t.Date;
        Note = t.Note ?? "";

        if (CanUseItems && (t.ItemName is not null || t.Quantity is not null || category?.ParentId is not null))
        {
            UseItems = true; // adds one line
            var line = Lines[0];
            line.SelectedSub = line.SubCategories.FirstOrDefault(o => o.Id == t.CategoryId) ?? line.SelectedSub;
            line.Load(t.ItemName, t.Quantity, t.Amount);
        }
        else
        {
            AmountText = Fmt.EditableAmount(t.Amount);
        }
    }

    [RelayCommand]
    private async Task DeleteEntry()
    {
        if (_editing is null || !await Ui.Confirm(Loc.T("Confirm_Delete"))) return;
        if (await Ui.Try(() => _finance.DeleteTransactionAsync(_editing.Id))) await Ui.Back();
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
        if (Converting && _editing is { } entry)
        {
            var how = entry.CardId is { } cardId && _snapshot is not null
                ? _snapshot.Cards.FirstOrDefault(c => c.Id == cardId)?.Name ?? ""
                : _snapshot is not null ? Display.AccountName(entry.AccountId, _snapshot) : "";
            Hint = Loc.F("Hint_ConvertToDebtOf", Fmt.Money(entry.Amount), Fmt.Date(entry.Date), how);
        }

        ShowCategory = _type is "income" or "expense" or "card";
        ShowLendByCard = _type == "lend" && Cards.Count > 0 && !IsEditing; // editing: the entry decides
        UpdatePaidWith();
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
            var keep = SelectedCategory?.Id;
            Categories = _snapshot.TopCategories(kind)
                .Select(c => new Option(c.Id, Display.CategoryName(c.Id, _snapshot)))
                .ToList();
            SelectedCategory = Categories.FirstOrDefault(c => c.Id == keep) ?? Categories.FirstOrDefault();
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

        // No duplicates (ADR 0027): an existing category is selected instead.
        if (_snapshot?.FindCategory(name, kind) is { } existing)
        {
            if (existing.ParentId is null && Categories.FirstOrDefault(c => c.Id == existing.Id) is { } option)
            {
                SelectedCategory = option;
                await Ui.Alert(Loc.F("Category_ExistsSelected", option.Display));
            }
            else await Ui.Alert(Display.CategoryExists(existing, _snapshot));
            return;
        }

        Category? created = null;
        if (!await Ui.Try(async () => created = await _finance.AddCategoryAsync(name, kind, null))) return;

        _snapshot = await _finance.LoadAsync();
        Categories = _snapshot.TopCategories(kind)
            .Select(c => new Option(c.Id, Display.CategoryName(c.Id, _snapshot)))
            .ToList();
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == created!.Id);
    }

    /// <summary>
    /// "+ Sub" on an item line: a new sub-category under the chosen category (Transport → Uber), selected
    /// on that line and offered on the other lines. A name that already exists is selected, not added again.
    /// </summary>
    private async Task NewSub(ItemLine line)
    {
        if (_snapshot is null || SelectedCategory is not { } parent) return;
        var name = await Ui.PromptText(Loc.F("NewSub_Prompt", parent.Display));
        if (name is null) return;

        if (_snapshot.FindCategory(name, CategoryKind.Expense) is { } existing)
        {
            if (line.SubCategories.FirstOrDefault(o => o.Id == existing.Id) is { } option)
            {
                line.SelectedSub = option;
                await Ui.Alert(Loc.F("Category_ExistsSelected", option.Display));
            }
            else await Ui.Alert(Display.CategoryExists(existing, _snapshot));
            return;
        }

        Category? created = null;
        if (!await Ui.Try(async () => created = await _finance.AddCategoryAsync(name, CategoryKind.Expense, parent.Id))) return;

        _snapshot = await _finance.LoadAsync();
        var subs = SubOptions();
        foreach (var l in Lines)
        {
            var keep = l.SelectedSub?.Id;
            l.SubCategories = subs;
            l.SelectedSub = subs.FirstOrDefault(o => o.Id == (l == line ? created!.Id : keep)) ?? subs.FirstOrDefault();
        }
    }

    [RelayCommand]
    private void AddLine() => Lines.Add(new ItemLine(SubOptions(), UpdateTotal, RemoveLine, NewSub));

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
            if (_editing is not null && _type is "borrow" or "lend")
            {
                // It was really lending/borrowing (ADR 0041): amount, date and account or card stay as entered.
                await _finance.ConvertToDebtAsync(_editing.Id, new PersonalDebt
                {
                    PersonName = PersonName,
                    Direction = _type == "borrow" ? DebtDirection.Borrowed : DebtDirection.Lent,
                    ExpectedReturnDate = _type == "borrow" && SelectedPayMonth is { } epm && _payMonthKeys.ElementAtOrDefault(epm.Id) is { } eMonth
                        ? Core.MonthKey.LastDay(eMonth)
                        : null,
                    Note = note
                });
            }
            else if (_editing is not null)
            {
                // Edit: one entry, from the amount box or the single item line (ADR 0026).
                var line = Lines.FirstOrDefault();
                var tx = ShowItems && line is not null
                    ? Make(Fmt.ParseMoney(line.PriceText) ?? 0, line.SelectedSub?.Id ?? SelectedCategory?.Id, line.Name, line.StoredQuantity)
                    : Make(Fmt.ParseMoney(AmountText) ?? 0, SelectedCategory?.Id, null, null);
                tx.Id = _editing.Id;
                await _finance.UpdateTransactionAsync(tx);
            }
            else if (_type is "borrow" or "lend")
            {
                await _finance.AddPersonalDebtAsync(new PersonalDebt
                {
                    PersonName = PersonName,
                    Direction = _type == "borrow" ? DebtDirection.Borrowed : DebtDirection.Lent,
                    Amount = Fmt.ParseMoney(AmountText) ?? 0,
                    Date = Date,
                    ExpectedReturnDate = _type == "borrow" && SelectedPayMonth is { } pm && _payMonthKeys.ElementAtOrDefault(pm.Id) is { } payMonth
                        ? Core.MonthKey.LastDay(payMonth)
                        : null,
                    AccountId = _type == "lend" && LendByCard ? null : SelectedAccount?.Id,
                    CardId = _type == "lend" && LendByCard ? SelectedCard?.Id : null,
                    Note = note
                });
            }
            else if (ShowItems)
            {
                // Blank lines are skipped; a line with a name but no price is an error (amount 0).
                // Qty is optional: "Uber ৳250" is a complete line (ADR 0027).
                var lines = Lines.Where(l => !string.IsNullOrWhiteSpace(l.Name) || !string.IsNullOrWhiteSpace(l.PriceText))
                    .Select(l => Make(Fmt.ParseMoney(l.PriceText) ?? 0, l.SelectedSub?.Id ?? SelectedCategory?.Id, l.Name, l.StoredQuantity))
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
