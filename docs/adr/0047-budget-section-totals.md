# 0047. Totals for "Payments due this month" and "Spending plan"

- Status: Accepted (Budget page of [0011](0011-monthly-budget.md) / [0019](0019-repeating-budget-items.md))
- Date: 2026-10-05

## Context
The Budget page's two lists had no totals of their own; only the summary card at the top added them up.

## Decision
- **Payments due this month** shows its total (card & loan payments planned) in the heading, with "paid ৳x ·
  left ৳y" under it.
- **Spending plan** shows the total of the budget items in the heading (next to "+ Add budget item"), with
  "spent ৳x · left ৳y" (left = what is still due on the items, an overspent item counting as 0, as in Dues).
- The two totals add up to "Planned" in the summary card.

## Consequences
- Each list can be read on its own.
