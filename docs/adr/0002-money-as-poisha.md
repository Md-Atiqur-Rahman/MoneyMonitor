# 0002. Store money as `long` poisha

- Status: Accepted
- Date: 2026-10-02

> **Amended by [0012](0012-card-emi-loans.md):** installments are now rounded down to the **poisha** (8,333.33), not whole taka.

## Context
Financial sums must be exact. `double` has binary rounding errors; `decimal` is exact but SQLite
has no decimal type (sqlite-net would store it as REAL or TEXT). Installment splits must also be
exact: 1,00,000 ÷ 3 must add back up to exactly 1,00,000.

## Decision
Every amount is a **`long` number of poisha** (1 Tk = 100 poisha) in entities, services and the
database. Conversion and display live in one place: `DailyAccount.Core.Money`
(`FromTaka`, `ToTaka`, `Format` with Bangladeshi grouping `৳1,00,000`).

Loan installments are rounded **down to whole taka** and the **last installment absorbs the
remainder** (`LiabilityEngine.BuildLoanSchedule`): 33,333 + 33,333 + 33,334.

## Consequences
- Sums are exact integers; no rounding drift across months.
- Every input must go through `Fmt.ParseMoney` / `Money.FromTaka`. Forgetting this would be a
  100× bug, which is why parsing is centralised and unit-tested.
- Display uses Indian/Bangladeshi digit grouping (lakh/crore), which `CultureInfo` does not offer for
  `en`, hence the custom formatter.
