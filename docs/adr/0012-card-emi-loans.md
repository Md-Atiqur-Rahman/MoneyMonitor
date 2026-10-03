# 0012. Card EMI loans and the combined card bill

- Status: Accepted (amends the rounding rule of [0002](0002-money-as-poisha.md))
- Date: 2026-10-02

> **Amended 2026-10-02 (user feedback):**
> - **Next month's dues are not payable from the app.** In Dues → "Coming next month" (and in the Budget tab when
>   viewing a future month) there is no Pay button; the row shows a grey **"Upcoming"** status instead. The user
>   pays a bill only in the month it is due.
> - **The purchases behind a bill are visible.** Liabilities → Cards → *This month's payment* lists
>   **"Purchases on this bill"**: the card purchases of the cycles whose statements are still unpaid (e.g. the
>   September purchase "Supermarket · 18 Sep · ৳1,500" on the October bill). Before, a billed purchase was only
>   visible in Reports, which made "EMI + purchases ৳1,500" hard to trace.

> **Amended 2026-10-03 (user request) — add-loan defaults:** "Billed on credit card" starts on the **first
> credit card** (not "None"; "None" is still in the list for a normal loan), and "Next installment to pay"
> starts on the **15th of the current month** (the card's due day when a card exists). Both can be changed.

## Context
In the sheet, all four loans are **EMIs on the user's credit card**. The card payment for
a month is `EMIs + last month's purchases` (20,000 + 10,000 = 30,000). The loans were already partly
paid when they were first written down ("Paid 1 of 6", "5 of 6"). The sheet's "available limit" also
subtracted the **original** loan amounts, which understates the real available limit.

## Decision
- `Loan.CardId` links a loan to a credit card; `Loan.InstallmentsPaidBefore` records installments
  paid before the loan was entered. Their `Due` rows are created as **Paid without a payment
  transaction**, so they don't distort any month's "paid" figure. At least one installment must remain.
- The add-loan form asks for the **next installment date** and the paid count; the schedule starts
  `paid` months before that date. A card EMI uses the card's due day.
- **Card bill** = unpaid card statements + unpaid EMI installments of that card up to the month
  (`FinanceSnapshot.CardBillDues`). Everywhere dues are listed, these are merged into **one row** per
  card ("Credit Card payment: EMI x + purchases y").
- **Pay card bill** (`FinanceService.PayCardBillAsync`) spreads the amount over those dues oldest-first
  and writes one `DuePayment` per due, so deleting a payment still restores exactly one due.
- **Limit used** = unpaid statements + **remaining** EMI installments + unbilled purchases.
- **Installments are rounded down to the poisha** (8,333.33), not to whole taka as in 0002, because
  that is how banks bill EMIs and how the sheet calculates. The last installment still absorbs the
  remainder (8,333.35). 1,00,000 / 3 is now 33,333.33 + 33,333.33 + 33,333.34.

## Consequences
- The card section shows this month's payment (৳30,000), next month's so far, the EMI list and
  the purchases of the open cycle; one button pays it all.
- Paying less than the full bill pays the oldest dues first; the rest stays as partial/pending.
- Existing users' loans keep their old whole-taka schedules; only new loans use poisha rounding.
