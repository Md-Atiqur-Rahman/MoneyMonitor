# 0024. The Dues page shows the budget "Due" (same total as Home)

- Status: Accepted (changes the Dues page of [0005](0005-unified-due-table-for-liabilities.md) / [0012](0012-card-emi-loans.md); refines the Home "Due" of [0018](0018-simple-home-screen.md))
- Date: 2026-10-03

> **Amended 2026-10-03 (user request):** **+ Expense** on the Dues page also fills in the **amount** — the
> amount still left on that budget item (e.g. Electric ৳2,049). It can be changed before saving
> (`addtx?type=expense&categoryId=…&amount=<poisha>`).

> **Amended 2026-10-03 (user request) — Budget → Due:** every budget item with something left has a
> **Due ›** button in the Budget tab (current month). It opens the Dues page with `?focus=<categoryId>`;
> that row is **highlighted and scrolled into view**, ready for **+ Expense** (amount pre-filled).

> **Amended 2026-10-03:** **Due ›** in the Budget tab is shown for the current **and past** months (data is
> entered for earlier months, ADR 0025) and opens the Dues page of **that** month (`?focus=…&month=…`).

## Context
Home's **Due** card showed the budget items not paid yet (the sheet's "Due" column: estimate − paid),
e.g. ৳53,619, while the **Dues tab** showed something else — the card bill and loan installments
(৳26,739.17). Two different meanings of "due" with two different numbers confused the user, who
decided that the Dues page is where the budget "Due" belongs.

## Decision
- One calculation in Core, used by both screens:
  - `BudgetPlan.UnpaidBudget` = Σ (estimate − spent) over the budget items, an overspent item counting
    as 0;
  - `FinanceSnapshot.NonCardDuesUpTo(month)` = unpaid loans / dated personal dues that are not on a card;
  - `FinanceSnapshot.MonthDue(month)` = the two together. Home's Due and the Dues page total are both
    this number, and Home's Due card now opens the Dues tab.
- **Dues page**, top to bottom:
  1. **Due this month** (total, budget spent bar, "Budget ৳x · spent ৳y").
  2. **Not paid yet**: each budget item with something left — "Estimate ৳x · spent ৳y", the amount
     left, and **+ Expense**, which opens the Add form with that category already chosen. Non-card
     loans / dated personal dues follow with their Pay button.
  3. **Fully paid**: items with nothing left (✓), with "over by ৳x" when overspent.
  4. **Card & loan payments** — a separate section, **not part of the Due total** (as in the sheet):
     this month's card payment with Pay, borrowing without a date, paid card bills, and next month's
     dues (Upcoming, no Pay).

## Consequences
- "Due" means one thing everywhere: what is still to be paid from the month's budget.
- The card bill keeps its own places to pay it: Dues → Card & loan payments, Liabilities → Cards,
  and Home's credit card row.
- Tested: unpaid budget ignores overspending; a non-card loan is added; a card EMI is not.
