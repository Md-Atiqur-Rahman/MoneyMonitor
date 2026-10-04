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

/// <summary>A loan in the month shown, with Pay (once due), Change month and Convert to EMI (ADR 0034).</summary>
public sealed record LoanRow(string Title, string Subtitle, string ProgressText, double Progress, string Remaining, string NextText, ICommand? Delete,
    ICommand? Pay = null, ICommand? Move = null, ICommand? Convert = null)
{
    public bool CanDelete => Delete is not null;
    public bool CanPay => Pay is not null;
    public bool CanMove => Move is not null;
    public bool CanConvert => Convert is not null;
    public bool HasActions => CanPay || CanMove || CanConvert;
}

/// <summary>
/// One card on the Cards tab. "Next month so far" (ADR 0031) = EMI (tap → Loans) + Purchases (tap → This
/// month tab) = Total.
/// </summary>
public sealed record CardRow(
    string Name, string Subtitle, string LimitText, string Available, double UsedProgress,
    string ThisMonth, string ThisMonthSub, ICommand? Pay, string BillPurchases, ICommand ShowBillPurchases, ICommand ShowLoans,
    string NextMonthTitle, string NextEmi, string NextPurchases, string NextTotal, string NextSub, ICommand ShowThisMonth,
    ICommand Edit, string PaidNote = "")
{
    /// <summary>Under "Paid": the amount and date in small text (ADR 0037).</summary>
    public bool HasPaidNote => PaidNote.Length > 0;
    public bool CanPay => Pay is not null;
    public bool ThisMonthPaid => Pay is null;
    public bool HasBillPurchases => BillPurchases.Length > 0;
}

/// <summary>A monthly card purchase added by itself (ADR 0038), with Change amount / Stop.</summary>
public sealed record SubscriptionRow(string Name, string Subtitle, string Amount, ICommand? Change, ICommand? Stop)
{
    public bool IsRunning => Stop is not null;
}

/// <summary>Card purchases of one earlier cycle (month), with the bill they are on (ADR 0022).</summary>
public sealed record PurchaseGroup(string Title, string Subtitle, string Total, List<TxRow> Items);

/// <summary>A borrowing or lending; <paramref name="Second"/> is "Change month" for borrowing (ADR 0030).</summary>
public sealed record DebtRow(string Name, string Subtitle, string Remaining, Color RemainingColor, string ActionText, ICommand? Action,
    string SecondText = "", ICommand? Second = null, string Progress = "", ICommand? Open = null)
{
    /// <summary>"Paid back ৳3,000 of ৳5,000 · left ৳2,000" / "Fully paid on 9 Oct" (ADR 0040).</summary>
    public bool HasProgress => Progress.Length > 0;
    public bool CanAct => Action is not null;
    public bool HasSecond => Second is not null;
}

