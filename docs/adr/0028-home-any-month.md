# 0028. Home of any earlier month

- Status: Accepted (extends [0018](0018-simple-home-screen.md) and [0025](0025-start-month-and-previous-months.md))
- Date: 2026-10-03

## Context
The user wants to look back at any earlier month (e.g. "last January") **in the same Home view**, not only
in Budget / Dues / Reports one page at a time.

## Decision
- Home's title is a month switcher: **‹ October 2026 ›**. Tapping the month name opens a list of every
  month from now back to the earliest of the start month, the first entry and the first budget
  (`FinanceSnapshot.MonthsUpTo`), so January is one tap away. No future months on Home (the forecast card
  already covers next month).
- When an earlier month is shown, a banner says so with **Back to this month**, and every card shows that
  month as it stood at its end:
  - **Bank Balance on 31 Jan 2026** = opening balances + entries dated up to that day
    (`FinanceSnapshot.TotalBalanceOn` / `BalanceOn`);
  - **Savings in January**, **Budget in January**, Total expenses, Due, card payment status — the same
    calculations as for this month, for that month;
  - the summary of the month before it, and the **forecast for the month after it** as of its last day.
- Every card opens its page **for that month**: Budget (`//budget?month=`), Reports (`report?month=`),
  Dues (`//dues?month=`), the summary card the Dues of the month before. Bank Balance opens Accounts
  (today's balances, with each account's full history).
- **+ Add income / expense** from an earlier month dates the entry on that month's last day (changeable).

## Consequences
- The whole picture of any past month is one screen away.
- A balance before the start month counts the opening balance as if it was there already (the opening
  balance has no date of its own); entries before it are still counted by date.
- Tested: balance at the end of a month counts only entries up to that day (card purchases never), and the
  list of months.
