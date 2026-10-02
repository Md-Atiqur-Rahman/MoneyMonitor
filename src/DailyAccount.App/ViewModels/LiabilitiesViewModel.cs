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

public sealed record LoanRow(string Title, string Subtitle, string ProgressText, double Progress, string Remaining, string NextText, ICommand? Delete)
{
    public bool CanDelete => Delete is not null;
}

public sealed record CardRow(
    string Name, string Subtitle, string LimitText, string Available, double UsedProgress,
    string ThisMonth, string ThisMonthSub, ICommand? Pay, string BillPurchases, ICommand ShowBillPurchases, ICommand ShowLoans,
    string NextMonth, string NextMonthSub,
    List<TxRow> Purchases, ICommand Edit)
{
    public bool CanPay => Pay is not null;
    public bool ThisMonthPaid => Pay is null;
    public bool HasBillPurchases => BillPurchases.Length > 0;
    public bool HasPurchases => Purchases.Count > 0;
}

/// <summary>Card purchases of one earlier cycle (month), with the bill they are on (ADR 0022).</summary>
public sealed record PurchaseGroup(string Title, string Subtitle, string Total, List<TxRow> Items);

public sealed record DebtRow(string Name, string Subtitle, string Remaining, Color RemainingColor, string ActionText, ICommand? Action)
{
    public bool CanAct => Action is not null;
}

public sealed partial class LiabilitiesViewModel(FinanceService finance) : ViewModelBase
{
    /// <summary>0 = loans, 1 = cards (shown first: that's where the EMIs live), 2 = personal,
    /// 3 = card purchases of earlier months (ADR 0022).</summary>
    [ObservableProperty] private int _section = 1;
    [ObservableProperty] private string _totalOwe = "";
    [ObservableProperty] private string _loansOwe = "";
    [ObservableProperty] private string _cardsOwe = "";
    [ObservableProperty] private string _lastMonthOwe = "";
    [ObservableProperty] private string _personalOwe = "";
    [ObservableProperty] private List<LoanRow> _loans = [];
    [ObservableProperty] private List<CardRow> _cards = [];
    [ObservableProperty] private List<DebtRow> _borrowed = [];
    [ObservableProperty] private List<DebtRow> _lent = [];
    [ObservableProperty] private List<PurchaseGroup> _pastPurchases = [];

    public bool ShowLoans => Section == 0;
    public bool ShowCards => Section == 1;
    public bool ShowOther => Section == 2;
    public bool ShowPast => Section == 3;
    public bool PastEmpty => PastPurchases.Count == 0;
    public bool BorrowedEmpty => Borrowed.Count == 0;
    public bool LentEmpty => Lent.Count == 0;

    partial void OnSectionChanged(int value)
    {
        OnPropertyChanged(nameof(ShowLoans));
        OnPropertyChanged(nameof(ShowCards));
        OnPropertyChanged(nameof(ShowOther));
        OnPropertyChanged(nameof(ShowPast));
    }

    public override async Task LoadAsync()
    {
        var today = DateTime.Today;
        await finance.GenerateDuesAsync(today);
        var s = await finance.LoadAsync();

        var cardLoanIds = s.Loans.Where(l => l.CardId is not null).Select(l => l.Id).ToHashSet();
        TotalOwe = Fmt.Money(s.OutstandingLiabilities(today));
        LoansOwe = Fmt.Money(s.Dues.Where(d => d.SourceType == DueSource.Loan && !cardLoanIds.Contains(d.SourceId)).Sum(d => d.Remaining));
        // Breakdown that adds up to the total (ADR 0023): Last month = unpaid card statements (last
        // month's purchases); Cards = remaining card EMIs + this cycle's purchases.
        var statementsOwed = s.Dues.Where(d => d.SourceType == DueSource.Card).Sum(d => d.Remaining);
        LastMonthOwe = Fmt.Money(statementsOwed);
        CardsOwe = Fmt.Money(s.Cards.Sum(c => s.CardLimitUsed(c, today)) - statementsOwed);
        PersonalOwe = Fmt.Money(s.Dues.Where(d => d.SourceType == DueSource.Personal).Sum(d => d.Remaining));

        Loans = s.Loans.Select(l => BuildLoan(l, s)).ToList();
        Cards = s.Cards.Select(c => BuildCard(c, s, today)).ToList();
        PastPurchases = s.Cards.SelectMany(c => BuildPastPurchases(c, s, today)).ToList();
        OnPropertyChanged(nameof(PastEmpty));

        Borrowed = s.Debts.Where(d => d.Direction == DebtDirection.Borrowed).Select(d =>
        {
            var due = s.DueFor(DueSource.Personal, d.Id);
            var left = due?.Remaining ?? 0;
            var sub = Loc.F("Debt_BorrowedSub", Fmt.Money(d.Amount), Fmt.Date(d.Date));
            if (d.ExpectedReturnDate is { } r) sub += " · " + Loc.F("Debt_ReturnBy", Fmt.Date(r));
            return new DebtRow(d.PersonName, sub,
                left > 0 ? Fmt.Money(left) : Loc.T("Debt_Settled"), Display.Warn,
                Loc.T("Repay"),
                left > 0 && due is not null ? new AsyncRelayCommand(() => Ui.Go($"{AppShell.Pay}?dueId={due.Id}")) : null);
        }).ToList();

        Lent = s.Debts.Where(d => d.Direction == DebtDirection.Lent).Select(d =>
        {
            var left = s.LendRemaining(d);
            return new DebtRow(d.PersonName,
                Loc.F("Debt_LentSub", Fmt.Money(d.Amount), Fmt.Date(d.Date)),
                left > 0 ? Fmt.Money(left) : Loc.T("Debt_Settled"), Display.Positive,
                Loc.T("MoneyReturned"),
                left > 0 ? new AsyncRelayCommand(() => Ui.Go($"{AppShell.Pay}?debtId={d.Id}")) : null);
        }).ToList();

        OnPropertyChanged(nameof(BorrowedEmpty));
        OnPropertyChanged(nameof(LentEmpty));
    }

