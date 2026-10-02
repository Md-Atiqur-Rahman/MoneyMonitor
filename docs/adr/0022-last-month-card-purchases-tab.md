# 0022. "Last month" tab for earlier card purchases

- Status: Accepted (changes the bill-purchases list added to [0012](0012-card-emi-loans.md))
- Date: 2026-10-03

> **Amended 2026-10-03 (user request) — tap to jump between tabs:** on Liabilities, *Purchases on this bill*
> opens **Last month**; *Next month so far* (next month's EMIs) opens **Loans**; and the three parts of
> "You owe in total" (Cards / Loans / Personal, underlined) open their own tab.

## Context
Card purchases of the **current** cycle were listed under the card, and the purchases on this month's
bill were listed inside "This month's payment". The user wants all card purchases of **earlier months**
in a separate place, and only a total for them on the card.

## Decision
- **Liabilities gets a fourth tab, "Last month"** (order: Cards · Last month · Loans · Personal). It lists
  every card purchase made **before the card's current cycle**, grouped by cycle month, newest first.
  Each group shows: "September 2026 · Credit Card", the group total, "on October bill ·
  Pending/Partial/Paid" (from that cycle's statement), and the purchases with a delete button
  (deletion rules of [0021](0021-delete-billed-card-purchase.md) apply).
- **Cards → This month's payment → "Purchases on this bill"** shows a single line, **"n purchases · ৳total"**,
  instead of the list. Tapping it opens the "Last month" tab.
- Purchases of the current cycle stay under the card ("Purchases this cycle").
- Segment buttons use 13 pt text so four fit on a phone.

## Consequences
- The card screen stays short; the detail of older purchases is one tap away.
- The tab shows all earlier cycles, not only the previous one; for a long history it could later be
  limited to the last few months.
