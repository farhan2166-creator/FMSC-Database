# Quick counter implementation and verification — 20 October 2026

The optional **Family Admission & Fees** workspace now coordinates the counter
visit through the existing registration, return, fee-review, collection and RDLC
paths. Full admission remains separate. No admission form, financial write rule,
receipt-number format or RDLC template was redesigned.

See the [workspace guide](../Pages/Students/Admissions/FamilyAdmissionWorkspace.md)
for the operator sequence and the boundary with detailed work.

## Resulting behavior

- Find an individual or family, include the children being handled, and retain
  each child's draft while reviewing siblings. Parent details carry forward.
- Save minimal new registrations and confirm reviewed existing returns.
  Preparation reports partial completion and stops at the child needing attention;
  retrying does not recreate children already saved. Official admission is later.
- Open established Fee Collection over the retained family, with exactly the
  selected children. Suggest allocations, permit per-child edits, confirm the
  payment and print the existing saved receipt. Keep remaining family bills after
  a partial collection and clear submitted payment amounts.
- Retain the review/discard guard for unrecorded payment entries. Detailed finance
  maintenance stays outside this counter payment view. Existing credit application,
  transaction recovery and receipt history are reused.
- Check an unresolved registration in a dedicated recovery editor. A different
  recovered ID cannot replace a loaded child's identity.
- Preserve current/future fully waived fee evidence in individual and family bill
  snapshots. All-zero bill validation uses decimal XML sums. Waiver evidence does
  not create a charge or payment. Printed templates remain unchanged.

## Final checks

| Check | Result |
|---|---|
| Release solution build, serial, no restore | PASS; application, Worker and Host; zero warnings/errors |
| Focused family workspace native fixtures | 17 PASS |
| Collection/credit and counter context native fixtures | 22 PASS |
| Existing post-fix native regression set | 1,876 PASS |
| Owned counter SQL/native/RDLC scenarios | 45 PASS |
| Existing test isolation/report safety checks | 6 PASS |
| Normal application rejects the Level 2 probe before database access | PASS |
| Whitespace/diff check | PASS |

The owned scenario ran real child saves through the family host, partial failure
and retry, pending status/number checks, future waiver reads, saved numbered bills,
an edited 600/400 family collection, remaining balances, committed-request recovery
and existing-child return confirmation. It rendered the saved family bill,
all-free individual bill and family receipt through the established RDLC templates.
No physical printer was used.

The final owned database instance was removed after testing; ownership checks and
cleanup passed. Local fictional screenshots, PDFs and runner metadata are retained
under ignored `artifacts/level2/evidence/FMSC_Level2_4da69fc9feec4d5fa3706b7ea487c312/`.
These checks used the working-tree changes based on commit `1eb7942d`, not a claim
of a published or merged release. The last follow-up to that run adjusted only
the counter's money-column widths and single-child heading; the focused native
checks were rebuilt and rerun afterward.

The test harness emitted a NuGet advisory-lookup warning while compiling; its
tests completed successfully. A fresh dependency advisory assessment is not part
of these results.

## Database delivery and live application

[Migration 27](../Migrations/27.sql) requires schema 26, adds the read-only waiver
procedure and corrects the all-zero bill total conversion in `IssueFeeBill`.
It advances the schema to 27. It does not rewrite issued bills, school records,
charges, payments, allocations or credit. Its SQL can be reapplied in sequence
after restoring the original schema-26 database. Reapplying it to schema 27 is
refused rather than rerunning it.

The live database was backed up with `COPY_ONLY` and `CHECKSUM`; `RESTORE VERIFYONLY`
with checksum passed before the upgrade. Migration 27 then committed in one
transaction. Before/after row counts and content checksums were unchanged for
students, enrollments, fee assignments, charges, payments, allocations, credit,
collection receipts and issued bills. Subsequent metadata reads confirmed schema
27 and both procedure changes.

The private backup remains outside the repository in the Windows profile's
`FMSC_SMS/PreUpgradeBackups` directory. The exact path and verification result are
recorded locally in ignored `artifacts/quick-counter-live-migration.json`.

## Remaining acceptance

The implementation and listed automated journeys are complete. The school operator
must still walk through representative visits to confirm comprehension and speed,
and check physical output on the intended printer. Small windows retain scrolling;
the normal and compact workspace captures were inspected. No physical mixed-DPI
monitor, complete installer or whole-application deployment sign-off is claimed
by this workspace verification.
