# 0009. Savings, spending and forecast formulas

- Status: Accepted
- Date: 2026-10-02

> **Amended by [0015](0015-forecast-from-budget.md):** the next-month forecast now comes from the budget; the "Next-month forecast" formula below is no longer shown in the app.

## Context
A card purchase in September is paid in October. Counting it as an expense in September **and**
as a payment in October would double-count it. The user also wants "how much will I save" and
"how much will I need to borrow next month".

## Decision
Two separate views, both shown in Reports:

**Spending view** (where did my money go?) = cash expenses + card purchases, in the month of purchase.

**Cash-flow view** (what's left?) — `MonthSummaryService`:
- `Savings` (saved so far) = Income − DuePayments made this month − cash Expenses
- `ProjectedSavings` = Savings − dues still unpaid (this month and overdue)
- Borrowed money and lending are **not** income or expenses.

**Next-month forecast** — `ForecastService`:
- Required = unpaid dues up to this month + next month's dues + card purchases not yet billed
  + expected spending (average of the last 3 months with data)
- Available = total balance in all accounts + expected monthly income (set in Settings)
- **Need to borrow** = max(0, Required − Available)

## Consequences
- A card purchase appears as spending once, and as a cash payment once, never as two expenses.
- The forecast is deliberately conservative: expected spending includes card purchases that are
  also partly counted as unbilled card dues.
- If expected income isn't set, the forecast assumes 0 and Home shows a hint to set it.
- Worked example (tested): income 1,00,000; dues 33,334 + 3,000; cash expenses 8,000 →
  projected savings 55,666.
