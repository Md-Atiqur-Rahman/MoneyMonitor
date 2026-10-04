using DailyAccount.Core.Models;
using DailyAccount.Core.Services;

namespace DailyAccount.Core.Data;

/// <summary>
/// What is owed on a card as seen on a day (ADR 0031): this month's purchases (the cycle running that day,
/// billed or not), last month's unpaid statements, and the card EMIs still to pay.
/// </summary>
public sealed record CardOwed(long ThisMonth, long LastMonth, long Emi)
{
    public long Total => ThisMonth + LastMonth + Emi;
}

/// <summary>Next month's card payment so far: its EMIs + the purchases that go on it (ADR 0031).</summary>
public sealed record CardNext(long Emi, long Purchases)
{
    public long Total => Emi + Purchases;
}

/// <summary>A loan in one month (see <see cref="FinanceSnapshot.LoanIn"/>).</summary>
public sealed record LoanInMonth(int PaidCount, long PaidAmount, long Remaining, Due? Installment)
{
    /// <summary>
    /// Shown in that month's Loans tab (ADR 0037): still running (something left after it, or not started yet),
    /// or its installment falls in that month — so a loan finished in that month shows once, then no more.
    /// </summary>
    public bool ShowsIn => Remaining > 0 || Installment is not null;
}

/// <summary>One step of a personal debt (ADR 0040): the money given, or a part paid back.</summary>
public sealed record DebtStep(DateTime Date, long Amount, bool IsGiven, int? AccountId, int? CardId);

/// <summary>
/// A personal debt in full (ADR 0040): lent to someone or borrowed from someone, how much has come / gone back,
/// what is left, and every step with its date.
/// </summary>
public sealed record DebtLedger(PersonalDebt Debt, long Total, long PaidBack, List<DebtStep> Steps)
{
    public long Left => Math.Max(0, Total - PaidBack);
    public bool IsFullyPaid => Left == 0;
    public DateTime? LastPayment => Steps.Where(x => !x.IsGiven).Select(x => (DateTime?)x.Date).DefaultIfEmpty(null).Max();
}

/// <summary>A card's payment of one month (ADR 0037): everything due on it, what was paid, and when.</summary>
public sealed record CardMonthBill(long Billed, long Paid, DateTime? PaidOn, List<Due> Dues)
{
    public long Remaining => Billed - Paid;
    public bool IsPaid => Billed > 0 && Remaining <= 0;
}

/// <summary>What a month left behind for the next one (see <see cref="FinanceSnapshot.Carry"/>).</summary>
public sealed record MonthCarry(string Month, long Income, long CashExpenses, long BillsPaid, long Saved,
    long CardToNextBill, long CarriedDues);

