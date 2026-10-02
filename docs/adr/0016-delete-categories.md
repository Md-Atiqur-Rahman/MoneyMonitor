# 0016. Deleting categories

- Status: Accepted (amends [0013](0013-items-and-sub-categories.md), which said categories can't be deleted)
- Date: 2026-10-02

## Context
The user creates categories on the fly ("Restaurant Bill", "Donation") and wants to remove ones they
no longer need. Deleting a category that past transactions point to would make those entries
"Uncategorized" and silently change old reports and budgets.

## Decision
`FinanceService.DeleteCategoryAsync`, reached from **⚙ → Categories → tap a category → Delete**
(with a confirmation):
- **Sub-category** (e.g. Bajar › Fish): its transactions are **moved to the parent** (Bajar), then it
  is deleted. Parent totals and budgets don't change.
- **Main category**: allowed only if **neither it nor its sub-categories have any transactions**. Its
  sub-categories and its budget lines in **every month** are deleted with it, in one DB transaction.
  If it has entries, the user gets "can't be deleted, rename it instead" (`Err_CategoryInUse`).
- The **last main category** of a kind (income/expense) can't be deleted (`Err_LastCategory`), so the
  Add form always has something to choose.

## Consequences
- History and reports never lose their category; a used main category can only be renamed.
- To get rid of a used main category, the user must first delete (or re-enter) its transactions;
  a "merge into another category" action could be added later if that becomes common.
- Removing a category only from one month's budget is still Budget → tap → "Remove from budget".
