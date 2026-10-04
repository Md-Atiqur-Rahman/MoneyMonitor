# 0049. A negative saving is called a shortfall

- Status: Accepted
- Date: 2026-10-05

## Context
When income doesn't cover what is planned or due, the app still said "Savings this month −৳2,921", "Save
−৳x", "Saved so far −৳x". A negative number labelled as saving reads wrong.

## Decision
- Whenever the figure is negative, the label changes and the amount is shown **without a minus** (orange):
  - Home green card: "Short this month" / "Short in September" (the card already turns red);
  - Home summary of the month before: "Short"; Home forecast: "Expected shortfall";
  - Budget summary and next month's forecast: "Short";
  - Reports cash flow: "Short so far (after dues)".
- Bangla: ঘাটতি. Positive figures keep "Savings / Save / Saved".
- The 6-month bars keep their sign (they compare months).

## Consequences
- Labels say what the number means; the "need to borrow" banners stay as they are.
