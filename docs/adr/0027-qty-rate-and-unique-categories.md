# 0027. Qty × rate on item lines, "+ Sub" on each line, unique category names

- Status: Accepted (extends [0013](0013-items-and-sub-categories.md); category order of [0011](0011-monthly-budget.md) / [0020](0020-default-budget-and-wording.md))
- Date: 2026-10-03

## Context
User feedback on "Enter item by item" and on Budget → "+ Add budget item":
1. The price of an item should be calculated from the quantity ("5 kg at ৳90"), and the kg/qty shown.
2. Qty must not look mandatory: Transport › Uber needs only a name and a price.
3. A sub-category could only be added in Settings → Categories, not while entering items.
4. The Budget's category list didn't show all categories (Baby Care didn't exist; the order didn't
   follow the user's list).
5. The same name could be added twice (e.g. "Fish" under Bajar and again under Others).

## Decision
- **Item line** = Type (sub-category) + **"+ Sub"** button · Item · **Qty · unit · Rate · Price**.
  - Units: — (none), kg, gm, ltr, ml, pcs, dozen, packet. gm / ml are priced **per kg / litre**
    ("250 gm × ৳800/kg = ৳200"), as at the bazar. Default unit is kg.
  - When qty **and** rate are given, **price = qty × rate** (poisha, rounded), recalculated when either
    changes; the price can still be typed directly. A line under the fields shows
    "5 kg × ৳90/kg = ৳450" (or "5 kg · ৳450" without a rate).
  - **Qty and rate are optional**; item + price is a complete line. A hint says so.
  - Stored: `Transaction.Quantity` = "5 kg" (`ItemMath.Format`); the rate is not stored, it is found back
    from price ÷ qty when editing (`ItemMath.Rate`). Older free-text quantities ("2kg") are read by
    `ItemMath.Parse`; text that isn't a number is kept as typed.
  - All the arithmetic is in Core (`Services/ItemMath.cs`).
- **"+ Sub"** on an item line asks for a name and creates a sub-category of the chosen category; it is
  selected on that line and offered on the other lines.
- **Names are unique per kind across all levels** (top and sub, English or Bangla name, any letter case):
  `FinanceSnapshot.FindCategory`, enforced in `AddCategoryAsync` and `RenameCategoryAsync`
  (`Err_CategoryExists`). Income and expense lists are separate. In the UI a name that already exists is
  **selected** instead of refused when it fits there ("\"Fish\" already exists, so it is selected");
  otherwise the message says where it is ("already exists under Bajar").
- **Default expense categories, in the user's order**: Bajar, Education, Family, **Baby Care** (new),
  Medicine, Fruits, Gas, Wifi, Mobile, Rent, DPS, Electric, Transport, Others. Existing databases get
  this order and Baby Care **once** (`AppMeta "category-order-v2"`); the user's own categories follow;
  defaults the user deleted earlier are not brought back. Baby Care is not added to the default budget.
- **Budget → "+ Add budget item"** lists every expense category that is not yet in that month's spending
  plan, in this order. Typing a new name that already exists uses that category (or says it is already
  in the plan).

## Consequences
- A shopping trip can be typed as the receipt reads (qty, rate) without a calculator; quick bills stay
  two fields.
- Reports show "Rice · 5 kg" as before.
- No look-alike categories split the totals.
- Tested: qty × rate (kg, gm, ml, pcs), rate back from price, quantity text both ways, uniqueness across
  levels / languages / rename, default order, one-time update of an older database.