/// <summary>
/// Everything in the database, loaded at once. A personal ledger holds a few thousand rows at most,
/// so the screens compute from memory (see docs/adr/0004-core-owns-data-and-in-memory-snapshot.md).
/// </summary>
public sealed record FinanceSnapshot(
    List<Account> Accounts,
    List<Category> Categories,
    List<Transaction> Transactions,
    List<Loan> Loans,
    List<CreditCard> Cards,
    List<RecurringBill> Bills,
    List<PersonalDebt> Debts,
    List<Due> Dues,
    List<BudgetItem> Budget,
    List<SalaryRate>? SalaryRates = null,
    List<CardSubscription>? Subscriptions = null)
{
    /// <summary>
    /// Everything about one personal debt (ADR 0040). Lent: given = the money out (cash/bank or card), paid back =
    /// "money returned". Borrowed: given = the money in, paid back = repayments of its due.
    /// </summary>
    public DebtLedger Ledger(PersonalDebt debt)
    {
        var steps = new List<DebtStep>();
        if (debt.Direction == DebtDirection.Lent)
        {
            steps.AddRange(Transactions
                .Where(t => t.DebtId == debt.Id && t.Type is TransactionType.LendOut or TransactionType.CardPurchase)
                .Select(t => new DebtStep(t.Date, t.Amount, true, t.AccountId, t.CardId)));
            steps.AddRange(Transactions
                .Where(t => t.DebtId == debt.Id && t.Type == TransactionType.LendReturn)
                .Select(t => new DebtStep(t.Date, t.Amount, false, t.AccountId, null)));
        }
        else
        {
            steps.AddRange(Transactions
                .Where(t => t.DebtId == debt.Id && t.Type == TransactionType.BorrowIn)
                .Select(t => new DebtStep(t.Date, t.Amount, true, t.AccountId, null)));
            var due = DueFor(DueSource.Personal, debt.Id);
            steps.AddRange(Transactions
                .Where(t => due is not null && t.DueId == due.Id && t.Type == TransactionType.DuePayment)
                .Select(t => new DebtStep(t.Date, t.Amount, false, t.AccountId, null)));
        }
        // A debt recorded without an account (borrowed in hand) has no money-in entry: its date still counts.
        if (!steps.Any(x => x.IsGiven)) steps.Add(new DebtStep(debt.Date, debt.Amount, true, debt.AccountId, debt.CardId));
        steps = [.. steps.OrderBy(x => x.Date).ThenBy(x => !x.IsGiven)];
        return new DebtLedger(debt, debt.Amount, steps.Where(x => !x.IsGiven).Sum(x => x.Amount), steps);
    }

    /// <summary>All personal debts, still open first, then the newest (Reports → Lent &amp; borrowed).</summary>
    public List<DebtLedger> Ledgers() =>
        [.. Debts.Select(Ledger).OrderBy(l => l.IsFullyPaid).ThenByDescending(l => l.Debt.Date)];

    /// <summary>The salary rate that applies in <paramref name="month"/>, if any (ADR 0035).</summary>
    public SalaryRate? SalaryRateIn(string month) =>
        (SalaryRates ?? []).Where(r => string.CompareOrdinal(r.FromMonth, month) <= 0)
            .OrderByDescending(r => r.FromMonth, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>The monthly salary of <paramref name="month"/> (0 when none / stopped).</summary>
    public long SalaryIn(string month) => SalaryRateIn(month)?.Amount ?? 0;

    /// <summary>Expected income of a month: its salary when one is set, else the fallback from Settings.</summary>
    private long Expected(string month, long fallback) => SalaryIn(month) is > 0 and var salary ? salary : fallback;

    public long Balance(Account account) => BalanceService.AccountBalance(account, Transactions);

    public long TotalBalance => BalanceService.TotalBalance(Accounts, Transactions);

    /// <summary>An account's balance at the end of <paramref name="day"/>: opening + entries dated up to that day (ADR 0028).</summary>
    public long BalanceOn(Account account, DateTime day) =>
        BalanceService.AccountBalance(account, Transactions.Where(t => t.Date.Date <= day.Date));

    /// <summary>Money in all active accounts at the end of <paramref name="day"/> (Home of a past month, ADR 0028).</summary>
    public long TotalBalanceOn(DateTime day) => Accounts.Where(a => a.IsActive).Sum(a => BalanceOn(a, day));

    /// <summary>
    /// The months that can be looked at, newest first: from <paramref name="current"/> back to the earliest of
    /// the start month, the first entry and the first budget (ADR 0028).
    /// </summary>
    public List<string> MonthsUpTo(string current, string? startMonth)
    {
        var earliest = Transactions.Select(t => MonthKey.Of(t.Date))
            .Concat(Budget.Select(b => b.Month))
            .Append(startMonth ?? current)
            .Append(current)
            .Min(StringComparer.Ordinal)!;
        var months = new List<string>();
        for (var m = current; string.CompareOrdinal(m, earliest) >= 0; m = MonthKey.Add(m, -1)) months.Add(m);
        return months;
    }

    public long UnbilledCardTotal(DateTime today) =>
        Cards.Sum(c => Unbilled(c, today));

    public long OutstandingLiabilities(DateTime today) =>
        BalanceService.OutstandingLiabilities(Dues, UnbilledCardTotal(today));

    public long Receivables => BalanceService.Receivables(Debts, Transactions);

    /// <summary>What a lent amount still has to come back.</summary>
    public long LendRemaining(PersonalDebt debt) =>
        debt.Amount - Transactions.Where(t => t.Type == TransactionType.LendReturn && t.DebtId == debt.Id).Sum(t => t.Amount);

    public MonthSummary Summary(string month) => MonthSummaryService.Summarize(month, Transactions, Dues);

    /// <summary>The month's cash flow in detail: money in, dues paid, cash expenses (ADR 0032).</summary>
    public CashFlow CashFlow(string month) => CashFlowService.Build(month, Transactions, Dues, Categories);

    public Forecast Forecast(DateTime today, long expectedIncome) =>
        ForecastService.ForNextMonth(
            MonthKey.Of(today),
            Dues,
            UnbilledCardTotal(today),
            MonthSummaryService.AverageMonthlySpending(Transactions, MonthKey.Of(today)),
            TotalBalance,
            expectedIncome);

    public BudgetPlan Plan(string month, long expectedIncome) =>
        BudgetService.Build(month, Categories, Transactions, Budget, Dues, Loans, Expected(month, expectedIncome));

    /// <summary>
    /// Next month's plan (ADR 0015). Income = expected income from Settings, or this month's
    /// actual income when that isn't set (<paramref name="incomeFromThisMonth"/> is then true).
    /// </summary>
    public (BudgetPlan Plan, bool BudgetCopied, bool IncomeFromThisMonth) NextMonthPlan(DateTime today, long expectedIncome)
    {
        var month = MonthKey.Of(today);
        var nextMonth = MonthKey.Add(month, 1);
        expectedIncome = Expected(nextMonth, expectedIncome);
        var income = expectedIncome > 0 ? expectedIncome : Summary(month).Income;
        var unbilled = Cards.ToDictionary(c => c.Id, c => Unbilled(c, today));
        var (plan, copied) = BudgetService.BuildForecast(
            MonthKey.Add(month, 1), Categories, Transactions, Budget, Dues, Loans, unbilled, income);
        return (plan, copied, expectedIncome == 0);
    }

    public Due? DueFor(DueSource source, int sourceId) =>
        Dues.FirstOrDefault(d => d.SourceType == source && d.SourceId == sourceId);

    // ---------- Credit card bill = statements + card EMIs (ADR 0012) ----------

    public IEnumerable<Loan> CardLoans(int cardId) => Loans.Where(l => l.CardId == cardId);

    /// <summary>Every Due billed on this card: its statements and the installments of its EMI loans.</summary>
    public IEnumerable<Due> CardDues(int cardId)
    {
        var loanIds = CardLoans(cardId).Select(l => l.Id).ToHashSet();
        return Dues.Where(d => (d.SourceType == DueSource.Card && d.SourceId == cardId)
                            || (d.SourceType == DueSource.Loan && loanIds.Contains(d.SourceId)));
    }

    /// <summary>Unpaid card dues up to and including <paramref name="month"/>, oldest first: what "Pay card bill" pays.</summary>
    public List<Due> CardBillDues(int cardId, string month) =>
        CardDues(cardId)
            .Where(d => d.DueMonth.Length > 0 && string.CompareOrdinal(d.DueMonth, month) <= 0 && d.Status != DueStatus.Paid)
            .OrderBy(d => d.DueDate).ThenBy(d => d.Id)
            .ToList();

    /// <summary>Next month's card payment so far: next month's EMIs + this cycle's purchases (not billed yet).</summary>
    public long CardNextMonthPayment(CreditCard card, DateTime today)
    {
        var next = MonthKey.Add(MonthKey.Of(today), 1);
        return CardDues(card.Id).Where(d => d.DueMonth == next).Sum(d => d.Remaining)
               + Unbilled(card, today);
    }

    /// <summary>
    /// Limit in use = everything still owed on the card: unpaid statements, the remaining EMI
    /// installments (not the original loan amount) and purchases not billed yet.
    /// </summary>
    public long CardLimitUsed(CreditCard card, DateTime today) =>
        CardDues(card.Id).Sum(d => d.Remaining) + Unbilled(card, today);

    /// <summary>
    /// Purchases of the cycle running on <paramref name="day"/> that are not on a statement yet (ADR 0030).
    /// Looking back at an earlier month, its last cycle may already be billed: then it is 0, so the purchases
    /// aren't counted twice (once on the statement, once as "not billed yet").
    /// </summary>
    private static string Key(DateTime cycleStart) => cycleStart.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Purchases of the cycle running on <paramref name="day"/>, up to that day: Liabilities → This month (ADR 0031).</summary>
    public List<Transaction> CyclePurchases(CreditCard card, DateTime day)
    {
        var start = LiabilityEngine.CycleStart(card, day);
        return Transactions.Where(t => t.Type == TransactionType.CardPurchase && t.CardId == card.Id
                                       && t.Date.Date >= start && t.Date.Date <= day.Date).ToList();
    }

    /// <summary>What is owed on a card as seen on <paramref name="day"/> (ADR 0031); the parts add up.</summary>
    public CardOwed CardOwedOn(CreditCard card, DateTime day)
    {
        var cycle = Key(LiabilityEngine.CycleStart(card, day));
        var statements = Dues.Where(d => d.SourceType == DueSource.Card && d.SourceId == card.Id).ToList();
        var thisStatement = statements.FirstOrDefault(d => d.PeriodKey == cycle);
        var loanIds = CardLoans(card.Id).Select(l => l.Id).ToHashSet();
        return new CardOwed(
            thisStatement?.Remaining ?? LiabilityEngine.UnbilledAmount(card, Transactions, day),
            // Statements of earlier cycles; later ones didn't exist yet on that day.
            statements.Where(d => string.CompareOrdinal(d.PeriodKey, cycle) < 0).Sum(d => d.Remaining),
            Dues.Where(d => d.SourceType == DueSource.Loan && loanIds.Contains(d.SourceId)).Sum(d => d.Remaining));
    }

    /// <summary>
    /// The card payment of the month after <paramref name="month"/>, so far (ADR 0031): its EMIs, plus the
    /// purchases that go on it — its statement if already made, else the purchases not billed yet.
    /// </summary>
    public CardNext CardNextMonth(CreditCard card, string month, DateTime day)
    {
        var next = MonthKey.Add(month, 1);
        var dues = CardDues(card.Id).Where(d => d.DueMonth == next).ToList();
        return new CardNext(
            dues.Where(d => d.SourceType == DueSource.Loan).Sum(d => d.Remaining),
            dues.Where(d => d.SourceType == DueSource.Card).Sum(d => d.Remaining) + Unbilled(card, day));
    }

    /// <summary>
    /// The card payment of <paramref name="month"/>: its statement + EMIs due that month, paid or not, with the
    /// date of the last payment on them. Unlike <see cref="CardBillDues"/> it doesn't lose a bill once it's paid.
    /// </summary>
    public CardMonthBill CardBillIn(CreditCard card, string month)
    {
        var dues = CardDues(card.Id).Where(d => d.DueMonth == month).ToList();
        var ids = dues.Select(d => d.Id).ToHashSet();
        var paidOn = Transactions.Where(t => t.Type == TransactionType.DuePayment && t.DueId is { } id && ids.Contains(id))
            .Select(t => (DateTime?)t.Date).DefaultIfEmpty(null).Max();
        return new CardMonthBill(dues.Sum(d => d.Amount), dues.Sum(d => d.PaidAmount), paidOn, dues);
    }

    /// <summary>
    /// The purchases a card statement is made of (ADR 0039): those of its cycle, oldest first. Reports → Cash flow
    /// → Loan &amp; Credit Card Paid → "Card · September purchases" opens them.
    /// </summary>
    public List<Transaction> StatementPurchases(Due statement)
    {
        if (statement.SourceType != DueSource.Card || Cards.FirstOrDefault(c => c.Id == statement.SourceId) is not { } card) return [];
        return Transactions
            .Where(t => t.Type == TransactionType.CardPurchase && t.CardId == card.Id
                        && Key(LiabilityEngine.CycleStart(card, t.Date)) == statement.PeriodKey)
            .OrderBy(t => t.Date).ThenBy(t => t.Id).ToList();
    }

    /// <summary>
    /// Card purchases of the month before the one running on <paramref name="day"/> — Liabilities → Last month
    /// (ADR 0037): only that one cycle, not every earlier one.
    /// </summary>
    public List<Transaction> LastCyclePurchases(CreditCard card, DateTime day)
    {
        var current = LiabilityEngine.CycleStart(card, day);
        return CyclePurchases(card, current.AddDays(-1));
    }

    public long Unbilled(CreditCard card, DateTime day)
    {
        var key = LiabilityEngine.CycleStart(card, day).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return Dues.Any(d => d.SourceType == DueSource.Card && d.SourceId == card.Id && d.PeriodKey == key)
            ? 0
            : LiabilityEngine.UnbilledAmount(card, Transactions, day);
    }

    /// <summary>
    /// A loan as seen in <paramref name="month"/> (ADR 0030): installments paid up to that month, what was
    /// still to pay after it, and the installment due in it (null when none falls in that month).
    /// </summary>
    public LoanInMonth LoanIn(Loan loan, string month)
    {
        var dues = Dues.Where(d => d.SourceType == DueSource.Loan && d.SourceId == loan.Id).OrderBy(d => d.Sequence).ToList();
        bool UpTo(Due d) => string.CompareOrdinal(d.DueMonth, month) <= 0;
        return new LoanInMonth(
            dues.Count(d => UpTo(d) && d.Status == DueStatus.Paid),
            dues.Where(UpTo).Sum(d => d.PaidAmount),
            dues.Sum(d => UpTo(d) ? d.Remaining : d.Amount),
            dues.FirstOrDefault(d => d.DueMonth == month));
    }

    /// <summary>
    /// Personal borrowing that counts in <paramref name="month"/> (ADR 0030): unpaid, with a pay month on or
    /// before it. Borrowing whose pay month hasn't come (or has none) is not part of that month's total.
    /// </summary>
    public IEnumerable<Due> PersonalDueBy(string month) =>
        Dues.Where(d => d.SourceType == DueSource.Personal && d.Remaining > 0
                        && d.DueMonth.Length > 0 && string.CompareOrdinal(d.DueMonth, month) <= 0);

    /// <summary>
    /// Everything owed as seen in <paramref name="month"/> on <paramref name="day"/>: card and loan dues,
    /// purchases not billed yet, and personal borrowing only once its pay month has come (ADR 0030).
    /// </summary>
    public long OwedIn(string month, DateTime day)
    {
        var cardDueIds = Cards.SelectMany(c => CardDues(c.Id)).Select(d => d.Id).ToHashSet();
        return Cards.Sum(c => CardOwedOn(c, day).Total)
               + Dues.Where(d => d.SourceType != DueSource.Personal && !cardDueIds.Contains(d.Id)).Sum(d => d.Remaining)
               + PersonalDueBy(month).Sum(d => d.Remaining);
    }

    /// <summary>
    /// Unpaid dues up to <paramref name="month"/> that are NOT billed on a credit card (other loans, dated
    /// personal borrowing). Together with <see cref="BudgetPlan.UnpaidBudget"/> they make the month's
    /// "Due" shown on Home and on the Dues page (ADR 0024).
    /// </summary>
    public List<Due> NonCardDuesUpTo(string month)
    {
        var cardDueIds = Cards.SelectMany(c => CardDues(c.Id)).Select(d => d.Id).ToHashSet();
        return Dues.Where(d => !cardDueIds.Contains(d.Id) && d.DueMonth.Length > 0
                               && string.CompareOrdinal(d.DueMonth, month) <= 0 && d.Status != DueStatus.Paid)
            .OrderBy(d => d.DueDate)
            .ToList();
    }

    /// <summary>The month's "Due": unpaid budget + unpaid non-card dues (ADR 0024).</summary>
    public long MonthDue(string month, long expectedIncome) =>
        Plan(month, expectedIncome).UnpaidBudget + NonCardDuesUpTo(month).Sum(d => d.Remaining);

    /// <summary>
    /// How a finished month carries into the next one (ADR 0025), from what really happened (actual,
    /// not the plan): Saved = income − bills paid − cash/bank expenses; CardToNextBill = that month's card
    /// purchases (they are on the next month's card bill); CarriedDues = dues of that month or earlier
    /// that are still unpaid (they show as overdue now).
    /// </summary>
    public MonthCarry Carry(string month)
    {
        var summary = Summary(month);
        var carried = Dues.Where(d => d.DueMonth.Length > 0 && string.CompareOrdinal(d.DueMonth, month) <= 0
                                      && d.Status != DueStatus.Paid)
            .Sum(d => d.Remaining);
        return new MonthCarry(month, summary.Income, summary.CashExpenses, summary.DuePaid, summary.Savings,
            summary.CardSpending, carried);
    }

    // ---------- Categories ----------

    public IEnumerable<Category> TopCategories(CategoryKind kind) =>
        Categories.Where(c => c.Kind == kind && c.ParentId is null).OrderBy(c => c.SortOrder).ThenBy(c => c.Name);

    public IEnumerable<Category> Children(int parentId) =>
        Categories.Where(c => c.ParentId == parentId).OrderBy(c => c.SortOrder).ThenBy(c => c.Name);

    /// <summary>
    /// The category or sub-category of this kind already called <paramref name="name"/> — in English or
    /// Bangla, any letter case (ADR 0027). Names are unique per kind across all levels.
    /// </summary>
    public Category? FindCategory(string name, CategoryKind kind, int? exceptId = null) =>
        Categories.FirstOrDefault(c => c.Kind == kind && c.Id != exceptId && Category.SameName(c, name));
}
