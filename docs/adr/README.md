# Architecture Decision Records

Every implementation decision in Daily Account is recorded here, one file per decision.
When a decision changes, add a new ADR that **supersedes** the old one (don't rewrite history).

| #    | Decision | Status |
|------|----------|--------|
| [0001](0001-dotnet-maui-android-only.md) | .NET MAUI (C#), Android only | Accepted |
| [0002](0002-money-as-poisha.md) | Store money as `long` poisha | Accepted (rounding amended by 0012) |
| [0003](0003-offline-sqlite-storage.md) | Offline SQLite storage, no internet permission | Accepted |
| [0004](0004-core-owns-data-and-in-memory-snapshot.md) | Core library owns data access; screens compute from an in-memory snapshot | Accepted |
| [0005](0005-unified-due-table-for-liabilities.md) | One `Due` table for every kind of liability | Accepted (bills superseded by 0011) |
| [0006](0006-localization-dictionary.md) | Bangla/English via a C# dictionary, not .resx | Accepted |
| [0007](0007-json-backup-file.md) | Manual JSON backup through the share sheet | Accepted |
| [0008](0008-ui-structure-and-navigation.md) | Shell tabs + MVVM (CommunityToolkit) UI structure | Accepted (tabs amended by 0011) |
| [0009](0009-spending-vs-cash-flow.md) | Savings, spending and forecast formulas | Accepted (forecast superseded by 0015) |
| [0010](0010-apk-sideload-and-signing.md) | Distribute as a side-loaded APK | Accepted |
| [0011](0011-monthly-budget.md) | Monthly budget (Estimate / Spent / Left) like the sheet | Accepted (carry-over refined by 0019) |
| [0012](0012-card-emi-loans.md) | Card EMI loans and the combined card bill | Accepted |
| [0013](0013-items-and-sub-categories.md) | Item-level expenses and sub-categories | Accepted (deletion added by 0016; qty × rate by 0027) |
| [0014](0014-sheet-import.md) | One-time import of the user's spreadsheet | Accepted |
| [0015](0015-forecast-from-budget.md) | Next-month forecast from the budget | Accepted |
| [0016](0016-delete-categories.md) | Deleting categories | Accepted |
| [0017](0017-category-group-report.md) | Category breakdown report (group-wise) | Accepted |
| [0018](0018-simple-home-screen.md) | Simple Home screen: five tappable numbers | Accepted |
| [0019](0019-repeating-budget-items.md) | Budget items repeat every month (or only this month); budget total shown | Accepted (wording amended by 0020) |
| [0020](0020-default-budget-and-wording.md) | Fixed default budget, and clearer budget wording | Accepted |
| [0021](0021-delete-billed-card-purchase.md) | Deleting a card purchase that is already on a statement | Accepted |
| [0022](0022-last-month-card-purchases-tab.md) | "Last month" tab for earlier card purchases | Accepted (layout changed by 0023) |
| [0023](0023-liabilities-layout.md) | Liabilities layout: card on top, total owed at the bottom | Accepted |
| [0024](0024-dues-page-is-the-budget-due.md) | The Dues page shows the budget "Due" (same total as Home) | Accepted |
| [0025](0025-start-month-and-previous-months.md) | Start from any previous month; last month's impact on this month | Accepted |
| [0026](0026-edit-entries.md) | Editing entries, and one list of all entries per month | Accepted |
| [0027](0027-qty-rate-and-unique-categories.md) | Qty × rate on item lines, "+ Sub", unique category names, Baby Care | Accepted |
| [0028](0028-home-any-month.md) | Home of any earlier month (‹ month ›, pick a month) | Accepted |
| [0029](0029-one-month-for-all-pages.md) | One month for all pages (Home, Budget, Dues, Reports, Accounts, Liabilities) | Accepted |
| [0030](0030-earlier-month-forecast-loans-and-pay-month.md) | Earlier months: forecast without double counting, loans per month, a pay month for borrowing | Accepted |
| [0031](0031-this-month-card-tab-and-wont-pay.md) | "This month" card tab, next month's card payment in parts, "won't pay" budget items | Accepted |
| [0032](0032-cash-flow-details.md) | Cash flow in detail: money in, dues paid, cash expenses | Accepted |
| [0033](0033-borrowed-money-in-budget-income.md) | Borrowed money counts in the month's income on the Budget | Accepted |
| [0034](0034-card-loans-with-and-without-installments.md) | Card loans with and without installments; money received counts as income | Accepted |

## Template

```markdown
# NNNN. Title

- Status: Proposed | Accepted | Superseded by NNNN
- Date: YYYY-MM-DD

## Context
What problem or force made a decision necessary?

## Decision
What we chose, stated plainly.

## Consequences
What becomes easier, what becomes harder, and what we must remember.
```
