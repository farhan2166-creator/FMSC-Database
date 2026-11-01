# Transfer Class vs Transfer Section — approved decision, 1 November 2026

**Rule:** When CampusId and ClassId remain unchanged and only SectionId
changes, the operation is **Transfer Section**, even if a caller mistakenly
invokes TransferEnrollmentClass. Record the move as TRANSFER, not
TRANSFER_CLASS, and never synchronize destination class fees solely because
the student changed sections.

The individual C# Transfer Class screen rejects the current class.
The bulk placement-plan procedure already routes by campus first, class
second, and section last:
- Different CampusId -> TransferEnrollmentCampus
- Same campus, different ClassId -> TransferEnrollmentClass
- Same campus and class, different SectionId -> TransferEnrollmentClassSection

Migration 69 adds the missing defensive guard to dbo.TransferEnrollmentClass:
after the destination is validated and same-place/campus checks pass, an
unchanged ClassId causes an error before updating enrollment history,
assignments or fees.

This does not impose an immediate-next-class rule. Same-year movement to
higher or lower classes remains permitted. Annual promotion and bulk
correction/history semantics remain deferred for their separate review.
Existing inactive/historical destination policy is unchanged.

No previously recorded TRANSFER_CLASS history is relabeled automatically:
historical corrections must be reviewed deliberately rather than guessed.
Use the normal migration process from schema 68 to 69 on a verified database
backup or an owned disposable test database; no production deployment is
part of this repository change.
