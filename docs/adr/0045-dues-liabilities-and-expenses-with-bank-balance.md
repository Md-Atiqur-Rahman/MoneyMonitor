# 0045. Dues: liabilities and expenses as two groups, with the bank balance

- Status: Accepted (refines the Dues page of [0024](0024-dues-page-is-the-budget-due.md))
- Date: 2026-10-05

## Context
"Not paid yet" mixed budget items (expenses) and loans / personal borrowing due (liabilities) in one list
with no subtotals. The card under "Due this month" said only "Budget ৳x · spent ৳y", so whether the money in
the bank covers the due had to be worked out by hand.

## Decision
- "Not paid yet" is two groups, each with its own total in the heading:
  1. **Liabilities** — loans not on a card and personal borrowing whose pay month has come;
  2. **Expenses** — budget items with something left (+ Expense / Skip as before).
  The two totals add up to "Due this month".
- Under "Due this month": **Total estimate**, **Total spent**, **Bank Balance** (on the month's last day for an
  earlier month, now for this month) and **Left after paying the due** = bank balance − due, orange when
  the bank doesn't cover it.

## Consequences
- The page answers "what is still to pay, of which kind, and can I pay it" at a glance.
