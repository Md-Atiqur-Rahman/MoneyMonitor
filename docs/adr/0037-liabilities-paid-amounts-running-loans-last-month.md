# 0037. Liabilities: paid amounts shown, only running loans, last month = one month

- Status: Accepted (refines [0022](0022-last-month-card-purchases-tab.md), [0023](0023-liabilities-layout.md), [0030](0030-earlier-month-forecast-loans-and-pay-month.md))
- Date: 2026-10-04

## Context
User feedback on the Liabilities page:
1. Once this month's card bill is paid, the card face and the Cards tab showed only "Paid" — no amount.
   (The current month looked only at unpaid dues, so a paid bill had nothing left to add up.)
2. The Loans tab listed loans finished in earlier months. It should list running loans, plus a loan whose
   last installment is in the month shown.
3. "Last month" listed every earlier month (in October: September and August). It should be only the
   month before.
4. Nothing may be deleted by this: it is only what the page shows.

## Decision
- `FinanceSnapshot.CardBillIn(card, month)`: the month's statement + EMIs, paid or not, with what was paid
  and the date of the last payment. Under **Paid** (card face and Cards tab) a small line: "৳30,000 paid on
  3 Oct · EMI ৳20,000 + purchases ৳10,000". "Purchases on this bill" stays visible after paying.
- `LoanInMonth.ShowsIn`: a loan is listed in a month when something is left after it (running or not
  started yet) or its installment falls in that month. A loan finished earlier isn't listed — it is still
  in the data and shows when an earlier month is chosen (‹).
- `FinanceSnapshot.LastCyclePurchases(card, day)`: Liabilities → Last month shows only the cycle before the
  one running on that day. Older purchases stay in the data, in their own month's view and in Reports.
- No data is deleted or changed by any of this; the filters only choose what is shown.
- **Last month's purchases are shown only** (user request): no 🗑 and no tap-to-edit there (`TxRow.ReadOnly`).
  A mistake in them is still fixed from Reports → All entries of that month.

## Consequences
- A paid bill reads like the sheet ("Paid", with the amount).
- The Loans tab stays short as loans finish.
- Tested: a paid bill keeps its amount and date; a finished loan shows in its last month only, a later loan
  shows as pending, nothing removed; last month is September only when seen from October.
