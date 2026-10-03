# 0017. Category breakdown report (group-wise)

- Status: Accepted
- Date: 2026-10-02

## Context
The user wants to see, for the current month, how Bajar spending splits into its groups
(Grocery, Meat, Fish, Vegetables, Tiffin, Cosmetics) and what was bought in each — the way the sheet
lists "Bajar on cash" and "Bajar on credit card" item by item.

## Decision
- **Reports → Spending by category: every category row is tappable** (shown with "›") and opens a
  **category breakdown** screen (`categoryreport?id=&month=`), not only Bajar.
- `ReportService.Breakdown` (Core, tested) returns, for one main category and month, one group per
  sub-category (and one for entries placed directly on the main category), largest first. Each group
  has its **cash/bank** total, its **card** total and its items.
- It uses the **spending view** ([0009](0009-spending-vs-cash-flow.md)): cash/bank expenses **and** card
  purchases of that month. If the category has a budget line, the screen also shows
  "Budget · spent · left" with the Budget tab's rule (spent = cash/bank only, [0011](0011-monthly-budget.md)).
- Items show name · qty, date and what paid (account or card name). The screen has its own month
  switcher.

## Consequences
- Checked against the sheet for October: Bajar cash 4,000 + card 3,000; Cosmetics 2,000,
  Grocery 2,165, Meat 1,600, Fish 1,050, Vegetables 500, Tiffin 120.
- The breakdown total can differ from the Budget tab's "Spent" for the same category because card
  purchases are included here; the screen shows both numbers so the difference is visible.
- **Entries without a type land in a group named after the main category.** An expense saved with
  only "Bajar" (single amount, no item-by-item type) shows as group "Bajar", not "Grocery". To get
  real groups, use **Enter item by item** and pick a type (Grocery, Meat, Fish…) on each line. The
  app does not guess a group from the note or item name.

## Verification on the phone (2026-10-02)
- October: Bajar total ৳830 (cash ৳830 · card ৳0), "Budget ৳18,000 · spent ৳830 · left ৳17,170"
  (same as the Budget tab); group Grocery ৳830 with Eggs · 30 (৳380) and Rice · 5kg (৳450), Cash.
- September (via ‹): total ৳1,500, all on card; the Supermarket entry, saved without a type, appears
  under group "Bajar", paid with "Credit Card".
