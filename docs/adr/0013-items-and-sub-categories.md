# 0013. Item-level expenses and sub-categories

- Status: Accepted
- Date: 2026-10-02

> **Amended 2026-10-02:** new categories (e.g. "Restaurant Bill", "Donation") can be created where they are needed: **Budget → + Add budget item → "+ New category…"** (now the first option, it was hidden at the end of a long list) and the **"+ New"** button next to Category in the Add form, which creates and selects it.

> **Amended 2026-10-02 (user request):** **DPS** and **Certificate** were removed from the default categories permanently. The sheet importer ([0014](0014-sheet-import.md)) creates them itself because that sheet uses them.

> **Amended by [0020](0020-default-budget-and-wording.md):** **DPS** is a default category again (it is in the default budget) and **Education** was added; **Certificate** stays removed.

## Context
The sheet logs shopping item by item with a quantity ("Grocery - Rice, 5kg, 450", "Fish - Rui, 2kg,
730") and groups items (Grocery, Meat, Fish, Cosmetics…) under budget lines like Bajar. It also has
budget lines for people (e.g. parents, children).

## Decision
- `Transaction.ItemName` and `Transaction.Quantity` (free text: "5kg", "250gm", "30") are optional.
- `Category.ParentId` gives **one level** of sub-categories (no sub-of-sub). A child always has its
  parent's kind. Defaults now follow the sheet: Bajar → Grocery, Meat, Fish, Vegetables, Tiffin,
  Cosmetics; Fruits, Transport, Medicine, Rent, Gas, Electric, Wifi, Mobile, DPS, Certificate,
  Family, Others; income: Salary, Bonus, Other income.
- The Add form has an **"Enter item by item"** switch (Expense and Card purchase): each line picks a
  type (the category or one of its sub-categories), item, qty and price; the total is shown live.
  All lines are saved as separate transactions **in one DB transaction** (`AddTransactionsAsync`) —
  all or nothing.
- A **Categories** screen (Settings) adds categories/sub-categories and renames them. Typing a new
  name clears its Bangla name, so the user's own name shows in both languages.
- Reports group spending by top-level category and list the **top items** of the month.

## Consequences
- A shopping trip of 14 items is 14 rows; history lists show "Rice · 5kg" with its category.
- ~~Categories can't be deleted yet~~ — see [0016](0016-delete-categories.md).
- Item names are free text; "Rice" and "rice" are grouped together in reports, typos are not.
