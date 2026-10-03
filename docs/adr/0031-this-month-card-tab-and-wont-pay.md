# 0031. "This month" card tab, next month's card payment in parts, and "won't pay" budget items

- Status: Accepted (refines [0022](0022-last-month-card-purchases-tab.md), [0023](0023-liabilities-layout.md), [0024](0024-dues-page-is-the-budget-due.md))
- Date: 2026-10-03

## Context
1. The Cards tab listed "Purchases this cycle" under the card. The user wants them in their own tab next to
   Cards, and "Next month so far" shown in parts like the sheet: EMI 24,164.33 + Purchases 14,197 =
   38,361 — Purchases opening that tab. "You owe in total" should show this month's part too.
2. In September some budget items weren't paid for lack of money and won't be. The user wants to strike
   such an item through so it leaves the month's Total Budget and Due — and to undo it.

## Decision
- **Liabilities tabs**: Cards · **This month** · Last month / Loans · Personal (two rows so the words fit).
  This month = the purchases of the card cycle running in the month shown (`FinanceSnapshot.CyclePurchases`),
  with the bill they go on.
- **Next month so far** on the Cards tab: **EMI** (→ Loans) · **Purchases** (→ This month) · **Total**
  (`CardNextMonth`: next month's EMIs + its statement if already made, else the purchases not billed yet).
- **You owe in total** = Cards (EMIs still to pay) + **This month** + Last month + Loans + Personal, from
  `CardOwedOn`; the parts add up, also for earlier months (a statement made later isn't "last month" yet).
  The card's "limit used" is the same card total.
- **Won't pay this month**: `BudgetItem.Skipped`, set per month with `SetBudgetSkippedAsync` from Budget
  (tap the item → "Won't pay this month (strike through)" / "Will pay after all") or Dues (**Skip** on a
  not-paid row, **Undo** in the new "Not paying this month" section). A skipped item counts only what was
  already spent: the rest leaves Total Budget, Save and Due. The row is struck through and says "planned ৳x".
  Never copied to the next month.

## Consequences
- The card face shows what next month's card payment is made of.
- A month can be closed honestly: what wasn't paid and won't be is visible but no longer "due".
- Tested: card parts in September and October add up and match the limit used; a skipped item leaves the
  budget and the Due for that month only, keeps what was spent, and can be undone.
