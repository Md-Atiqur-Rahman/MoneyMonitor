# 0030. Earlier months: forecast without double counting, loans per month, a pay month for borrowing

- Status: Accepted (refines [0029](0029-one-month-for-all-pages.md); personal dues of [0005](0005-unified-due-table-for-liabilities.md) / [0024](0024-dues-page-is-the-budget-due.md))
- Date: 2026-10-03

## Context
User feedback after importing September from the sheet:
1. September's forecast for October should be the October card payment, ৳38,361.33 (EMIs 24,164.33 +
   September's card purchases 14,197). It showed more.
2. Loans in September should look like September: Loan-1 and Loan-2 start there ("1 of 6", "1 of 3"),
   Loan-3 is at 5 and Loan-4 at 3, and Loan-last (1 lac in 3) is fully paid in September.
3. Liabilities → "Last month" in September should hold August's card purchases.
4. Money borrowed (30k from a friend) goes into the bank balance and is a liability in the Personal tab, with
   the **month** it will be paid back (no day). From that month it is part of that month's total and has a
   Pay button; before it, it is not counted. The month can be changed.

## Decision
- **Not billed yet** (`FinanceSnapshot.Unbilled`) = purchases of the cycle running on the day looked at,
  **0 when that cycle already has a statement**. Seen from 30 Sept, September's purchases are on the 1 Oct
  statement, so October's forecast is EMIs + that statement, not that plus the purchases again. Used by the
  forecast, the limit used, "next month so far" and the total owed. For the current month nothing changes
  (its cycle is never billed yet).
- **A loan in a month** (`FinanceSnapshot.LoanIn`): installments paid up to that month, what was left
  after it, and that month's installment. Liabilities shows "Installment 1 of 6 · ৳8,333.33 · paid" (and
  "· Fully paid" on the last one), or "… · due 15 Oct" when unpaid; months without one show the next.
- **Pay month for borrowing**: the Borrow form asks "Pay back in (month)" — "Not decided yet" or one of the
  25 months from the borrowing month — and stores that month's last day. `SetDebtPayMonthAsync` changes
  it (Liabilities → Personal → **Change month**). A personal debt counts in a month (Liabilities total,
  Personal total, Home's Due and the Dues page via `NonCardDuesUpTo`) only **from its pay month on**
  (`PersonalDueBy`), and **Pay** shows only then. Without a month it is listed but not counted.
- Last month tab: unchanged rule (cycles before the one running on the day looked at), so September shows
  August's purchases once they are in the data.
- The September import (private tool) uses the sheet's September tab: Paid includes September, so paid
  before September = Loan-last 2, Loan-1 0, Loan-2 0, Loan-3 4, Loan-4 2.

## Consequences
- An earlier month reads like the sheet of that month.
- Amounts are whole poisha: 24,164.33 in the sheet is 24,164.32 in the app (each installment rounded, the
  last installment of a loan takes the remainder).
- Tested: September's forecast counts its purchases once (and October's unbilled purchases still go to
  November); a loan as seen in August / September / October; a pay month counts from that month, can be
  moved, and stops counting once paid.
