using System.Globalization;
using DailyAccount.Core.Models;

namespace DailyAccount.Core.Services;

/// <summary>
/// Turns every kind of liability into Due rows, and applies payments to Dues.
/// Pure logic: callers load data from the DB, pass it in, and persist what comes back.
/// Every generator is idempotent: it only returns Dues whose PeriodKey does not exist yet.
/// </summary>
public static class LiabilityEngine
{
    // ---------- Loans ----------

    /// <summary>
    /// Splits TotalPayable into equal monthly installments, rounded down to the poisha like a bank EMI.
    /// The last installment absorbs the remainder: 50,000 / 6 = 8,333.33 × 5 + 8,333.35.
    /// The first <see cref="Loan.InstallmentsPaidBefore"/> installments are marked Paid without a
    /// payment transaction: they were paid before the loan was entered (ADR 0012).
    /// </summary>
    public static List<Due> BuildLoanSchedule(Loan loan)
    {
        if (loan.InstallmentCount <= 0) throw new ArgumentException("InstallmentCount must be > 0.");
        if (loan.TotalPayable <= 0) throw new ArgumentException("TotalPayable must be > 0.");
        if (loan.InstallmentsPaidBefore < 0 || loan.InstallmentsPaidBefore > loan.InstallmentCount)
            throw new ArgumentException("InstallmentsPaidBefore out of range.");

        var perInstallment = loan.TotalPayable / loan.InstallmentCount;
        var dues = new List<Due>(loan.InstallmentCount);

        for (var i = 1; i <= loan.InstallmentCount; i++)
        {
            var month = MonthKey.Add(loan.StartMonth, i - 1);
            var amount = i < loan.InstallmentCount
                ? perInstallment
                : loan.TotalPayable - perInstallment * (loan.InstallmentCount - 1);
            var paidBefore = i <= loan.InstallmentsPaidBefore;

            dues.Add(new Due
            {
                SourceType = DueSource.Loan,
                SourceId = loan.Id,
                PeriodKey = i.ToString(CultureInfo.InvariantCulture),
                Sequence = i,
                Title = $"{loan.Lender} ({i}/{loan.InstallmentCount})",
                DueMonth = month,
                DueDate = MonthKey.DayIn(month, loan.DueDay),
                Amount = amount,
                PaidAmount = paidBefore ? amount : 0,
                Status = paidBefore ? DueStatus.Paid : DueStatus.Pending
            });
        }
        return dues;
    }

    // ---------- Credit cards ----------

    /// <summary>Start (inclusive) of the statement cycle that contains <paramref name="date"/>.</summary>
    public static DateTime CycleStart(CreditCard card, DateTime date)
    {
        var d = date.Date;
        var thisMonthStatement = MonthKey.DayIn(MonthKey.Of(d), card.StatementDay);
        return d >= thisMonthStatement
            ? thisMonthStatement
            : MonthKey.DayIn(MonthKey.Of(d.AddMonths(-1)), card.StatementDay);
    }

    /// <summary>Statement date = end (exclusive) of the cycle = next cycle's start.</summary>
    public static DateTime StatementDate(CreditCard card, DateTime cycleStart) =>
        MonthKey.DayIn(MonthKey.Of(cycleStart.AddMonths(1)), card.StatementDay);

    /// <summary>
    /// For every closed cycle (statement date &lt;= today) that has purchases and no Due yet,
    /// returns one Due for the cycle's total. Statement on 1 Oct for Sep purchases → DueMonth "2026-10".
    /// </summary>
    public static List<Due> BuildCardStatements(
        CreditCard card, IEnumerable<Transaction> transactions, IEnumerable<Due> existingDues, DateTime today)
    {
        var existingKeys = existingDues
            .Where(d => d.SourceType == DueSource.Card && d.SourceId == card.Id)
            .Select(d => d.PeriodKey)
            .ToHashSet();

        var result = new List<Due>();
        var cycles = transactions
            .Where(t => t.Type == TransactionType.CardPurchase && t.CardId == card.Id)
            .GroupBy(t => CycleStart(card, t.Date));

        foreach (var cycle in cycles.OrderBy(g => g.Key))
        {
            var statementDate = StatementDate(card, cycle.Key);
            if (statementDate > today.Date) continue; // still open

            var key = cycle.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (existingKeys.Contains(key)) continue;

            var total = cycle.Sum(t => t.Amount);
            if (total <= 0) continue;

            // If DueDay falls before the statement day, the bill is due the following month.
            var dueDate = MonthKey.DayIn(MonthKey.Of(statementDate), card.DueDay);
            if (dueDate < statementDate) dueDate = MonthKey.DayIn(MonthKey.Of(statementDate.AddMonths(1)), card.DueDay);

            result.Add(new Due
            {
                SourceType = DueSource.Card,
                SourceId = card.Id,
                PeriodKey = key,
                Title = $"{card.Name} bill ({cycle.Key:MMM yyyy})",
                DueMonth = MonthKey.Of(statementDate),
                DueDate = dueDate,
                Amount = total,
                Status = DueStatus.Pending
            });
        }
        return result;
    }

