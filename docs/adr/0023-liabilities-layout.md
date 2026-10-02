# 0023. Liabilities page layout: card on top, total owed at the bottom

- Status: Accepted (changes the Liabilities layout of [0012](0012-card-emi-loans.md) and [0022](0022-last-month-card-purchases-tab.md))
- Date: 2026-10-03

## Context
The Liabilities page opened with "You owe in total", then the tab switch, then — inside the Cards tab —
the blue card face (available limit), the month's payment and a list of EMIs that duplicated the
Loans tab. The user wanted the most important number, this month's card payment, first.

## Decision
New order, top to bottom:
1. **Card face (blue), always visible above the tabs**: card name + Edit, **This month's payment**
   (large), then **Available ৳x** with the usage bar, "Limit used …" and the statement/due days.
2. The tab switch: Cards · Last month · Loans · Personal.
3. **Cards tab**: the *This month's payment* card exactly as before (Pay card bill, breakdown,
   *Purchases on this bill* → Last month tab, *Next month so far* → Loans tab) and *Purchases this
   cycle*. The **"EMIs on this card" list is removed** — the EMIs are in the Loans tab.
4. **"You owe in total" moves to the bottom** and gets a fourth part, **Last month**:
   - *Cards* = remaining card EMIs + purchases of the current cycle,
   - *Last month* = unpaid card statements (last month's purchases),
   - *Loans* = loans not on a card, *Personal* = personal borrowing.
   The four parts add up to the total. Each part opens its tab.

## Consequences
- The first thing on the page is what has to be paid this month.
- "Cards" in the total no longer includes the unpaid statement; that amount is shown as "Last month".
- With several credit cards, one card face per card is shown above the tabs.
