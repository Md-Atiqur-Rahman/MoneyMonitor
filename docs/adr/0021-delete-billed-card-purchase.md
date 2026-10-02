# 0021. Deleting a card purchase that is already on a statement

- Status: Accepted (replaces the "billed purchase can't be deleted" rule of [0005](0005-unified-due-table-for-liabilities.md))
- Date: 2026-10-02

> **Amended 2026-10-02 — statements follow their purchases.** Found while checking this change on the phone:
> a purchase **entered after** its cycle's statement already existed (e.g. September purchases typed in on
> 3 Oct) was never added to the statement, because statements were generated only once per cycle.
> `LiabilityEngine.ReconcileCardStatements` now runs in `GenerateDuesAsync` (every app open): each existing
> statement is set to the total of its cycle's purchases, **never below what was already paid**; a
> statement with no purchases and no payment is removed. Tested: late purchases raise the bill; a paid
> amount is never undercut.
>
> Also checked on the phone: a deletion survives force-closing the app and installing an update.

## Context
The user wanted to remove a demo card purchase (Supermarket, 18 Sep, ৳1,759) that had already become
part of the October card statement. Until now a purchase on a statement could never be deleted, because
the statement amount was fixed when it was generated — so a wrong entry stayed on the bill forever.

## Decision
`FinanceService.DeleteTransactionAsync` for a **card purchase**:
- **Not billed yet** (current cycle): deleted, as before.
- **On a statement that still has at least the purchase amount unpaid**: the statement's amount is
  **reduced by the purchase** and the purchase is deleted, in one DB transaction. If the statement
  drops to ৳0 it is deleted too (and it isn't generated again, because its cycle has no purchases left).
  The status is recalculated (Pending / Partial / Paid).
- **On a statement that is already paid** (less unpaid than the purchase): refused with
  "This purchase's card bill is already paid, so it can't be deleted." — money that actually left the
  bank must stay explained.
- UI: Liabilities → Cards → *Purchases on this bill* now has a delete button per purchase (with the
  usual confirmation).

## Consequences
- Mistakes on an unpaid bill can be fixed; the bill and the card limit update immediately.
- Paid history is still protected.
- Tested: only purchase → bill removed and not regenerated; one of two → bill reduced to the other;
  paid bill → refused.
