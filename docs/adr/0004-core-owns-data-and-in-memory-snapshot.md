# 0004. Core library owns data access; screens compute from an in-memory snapshot

- Status: Accepted
- Date: 2026-10-02

## Context
The calculations (installments, card statements, savings, forecast) are the heart of the app and
must be trustworthy. MAUI projects can't be unit-tested easily on a PC. A typical repository-per-
entity layer would add a lot of code for a database that holds, at most, a few thousand rows.

## Decision
- `DailyAccount.Core` (plain `net10.0`, no MAUI dependency) contains:
  - `Models/` entities, `Money`, `MonthKey`;
  - `Services/` **pure, static** calculators: `LiabilityEngine`, `BalanceService`,
    `MonthSummaryService`, `ForecastService`;
  - `Data/` `FinanceDatabase` (connection + schema), `FinanceService` (every read/write use case),
    `FinanceSnapshot`, `BackupData`, `FinanceException`.
- `FinanceService.LoadAsync()` loads **all tables into a `FinanceSnapshot`**; view models compute
  what they show from it in memory.
- Multi-row writes (pay a due, add a loan with its schedule, restore) run inside one SQLite
  transaction (`RunInTransactionAsync`).
- Broken rules throw `FinanceException(key)` where the key is a translation key ("Err_Amount"),
  so Core stays language-neutral and the UI translates.
- `DailyAccount.Core.Tests` (xUnit) covers the calculators **and** runs `FinanceService` end-to-end
  against a real temporary SQLite file.

## Consequences
- All business logic is testable on the PC (`dotnet test`) without a phone or emulator.
- The App project only formats and displays; view models stay thin.
- Loading everything per screen is O(rows). Fine for years of personal data (thousands of rows);
  if it ever gets slow, add targeted queries in `FinanceService` without changing the screens' API.
