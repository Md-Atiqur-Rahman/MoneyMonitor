# 0018. Simple Home screen: five tappable numbers

- Status: Accepted (replaces the Home layout of [0011](0011-monthly-budget.md) / [0015](0015-forecast-from-budget.md))
- Date: 2026-10-02

> **Amended 2026-10-02 (user request: "don't remove next month forecast"):** Home has a sixth card, right after Savings: **"{Next month} forecast (savings)"** — next month's Save (green, or orange + "need to borrow" when negative) with "needs ৳x of ৳y income". It opens the Budget tab, where the full forecast breakdown stays at the bottom.

> **Amended 2026-10-02 (user: "Due should be the budget amount that is not paid yet"):** Home's **Due** now
> follows the sheet's "Due" column: the sum of **estimate − spent** over the budget items (an overspent
> item counts as 0), plus loans/personal dues that are not on a credit card. The card payment is still
> excluded (own row). The card opens the **Budget tab** (each item's "Left"), no longer the Dues tab.
> Example on the phone: budget ৳65,799 − spent ৳12,180 = Due ৳53,619.

> **Amended 2026-10-02 (user request):** the "In all accounts" card is renamed **Bank Balance** (it still
> includes every account, cash too), and the **next-month forecast is the last card** on Home.
> Order now: Savings · Budget this month · Total expenses · Due · Credit card payment · Bank Balance ·
> Next month forecast.

> **Amended 2026-10-03 (user request):** the next-month forecast card shows two numbers: **Card & loan
> payments** (next month's EMIs + this cycle's card purchases) and **Assumed savings** (income − payments −
> budget; orange when negative), with "after budget ৳x, from income ৳y" underneath.

> **Amended 2026-10-03 (user request):** **Bank Balance** moved into the green card, above **Savings this
> month**: one card, two parts, each tappable as before (Bank Balance → Accounts, Savings → Budget).
> The separate Bank Balance card is gone.

## Context
The user found Home hard to understand: it showed income, planned, paid+spent, left, a due list, the
next-month forecast and a net-worth block — too many numbers, several of them overlapping.

## Decision
Home shows **only five things**, in this order, and **each card opens its details page**:

| Card | Number | Opens |
|---|---|---|
| **Savings this month** | the plan's Save (income − all planned); orange + "need to borrow ৳x" when negative | Budget tab |
| **Total expenses** | this month's spending, cash/bank + card ("cash x · card y") | Reports |
| **Due** | unpaid dues up to this month that are **not on a credit card** (loans, personal); "n pending · next …", "All paid" or "Nothing due" | Dues tab |
| **Credit card payment** (one per card) | this month's card bill (EMIs + last month's purchases) with a status chip: **Paid** (green), **Pending · due 15 Oct** / **Partly paid** (orange), **Overdue** (red), **No bill** (grey) | Liabilities (Cards section) |
| **In all accounts** | total balance, with each account listed ("Bank A ৳85,000 · Cash ৳3,820") | Accounts tab |

- **Income is not shown on Home** (user's request); it is still in Budget and Reports.
- Card dues are excluded from "Due" so nothing is counted twice.
- The **next-month forecast** moved to the bottom of the **Budget tab** (shown when viewing the current
  month). The "Where you stand" block (owe / net worth) was dropped from Home; total owed remains in
  Liabilities.
- The "+ Add income / expense" bar stays at the bottom.

## Consequences
- Each number on Home has exactly one meaning and one place to see the details.
- With several credit cards, Home shows one payment card per credit card.
- Net worth is no longer displayed anywhere; it can come back in Reports if wanted.
