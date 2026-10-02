# 0025. Start from any previous month; see how last month carries into this one

- Status: Accepted (extends [0011](0011-monthly-budget.md), [0018](0018-simple-home-screen.md), [0024](0024-dues-page-is-the-budget-due.md))
- Date: 2026-10-03

## Context
The user wants to begin their records in an earlier month (and later possibly redo that with another
month), enter everything for it month by month, look at every page for any month, and understand
how the previous month affects the current one. Until now budgets were only created for the current
and future months, the Dues page showed only the current month, and an account's opening balance
had no date.

## Decision
- **Settings → "My data starts from"** (`AppSettings.StartMonth`, chosen on the phone). It means:
  - account opening balances are the balances **on the 1st of that month** (shown as "Balance on 1 Aug
    2026" in Add/Edit account and the account page);
  - **every month from the start month on has a budget**: `FinanceService.FillBudgetMonthsAsync(from, to)`
    gives a month without a budget the Monthly lines of the nearest earlier budget month, and months
    before the first budget the Monthly lines of the first one (filled backwards). Months that already
    have a budget are not touched. Budget, Dues and Home call it when they load.
- **Dues page gets ‹ month ›** (also `//dues?month=yyyy-MM`). For a past month, **+ Expense** saves the
  expense on the **last day of that month** (`addtx?…&date=`); a future month has no + Expense.
  The total reads "Due in September" for other months.
- **Home gets a "Last month" card** (before the next-month forecast), built from what **actually**
  happened (`FinanceSnapshot.Carry`):
  - **Saved** = income − cash/bank expenses − bills paid (orange when negative),
  - **→ on this month's card bill** = last month's card purchases,
  - **→ unpaid, carried to this month** = dues of last month or earlier still unpaid (overdue now).
  It is shown from the start month on (or whenever last month has activity) and opens last month's Dues.
- How the impact works: last month's saving is already part of the Bank Balance; its card purchases are
  this month's card bill (ADR 0012); its unpaid dues are overdue this month (ADR 0005).

## Consequences
- Data can be entered for old months with the normal forms (the Add form's date, the Pay screen's
  date, Budget/Dues ‹ ›).
- Changing a past month's Monthly budget still updates later months (ADR 0019), so entering August's
  budget also sets September and October — the intended "from now on" behaviour.
- The start month lives in the phone's preferences, not in backups; after a restore it may need to be
  set again.
- Tested: back-filling past months (one-off lines skipped, existing months kept, idempotent) and the
  month carry (actual saving, card purchases to next bill, unpaid dues carried).
