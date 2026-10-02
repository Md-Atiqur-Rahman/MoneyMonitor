# 0011. Monthly budget (Estimate / Spent / Left) like the user's sheet

- Status: Accepted (supersedes the "recurring bills" part of [0005](0005-unified-due-table-for-liabilities.md))
- Date: 2026-10-02

> **Amended 2026-10-02:** DPS and Certificate are no longer default categories (see [0013](0013-items-and-sub-categories.md)); a user who wants them adds them as ordinary budget items.

## Context
The user's real tool is a monthly spreadsheet (`docs/Daily Account.xlsx`). Its core is a budget table:
each line (Bajar, Rent, DPS, Parents, Wifi…) has an **Estimate**, what was actually paid (**Bank**) and
what's left (**Due**), and **Save = Income − all estimates − the credit-card payment**. A negative
Save means "you'll have to borrow". The v1 app had no budgets and computed savings from actual
spending only.

## Decision
- New table `BudgetItem(Month, CategoryId, Estimate)`, unique per month + category. Budgets are set on
  **top-level expense categories** only.
- **Copy forward**: when a month (current or future) is opened and has no budget, the estimates of the
  latest earlier month are copied (`FinanceService.EnsureBudgetAsync`). The user edits any line.
- `BudgetService.Build` produces the plan:
  - **Due lines**: everything payable this month from the `Due` table, grouped the way it is paid —
    one line per credit card (statement + card EMIs, [0012](0012-card-emi-loans.md)), one per other loan
    or personal debt. Estimate = due amount, Spent = paid.
  - **Budget lines**: Spent = cash/bank `Expense` in the category **and its sub-categories**.
    Card purchases are shown as "on card" but **not** subtracted: they are paid next month through
    the card line (as in the sheet), so counting them now would count them twice.
  - Spending in a category with no budget line still appears (estimate 0), so totals stay honest.
  - Income = recorded income for the month, or the expected income from Settings if none yet.
  - **Save = Income − (due lines + estimates)**; NeedToBorrow = max(0, −Save).
- UI: a **Budget tab** (Android shows max five tabs, so **Reports moved** to a button on Home).
  Home's hero now shows the plan's Save (orange when negative).
- **DPS and Certificate are ordinary expense lines** (user's choice), not savings.
- The "Monthly bills" feature is removed from the UI: rent, wifi, DPS etc. are budget lines now, and
  keeping both would double count. `RecurringBill` stays in the schema so old data/backups still load.

## Consequences
- The app's main number matches the sheet's "Save" (tested against the sheet: −4,020.36 vs −4,020.33,
  the difference is EMI rounding, see [0012](0012-card-emi-loans.md)).
- If every line of a month is removed, opening the month copies the previous budget again.
- Past months without a budget are not back-filled.
