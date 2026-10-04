# 0041. An entry that was really lending or borrowing becomes a personal debt

- Status: Accepted (extends editing of [0026](0026-edit-entries.md) and personal debts of [0040](0040-lending-by-card-and-who-owes-what.md))
- Date: 2026-10-04

## Context
In September the user entered ৳5,000 as an expense in a category made just for it — it was really cash
lent to a brother. The edit screen showed only Income / Expense / Card purchase / Transfer, so it couldn't be
corrected into a Lend.

## Decision
- In edit mode the type buttons also show **Lend** for an expense or card purchase, and **Borrow** for an
  income. Choosing it asks the person's name (and for Borrow the pay-back month); the amount, date, account / card boxes are hidden and the hint names the entry's own
  ("৳x on 29 Sept · card"), because those stay as they are.
- `ConvertToDebtAsync(transactionId, debt)`:
  - expense → a lend from the same account (the expense is replaced by "lent", balance unchanged);
  - card purchase → a lend through that card: **the same purchase is kept**, linked to the person, without a
    category, so its card bill doesn't change and it stops counting as spending;
  - income → a borrow into the same account, with its due.
  Only once per entry, and only these kinds (`Err_ConvertNotAllowed`).
- The category it was in stays as it is; a category made only for it (and its budget line) can then be
  deleted in Settings → Categories, which is only allowed once it has no entries.

## Consequences
- A mistaken entry is corrected in place instead of delete-and-retype, and keeps its links (card bill).
- Tested: an expense and a card purchase converted to lends (balance, spending, bill and dates as before);
  a second conversion is refused.
