using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DailyAccount.App.Localization;
using DailyAccount.App.Services;
using DailyAccount.Core;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;
using DailyAccount.Core.Services;

namespace DailyAccount.App.ViewModels;

/// <summary>
/// New loan. Can be a card EMI (billed on a credit card) and can already be partly paid:
/// the user gives the next installment to pay and how many were paid before (ADR 0012).
/// ADR 0034: or a loan without installments (one amount in a pay month, can become EMI later); and the
/// money received now goes into an account on a date, so it counts in that month's income.
/// </summary>
public sealed partial class AddLoanViewModel : ViewModelBase
{
    private readonly FinanceService finance;
    private FinanceSnapshot? _snapshot;

    public AddLoanViewModel(FinanceService finance)
    {
        this.finance = finance;
        Kinds =
        [
            new("emi", Loc.T("LoanKind_Emi"), SelectKind),
            new("one", Loc.T("LoanKind_One"), SelectKind),
        ];
        SelectKind(Kinds[0]);
        var months = Display.MonthOptions(DateTime.Today);
        _payMonthKeys = [.. months.Values];
        PayMonths = months.Keys.Select((label, i) => new Option(i, label)).ToList();
        SelectedPayMonth = PayMonths.ElementAtOrDefault(1); // next month by default
    }

    public List<ChipOption> Kinds { get; }
    [ObservableProperty] private bool _noInstallments;
    public bool WithInstallments => !NoInstallments;
    partial void OnNoInstallmentsChanged(bool value) => OnPropertyChanged(nameof(WithInstallments));

    private void SelectKind(ChipOption option)
    {
        foreach (var k in Kinds) k.IsSelected = k == option;
        NoInstallments = option.Key == "one";
    }

    /// <summary>Without installments: the month it is to be paid (on that month's card bill).</summary>
    [ObservableProperty] private List<Option> _payMonths = [];
    [ObservableProperty] private Option? _selectedPayMonth;
    private readonly List<string> _payMonthKeys;

    /// <summary>Money received now (ADR 0034). Off for a loan that was running before ("money came earlier").</summary>
    [ObservableProperty] private bool _moneyReceived = true;
    [ObservableProperty] private Option? _receivedIn;
    [ObservableProperty] private DateTime _receivedOn = DateTime.Today;

    [ObservableProperty] private string _lender = "";
    [ObservableProperty] private string _totalText = "";
    [ObservableProperty] private string _installmentsText = "";
    [ObservableProperty] private string _paidBeforeText = "0";
    [ObservableProperty] private DateTime _nextDate = DateTime.Today;
    [ObservableProperty] private List<Option> _cards = [];
    [ObservableProperty] private Option? _selectedCard;
    [ObservableProperty] private List<Option> _accounts = [];
    [ObservableProperty] private Option? _payFrom;
    [ObservableProperty] private string _preview = "";

    public override async Task LoadAsync()
    {
        var s = _snapshot = await finance.LoadAsync();
        Accounts = Display.AccountOptions(s);
        PayFrom ??= Accounts.FirstOrDefault();
        ReceivedIn ??= Accounts.FirstOrDefault();
        Cards = [new Option(0, Loc.T("None")), .. s.Cards.Select(c => new Option(c.Id, c.Name))];

        // Defaults (user request): the first credit card instead of "None", and the next installment
        // on the card's due day (15th) of the current month. Both can still be changed.
        if (SelectedCard is null)
        {
            SelectedCard = Cards.Count > 1 ? Cards[1] : Cards[0];
            var dueDay = s.Cards.FirstOrDefault(c => c.Id == SelectedCard.Id)?.DueDay ?? 15;
            NextDate = MonthKey.DayIn(MonthKey.Of(DateTime.Today), dueDay);
        }
    }

    partial void OnTotalTextChanged(string value) => UpdatePreview();
    partial void OnInstallmentsTextChanged(string value) => UpdatePreview();

