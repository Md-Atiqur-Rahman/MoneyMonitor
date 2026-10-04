# 0040. Lending through a card, and who owes what

- Status: Accepted (extends personal borrow/lend of [0005](0005-unified-due-table-for-liabilities.md), [0030](0030-earlier-month-forecast-loans-and-pay-month.md))
- Date: 2026-10-04

## Context
The user lent cash to a brother last month and lent money to his father through the credit card the month
before; this month both paid back. Lending was only possible from an account, and the app showed a single
"left" amount per person. The user wants a proper record: to whom, how much was paid back and when, what is
outstanding or whether it is fully paid — and the same for money he borrowed and repaid.

## Decision
- **Lend → "Paid with credit card"** (`PersonalDebt.CardId`): a card purchase linked to the debt
  (`Transaction.DebtId`, item = the person). It is on that card's bill like any purchase, but **not own
  spending** (`TransactionRules.IsSpending`: Budget "on card", spending totals and the average leave it out).
- Money coming back is entered as before: Liabilities → Personal → **Money returned** (part or all, into
  any account, with a date). Repaying borrowed money: **Repay**.
- `FinanceSnapshot.Ledger(debt)`: total, paid back, left, fully paid, and every step (given / paid back) with
  date and account or card. `Ledgers()`: all of them, open first.
- Personal tab rows show "Paid back ৳3,000 of ৳5,000 · left ৳2,000" / "Fully paid · last on 9 Oct" and the
  account or card; tapping a row opens its steps.
- Reports → **Lent & borrowed ›**: every person with lent/borrowed amount, progress and what is left; the
  totals still to come back and still owed; each row opens the steps.
- An entry that belongs to a debt (money lent through a card) can't be edited or deleted on its own.

## Consequences
- Lending is recorded however it was paid, and the bill and the person's record stay linked.
- Tested: lent by card is on the bill and not spending, and can't be deleted/edited alone; ledgers for a
  lend paid back in two parts (fully paid) and a borrow partly repaid; open ones listed first.
