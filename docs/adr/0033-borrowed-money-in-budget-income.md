# 0033. Borrowed money counts in the month's income on the Budget

- Status: Accepted (changes the income of the Budget plan, [0011](0011-monthly-budget.md); matches the cash flow of [0032](0032-cash-flow-details.md))
- Date: 2026-10-03

## Context
September's Budget showed Income ৳1,00,000 (salary + bonus) although ৳10,000 was borrowed from a friend and
spent that month. The user: the borrowing is a liability, but it is also money the month had — "otherwise
the question arises where this amount is used". The sheet counts it too.

## Decision
- `BudgetPlan.Income` = income (or the expected income when none is recorded) **+ money borrowed and lent
  money returned in that month** (`BudgetPlan.Borrowed`; `Earned` = the part without it).
- The Budget shows the parts under "Income": "income ৳1,00,000 + borrowed ৳10,000". Save, "need to borrow",
  Home's savings follow. Example: 1,10,000 − 60,000 − 55,000 = **−5,000**.
- The borrowing stays a liability (Liabilities → Personal) with its own pay month (ADR 0030); paying it back
  is a due in that month.
- Next month's forecast uses the expected income only: future borrowing isn't planned.

## Consequences
- Budget, Home and Reports → Cash flow agree on what came in.
- The sheet's Save can differ from the app's when the sheet's formula leaves out a one-month item.
- Tested: a month with salary, bonus and a borrowing has income = earned + borrowed.
