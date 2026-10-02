# 0026. Editing entries, and one list of all entries per month

- Status: Accepted
- Date: 2026-10-03

> **Amended 2026-10-03 (user feedback):** the edit screen shows the **type buttons** (Income, Expense, Card
> purchase, Transfer — not Borrow/Lend) with the entry's type selected, and the type **can be changed**
> (e.g. an expense that was really a card purchase; the card bill follows). The "Enter item by item" switch
> is shown too; switching keeps the amount. Changing the category is kept when switching type.

## Context
Entries could only be deleted, and only from the list they happened to live in (an account's history,
or the card sections). There was no way to correct an amount, category or date, and no single place
to find an entry.

## Decision
- **Edit**: `FinanceService.UpdateTransactionAsync` changes an **income, expense, transfer or card
  purchase**; the kind can change between these four (see the note above). **Bill payments and borrow/lend records can't be edited**
  (`Err_EditNotAllowed`): delete them and enter them again, so dues and receivables stay exact.
  For a card purchase on a statement the statement follows the change (ADR 0021), but the purchases of
  a cycle may never total less than what was already paid on its statement (`Err_CardBilled`).
- **Edit screen** = the Add form opened with `addtx?id=`: filled in, type chips hidden, title
  "Edit entry", a red **Delete** button at the bottom. An entry with an item/qty or a sub-category is
  shown as one item line (type, item, qty, price).
- **Where to tap**: any entry row (account history, Liabilities card purchases, Last month tab, the new
  All entries list) — tap the text to edit, 🗑 to delete — and items in the category report.
- **All entries**: Reports → **"All entries of this month ›"** opens a list of every entry of that month
  (`entries?month=`) with ‹ ›, newest first, "+" for money in, "−" for money out, "↔" for transfers,
  and a summary "n entries · in ৳x · out ৳y".

## Consequences
- Mistakes are fixed in place instead of delete-and-retype.
- A multi-line shopping trip is still several entries; each line is edited on its own.
- Tested: edit amount/category/item and balance; billed purchase changes its unpaid bill; a paid bill
  can't be undercut but can be raised; payments and kind changes are refused.
