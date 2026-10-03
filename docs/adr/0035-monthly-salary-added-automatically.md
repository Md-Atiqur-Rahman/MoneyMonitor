# 0035. Monthly salary, added automatically and changeable from a month

- Status: Accepted (expected income of [0015](0015-forecast-from-budget.md) now comes from the salary when set)
- Date: 2026-10-03

## Context
The salary is the same every month until an increment. Typing it every month is needless, and the
forecast's "expected monthly income" in Settings was a second place to keep the same number.

## Decision
- `SalaryRate` table: **from month, amount, pay day, account**. An increment is a new rate from its month
  (`SetSalaryAsync`); later rates are replaced by it; amount 0 = stopped from that month.
- `GenerateSalaryAsync(today)` (run with `GenerateDuesAsync`, i.e. whenever a page refreshes): for every month
  from the first rate up to today whose **pay day has come**, adds an Income entry in **Salary** to that
  account ("Salary (automatic)"), **once** — marked in `AppMeta` (`salary:yyyy-MM`).
  - A month that already has Salary income typed by hand is marked and left alone (no double salary).
  - A month whose automatic salary was deleted (e.g. not paid) is not added again.
- Changing the amount from a month also updates the automatic entries already added from that month on
  (amount, account, day); typed ones are never changed. Stopping removes the automatic ones from that month.
- **Expected income** of a month = its salary rate when set, else Settings' "Expected monthly income". So
  this month's plan before pay day and next month's forecast use the salary, increments included.
- Settings → **Monthly salary**: amount, pay day, paid into, from month (default this month), Save / Stop,
  and the list of rates ("From Sept 2026: ৳80,000 on day 1 into Bank A").
- Backups carry the rates; after a restore, months already holding a salary are not added again.

## Consequences
- Salary appears on its own in Budget income, the bank balance and Reports; only changes need input.
- Bonuses and other income are still entered by hand.
- Tested: added once a month from its pay day (and expected before it), an increment from its month updates
  added entries and the forecast, stop, typed / deleted months left alone, backup and restore.
