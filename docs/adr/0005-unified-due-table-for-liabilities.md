# 0005. One `Due` table for every kind of liability

- Status: Accepted
- Date: 2026-10-02

> **Amended by [0011](0011-monthly-budget.md):** recurring bills are no longer created from the UI; fixed monthly costs are budget lines. Card EMIs are grouped with the card bill ([0012](0012-card-emi-loans.md)).

## Context
The user needs to know "what must I pay this month, and next month?". Liabilities come in four
shapes: installment loans (1 lac / 3 months), credit-card bills (last month's purchases, statement on
the 1st), recurring bills (rent, DPS), and personal borrowing (from a friend, maybe without a date).

## Decision
Each liability keeps its own definition table (`Loan`, `CreditCard`, `RecurringBill`,
`PersonalDebt`), and every amount payable becomes a row in **one `Due` table**:
`SourceType + SourceId + PeriodKey` (unique), `DueMonth` ("yyyy-MM", empty = no fixed date),
`DueDate`, `Amount`, `PaidAmount`, `Status` (Pending / Partial / Paid).

How rows are created (`LiabilityEngine`):
| Source | When | PeriodKey |
|---|---|---|
| Loan | All installments when the loan is saved | installment number |
| Card | On app open, for every **closed** cycle with purchases | cycle start "yyyy-MM-dd" |
| Bill | On app open, every month from start **through next month** | "yyyy-MM" |
| Personal (borrowed) | When the borrowing is saved | "0" |

- Generation is **idempotent** (skips existing PeriodKeys) so it can safely run on every app open
  (`FinanceService.GenerateDuesAsync`).
- A card purchase is a `Transaction(CardPurchase)` that does **not** touch any account balance;
  it only becomes payable when its cycle closes.
- Paying is `FinanceService.PayDueAsync`: updates `PaidAmount/Status` and inserts a
  `Transaction(DuePayment)` against the paying account, in one DB transaction. Partial payments are
  allowed; overpaying is rejected.
- Money lent to someone is **not** a Due; it is a receivable (`LendOut` / `LendReturn`).

## Consequences
- "Due this month", "next month", "overdue" and "total owed" are simple queries over one table.
- Deleting a payment must give the amount back to its Due (implemented and tested).
- ~~A card purchase already on a statement can't be deleted~~ — see [0021](0021-delete-billed-card-purchase.md):
  it can be, while that statement is unpaid; the statement is reduced.
- Stopping a bill deletes only its future **unpaid** dues.
- Deleting a loan is only allowed while nothing has been paid on it.
