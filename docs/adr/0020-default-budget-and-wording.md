# 0020. Fixed default budget, and clearer budget wording

- Status: Accepted (amends [0019](0019-repeating-budget-items.md) wording and [0013](0013-items-and-sub-categories.md) DPS removal)
- Date: 2026-10-02

## Context
1. The words introduced in 0019 ("only this month", "Remove (this and coming months)") were not clear
   to the user. They asked to reuse names the app already used in earlier features.
2. The user wants a fixed monthly budget to be present **without any input**, and to show in every
   month:

| Item | ৳ | Item | ৳ |
|---|---|---|---|
| Bajar | 18,000 | Rent | 11,350 |
| Education | 1,500 | DPS | 20,000 |
| Family | 3,000 | Electric | 2,049 |
| Medicine | 500 | Transport | 2,000 |
| Fruits | 1,500 | Others | 1,000 |
| Gas | 1,700 | Wifi | 600 |
| Mobile | 300 | **Total** | **63,499** |

## Decision
**Wording** — taken from the old monthly-bills feature ([0005](0005-unified-due-table-for-liabilities.md):
"Monthly bill", "Stop … from next month") and category deletion ([0016](0016-delete-categories.md): "Delete"):

| 0019 wording | Now |
|---|---|
| every month (tag) | **Monthly** |
| only this month (tag) | **Stops after this month** |
| Every month (carry to next month) | **Monthly (comes every month)** |
| Only this month | **Stop after this month** |
| Only this month (not next month) | **Stop from next month** |
| Repeat every month | **Make monthly again** |
| Remove (this and coming months) | **Delete from budget** — now with a confirmation that says past months stay as they were |

The behaviour of 0019 is unchanged.

**Default budget**
- The list lives in `FinanceDatabase.DefaultBudget`. `FinanceService.ApplyDefaultBudgetAsync(month)` runs
  **once per database** (marker row in the new `AppMeta` table): it adds the list to the current month
  and to later months that already have a budget, as **Monthly** lines, so the normal carry-over shows
  it in every following month.
- It **never overwrites** a line the user already has (their amount wins) and keeps the user's own
  lines (e.g. Restaurant Bill). Missing categories are created.
- Home and the Budget tab call it when they load.
- Categories: **DPS** is a default category again (the user had asked to remove it, but now wants it in
  the default budget — the latest request wins). **Education** is a new default. **Certificate**
  stays removed. The sheet importer creates only Certificate (and the people) itself now.
- The amounts are an example: every line can be changed, stopped from next month or deleted.

## Consequences
- A fresh install, or the user's phone after this update, shows a 13-line monthly budget (৳63,499)
  immediately; on the phone, the existing Bajar and Rent amounts are kept and Restaurant Bill stays.
- Restoring a backup does not run the default again (the marker table is not part of backups and is
  not cleared by a restore).
- Tested: applied once, 13 lines / ৳63,499, repeats into the next month, keeps user amounts and
  lines, re-creates a deleted DPS category.
