# 0029. One month for all pages

- Status: Accepted (extends [0028](0028-home-any-month.md); month switchers of [0025](0025-start-month-and-previous-months.md))
- Date: 2026-10-03

## Context
After [0028](0028-home-any-month.md), moving Home to September showed September on Home and Budget only.
Dues, Accounts (history) and Liabilities still showed the current month. The user expects **one** month:
"when I move to a month, only that month's info is shown" on every page.

## Decision
- **`MonthState`** (App singleton) holds the month the whole app shows. Every month switcher writes to
  it — Home, Budget, Dues, Reports, and new **‹ month ›** bars on Accounts, account history and
  Liabilities — so moving on any page moves all pages. It starts at this month on app start; each page
  with the bar has **Back to this month** when an earlier month is shown. Home shows no future month: coming
  to Home from a later month (Budget/Dues) brings everything back to this month.
- An earlier month is seen **as of its last day** (`MonthKey.AsOf`): this month and later as of today.
- **Accounts**: each balance and the total at the end of that month (`BalanceOn`), titled
  "Bank Balance on 30 Sept 2026".
- **Account history**: only that month's entries, with "On 1 Sept: balance · in ৳x · out ৳y" and the
  balance at the month's end.
- **Liabilities**:
  - Cards — that month's bill (an earlier month shows its own bill, "Paid ৳x" once paid), the purchases of
    the cycle running at the month's end, next month = the month after; the "Last month" tab lists the
    cycles before it; the limit used is as of that day.
  - Loans — the installment of that month ("September installment ৳x · paid" or "Next: ৳x on …").
  - Personal — only borrowing/lending made by the month's end.
- **Paid / unpaid is today's knowledge**: a September bill paid on 5 October shows as Paid in September's
  view. Statuses have no history of their own, and the user looks back to see what happened.

## Consequences
- One mental model: the month in the header is the month of every number in the app.
- Pages that open other pages keep the month (Budget → Dues, Home → Reports …).
- Loan progress ("n of N paid") and the total still owed count every payment known today.
- Tested (Core): the day an earlier month is seen as of; balance at a month's end (ADR 0028).
