# 0019. Budget items repeat every month (or only this month), and the budget total is shown

- Status: Accepted (refines the copy-forward rule of [0011](0011-monthly-budget.md))
- Date: 2026-10-02

> **Amended by [0020](0020-default-budget-and-wording.md):** the user-facing words are now **Monthly** / **Stops after this month** / **Stop from next month** / **Make monthly again** / **Delete from budget**.

## Context
The user wants a budget item (Bajar ৳15,000, Rent ৳12,000, Restaurant Bill ৳2,000…) to be set once and
then appear in every following month with the same amount — but also to be able to say "this one is
only for this month" (e.g. Eid shopping). They also want to see the month's **total budget**.

Until now a new month copied **all** lines of the latest earlier budget, and a change made after a
later month had already been opened was not carried into it.

## Decision
- `BudgetItem.OnlyThisMonth` (bool). Stored inverted ("only this month" = true) so that rows created
  before the column existed (NULL → false) keep repeating.
- **Carry-over**: a new month copies only the **repeating** lines (`EnsureBudgetAsync`); the in-memory
  next-month forecast uses the same rule.
- **Changes go forward**: `SetBudgetAsync` writes a repeating line into the given month **and every
  later month that already has a budget**; switching a line to "only this month" removes it from
  those later months.
- **Remove** (`RemoveBudgetAsync`) deletes the line from the given month and all later months, so it
  doesn't come back; earlier months keep their history.
- UI (Budget tab): adding an item asks the amount, then **"Every month (carry to next month)"** or
  **"Only this month"**. Each line is tagged "every month" / "only this month". Tapping a line offers
  *Change estimate*, *Repeat every month* / *Only this month (not next month)*, *Remove (this and
  coming months)*.
- **Totals**: the Budget tab summary shows *Card & loan payments* and *Budget items* separately, then
  *Total planned*. Home gets a **"Budget this month"** card: budget items total with
  "spent ৳x · left ৳y" (`BudgetPlan.BudgetEstimate / BudgetSpent / BudgetLeft`), opening the Budget tab.

## Consequences
- Set a budget once; it keeps working month after month until changed or removed.
- Editing an old month also overwrites the same line in later months that exist — intended for
  "from now on" changes; to change only one month, mark that line "only this month" first.
- Tested: carry-over excludes one-off lines (also in the forecast), forward updates, switching to
  one-off, removal keeps earlier months, budget totals exclude card payments.
