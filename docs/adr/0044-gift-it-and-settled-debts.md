# 0044. "Gift it", and where settled debts show

- Status: Accepted (extends [0042](0042-people-list-for-lending-and-borrowing.md), [0043](0043-lent-and-borrowed-in-two-groups.md))
- Date: 2026-10-05

## Context
With family and friends the user may decide not to take lent money back. "Donate" doesn't fit; the user
chose **"Gift it" (উপহার দিন)**. Also, a debt that is settled — paid back, given as a gift, or borrowed money
repaid — should show only in the month it was settled, while reports keep it for a year.

## Decision
- `TransactionType.LendGift`: **Gift it** on a person in Liabilities → Personal → You lent asks how much
  (all that is left by default) and confirms; `GiftToPersonAsync` closes their lends oldest first. No
  account moves and it counts as **neither income nor spending** (user's choice): it only stops being
  "still to come back" (also in receivables / net worth).
- Ledgers know gifts (`DebtLedger.Gifted`, `SettledOn`; `PersonLedger.LentSettledOn` / `BorrowedSettledOn`).
  Texts: "Given as a gift ৳x" / "Paid back ৳y · gift ৳x"; steps show 🎁.
- **Liabilities → Personal**: open ones, plus ones settled **in the month shown**; after that month they are
  not listed there (the data stays).
- **Reports → Lent & borrowed**: You lent / You borrowed list open ones; a third group **Settled (last 12
  months)** lists the ones settled within a year, with the date; older settled ones drop off the report.
  A person's page always shows every step.

## Consequences
- Lists show what still needs attention; history stays reachable for a year in Reports and always on the
  person's page.
- Tested: a gift closes what is left (oldest first, not more than owed) without changing balances, income,
  spending or cash flow; settled dates for gifts and for repaid borrowing.
