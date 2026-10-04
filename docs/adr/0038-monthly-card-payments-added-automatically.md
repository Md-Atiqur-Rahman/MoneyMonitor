# 0038. Monthly card payments (subscriptions), added automatically

- Status: Accepted (same idea as the monthly salary, [0035](0035-monthly-salary-added-automatically.md))
- Date: 2026-10-04

## Context
Some card purchases repeat every month with the same amount, e.g. an AI subscription charged to the credit
card. The user wants to add it once (say in September) and have it paid every month after that without
entering it again.

## Decision
- `CardSubscription` table: name, amount, card, category, day of month, from month, stop month.
- `GenerateSubscriptionsAsync(today)` runs first in `GenerateDuesAsync` (every page refresh), so the
  purchases land on the statements made right after: for every month from the start whose day has come,
  one **card purchase** (item = the name, note "Automatic") is added — once, marked in `AppMeta`
  (`sub:{id}:{yyyy-MM}`).
  - A month that already has a purchase of that name on that card (typed by hand) is left alone.
  - A deleted automatic purchase is not added again.
- **Change amount** applies to the purchases still to be added; **Stop** from a month keeps every purchase
  already added. Subscriptions are never deleted (the data stays).
- Liabilities → **Personal** → "Monthly card payments": the list ("Day 20 every month · card · since
  September 2026") with **+ Add** (name, amount, day, card, category, first month), Change amount, Stop.
- Backups carry subscriptions; after a restore, months already holding the purchase are not doubled.

## Consequences
- A subscription shows up by itself in "This month", next month's card payment, Budget and Reports.
- Tested: added on its day every month and billed next month; a typed first month isn't doubled; new amount
  for later months; stop keeps earlier purchases; a deleted one isn't re-added; backup and restore.