    private LoanRow BuildLoan(Loan loan, FinanceSnapshot s)
    {
        var dues = s.Dues.Where(d => d.SourceType == DueSource.Loan && d.SourceId == loan.Id).OrderBy(d => d.Sequence).ToList();
        var paidCount = dues.Count(d => d.Status == DueStatus.Paid);
        var paidAmount = dues.Sum(d => d.PaidAmount);
        var next = dues.FirstOrDefault(d => d.Status != DueStatus.Paid);
        var dueIds = dues.Select(d => d.Id).ToHashSet();
        var hasAppPayments = s.Transactions.Any(t => t.DueId is { } id && dueIds.Contains(id));

        var subtitle = Loc.F("Loan_Sub", Fmt.Money(loan.TotalPayable), Fmt.Number(loan.InstallmentCount));
        if (s.Cards.FirstOrDefault(c => c.Id == loan.CardId) is { } card)
            subtitle += " · " + Loc.F("Loan_OnCard", card.Name);

        return new LoanRow(
            loan.Lender,
            subtitle,
            Loc.F("Loan_Progress", Fmt.Number(paidCount), Fmt.Number(loan.InstallmentCount)),
            loan.TotalPayable == 0 ? 0 : (double)paidAmount / loan.TotalPayable,
            Loc.T("Remaining") + " " + Fmt.Money(dues.Sum(d => d.Remaining)),
            next is null
                ? Loc.T("Loan_Done")
                : Loc.F("Loan_Next", Fmt.Money(next.Remaining), next.DueDate is { } dd ? Fmt.Date(dd) : ""),
            hasAppPayments ? null : new AsyncRelayCommand(() => DeleteLoanAsync(loan)));
    }

