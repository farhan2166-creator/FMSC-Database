# School operating workflow

## Set up the school once

Use Setup Readiness to complete the school profile, academic year, campus,
classes/sections and class fee rates. Set the monthly cutoff deliberately. Keep
student fee assignments separate from the class rate matrix: saving a new matrix
rate does not rewrite a child's agreed fees or issued bills.

## Admit or activate a child

Open **Daily Work → Student Admissions**:

- **Admit / Edit Student:** complete the child's details and placement, review the
  opening fees and save. Admission, placement and opening charges commit together.
  Record money through Fee Collection, then print the receipt.
- **Registration & Pending:** use when registration precedes formal admission.
  Save a temporary QR reference, link siblings where appropriate and collect
  reviewed opening fees if needed. Later complete details and confirm from the
  pending list. Confirmation keeps the same child, bills and payments.
- **Existing Student Setup:** review a historical record, explicitly choose its
  current placement and billing start, and choose whether admission fees were
  already paid before the system. A prior-payment acknowledgement creates neither
  a new charge nor a fabricated cash receipt. Only the selected child is activated.

Do not create another student for a returning child. Use their existing profile
and the promotion/readmission/return actions; old dues and unused credit stay with
the same student. Student Data Review contains record corrections and duplicate
review, rather than another intake route.

## Collect money

Find the child in **Fee Collection**, verify the identity and account, then enter
money actually received. Suggested allocation pays oldest tuition first, followed
by other fees oldest first. Use Adjust Allocation for a deliberate different
allocation. Previous-year dues remain under their original placement.

Select siblings for one atomic family collection and one family receipt. Review
which children are included; a child with no fees is not automatically confirmed
as returned. Printing a bill does not create charges or record money.

- Partial payment leaves a real unpaid balance.
- A discount on an existing bill can reduce only the unpaid part, including a
  partial bill. It does not refund money already collected.
- Excess received becomes the selected child's credit. A credit transfer moves
  value between linked siblings and creates no new cash.
- Use Student Credit to deposit/refund/transfer unused credit. Credit application
  and deposits are not ordinary cash receipts eligible for payment correction.
- Use Student Fee Corrections for a mistaken ordinary collection. A partial refund,
  correction or paid-bill reduction changes the remaining adjustable amount.
- When the save outcome is uncertain, check the saved history/recovery prompt.
  Never enter the same money again merely because refresh or acknowledgement failed.

## Monthly and exceptional billing

**Daily Work → Billing Center** opens with Billing Status. Current-month charges
are automatic for eligible active placements with explicit billing commencement.
Legacy reference records stay excluded. Select a setup exception to open that
child's setup or collection account. This navigation does not activate anyone.

An administrator reviews missed months and chooses whether to include them.
Manual Recovery is for reviewed exceptions, including deliberately reposting a
reversed charge. Automatic billing does not silently recreate reversals.

Choose recurring concession start/end months in Fee Concession. Review whether
issued bills in that period should also change, and choose credit or refund for
paid reductions. A temporary concession restores the prior agreement afterward.
For a separate issued-bill change, use its unpaid-balance adjustment or an explicit
bill-rate correction.
Paid rate reductions release the paid difference as credit or an explicit refund;
original payments remain in history. Review the correction preview before saving.

## Movement and departure

Promotion/readmission selects a new placement and rates without copying old debt.
Confirm actual returns individually. Transfer and leaving use the configured
cutoff by default. Leaving can retain issued bills or waive from a chosen month;
review the financial effect before applying. Paid money must be reconciled,
not deleted. A leaving student can still owe earlier fees or hold unused credit.

## Close the day

Reconcile receipts and refunds with actual cash/bank receipts in Daily Collection
Closing. Existing credit applied to fees and sibling transfers are noncash. Review
student statements and outstanding balances, and make a verified backup.

## Deployment

Use the verified database-free complete installer artifact. A fresh install gets
the owner's matching detached database pair locally after download. An existing
schema-19/20 installation upgrades through migrations 20/21 with a verified backup before
application activation. Never replace an installed school database with a test
fixture or a development DACPAC. See INSTALLATION.md and RELEASE_READINESS.md.
