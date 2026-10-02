namespace DailyAccount.Core.Models;

public enum AccountType
{
    Bank,
    Cash,
    MobileWallet
}

public enum CategoryKind
{
    Income,
    Expense
}

public enum TransactionType
{
    /// <summary>Money in to an account (salary, bonus).</summary>
    Income,
    /// <summary>Money out of an account (food, transport).</summary>
    Expense,
    /// <summary>Account → ToAccount. Not income or expense.</summary>
    Transfer,
    /// <summary>Purchase on a credit card. No account changes now; billed on the next statement.</summary>
    CardPurchase,
    /// <summary>Payment against a Due (loan installment, card bill, bill, personal borrowing).</summary>
    DuePayment,
    /// <summary>Money borrowed from a person, received into an account.</summary>
    BorrowIn,
    /// <summary>Money lent to a person, paid out of an account.</summary>
    LendOut,
    /// <summary>Lent money returned to an account.</summary>
    LendReturn
}

public enum DueSource
{
    Loan,
    Card,
    Bill,
    Personal
}

public enum DueStatus
{
    Pending,
    Partial,
    Paid
}

public enum DebtDirection
{
    Borrowed,
    Lent
}
