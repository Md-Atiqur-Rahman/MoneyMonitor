using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DailyAccount.App.Localization;
using DailyAccount.App.Services;
using DailyAccount.Core.Data;
using DailyAccount.Core.Models;

namespace DailyAccount.App.ViewModels;

/// <summary>
/// Three modes on one screen: paying a Due (?dueId=), paying a whole card bill — statement + EMIs
/// (?cardId=&amp;month=, ADR 0012), or recording lent money coming back (?debtId=).
/// All take an amount (partial allowed), an account and a date.
/// </summary>
public sealed partial class PayViewModel(FinanceService finance) : ViewModelBase, IQueryAttributable
{
    private int? _dueId;
    private int? _debtId;
    private int? _personId; // money returned by a person, settles their lends oldest first (ADR 0042)
    private int? _cardId;
    private string _cardMonth = "";

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private bool _isDue;
    [ObservableProperty] private string _dueAmount = "";
    [ObservableProperty] private string _alreadyPaid = "";
    [ObservableProperty] private string _remainingLabel = "";
    [ObservableProperty] private string _remaining = "";
    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private string _accountLabel = "";
    [ObservableProperty] private List<Option> _accounts = [];
    [ObservableProperty] private Option? _selectedAccount;
    [ObservableProperty] private DateTime _date = DateTime.Today;
    [ObservableProperty] private string _buttonText = "";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("dueId", out var d) && int.TryParse(d?.ToString(), out var dueId)) _dueId = dueId;
        if (query.TryGetValue("debtId", out var b) && int.TryParse(b?.ToString(), out var debtId)) _debtId = debtId;
        if (query.TryGetValue("personId", out var pp) && int.TryParse(pp?.ToString(), out var personId)) _personId = personId;
        if (query.TryGetValue("cardId", out var c) && int.TryParse(c?.ToString(), out var cardId)) _cardId = cardId;
        if (query.TryGetValue("month", out var m)) _cardMonth = m?.ToString() ?? "";
    }

    public override async Task LoadAsync()
    {
        var s = await finance.LoadAsync();
        Accounts = Display.AccountOptions(s);
        int? preferredAccount = null;

        if (_cardId is { } cardId && s.Cards.FirstOrDefault(c => c.Id == cardId) is { } card)
        {
            var bill = s.CardBillDues(cardId, _cardMonth);
            var emi = bill.Where(d => d.SourceType == DueSource.Loan).Sum(d => d.Remaining);
            var statement = bill.Where(d => d.SourceType == DueSource.Card).Sum(d => d.Remaining);
            var total = emi + statement;

            IsDue = true;
            Title = Loc.F("Pay_Title", Loc.F("Due_CardPayment", card.Name));
            Subtitle = Loc.F("Due_CardBreakdown", Fmt.Money(emi), Fmt.Money(statement)) +
                       (bill.FirstOrDefault()?.DueDate is { } dd ? " · " + Loc.F("DueOn", Fmt.Date(dd)) : "");
            DueAmount = Fmt.Money(bill.Sum(d => d.Amount));
            AlreadyPaid = Fmt.Money(bill.Sum(d => d.PaidAmount));
            RemainingLabel = Loc.T("Remaining");
            Remaining = Fmt.Money(total);
            AmountText = Fmt.EditableAmount(total);
            AccountLabel = Loc.T("Pay_From");
            ButtonText = Loc.T("Pay") + " " + Fmt.Money(total);
            preferredAccount = card.PayFromAccountId;
        }
        else if (_dueId is { } dueId && s.Dues.FirstOrDefault(x => x.Id == dueId) is { } due)
        {
            IsDue = true;
            var title = Display.DueTitle(due, s);
            Title = Loc.F("Pay_Title", title);
            Subtitle = due.DueDate is { } dd ? Loc.F("DueOn", Fmt.Date(dd)) : Loc.T("Dues_NoDate");
            DueAmount = Fmt.Money(due.Amount);
            AlreadyPaid = Fmt.Money(due.PaidAmount);
            RemainingLabel = Loc.T("Remaining");
            Remaining = Fmt.Money(due.Remaining);
            AmountText = Fmt.EditableAmount(due.Remaining);
            AccountLabel = Loc.T("Pay_From");
            ButtonText = Loc.T("Pay") + " " + Fmt.Money(due.Remaining);
            preferredAccount = due.SourceType switch
            {
                DueSource.Loan => s.Loans.FirstOrDefault(l => l.Id == due.SourceId)?.PayFromAccountId,
                DueSource.Card => s.Cards.FirstOrDefault(c => c.Id == due.SourceId)?.PayFromAccountId,
                _ => null
            };
        }
        else if (_personId is { } personId && s.PeopleLedgers().FirstOrDefault(x => x.Person.Id == personId) is { } person)
        {
            IsDue = false;
            Title = Loc.F("Return_Title", person.Person.Name);
            Subtitle = Loc.F("Debt_LentTimes", Fmt.Money(person.LentTotal), Fmt.Number(person.Lent.Count));
            RemainingLabel = Loc.T("Return_Owed");
            Remaining = Fmt.Money(person.LentLeft);
            AmountText = Fmt.EditableAmount(person.LentLeft);
            AccountLabel = Loc.T("Return_Into");
            ButtonText = Loc.T("Return_Button");
            preferredAccount = person.Lent.Select(l => l.Debt.AccountId).FirstOrDefault(a => a is not null);
        }
        else if (_debtId is { } debtId && s.Debts.FirstOrDefault(x => x.Id == debtId) is { } debt)
        {
            IsDue = false;
            Title = Loc.F("Return_Title", debt.PersonName);
            Subtitle = Loc.F("Debt_LentSub", Fmt.Money(debt.Amount), Fmt.Date(debt.Date));
            RemainingLabel = Loc.T("Return_Owed");
            var left = s.LendRemaining(debt);
            Remaining = Fmt.Money(left);
            AmountText = Fmt.EditableAmount(left);
            AccountLabel = Loc.T("Return_Into");
            ButtonText = Loc.T("Return_Button");
            preferredAccount = debt.AccountId;
        }

        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == preferredAccount) ?? Accounts.FirstOrDefault();
    }

    [RelayCommand]
    private async Task Confirm()
    {
        var amount = Fmt.ParseMoney(AmountText) ?? 0;
        if (SelectedAccount is null)
        {
            await Ui.Alert(Loc.T("NeedAccountFirst"));
            return;
        }

        var accountId = SelectedAccount.Id;
        var ok = await Ui.Try(() =>
            _cardId is { } cardId ? finance.PayCardBillAsync(cardId, _cardMonth, amount, accountId, Date)
            : _dueId is { } dueId ? finance.PayDueAsync(dueId, amount, accountId, Date)
            : _personId is { } personId ? finance.ReceiveFromPersonAsync(personId, amount, accountId, Date)
            : finance.ReceiveLendReturnAsync(_debtId ?? 0, amount, accountId, Date));
        if (ok) await Ui.Back();
    }

    [RelayCommand]
    private Task Cancel() => Ui.Back();
}
