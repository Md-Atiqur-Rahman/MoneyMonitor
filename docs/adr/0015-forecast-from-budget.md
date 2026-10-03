# 0015. Next-month forecast from the budget

- Status: Accepted (supersedes the forecast part of [0009](0009-spending-vs-cash-flow.md))
- Date: 2026-10-02

## Context
After the budget ([0011](0011-monthly-budget.md)) was added, Home showed two answers that looked
contradictory: this month's plan said "need to borrow ৳4,000" while the old forecast said
"November: no need to borrow". The old forecast estimated spending as the average of past months —
with only one month of history that was just September's card purchases (৳10,000) — and ignored
the budget the user actually plans with.

## Decision
The next-month forecast is **next month's plan**, computed exactly like the Budget tab and the sheet
(`BudgetService.BuildForecast`, `FinanceSnapshot.NextMonthPlan`):
- **Payments** = next month's dues (e.g. card EMIs) **+ card purchases not billed yet** (they become
  next month's statement).
- **Budget** = next month's estimates; if next month has none yet, the latest earlier budget is used
  **in memory** (nothing is saved until the user opens that month).
- **Income** = expected income from Settings; if not set, this month's actual income.
- **Save** = Income − (Payments + Budget); negative → "need to borrow".

`ForecastService` (balance-based forecast) stays in Core with its tests but is no longer shown.

## Consequences
- Home's top card (this month) and the forecast (next month) use one formula, so they can't disagree
  for different reasons. Example from the sheet: November = 26,722.67 + 66,659 = 93,381.67 needed,
  income of about 1 lakh → a small positive saving.
- The forecast does not carry over money left (or owed) from this month; like the sheet, each month
  stands on its own. Unpaid dues of this month remain visible in Dues and in this month's plan.
