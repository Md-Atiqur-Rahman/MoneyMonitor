using DailyAccount.Core.Models;
using DailyAccount.Core.Services;

namespace DailyAccount.Core.Data;

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
    List<BudgetItem> Budget)
{
    public long Balance(Account account) => BalanceService.AccountBalance(account, Transactions);

    public long TotalBalance => BalanceService.TotalBalance(Accounts, Transactions);

    public long UnbilledCardTotal(DateTime today) =>
        Cards.Sum(c => LiabilityEngine.UnbilledAmount(c, Transactions, today));

    public long OutstandingLiabilities(DateTime today) =>
        BalanceService.OutstandingLiabilities(Dues, UnbilledCardTotal(today));

    public long Receivables => BalanceService.Receivables(Debts, Transactions);

    /// <summary>What a lent amount still has to come back.</summary>
    public long LendRemaining(PersonalDebt debt) =>
        debt.Amount - Transactions.Where(t => t.Type == TransactionType.LendReturn && t.DebtId == debt.Id).Sum(t => t.Amount);

    public MonthSummary Summary(string month) => MonthSummaryService.Summarize(month, Transactions, Dues);

    public Forecast Forecast(DateTime today, long expectedIncome) =>
        ForecastService.ForNextMonth(
            MonthKey.Of(today),
            Dues,
            UnbilledCardTotal(today),
            MonthSummaryService.AverageMonthlySpending(Transactions, MonthKey.Of(today)),
            TotalBalance,
            expectedIncome);

    public BudgetPlan Plan(string month, long expectedIncome) =>
        BudgetService.Build(month, Categories, Transactions, Budget, Dues, Loans, expectedIncome);

    /// <summary>
    /// Next month's plan (ADR 0015). Income = expected income from Settings, or this month's
    /// actual income when that isn't set (<paramref name="incomeFromThisMonth"/> is then true).
    /// </summary>
    public (BudgetPlan Plan, bool BudgetCopied, bool IncomeFromThisMonth) NextMonthPlan(DateTime today, long expectedIncome)
    {
        var month = MonthKey.Of(today);
        var income = expectedIncome > 0 ? expectedIncome : Summary(month).Income;
        var unbilled = Cards.ToDictionary(c => c.Id, c => LiabilityEngine.UnbilledAmount(c, Transactions, today));
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
               + LiabilityEngine.UnbilledAmount(card, Transactions, today);
    }

    /// <summary>
    /// Limit in use = everything still owed on the card: unpaid statements, the remaining EMI
    /// installments (not the original loan amount) and purchases not billed yet.
    /// </summary>
    public long CardLimitUsed(CreditCard card, DateTime today) =>
        CardDues(card.Id).Sum(d => d.Remaining) + LiabilityEngine.UnbilledAmount(card, Transactions, today);

    // ---------- Categories ----------

    public IEnumerable<Category> TopCategories(CategoryKind kind) =>
        Categories.Where(c => c.Kind == kind && c.ParentId is null).OrderBy(c => c.SortOrder).ThenBy(c => c.Name);

    public IEnumerable<Category> Children(int parentId) =>
        Categories.Where(c => c.ParentId == parentId).OrderBy(c => c.SortOrder).ThenBy(c => c.Name);
}
