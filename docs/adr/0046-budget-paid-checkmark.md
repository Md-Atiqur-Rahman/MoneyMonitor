# 0046. A ✓ for paid items on the Budget page

- Status: Accepted (matches the Dues page's "Fully paid", [0024](0024-dues-page-is-the-budget-due.md))
- Date: 2026-10-05

## Context
The Dues page shows a green ✓ for a budget item that is fully paid. The Budget page showed "৳0" (or a
negative amount when overspent) in the same place, which reads less clearly.

## Decision
- On the Budget page, a budget item with nothing left shows a green **✓** instead of its "left" amount; if it
  was overspent, its note says "over by ৳x". A card / loan payment line that is paid shows ✓ too.
- Not for an item struck through as "won't pay" (ADR 0031) or an item without an estimate — those weren't
  paid.

## Consequences
- Budget and Dues mark a paid item the same way.
