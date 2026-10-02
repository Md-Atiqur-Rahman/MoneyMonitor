# 0014. One-time import of the user's spreadsheet

- Status: Accepted
- Date: 2026-10-02

> **Amended 2026-10-03 — kept private:** the sheet (`docs/*.xlsx`), the importer (`tools/DailyAccount.SheetImport/`)
> and `SheetScenarioTests.cs` contain the owner's real financial data, so they are **git-ignored** and live only on
> the owner's PC. The test project references the importer only when it exists, and the solution no longer
> lists it, so a fresh clone builds and tests without them. Names and banks in the ADRs were replaced with
> neutral ones (Bank A, Credit Card, Supermarket, Parents).

## Context
The user has been tracking October 2026 in `docs/Daily Account.xlsx` and should not have to retype it.
The app already restores JSON backups ([0007](0007-json-backup-file.md)).

## Decision
- A small console tool, `tools/DailyAccount.SheetImport`, enters the sheet's data through the real
  `FinanceService` (same validation, schedules and statements as the phone) into a temporary database
  and exports it as a normal backup file. The user restores it from Settings → Restore from file.
- The data is written as code (`SheetData.cs`) rather than parsed from the .xlsx, because the sheet
  is free-form (several tables on one sheet, dates typed as day/month but stored by Excel as
  month/day) and this import happens once.
- Interpretations confirmed with the user:
  - "Paid" counts are installments paid **before** October; **October's card payment is not paid yet**.
  - Balances are derived from the sheet: the salary account: salary − October payments;
    cash in hand **200**; the other two banks **0** (editable in the app).
- Assumptions (not in the sheet): card statement day 1, due day 15; undated October bank payments
  on 1 Oct; the October card bajar dated 2 Oct (the sheet's "10/3" is a date typo); supermarket purchases
  → Bajar › Grocery; AI subscription, VAT, restaurant and cash-from-card → Others.
- `SheetScenarioTests` imports the data into SQLite and checks the app's numbers against the sheet.

## Consequences
- The import is reproducible: `dotnet run --project tools/DailyAccount.SheetImport -- <file.json>`.
- Restoring replaces everything on the phone, so it must be done before entering new data.
- Differences from the sheet are documented and tested: EMI rounding (+0.03), the available card limit
  (uses remaining EMIs), and Bajar "left" (the sheet also deducted the 200 cash kept in hand).