    private CardRow BuildCard(CreditCard card, FinanceSnapshot s, DateTime today)
    {
        var month = MonthKey.Of(today);
        var bill = s.CardBillDues(card.Id, month);
        var emi = bill.Where(d => d.SourceType == DueSource.Loan).Sum(d => d.Remaining);
        var statement = bill.Where(d => d.SourceType == DueSource.Card).Sum(d => d.Remaining);
        var used = s.CardLimitUsed(card, today);
        var cycleStart = LiabilityEngine.CycleStart(card, today);
        var next = MonthKey.Add(month, 1);
        var nextEmi = s.CardDues(card.Id).Where(d => d.SourceType == DueSource.Loan && d.DueMonth == next).Sum(d => d.Remaining);
        var unbilled = LiabilityEngine.UnbilledAmount(card, s.Transactions, today);

        // Purchases on this month's bill: the cycles of the unpaid statements (keyed by cycle start).
        var billCycles = bill.Where(d => d.SourceType == DueSource.Card).Select(d => d.PeriodKey).ToHashSet();
        var billPurchases = s.Transactions
            .Where(t => t.Type == TransactionType.CardPurchase && t.CardId == card.Id
                        && billCycles.Contains(LiabilityEngine.CycleStart(card, t.Date).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
        // Shown as one total line; the purchases themselves are in the "Last month" tab (ADR 0022).
        var billSummary = billPurchases.Count == 0 ? ""
            : Loc.F(billPurchases.Count == 1 ? "Card_BillPurchasesSummary1" : "Card_BillPurchasesSummary",
                Fmt.Number(billPurchases.Count), Fmt.Money(billPurchases.Sum(t => t.Amount)));

        var purchases = s.Transactions
            .Where(t => t.Type == TransactionType.CardPurchase && t.CardId == card.Id && t.Date.Date >= cycleStart)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Select(t => Display.ToRow(t, s, null, DeletePurchaseAsync))
            .ToList();

        return new CardRow(
            card.Name,
            Loc.F("Card_Sub", Fmt.Number(card.StatementDay), Fmt.Number(card.DueDay)),
            Loc.F("Card_LimitUsed", Fmt.Money(used), Fmt.Money(card.CreditLimit)),
            card.CreditLimit > 0 ? Loc.F("Card_Available", Fmt.Money(card.CreditLimit - used)) : "",
            card.CreditLimit > 0 ? Math.Clamp((double)used / card.CreditLimit, 0, 1) : 0,
            emi + statement > 0 ? Fmt.Money(emi + statement) : Loc.T("Card_Paid"),
            Loc.F("Due_CardBreakdown", Fmt.Money(emi), Fmt.Money(statement)) +
                (bill.FirstOrDefault()?.DueDate is { } dd ? " · " + Loc.F("DueOn", Fmt.DayMonth(dd)) : ""),
            emi + statement > 0 ? new AsyncRelayCommand(() => Ui.Go($"{AppShell.Pay}?cardId={card.Id}&month={month}")) : null,
            billSummary,
            new RelayCommand(() => Section = 3),
            new RelayCommand(() => Section = 0),
            Fmt.Money(nextEmi + unbilled),
            Loc.F("Due_CardBreakdown", Fmt.Money(nextEmi), Fmt.Money(unbilled)) + " · " +
                Loc.F("Card_NextStatement", Fmt.Date(LiabilityEngine.StatementDate(card, cycleStart))),
            purchases,
            new AsyncRelayCommand(() => Ui.Go($"{AppShell.AddCard}?id={card.Id}")));
    }

    /// <summary>
    /// Card purchases made before the current cycle, one group per cycle (newest first), each with its
    /// statement month and status. Purchases can be deleted under the rules of ADR 0021.
    /// </summary>
    private IEnumerable<PurchaseGroup> BuildPastPurchases(CreditCard card, FinanceSnapshot s, DateTime today)
    {
        var currentCycle = LiabilityEngine.CycleStart(card, today);
        return s.Transactions
            .Where(t => t.Type == TransactionType.CardPurchase && t.CardId == card.Id && t.Date.Date < currentCycle)
            .GroupBy(t => LiabilityEngine.CycleStart(card, t.Date))
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var key = g.Key.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                var statement = s.Dues.FirstOrDefault(d => d.SourceType == DueSource.Card && d.SourceId == card.Id && d.PeriodKey == key);
                var billMonth = MonthKey.Of(LiabilityEngine.StatementDate(card, g.Key));
                var status = statement is null ? "" : " · " + Display.Status(statement.Status);
                return new PurchaseGroup(
                    $"{Fmt.Month(MonthKey.Of(g.Key))} · {card.Name}",
                    Loc.F("Card_OnBill", Fmt.MonthName(billMonth)) + status,
                    Fmt.Money(g.Sum(t => t.Amount)),
                    g.OrderBy(t => t.Date).ThenBy(t => t.Id).Select(t => Display.ToRow(t, s, null, DeletePurchaseAsync)).ToList());
            });
    }

    private async Task DeleteLoanAsync(Loan loan)
    {
        if (!await Ui.Confirm(Loc.T("Confirm_Delete"))) return;
        if (await Ui.Try(() => finance.DeleteLoanAsync(loan.Id))) await LoadAsync();
    }

    private async Task DeletePurchaseAsync(Transaction t)
    {
        if (!await Ui.Confirm(Loc.T("Confirm_Delete"))) return;
        if (await Ui.Try(() => finance.DeleteTransactionAsync(t.Id))) await LoadAsync();
    }

    [RelayCommand] private void ShowSection(string index) => Section = int.Parse(index);
    [RelayCommand] private Task AddLoan() => Ui.Go(AppShell.AddLoan);
    [RelayCommand] private Task AddCard() => Ui.Go(AppShell.AddCard);
    [RelayCommand] private Task Borrow() => Ui.Go($"{AppShell.AddTransaction}?type=borrow");
    [RelayCommand] private Task Lend() => Ui.Go($"{AppShell.AddTransaction}?type=lend");
}
