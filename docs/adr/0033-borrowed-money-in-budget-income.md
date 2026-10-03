# 0033. Borrowed money counts in the month's income on the Budget

- Status: Accepted (changes the income of the Budget plan, [0011](0011-monthly-budget.md); matches the cash flow of [0032](0032-cash-flow-details.md))
- Date: 2026-10-03

## Context
September's Budget showed Income ৳1,44,500 (salary + bonus) although ৳30,000 was borrowed from a friend and
spent that month. The user: the borrowing is a liability, but it is also money the month had — "otherwise
the question arises where this amount is used". The sheet counts it too (Total 174,500).

## Decision
- `BudgetPlan.Income` = income (or the expected income when none is recorded) **+ money borrowed and lent
  money returned in that month** (`BudgetPlan.Borrowed`; `Earned` = the part without it).
- The Budget shows the parts under "Income": "income ৳1,44,500 + borrowed ৳30,000". Save, "need to borrow",
  Home's savings follow. September: 1,74,500 − 1,15,043.66 − 65,610 = **−6,153.66**.
- The borrowing stays a liability (Liabilities → Personal) with its own pay month (ADR 0030); paying it back
  is a due in that month.
- Next month's forecast uses the expected income only: future borrowing isn't planned.

## Consequences
- Budget, Home and Reports → Cash flow agree on what came in.
- The sheet's September Save (−1,153.67) differs by a one-month item's ৳5,000, which the sheet's formula leaves out.
- Tested: a month with salary, bonus and a borrowing has income 1,74,500 with 30,000 borrowed.
