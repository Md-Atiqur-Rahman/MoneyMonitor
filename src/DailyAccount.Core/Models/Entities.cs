using SQLite;

namespace DailyAccount.Core.Models;

// All money fields are in poisha (1 Tk = 100 poisha). See Money.

public class Account
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? NameBn { get; set; }
    public AccountType Type { get; set; }
    public long OpeningBalance { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Income or expense category. Expense categories can have one level of children
/// (Bajar → Grocery, Meat, Fish…); budgets are set on top-level categories (ADR 0011, 0013).
/// </summary>
public class Category
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? NameBn { get; set; }
    public CategoryKind Kind { get; set; }
    /// <summary>Parent category for a sub-category; null for top level.</summary>
    [Indexed] public int? ParentId { get; set; }
    public int SortOrder { get; set; }

    /// <summary>True when <paramref name="name"/> is this category's English or Bangla name (trimmed, any case).</summary>
    public static bool SameName(Category c, string name)
    {
        name = name.Trim();
        return string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)
               || (c.NameBn is not null && string.Equals(c.NameBn, name, StringComparison.OrdinalIgnoreCase));
    }
}

[Table("Transactions")]
public class Transaction
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public DateTime Date { get; set; }
    /// <summary>Always positive; the direction comes from Type.</summary>
    public long Amount { get; set; }
    public TransactionType Type { get; set; }
    [Indexed] public int? AccountId { get; set; }
    public int? ToAccountId { get; set; }
    public int? CategoryId { get; set; }
    [Indexed] public int? CardId { get; set; }
    [Indexed] public int? DueId { get; set; }
    [Indexed] public int? DebtId { get; set; }
    /// <summary>Money received for a loan (BorrowIn), so it counts in that month's income (ADR 0034).</summary>
    [Indexed] public int? LoanId { get; set; }
    public string? Note { get; set; }
    /// <summary>What was bought, e.g. "Rice" (ADR 0013).</summary>
    public string? ItemName { get; set; }
    /// <summary>Free-text quantity, e.g. "5kg", "250gm", "30".</summary>
    public string? Quantity { get; set; }
}

public class Loan
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string Lender { get; set; } = "";
    public long Principal { get; set; }
    /// <summary>Principal + interest/charges: the amount actually repaid across installments.</summary>
    public long TotalPayable { get; set; }
    public int InstallmentCount { get; set; }
    /// <summary>Month of the first installment, "yyyy-MM".</summary>
    public string StartMonth { get; set; } = "";
    public int DueDay { get; set; } = 1;
    public int? PayFromAccountId { get; set; }
    public string? Note { get; set; }
    /// <summary>Card EMI: the installments are billed on this credit card (ADR 0012).</summary>
    [Indexed] public int? CardId { get; set; }
    /// <summary>Installments already paid before the loan was entered in the app.</summary>
    public int InstallmentsPaidBefore { get; set; }
    /// <summary>
    /// A loan without installments (ADR 0034): one amount due in <see cref="StartMonth"/>, which can be moved,
    /// until it is converted into EMI.
    /// </summary>
    public bool NoInstallments { get; set; }
}

public class CreditCard
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string Name { get; set; } = "";
    public long CreditLimit { get; set; }
    /// <summary>Day of month the statement is generated (1-28).</summary>
    public int StatementDay { get; set; } = 1;
    /// <summary>Day of month the bill must be paid by.</summary>
    public int DueDay { get; set; } = 15;
    public int? PayFromAccountId { get; set; }
}

/// <summary>Superseded by budgets (ADR 0011). Kept so old data and backups still load.</summary>
public class RecurringBill
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? NameBn { get; set; }
    public long Amount { get; set; }
    public int DayOfMonth { get; set; } = 1;
    /// <summary>"yyyy-MM"</summary>
    public string StartMonth { get; set; } = "";
    /// <summary>"yyyy-MM", inclusive; null = no end.</summary>
    public string? EndMonth { get; set; }
    public bool IsActive { get; set; } = true;
}

public class PersonalDebt
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string PersonName { get; set; } = "";
    public DebtDirection Direction { get; set; }
    public long Amount { get; set; }
    public DateTime Date { get; set; }
    public DateTime? ExpectedReturnDate { get; set; }
    public int? AccountId { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// One amount payable for one month. Every liability (loan, card, bill, personal) becomes Due rows.
/// </summary>
public class Due
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed(Name = "IX_Due_Source", Order = 1, Unique = true)] public DueSource SourceType { get; set; }
    [Indexed(Name = "IX_Due_Source", Order = 2, Unique = true)] public int SourceId { get; set; }
    /// <summary>Unique per source: installment no ("3"), card cycle start ("2026-09-01"), bill month ("2026-10").</summary>
    [Indexed(Name = "IX_Due_Source", Order = 3, Unique = true)] public string PeriodKey { get; set; } = "";
    /// <summary>Installment number for loans (1-based); 0 otherwise.</summary>
    public int Sequence { get; set; }
    public string Title { get; set; } = "";
    /// <summary>"yyyy-MM"; empty = open-ended (personal borrowing without a return date).</summary>
    [Indexed] public string DueMonth { get; set; } = "";
    public DateTime? DueDate { get; set; }
    public long Amount { get; set; }
    public long PaidAmount { get; set; }
    public DueStatus Status { get; set; }

    [Ignore] public long Remaining => Amount - PaidAmount;
}

/// <summary>
/// The monthly salary from a month on (ADR 0035): added automatically as income on <see cref="Day"/> of every
/// month into <see cref="AccountId"/>. An increment is a new row from that month; Amount 0 = stopped.
/// </summary>
public class SalaryRate
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed(Unique = true)] public string FromMonth { get; set; } = "";
    public long Amount { get; set; }
    public int AccountId { get; set; }
    public int Day { get; set; } = 1;
}

/// <summary>
/// A card purchase that repeats every month by itself (ADR 0038), e.g. an AI subscription: added on
/// <see cref="Day"/> of every month from <see cref="FromMonth"/> until <see cref="StopMonth"/> (not included).
/// </summary>
public class CardSubscription
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string Name { get; set; } = "";
    public long Amount { get; set; }
    public int CardId { get; set; }
    public int? CategoryId { get; set; }
    public int Day { get; set; } = 1;
    public string FromMonth { get; set; } = "";
    /// <summary>First month without it; null = still running. Stopping never deletes earlier purchases.</summary>
    public string? StopMonth { get; set; }
}

/// <summary>Small key/value facts about the database itself, e.g. which one-time setup steps have run.</summary>
public class AppMeta
{
    [PrimaryKey] public string Key { get; set; } = "";
    public string? Value { get; set; }
}

/// <summary>Planned spending ("Estimate") for one top-level expense category in one month (ADR 0011).</summary>
public class BudgetItem
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed(Name = "IX_Budget_Month_Category", Order = 1, Unique = true)] public string Month { get; set; } = "";
    [Indexed(Name = "IX_Budget_Month_Category", Order = 2, Unique = true)] public int CategoryId { get; set; }
    public long Estimate { get; set; }
    /// <summary>
    /// true = a one-off line, not carried into the next month (ADR 0019). Stored inverted so that rows
    /// created before this column existed (NULL → false) keep repeating every month.
    /// </summary>
    public bool OnlyThisMonth { get; set; }

    /// <summary>
    /// "Won't pay this month" (ADR 0031): shown struck through; only what was already spent counts, so the
    /// rest leaves the month's budget total and its Due. Per month only — never copied to the next month.
    /// </summary>
    public bool Skipped { get; set; }
}
