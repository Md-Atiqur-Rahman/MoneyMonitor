# 0043. Lent & borrowed report in two groups

- Status: Accepted (refines the report of [0040](0040-lending-by-card-and-who-owes-what.md) / [0042](0042-people-list-for-lending-and-borrowing.md))
- Date: 2026-10-04

> **Amended 2026-10-04 (user request):** Liabilities → Personal shows the totals in the headings too:
> **You borrowed — ৳x** ("repaid ৳y · still to pay ৳z") and **You lent — ৳x** ("paid back ৳y · still to come back
> ৳z"), as of the month shown.

## Context
Reports → Lent & borrowed listed lending and borrowing mixed in one list with one total ("still to come
back"). The user wants them grouped: first everything lent with its total, then everything borrowed with its
total.

## Decision
- The page shows two groups (`FlowSection`), each a card with its own rows and total:
  1. **You lent** — one row per person ("Father (2) · lent ৳x · nothing paid back yet", what is left),
     total **Still to come back**, and "Lent in total ৳x · paid back ৳y".
  2. **You borrowed** — one row per person, total **You still owe**, and "Borrowed in total ৳x · repaid ৳y".
- Rows still open the person's page with every step. Other breakdown pages keep their single list.

## Consequences
- The two directions never mix; each total answers one question (what comes back / what I still owe).