/// <summary>
/// Cards, loans and personal debts for the month shown (ADR 0029): that month's card bill and purchases,
/// the installment due in it, and what was owed at its end. Paid/unpaid is what is known today.
/// </summary>
public sealed partial class LiabilitiesViewModel(FinanceService finance, MonthState months) : ViewModelBase
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

    /// <summary>0 = loans, 1 = cards (shown first: that's where the EMIs live), 2 = personal,
    /// 3 = card purchases of earlier months (ADR 0022), 4 = this month's card purchases (ADR 0031).</summary>
    [ObservableProperty] private int _section = 1;
    [ObservableProperty] private string _totalOwe = "";
    [ObservableProperty] private string _thisMonthOwe = "";
    [ObservableProperty] private List<PurchaseGroup> _thisMonthPurchases = [];
    [ObservableProperty] private string _loansOwe = "";
    [ObservableProperty] private string _cardsOwe = "";
    [ObservableProperty] private string _lastMonthOwe = "";
    [ObservableProperty] private string _personalOwe = "";
    [ObservableProperty] private List<LoanRow> _loans = [];
    [ObservableProperty] private List<CardRow> _cards = [];
    [ObservableProperty] private List<DebtRow> _borrowed = [];
    [ObservableProperty] private List<DebtRow> _lent = [];
    [ObservableProperty] private List<SubscriptionRow> _subscriptions = [];
    public bool SubscriptionsEmpty => Subscriptions.Count == 0;
    [ObservableProperty] private List<PurchaseGroup> _pastPurchases = [];

    public bool ShowLoans => Section == 0;
    public bool ShowCards => Section == 1;
    public bool ShowOther => Section == 2;
    public bool ShowPast => Section == 3;
    public bool ShowThis => Section == 4;
    public bool PastEmpty => PastPurchases.Count == 0;
    public bool ThisEmpty => ThisMonthPurchases.All(g => g.Items.Count == 0);
    public bool BorrowedEmpty => Borrowed.Count == 0;
    public bool LentEmpty => Lent.Count == 0;

    partial void OnSectionChanged(int value)
    {
        OnPropertyChanged(nameof(ShowLoans));
        OnPropertyChanged(nameof(ShowCards));
        OnPropertyChanged(nameof(ShowOther));
        OnPropertyChanged(nameof(ShowPast));
        OnPropertyChanged(nameof(ShowThis));
    }

    public override async Task LoadAsync()
    {
        await finance.GenerateDuesAsync(DateTime.Today);
        var s = await finance.LoadAsync();
        // An earlier month is shown as of its last day; "today" below means that day.
        var today = months.AsOf;
        var month = months.Month;
        MonthTitle = Fmt.Month(month);
        IsPastMonth = months.IsPast;

        var cardLoanIds = s.Loans.Where(l => l.CardId is not null).Select(l => l.Id).ToHashSet();
        // Personal borrowing counts only from its pay month (ADR 0030).
        TotalOwe = Fmt.Money(s.OwedIn(month, today));
        LoansOwe = Fmt.Money(s.Dues.Where(d => d.SourceType == DueSource.Loan && !cardLoanIds.Contains(d.SourceId)).Sum(d => d.Remaining));
        // Breakdown that adds up to the total (ADR 0023, 0031): This month = the purchases of the cycle
        // running then; Last month = unpaid statements of earlier cycles; Cards = card EMIs still to pay.
        var owed = s.Cards.Select(c => s.CardOwedOn(c, today)).ToList();
        ThisMonthOwe = Fmt.Money(owed.Sum(o => o.ThisMonth));
        LastMonthOwe = Fmt.Money(owed.Sum(o => o.LastMonth));
        CardsOwe = Fmt.Money(owed.Sum(o => o.Emi));
        var debtsSoFar = s.Debts.Where(d => d.Date.Date <= today).Select(d => d.Id).ToHashSet();
        PersonalOwe = Fmt.Money(s.PersonalDueBy(month).Where(d => debtsSoFar.Contains(d.SourceId)).Sum(d => d.Remaining));

        // Running loans, and a loan finished in this month; earlier finished ones are hidden, not deleted (ADR 0037).
        Loans = s.Loans.Where(l => s.LoanIn(l, month).ShowsIn).Select(l => BuildLoan(l, s, month)).ToList();
        Cards = s.Cards.Select(c => BuildCard(c, s, today, month)).ToList();
        PastPurchases = s.Cards.SelectMany(c => BuildPastPurchases(c, s, today)).ToList();
        ThisMonthPurchases = s.Cards.Select(c => BuildThisMonth(c, s, today)).ToList();
        OnPropertyChanged(nameof(PastEmpty));
        OnPropertyChanged(nameof(ThisEmpty));

        Borrowed = s.Debts.Where(d => d.Direction == DebtDirection.Borrowed && d.Date.Date <= today).Select(d =>
        {
            var due = s.DueFor(DueSource.Personal, d.Id);
            var left = due?.Remaining ?? 0;
            var payMonth = due?.DueMonth is { Length: 7 } pm ? pm : null;
            var sub = Loc.F("Debt_BorrowedSub", Fmt.Money(d.Amount), Fmt.Date(d.Date)) + " · "
                      + (payMonth is null ? Loc.T("Debt_NoPayMonth") : Loc.F("Debt_PayIn", Fmt.Month(payMonth)));
            // Pay shows once the pay month has come (ADR 0030); before that only the month can be changed.
            var payable = left > 0 && due is not null && payMonth is not null && string.CompareOrdinal(payMonth, month) <= 0;
            return new DebtRow(d.PersonName, sub,
                left > 0 ? Fmt.Money(left) : Loc.T("Debt_Settled"), Display.Warn,
                Loc.T("Repay"),
                payable ? new AsyncRelayCommand(() => Ui.Go($"{AppShell.Pay}?dueId={due!.Id}")) : null,
                Loc.T("Debt_ChangeMonth"),
                left > 0 ? new AsyncRelayCommand(() => ChangePayMonthAsync(d, payMonth)) : null,
                Display.DebtProgress(s.Ledger(d)),
                new AsyncRelayCommand(() => Ui.Go($"{AppShell.Flow}?kind=debt&debtId={d.Id}")));
        }).ToList();

        Lent = s.Debts.Where(d => d.Direction == DebtDirection.Lent && d.Date.Date <= today).Select(d =>
        {
            var left = s.LendRemaining(d);
            var how = d.CardId is { } cardId ? Loc.F("Debt_ByCard", s.Cards.FirstOrDefault(c => c.Id == cardId)?.Name ?? "")
                : Display.AccountName(d.AccountId, s);
            return new DebtRow(d.PersonName,
                Loc.F("Debt_LentSub", Fmt.Money(d.Amount), Fmt.Date(d.Date)) + (how.Length > 0 ? " · " + how : ""),
                left > 0 ? Fmt.Money(left) : Loc.T("Debt_Settled"), Display.Positive,
                Loc.T("MoneyReturned"),
                left > 0 ? new AsyncRelayCommand(() => Ui.Go($"{AppShell.Pay}?debtId={d.Id}")) : null,
                Progress: Display.DebtProgress(s.Ledger(d)),
                Open: new AsyncRelayCommand(() => Ui.Go($"{AppShell.Flow}?kind=debt&debtId={d.Id}")));
        }).ToList();

        Subscriptions = (s.Subscriptions ?? []).OrderBy(x => x.StopMonth is not null).ThenBy(x => x.Name).Select(x =>
        {
            var running = x.StopMonth is null || string.CompareOrdinal(x.StopMonth, month) > 0;
            var card = s.Cards.FirstOrDefault(c => c.Id == x.CardId)?.Name ?? "";
            var sub = Loc.F("Sub_Line", Fmt.Number(x.Day), card, Fmt.Month(x.FromMonth))
                      + (x.StopMonth is { } stop ? " · " + Loc.F("Sub_StoppedFrom", Fmt.Month(stop)) : "");
            return new SubscriptionRow(x.Name, sub, Fmt.Money(x.Amount),
                running ? new AsyncRelayCommand(() => ChangeSubscriptionAsync(x)) : null,
                running ? new AsyncRelayCommand(() => StopSubscriptionAsync(x)) : null);
        }).ToList();
        OnPropertyChanged(nameof(SubscriptionsEmpty));
        OnPropertyChanged(nameof(BorrowedEmpty));
        OnPropertyChanged(nameof(LentEmpty));
    }

    private LoanRow BuildLoan(Loan loan, FinanceSnapshot s, string month)
    {
        var dues = s.Dues.Where(d => d.SourceType == DueSource.Loan && d.SourceId == loan.Id).OrderBy(d => d.Sequence).ToList();
        // As it was in the month shown (ADR 0030): Loan-1 starting in September is "1 of 6" there.
        var inMonth = s.LoanIn(loan, month);
        var paidCount = inMonth.PaidCount;
        var paidAmount = loan.TotalPayable - inMonth.Remaining;
        var installment = inMonth.Installment;
        var next = installment
                   ?? dues.FirstOrDefault(d => d.Status != DueStatus.Paid && string.CompareOrdinal(d.DueMonth, month) > 0);
        var dueIds = dues.Select(d => d.Id).ToHashSet();
        var hasAppPayments = s.Transactions.Any(t => t.DueId is { } id && dueIds.Contains(id));

        var subtitle = loan.NoInstallments
            ? Loc.F("Loan_SubOne", Fmt.Money(loan.TotalPayable))
            : Loc.F("Loan_Sub", Fmt.Money(loan.TotalPayable), Fmt.Number(loan.InstallmentCount));
        if (s.Cards.FirstOrDefault(c => c.Id == loan.CardId) is { } card)
            subtitle += " · " + Loc.F("Loan_OnCard", card.Name);

        return new LoanRow(
            loan.Lender,
            subtitle,
            Loc.F("Loan_Progress", Fmt.Number(paidCount), Fmt.Number(loan.InstallmentCount)),
            loan.TotalPayable == 0 ? 0 : (double)paidAmount / loan.TotalPayable,
            Loc.T("Remaining") + " " + Fmt.Money(inMonth.Remaining),
            loan.NoInstallments && dues.FirstOrDefault() is { } one
                ? Loc.F(one.Status == DueStatus.Paid ? "Loan_OnePaid" : "Loan_OneDue", Fmt.Month(one.DueMonth))
                  + (one.Status != DueStatus.Paid && string.CompareOrdinal(one.DueMonth, month) > 0 ? " · " + Loc.T("Status_Pending") : "")
            : installment is null && paidCount == 0 && next is not null
                ? Loc.F("Loan_StartsIn", Fmt.Month(next.DueMonth), Fmt.Money(next.Amount))
            : installment is not null
                ? Loc.F(installment.Status == DueStatus.Paid ? "Loan_InstallmentPaid" : "Loan_InstallmentDue",
                        Fmt.Number(installment.Sequence), Fmt.Number(loan.InstallmentCount), Fmt.Money(installment.Amount),
                        installment.DueDate is { } idd ? Fmt.DayMonth(idd) : "")
                  + (installment.Sequence == loan.InstallmentCount && installment.Status == DueStatus.Paid ? " · " + Loc.T("Loan_Done") : "")
                : next is null
                    ? Loc.T("Loan_Done")
                    : Loc.F("Loan_Next", Fmt.Money(next.Remaining), next.DueDate is { } dd ? Fmt.Date(dd) : ""),
            hasAppPayments ? null : new AsyncRelayCommand(() => DeleteLoanAsync(loan)),
            // Pay once its month has come (ADR 0034); before that it is pending.
            dues.FirstOrDefault(d => d.Status != DueStatus.Paid && string.CompareOrdinal(d.DueMonth, month) <= 0) is { } payable
                ? new AsyncRelayCommand(() => Ui.Go($"{AppShell.Pay}?dueId={payable.Id}"))
                : null,
            dues.Any(d => d.PaidAmount == 0) ? new AsyncRelayCommand(() => MoveLoanAsync(loan, dues)) : null,
            loan.NoInstallments && dues.Any(d => d.Status != DueStatus.Paid) ? new AsyncRelayCommand(() => ConvertLoanAsync(loan, dues)) : null);
    }

    /// <summary>"Change month": the pay month of a loan without installments, or where an EMI's unpaid installments start.</summary>
    private async Task MoveLoanAsync(Loan loan, List<Due> dues)
    {
        var first = dues.Where(d => d.PaidAmount == 0).OrderBy(d => d.Sequence).First();
        var options = Display.MonthOptions(MonthKey.FirstDay(MonthKey.Add(MonthKey.Of(DateTime.Today), -2)));
        var choice = await Ui.Choose(Loc.T(loan.NoInstallments ? "Loan_ChoosePayMonth" : "Loan_ChooseStartMonth"), [.. options.Keys]);
        if (choice is null || !options.TryGetValue(choice, out var m) || m == first.DueMonth) return;
        if (await Ui.Try(() => finance.MoveLoanAsync(loan.Id, m))) await LoadAsync();
    }

    /// <summary>"Convert to EMI": new total (unpaid by default, can include interest), installments, from which month.</summary>
    private async Task ConvertLoanAsync(Loan loan, List<Due> dues)
    {
        var unpaid = dues.Sum(d => d.Remaining);
        var total = await Ui.PromptMoney(Loc.F("Loan_ConvertTotal", Fmt.Money(unpaid)), unpaid);
        if (total is not > 0) return;
        var count = await Ui.PromptInt(Loc.T("Loan_ConvertCount"));
        if (count is not > 0) return;
        var options = Display.MonthOptions(DateTime.Today);
        var choice = await Ui.Choose(Loc.T("Loan_ConvertFrom"), [.. options.Keys]);
        if (choice is null || !options.TryGetValue(choice, out var m)) return;
        if (await Ui.Try(() => finance.ConvertLoanToEmiAsync(loan.Id, total.Value, count.Value, m))) await LoadAsync();
    }

    private CardRow BuildCard(CreditCard card, FinanceSnapshot s, DateTime today, string month)
    {
        // An earlier month: its own bill, paid or not; this month: everything still unpaid up to it.
        var bill = string.CompareOrdinal(month, MonthKey.Of(DateTime.Today)) < 0
            ? s.CardDues(card.Id).Where(d => d.DueMonth == month).ToList()
            : s.CardBillDues(card.Id, month);
        var emi = bill.Where(d => d.SourceType == DueSource.Loan).Sum(d => d.Remaining);
        var statement = bill.Where(d => d.SourceType == DueSource.Card).Sum(d => d.Remaining);
        // The month's whole bill, paid or not: "Paid" + "৳30,000 paid on 3 Oct" (ADR 0029, 0037).
        var monthBill = s.CardBillIn(card, month);
        var paidNote = emi + statement > 0 || monthBill.Paid == 0 ? ""
            : Loc.F("Card_PaidNote", Fmt.Money(monthBill.Paid), monthBill.PaidOn is { } on ? Fmt.DayMonth(on) : "")
              + " · " + Loc.F("Due_CardBreakdown",
                  Fmt.Money(monthBill.Dues.Where(d => d.SourceType == DueSource.Loan).Sum(d => d.Amount)),
                  Fmt.Money(monthBill.Dues.Where(d => d.SourceType == DueSource.Card).Sum(d => d.Amount)));
        var used = s.CardOwedOn(card, today).Total;
        var cycleStart = LiabilityEngine.CycleStart(card, today);
        var next = MonthKey.Add(month, 1);
        var nextPay = s.CardNextMonth(card, month, today); // EMI + purchases, counted once (ADR 0030, 0031)

        // Purchases on this month's bill (paid or not): the cycles of its statements (keyed by cycle start).
        var billCycles = bill.Concat(monthBill.Dues).Where(d => d.SourceType == DueSource.Card).Select(d => d.PeriodKey).ToHashSet();
        var billPurchases = s.Transactions
            .Where(t => t.Type == TransactionType.CardPurchase && t.CardId == card.Id
                        && billCycles.Contains(LiabilityEngine.CycleStart(card, t.Date).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
        // Shown as one total line; the purchases themselves are in the "Last month" tab (ADR 0022).
        var billSummary = billPurchases.Count == 0 ? ""
            : Loc.F(billPurchases.Count == 1 ? "Card_BillPurchasesSummary1" : "Card_BillPurchasesSummary",
                Fmt.Number(billPurchases.Count), Fmt.Money(billPurchases.Sum(t => t.Amount)));

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
            Loc.F("Card_NextMonthIn", Fmt.MonthName(next)),
            Fmt.Money(nextPay.Emi),
            Fmt.Money(nextPay.Purchases),
            Fmt.Money(nextPay.Total),
            Loc.F("Card_NextStatement", Fmt.Date(LiabilityEngine.StatementDate(card, cycleStart))),
            new RelayCommand(() => Section = 4),
            new AsyncRelayCommand(() => Ui.Go($"{AppShell.AddCard}?id={card.Id}")),
            paidNote);
    }

    /// <summary>This month tab (ADR 0031): the purchases of the cycle running then, and the bill they go on.</summary>
    private PurchaseGroup BuildThisMonth(CreditCard card, FinanceSnapshot s, DateTime today)
    {
        var start = LiabilityEngine.CycleStart(card, today);
        var items = s.CyclePurchases(card, today);
        var billMonth = MonthKey.Of(LiabilityEngine.StatementDate(card, start));
        return new PurchaseGroup(
            $"{Fmt.Date(start)} – {Fmt.Date(today)} · {card.Name}",
            Loc.F("Card_OnBill", Fmt.MonthName(billMonth)),
            Fmt.Money(items.Sum(t => t.Amount)),
            items.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).Select(t => Display.ToRow(t, s, null, DeletePurchaseAsync)).ToList());
    }

    /// <summary>
    /// Card purchases made before the current cycle, one group per cycle (newest first), each with its
    /// statement month and status. Purchases can be deleted under the rules of ADR 0021.
    /// </summary>
    private IEnumerable<PurchaseGroup> BuildPastPurchases(CreditCard card, FinanceSnapshot s, DateTime today)
    {
        // Only the month before (ADR 0037): in October, September — not August and older (still in the data).
        return s.LastCyclePurchases(card, today)
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
                    // Shown only: last month's purchases can't be deleted or edited here (ADR 0037).
                    g.OrderBy(t => t.Date).ThenBy(t => t.Id).Select(t => Display.ToRow(t, s, null, DeletePurchaseAsync).ReadOnly()).ToList());
            });
    }

    /// <summary>"Change month": the month borrowed money is to be paid back, or none yet (ADR 0030).</summary>
    private async Task ChangePayMonthAsync(PersonalDebt debt, string? current)
    {
        var options = Display.PayMonthOptions(debt.Date);
        var choice = await Ui.Choose(Loc.T("Debt_ChooseMonth"), [.. options.Keys]);
        if (choice is null || !options.TryGetValue(choice, out var month) || month == current) return;
        if (await Ui.Try(() => finance.SetDebtPayMonthAsync(debt.Id, month))) await LoadAsync();
    }

    /// <summary>
    /// "+ Add" a monthly card purchase (ADR 0038): name, amount, day, card, category, first month. From then on it
    /// is added by itself every month on that day.
    /// </summary>
    [RelayCommand]
    private async Task AddSubscription()
    {
        var s = await finance.LoadAsync();
        if (s.Cards.Count == 0) { await Ui.Alert(Loc.T("NeedCardFirst")); return; }
        var name = await Ui.PromptText(Loc.T("Sub_AskName"));
        if (name is null) return;
        var amount = await Ui.PromptMoney(Loc.F("Sub_AskAmount", name));
        if (amount is not > 0) return;
        var day = await Ui.PromptInt(Loc.T("Sub_AskDay"), 1); // the 1st by default: charged as soon as a month starts
        if (day is not (>= 1 and <= 31)) { await Ui.Alert(Loc.T("Err_Day")); return; }

        var card = s.Cards[0];
        if (s.Cards.Count > 1)
        {
            var pick = await Ui.Choose(Loc.T("Card"), [.. s.Cards.Select(c => c.Name)]);
            if (pick is null) return;
            card = s.Cards.First(c => c.Name == pick);
        }
        var categories = s.TopCategories(CategoryKind.Expense).ToDictionary(c => Display.CategoryName(c.Id, s), c => c.Id);
        var category = await Ui.Choose(Loc.T("Category"), [.. categories.Keys]);
        if (category is null) return;
        var months = Display.MonthOptions(DateTime.Today.AddMonths(-3));
        var from = await Ui.Choose(Loc.T("Sub_AskFrom"), [.. months.Keys]);
        if (from is null) return;

        var sub = new CardSubscription
        {
            Name = name, Amount = amount.Value, Day = day.Value, CardId = card.Id,
            CategoryId = categories[category], FromMonth = months[from]
        };
        if (await Ui.Try(() => finance.AddSubscriptionAsync(sub, DateTime.Today))) await LoadAsync();
    }

    private async Task ChangeSubscriptionAsync(CardSubscription sub)
    {
        var amount = await Ui.PromptMoney(Loc.F("Sub_AskNewAmount", sub.Name), sub.Amount);
        if (amount is not > 0) return;
        if (await Ui.Try(() => finance.SetSubscriptionAmountAsync(sub.Id, amount.Value))) await LoadAsync();
    }

    private async Task StopSubscriptionAsync(CardSubscription sub)
    {
        var months = Display.MonthOptions(DateTime.Today);
        var from = await Ui.Choose(Loc.F("Sub_AskStop", sub.Name), [.. months.Keys]);
        if (from is null) return;
        if (await Ui.Try(() => finance.StopSubscriptionAsync(sub.Id, months[from]))) await LoadAsync();
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