    /// <summary>
    /// Keeps existing statements equal to the purchases of their cycle (ADR 0021): a purchase entered or
    /// deleted after the statement was made changes the statement. A statement never goes below what
    /// was already paid on it; one with no purchases and no payment is removed.
    /// </summary>
    public static (List<Due> Updated, List<Due> Removed) ReconcileCardStatements(
        CreditCard card, IEnumerable<Transaction> transactions, IEnumerable<Due> existingDues)
    {
        var totals = transactions
            .Where(t => t.Type == TransactionType.CardPurchase && t.CardId == card.Id)
            .GroupBy(t => CycleStart(card, t.Date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var updated = new List<Due>();
        var removed = new List<Due>();
        foreach (var due in existingDues.Where(d => d.SourceType == DueSource.Card && d.SourceId == card.Id))
        {
            var total = Math.Max(totals.GetValueOrDefault(due.PeriodKey), due.PaidAmount);
            if (total == due.Amount) continue;
            if (total == 0)
            {
                removed.Add(due);
                continue;
            }
            due.Amount = total;
            due.Status = due.PaidAmount == 0 ? DueStatus.Pending
                : due.PaidAmount >= due.Amount ? DueStatus.Paid : DueStatus.Partial;
            updated.Add(due);
        }
        return (updated, removed);
    }

    /// <summary>Purchases in the cycle still open on <paramref name="today"/>. They become next statement's bill.</summary>
    public static long UnbilledAmount(CreditCard card, IEnumerable<Transaction> transactions, DateTime today)
    {
        var start = CycleStart(card, today);
        return transactions
            .Where(t => t.Type == TransactionType.CardPurchase && t.CardId == card.Id && t.Date.Date >= start)
            .Sum(t => t.Amount);
    }

    // ---------- Recurring bills ----------

    /// <summary>Dues for each month from the bill's StartMonth through <paramref name="throughMonth"/>.</summary>
    public static List<Due> BuildBillDues(RecurringBill bill, IEnumerable<Due> existingDues, string throughMonth)
    {
        var result = new List<Due>();
        if (!bill.IsActive) return result;

        var existingKeys = existingDues
            .Where(d => d.SourceType == DueSource.Bill && d.SourceId == bill.Id)
            .Select(d => d.PeriodKey)
            .ToHashSet();

        var last = bill.EndMonth is { Length: > 0 } end && string.CompareOrdinal(end, throughMonth) < 0
            ? end
            : throughMonth;

        for (var month = bill.StartMonth; string.CompareOrdinal(month, last) <= 0; month = MonthKey.Add(month, 1))
        {
            if (existingKeys.Contains(month)) continue;
            result.Add(new Due
            {
                SourceType = DueSource.Bill,
                SourceId = bill.Id,
                PeriodKey = month,
                Title = bill.Name,
                DueMonth = month,
                DueDate = MonthKey.DayIn(month, bill.DayOfMonth),
                Amount = bill.Amount,
                Status = DueStatus.Pending
            });
        }
        return result;
    }

    // ---------- Personal borrowing ----------

    /// <summary>Money borrowed from a person becomes a single Due. No return date = open-ended (DueMonth "").</summary>
    public static Due BuildPersonalDue(PersonalDebt debt)
    {
        if (debt.Direction != DebtDirection.Borrowed)
            throw new ArgumentException("Only borrowed money is a liability. Lent money is a receivable.");

        return new Due
        {
            SourceType = DueSource.Personal,
            SourceId = debt.Id,
            PeriodKey = "0",
            Title = debt.PersonName,
            DueMonth = debt.ExpectedReturnDate is { } d ? MonthKey.Of(d) : "",
            DueDate = debt.ExpectedReturnDate,
            Amount = debt.Amount,
            Status = DueStatus.Pending
        };
    }

    // ---------- Payments ----------

    /// <summary>
    /// Applies a (full or partial) payment to <paramref name="due"/> and returns the transaction to save.
    /// </summary>
    public static Transaction ApplyPayment(Due due, long amount, int accountId, DateTime date, string? note = null)
    {
        if (amount <= 0) throw new ArgumentException("Payment must be > 0.");
        if (amount > due.Remaining) throw new ArgumentException("Payment is more than the remaining amount.");

        due.PaidAmount += amount;
        due.Status = due.PaidAmount >= due.Amount ? DueStatus.Paid : DueStatus.Partial;

        return new Transaction
        {
            Date = date,
            Amount = amount,
            Type = TransactionType.DuePayment,
            AccountId = accountId,
            DueId = due.Id,
            Note = note ?? due.Title
        };
    }
}