    /// <summary>Shows the exact split the app will create, e.g. "6 × ৳8,333.33, last ৳8,333.35".</summary>
    private void UpdatePreview()
    {
        var total = Fmt.ParseMoney(TotalText);
        var count = Fmt.ParseInt(InstallmentsText);
        if (total is not > 0 || count is not (> 0 and <= 600))
        {
            Preview = "";
            return;
        }
        var schedule = LiabilityEngine.BuildLoanSchedule(new Loan { TotalPayable = total.Value, InstallmentCount = count.Value, StartMonth = "2000-01" });
        Preview = Loc.F("Loan_Preview", Fmt.Number(count.Value), Fmt.Money(schedule[0].Amount), Fmt.Money(schedule[^1].Amount));
    }

    [RelayCommand]
    private async Task Save()
    {
        var total = Fmt.ParseMoney(TotalText) ?? 0;
        var paidBefore = Fmt.ParseInt(PaidBeforeText) ?? 0;
        var card = SelectedCard is { Id: > 0 } c ? _snapshot?.Cards.FirstOrDefault(x => x.Id == c.Id) : null;

        if (NoInstallments) paidBefore = 0;
        var payMonth = SelectedPayMonth is { } pm ? _payMonthKeys[pm.Id] : MonthKey.Of(DateTime.Today);
        var ok = await Ui.Try(() => finance.AddLoanAsync(new Loan
        {
            Lender = Lender,
            Principal = total,
            TotalPayable = total,
            NoInstallments = NoInstallments,
            InstallmentCount = NoInstallments ? 1 : Fmt.ParseInt(InstallmentsText) ?? 0,
            InstallmentsPaidBefore = paidBefore,
            // The next installment is number (paidBefore + 1), so the schedule starts paidBefore months earlier.
            StartMonth = NoInstallments ? payMonth : MonthKey.Add(MonthKey.Of(NextDate), -paidBefore),
            DueDay = card?.DueDay ?? NextDate.Day,
            CardId = card?.Id,
            PayFromAccountId = card?.PayFromAccountId ?? PayFrom?.Id
        }, MoneyReceived ? ReceivedIn?.Id : null, ReceivedOn));
        if (ok) await Ui.Back();
    }
}

/// <summary>New card, or edit an existing one (?id=).</summary>
public sealed partial class AddCardViewModel(FinanceService finance) : ViewModelBase, IQueryAttributable
{
    private int? _editId;
    private bool _loaded;

    [ObservableProperty] private string _title = Loc.T("AddCard_Title");
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _limitText = "";
    [ObservableProperty] private string _statementDayText = "1";
    [ObservableProperty] private string _dueDayText = "15";
    [ObservableProperty] private List<Option> _accounts = [];
    [ObservableProperty] private Option? _payFrom;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var v) && int.TryParse(v?.ToString(), out var id)) _editId = id;
    }

    public override async Task LoadAsync()
    {
        var s = await finance.LoadAsync();
        Accounts = Display.AccountOptions(s);
        if (_loaded) return;
        _loaded = true;

        if (_editId is { } id && s.Cards.FirstOrDefault(c => c.Id == id) is { } card)
        {
            Title = Loc.T("EditCard_Title");
            Name = card.Name;
            LimitText = Fmt.EditableAmount(card.CreditLimit);
            StatementDayText = card.StatementDay.ToString();
            DueDayText = card.DueDay.ToString();
            PayFrom = Accounts.FirstOrDefault(a => a.Id == card.PayFromAccountId);
        }
        PayFrom ??= Accounts.FirstOrDefault();
    }

    [RelayCommand]
    private async Task Save()
    {
        var card = new CreditCard
        {
            Id = _editId ?? 0,
            Name = Name,
            CreditLimit = Fmt.ParseMoney(LimitText) ?? 0,
            StatementDay = Fmt.ParseInt(StatementDayText) ?? 0,
            DueDay = Fmt.ParseInt(DueDayText) ?? 0,
            PayFromAccountId = PayFrom?.Id
        };
        var ok = await Ui.Try(() => _editId is null ? finance.AddCardAsync(card, DateTime.Today) : finance.UpdateCardAsync(card));
        if (ok) await Ui.Back();
    }
}
