# 0034. Card loans with and without installments; money received counts as income

- Status: Accepted (extends [0012](0012-card-emi-loans.md); same idea as borrowing from a person, [0030](0030-earlier-month-forecast-loans-and-pay-month.md) / [0033](0033-borrowed-money-in-budget-income.md))
- Date: 2026-10-03

## Context
Money is borrowed three ways: from a person (done), a **card loan in installments**, and a **card loan
without installments**. For both card loans the money received should count in that month's income like
the 30k from a friend, and the loan should be in the Loans tab — pending until its month, then with Pay. A
loan without installments has a pay month that can change, and can later be converted into EMI from a
chosen month (with the bank's new total). Decided with the user: money received = account + date; a loan
without installments is paid on that month's card bill; Convert asks a new total (unpaid by default).

## Decision
- **New loan form**: "With installments (EMI)" / "Without installments". Without: no installment fields,
  "Pay in (month)" instead. **Money received now** (on by default): account + date → a `BorrowIn` entry
  with `Transaction.LoanId`; off for a loan that was already running (money came earlier).
- `Loan.NoInstallments`: one due (amount = total) in `StartMonth`. On a card it is part of that month's
  card bill like an EMI.
- `MoveLoanAsync(loan, month)`: the unpaid dues (nothing paid on them) start in that month, monthly after it
  — the pay month of a one-amount loan, or the effective month of an EMI. Liabilities → Loans **Change month**.
- `ConvertLoanToEmiAsync(loan, total, count, month)`: only for a loan without installments; what was paid
  stays as a closed installment 1; `total` (default: unpaid; more with interest) is split into `count`
  installments from `month` (last one takes the rounding). Liabilities → Loans **Convert to EMI**.
- Loans tab in the month shown: "Starts November 2026 · ৳10,000 a month · pending" before the first
  installment; "Pay in November 2026 · pending" for one amount; **Pay** (→ that due) once its month has
  come; Change month / Convert buttons while something is unpaid.
- Deleting a loan (only possible without payments) also removes the money it brought in.
- Cash flow → Money in shows "Loan: {bank}" with "to pay back · see Liabilities → Loans".

## Consequences
- Every way of borrowing raises that month's income and the bank balance, and becomes a liability with
  its own months.
- Tested: an EMI loan's money in, income, pending month and card payment; a one-amount loan on the card
  bill of its month and moved; converting with a new total and moving the effective month; a partly paid
  one-amount loan keeps its payment when converted.
