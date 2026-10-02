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
| [0013](0013-items-and-sub-categories.md) | Item-level expenses and sub-categories | Accepted (deletion added by 0016) |
| [0014](0014-sheet-import.md) | One-time import of the user's spreadsheet | Accepted |
| [0015](0015-forecast-from-budget.md) | Next-month forecast from the budget | Accepted |
| [0016](0016-delete-categories.md) | Deleting categories | Accepted |
| [0017](0017-category-group-report.md) | Category breakdown report (group-wise) | Accepted |
| [0018](0018-simple-home-screen.md) | Simple Home screen: five tappable numbers | Accepted |
| [0019](0019-repeating-budget-items.md) | Budget items repeat every month (or only this month); budget total shown | Accepted (wording amended by 0020) |
| [0020](0020-default-budget-and-wording.md) | Fixed default budget, and clearer budget wording | Accepted |
| [0021](0021-delete-billed-card-purchase.md) | Deleting a card purchase that is already on a statement | Accepted |
| [0022](0022-last-month-card-purchases-tab.md) | "Last month" tab for earlier card purchases | Accepted |

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
