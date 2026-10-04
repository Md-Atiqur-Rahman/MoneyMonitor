# 0048. Reports: "Due this month" is the same due as Home and Dues

- Status: Accepted (changes the cash flow of [0032](0032-cash-flow-details.md) to use the due of [0024](0024-dues-page-is-the-budget-due.md) / [0045](0045-dues-liabilities-and-expenses-with-bank-balance.md))
- Date: 2026-10-05

> **Amended 2026-10-05 (user):** the due comes before the result. Cash flow reads Money in − Loan & Credit Card
> Paid − Cash expenses − **Due this month** = **Saved so far (after dues)**, with "before paying the due: ৳x"
> under it; the separate "Saved so far" / "Projected savings" lines are merged into that one result.

## Context
Reports → Cash flow had "Dues still to pay", counted from loans, card bills and dated borrowing only. It
left out the budget items not paid yet, so it differed from "Due this month" on Home and the Dues page
(e.g. ৳30,000 against ৳68,880.92), and "Projected savings" was too optimistic.

## Decision
- The line is **"Due this month (still to pay)"** = `FinanceSnapshot.MonthDue` (liabilities + unpaid budget
  items), with "liabilities ৳x · expenses ৳y" under it; tapping it opens that month's Dues page.
- **Projected savings** = saved so far (cash flow net) − that due; the 6-month trend uses the same.

## Consequences
- One number for "still to pay" everywhere: Home, Dues, Reports.
