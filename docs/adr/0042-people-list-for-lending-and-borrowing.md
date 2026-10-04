# 0042. A list of people for lending and borrowing

- Status: Accepted (refines [0040](0040-lending-by-card-and-who-owes-what.md) and [0041](0041-entry-that-was-lending-becomes-a-debt.md))
- Date: 2026-10-04

## Context
The user lent his father money twice through the card (two entries). Lent & borrowed listed them as two
separate lines although it is one person. A person's name was typed every time, so the same person could
also end up under two spellings. The user wants "+ Add person" next to a person dropdown, like "+ New" for
categories, and one total per person.

## Decision
- `Person` table (id, name; names unique in any letter case, `Err_PersonExists`). `PersonalDebt.PersonId`;
  `PersonName` stays as the person's name for display.
- Existing debts are linked once to a person of the same name (any letter case), made when missing
  (`FinanceDatabase.LinkPeople`, on start and after restoring a backup; older backups work).
- Lend / Borrow form (also when turning an entry into a lend/borrow): **Person ▾** + **+ Add person**; a name
  that already exists is selected instead of added. A person must be chosen (`Err_Person`).
- `FinanceSnapshot.PeopleLedgers()`: per person, all lends and all borrows with totals, paid back, left.
  - Liabilities → Personal → You lent: **one row per person** ("Lent ৳x in 2 times · by card"), one
    **Money returned**: `ReceiveFromPersonAsync` settles that person's lends **oldest first**.
  - Reports → Lent & borrowed: one line per person (and direction) with the total and what is left; tapping
    opens **all steps with that person** (`flow?kind=person`).
  - Borrowed money keeps a row per borrowing (each has its own pay-back month); tapping opens the person.
- Backups carry people.

## Consequences
- Two lends to one person read as one total, and paying back doesn't need splitting by hand.
- Tested: unique people; two lends add up; money back settles the oldest first and can't exceed what is
  owed; older debts with the same name in another case become one person, also through backup/restore.
