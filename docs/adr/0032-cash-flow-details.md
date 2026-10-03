# 0032. Cash flow in detail: money in, dues paid, cash expenses

- Status: Accepted (changes the Reports cash flow; uses the month of [0029](0029-one-month-for-all-pages.md))
- Date: 2026-10-03

## Context
Reports → Cash flow showed three numbers (Income, Dues paid, Cash expenses) with no way to see what was
in them. The user wants to understand how money came in: "Salary 1,01,000, Bonus 43,500, Borrowed from
a friend 30,000, Total 1,74,500" — and the same detail for dues paid and cash expenses. The sheet counts
borrowed money as part of the month's money ("Total 174,500").

## Decision
- `CashFlowService.Build` (Core) → `CashFlow`: **money in** by income category, money borrowed (per person)
  and lent money returned; **dues paid** per due (each installment, each card statement, each personal
  repayment, with date and account); **cash expenses** per top-level category with its entries (sub-categories
  add into their parent). `Net` = money in − dues paid − cash expenses = what the month left in the accounts.
- Reports → Cash flow: the first line is **Money in** (income + borrowed + returned), and "Saved so far" /
  "Projected savings" / the 6-month trend use that cash flow. September: 1,74,500 − 1,15,043.66 − 57,990 =
  **1,466.34**, the sheet's "Bank" result.
- Each line is tappable → **flow?kind=in|dues|spent**: a page with the month (‹ › shared, ADR 0029), one
  row per part and the **Total**. Dues paid read "Loan-1 · installment 1 of 6 · EMI on Credit Card
  Card", "Credit Card · August purchases", "Paid back Friend". Cash expense rows open the
  category report.
- The Budget's "Save" is unchanged: it plans with income only (borrowing isn't income to plan with).

## Consequences
- Reports and the sheet agree on what a month brought in and left.
- Two "savings" now exist on purpose: the plan (income − everything planned) and the cash flow (what really
  stayed in the accounts, borrowing included); borrowing is visible as its own line.
- Tested: money in (income by category in category order, borrowed per person), dues paid per installment,
  cash expenses by parent category, net.
