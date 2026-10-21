# Student fee-posting hold and resume

Owner policy: 21 October 2026. Implements the direction in
[the owner discussion](../Audit/Reviews/FMSC_Student_Hold_Resume_Discussion_2026-10-21.md).
Reviewed starting commit: `c3184b6`. That discussion is historical evidence;
its recorded pause is not rewritten as an implementation result.

## Operator workflow

In Student Admissions, find the student and use Actions → Put on Hold.
Record the effective date and reason. Keep issued monthly bills, use the school
cutoff, or choose the first month to waive. A waiver can release paid amounts;
choose advance or recorded refund explicitly. Review the amounts, then Save Hold.

A hold stops automatic monthly billing and ordinary manual posting. It does not
change the admission number or class placement start. The student remains
searchable as On Hold. Existing dues remain collectible, including through the
family counter. Annual/one-time charges, family membership, original receipts and
earlier-placement dues remain recorded.

Use Actions → Lift Hold / Resume when the student returns. Choose the effective
return date, placement and first month to charge. Select eligible previously waived
fees to restore, including those retained from an earlier hold episode. Missing monthly fees from the chosen month through the return
month are included in the review; existing bills are not duplicated, and
previously reversed fees are not silently recreated. Previously waived bills
remain waived unless selected for restoration. Previously recorded refunds stay
refunded; restoration creates dues rather than recreating old payments.

Available advance is applied in the resume transaction only when selected. This
does not receive new cash. Existing collection and later billing retain their
normal credit rules. Unbilled annual fees for the selected placement can be
included explicitly. An existing or reversed annual bill is not recreated by
that option. The review lists changed and new fees and the resulting outstanding.

For a later academic year, prepare the new placement through the established
promotion/placement workflow first, then select that placement in Resume. The
student-level hold continues to block posting while the placement is prepared.
Resume retains the older placement and its dues. It does not assign a new
admission number or silently copy an old fee agreement into another year.

Leaving certificate creation, issuance, printing and collection retain their
existing permissions and controls. A certificate does not itself prohibit
lifting a hold. Certificate evidence remains when a held student resumes.
Record Departure retains the existing formal departure workflow separately.

Existing Left records are never automatically converted. Review Left Record as
Temporary Hold explicitly adopts only the selected record, preserving its
historical origin. Resume is then a separate reviewed decision.

## Accounting and recovery

Migration 30 records independent hold episodes, the original terms and linked
financial corrections for waived/restored bills, and request outcomes. These are
operational history and recovery records, not the retiring audit trail.

SQL enforces posting eligibility, including direct/alternate writes. Saved
no-fee absence periods also stay excluded from missed-fee expectations. Legitimate
financial corrections and collection against existing dues remain available.
Resume preserves placement start dates; its chosen billing month is a distinct
financial decision. Existing financial correction procedures reconcile paid
reductions as advance or refund.

Review snapshots include the student's placements, fee assignments, bills,
payments, allocations, advance and hold history. A changed review is refused.
The billing/account locks and outer transaction protect the complete decision;
repeated requests return their original result instead of repeating effects.

Before submitting, the desktop flushes a recovery intent tied to the database,
operator and request. An uncertain result disables another submission. Check
Save Outcome reads the committed decision under a billing lock. Reopening Hold
or Resume also discovers the pending decision from the other action. A different
database/operator cannot clear its evidence. A confirmed negative outcome allows
a new review; checking never retries the write.

## Delivery and verification

Apply `Migrations/30.sql` then `Migrations/31.sql` after schema 29, using the transaction and backup process
in [the migration instructions](../Migrations/README.md). Application and installer
schema requirements are 31. No existing Left record, bill or payment is rewritten
by installing this migration.

`Tools/TestStudentPostingHolds.ps1` creates a separately owned schema-only LocalDB
fixture, applies the numbered migrations, seeds fictional records and exercises
the accounting, permissions, certificate and cross-year paths. It accepts no
school-database connection. `ThemeSmokeChecks --student-holds` verifies desktop
review, invalidation, duplicate-click and persistent outcome recovery using
injected responses. Neither test writes to the school database. Test success
does not constitute physical-printer or school-operator acceptance.

Verified on 21 October 2026 against the working changes from `c3184b6`:

- 57 hold/resume SQL checks, including a previous reviewed rate correction followed by a collection-time concession.
- 43 native desktop hold/resume, layout and persistent outcome-recovery checks.
- 95 existing automatic-billing SQL regression checks.
- 1,876 existing post-fix desktop checks and 60 family-workspace checks.
- 1,922 offline help assertions.

Migration 30 was also applied to the authorized school database after a COPY_ONLY
checksum backup and RESTORE VERIFYONLY. The production structural contract passed
inside the migration transaction. Before/after SHA-256 comparisons confirmed that
all rows in the 12 checked student, placement, fee, bill, payment, credit, family,
certificate and lifecycle tables were unchanged. No existing student was placed
on hold or converted from Left. The verified backup and local application evidence
are retained under ignored `artifacts/backups` and `artifacts/hold-live-migration.json`.

Follow-up correction, 21 October 2026 (reviewed commit `63f19db`): an empty
SQL JSON scalar fee list can be returned as NULL. The desktop previously tried
to parse that empty value and displayed a generic error during Put on Hold.
The renderer now accepts NULL, blank and empty-array lists without changing any
financial calculations. The expanded 53 native hold/recovery checks passed,
including a populated waiver list with a NULL new-fee list. Four further checks
rendered the actual read-only SQL preview response through the asynchronous
screen path. No hold or fee mutation was submitted. Unexpected hold-screen
exceptions are now retained in the local diagnostic log.
Confirm Return is for a promoted student awaiting ordinary return confirmation.
It must not be used to lift a posting hold. Held students are excluded from that
selection; Admission directs them to **Actions > Lift Hold / Resume**. Migration
31 also rejects an old return confirmation before changing fee assignments or
placement, including a return with no payable opening fee. The check shares the
billing lock with hold/resume, so those decisions cannot pass each other.

Verified on 21 October 2026: 62 isolated SQL checks and 55 native desktop checks
passed, including rejection of a stale all-zero return and successful ordinary
promoted return. Debug and Release builds succeeded. Migration 31 was applied
after a COPY_ONLY checksum backup and RESTORE VERIFYONLY. SHA-256 comparisons
confirmed all rows unchanged across the 12 existing business tables plus the
three hold/decision tables and fee-correction records. The updated application's
installation compatibility verifier returned success. Local backup/application
evidence is retained under ignored `artifacts/hold-return-live-migration.json`.
