# 0039. "Loan & Credit Card Paid", and a card bill opens its purchases

- Status: Accepted (refines the cash flow details of [0032](0032-cash-flow-details.md))
- Date: 2026-10-04

## Context
Reports → Cash flow called the second line "Dues paid". The user found "dues" unclear there (the app also
uses "Due" for unpaid budget items, ADR 0024) and asked for **"Loan & Credit Card Paid"**. In its breakdown,
a card bill line ("Credit Card · September purchases ৳10,000") showed only the total; the user wants to
open it and see the purchases it is made of.

## Decision
- The line and its page are named **"Loan & Credit Card Paid"** ("লোন ও ক্রেডিট কার্ড পরিশোধ").
- `FinanceSnapshot.StatementPurchases(statement)`: the card purchases of that statement's cycle, oldest first.
- In the breakdown, a card bill row is tappable → `flow?kind=bill&dueId=…&month=…`: the bill's purchases
  (item · date · category · amount) with the total, and "paid in October: ৳x of ৳y". Loan installment and
  repayment rows stay as they are (there is nothing below them).
- That page shows the bill's own title instead of ‹ month › (the purchases belong to the bill, not to a
  month to page through).

## Consequences
- From Reports one can follow money out: month → card bill → each purchase.
- Tested: a statement lists exactly its cycle's purchases and they add up to it; a loan due lists none.
