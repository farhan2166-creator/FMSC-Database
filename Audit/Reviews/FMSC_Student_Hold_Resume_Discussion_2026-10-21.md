# Student fee-posting hold and resume: owner discussion

Date: 21 October 2026  
Status: Agreed direction recorded; implementation paused at the owner's request.  
Implementation state: Source inspection only. No application or database changes were made for this workflow.

## Why this discussion started

The owner questioned the current **Mark as Left** process and asked for its behavior to be inspected before making changes. The initial code review described it as a departure that closes the placement, with financial choices. The owner clarified that this does not match the intended school workflow.

The owner explained that a student may disappear without informing the school. Marking that student Left was intended to put them on an indefinite, temporary hold so they disappear from the fee generator and manual-posting selection. It was not intended to require a permanent withdrawal or ordinary readmission when they return.

The financial decisions should remain in the process. The wording and lifecycle meaning need correction.

## Current behavior found in source

The reviewed entry point is Student Admissions -> Actions -> Mark as Left. The dialog calls `dbo.CloseStudentAdmission`, whose latest checked-in definition is in `Migrations/21.sql`.

The current implementation:

- Requires a leaving date, reason and student-administration permission.
- Requires a review before applying the change and rejects a stale reviewed snapshot.
- Offers three treatments for already-issued monthly fees: school cutoff, keep issued bills, or waive from a selected month.
- Defaults to the school-cutoff treatment and to retaining released payments as credit.
- Can waive unpaid fees and release already-applied payments as credit or a recorded refund. Reductions require financial permission.
- Marks the student and current placement Left, records departure history, and sets the placement's billing readiness to false.
- Retains earlier-placement dues, annual/one-time charges, original payments, family links and history.
- Does not require payment clearance or a leaving certificate just to record the departure.
- Does not post missing fees before closing the placement.

The school-cutoff helper uses whole months, not daily proration. With a cutoff of the 15th, for example, a departure on October 14 selects October onward for waiver; October 15 selects November onward. The selected-month option can also reach earlier months within the placement.

These are source-review findings, not confirmation that a live database was changed or that the complete workflow was executed. Initial inspection also found that the existing Confirm Return Admission screen confirms promoted students; it is not already a general lift-hold workflow.

## Owner's intended meaning

**Mark Left should mean a temporary fee-posting hold that can be lifted at any time.**

The school operator should decide the related billing policy. The application should present the consequences and record the choice rather than impose a permanent-departure/readmission interpretation.

### Placing the hold

- Record the effective hold date and reason.
- Stop automatic fee generation and ordinary manual posting while the hold remains active.
- Let the operator retain issued fees or waive selected eligible fees/months.
- Retain the choice of credit or recorded refund where reducing a fee releases previously applied payments.
- Preserve student identity, placement history, payments, family links and retained outstanding fees.
- Keep the student searchable, with a clear hold indicator, while excluding them from ordinary posting candidates.

### Lifting the hold

- Permit lifting the hold without treating every return as a fresh admission.
- Let the operator select the return/effective date and billing commencement month.
- Support billing from the hold period, the return/joining period, or another explicitly selected applicable month.
- Let the operator choose which previously waived fees to restore and whether absence-period fees should be charged.
- Let the operator decide whether available account credit should be applied.
- Reuse the existing student record. A return within the same placement should not require a new admission number or ordinary readmission.
- Preserve previously recorded refunds. Restoring a fee must not falsely restore a payment that was already refunded.

### Leaving certificates

An existing Left/on-hold status must not prevent the student from obtaining a leaving certificate. Creating, issuing, printing and collecting a certificate should remain available through the appropriate permissions and existing certificate controls.

The hold decision and certificate decision must be recorded separately. The owner did not authorize making certificate possession a blanket barrier to lifting a temporary hold.

### Appropriate safeguards

The application should enforce permissions, accurate accounting, atomic saving, duplicate-charge prevention and preservation of history. Those safeguards must not be used to impose school-policy decisions that the operator should make.

## Proposed implementation approach discussed

The following approach was proposed and the owner instructed implementation to begin, then paused the work during initial inspection. These are planned changes, not completed features.

1. **Separate fee-posting hold from permanent withdrawal.** Use clear actions such as Put on Hold and Lift Hold / Resume, with a visible On Hold indicator.
2. **Record holds independently.** Retain effective dates, reasons, operator identity, financial decisions and linked changes. Preserve repeated hold/resume episodes.
3. **Enforce posting eligibility in SQL as well as the UI.** Both automatic generation and manual posting must respect an active hold, including alternate posting entry points. Normal posting should resume only under the reviewed resume decision.
4. **Provide a reviewed resume plan.** Show retained outstanding, waived fees selected for restoration, new absence-period charges, credit to apply and resulting outstanding before saving.
5. **Preserve financial evidence.** Link restored fees to their earlier reductions/cancellations. Keep original bills, receipts, payment allocations, refunds, credit movements and recovery markers. Do not duplicate existing charges or erase financial history.
6. **Keep certificate access independent.** Check the existing certificate paths for held and previously Left students without introducing a hold-status restriction.
7. **Review existing Left records explicitly.** Do not automatically relabel every historical departure as a hold, or infer a permanent departure solely from certificate presence. Existing school records need an operator-reviewed transition path.
8. **Deliver required SQL in a versioned migration.** The repository required schema 29 when inspected; migration 30 was proposed. Recheck the current migration sequence before resuming implementation. Update the compatibility contract, installer version and migration instructions together as required.
9. **Update related wording and help.** Distinguish temporary hold/resume, genuine readmission, formal departure and certificate issuance without changing their meaning silently.

## Verification planned

- Active holds disappear from automatic billing candidates and ordinary manual posting.
- Direct or alternate posting entry points cannot accidentally bypass the hold.
- Outstanding fees remain collectible while posting is held.
- Keep-issued, unpaid waiver, partially paid waiver, credit and refund decisions reconcile correctly.
- Resume can use the selected billing period and restore selected waived fees without duplicating charges or payments.
- Previously refunded money is not treated as newly received money.
- Available credit is applied only according to the reviewed choice.
- Same-placement returns, cross-year placement choices and repeated holds preserve lifecycle history.
- Leaving certificate creation/issuance remains available while held or already marked Left.
- Changed financial data invalidates a stale preview.
- Repeated submissions and uncertain connection outcomes cannot repeat financial effects; recovery can establish the saved outcome.
- Existing Left records are handled through the explicit reviewed transition path.

## Pause and next step

After authorizing implementation, the owner said: **"put it on hold for now"**. Work stopped during read-only inspection. No hold/resume migration, UI change, stored-procedure modification or live database mutation was made.

This document preserves the discussion for a later continuation. When the owner resumes the work, start from these agreed semantics, recheck repository/database versions, complete the detailed implementation design, then implement and verify the workflow. Saving and pushing this document does not resume the paused implementation.
