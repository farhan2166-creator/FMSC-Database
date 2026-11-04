# Business decisions

The current source-resident rule record is [Core/BusinessDecisions.cs](../Core/BusinessDecisions.cs).
It survives loss of this documentation; nearby C# comments carry the same BD identifiers.
This is a maintenance policy record, not new enforcement or release acceptance.
Update the source record and its implementation comment when an owner decision changes.

## Admission And Placement

### BD-01

Registration (31 Oct; sibling clarification 2 Nov): Admission owns pending
registration. Pending Save confirms only after required-detail and official-number
review; Save Registration Draft remains non-confirming. The sibling panel excludes
the loaded child. That child uses its main Confirm & Save and registration actions;
siblings retain row actions. Do not restore the retired Family Admission route.

### BD-02

Reopening (migration 64): only a cancelled pending identity without an
official number can reopen. Reuse its identity/reference with a new pending
placement and a reviewed reason/date, not before cancellation. Preserve cancelled
episodes, family links and finances; do not revive cancelled bills/agreements.
Recheck the financial/placement snapshot at commit. Identical request replay is
safe; changed replay is refused. Admitted former students use readmission instead.

### BD-03

Activation: placement start records class history; billing commencement is
an independent reviewed date. Apply the school cutoff to billing commencement,
not enrollment start. Admission/selected one-time charges use the chosen billing
date. Pending registration is not admission or permission to post ordinary fees.

### BD-04

Historical placement (migrations 57/61): confirmed past-year admission can
remain Active with BillingReady=0; it is not awaiting return. Do not enable old
billing to repair that classification. Confirm Return requires a confirmed current
year placement awaiting billing, excluding expired/future years and legacy drafts.
Historical dues remain collectible; factual placement history stays editable.

### BD-05

Hold/resume (21 Oct; migration 30): hold blocks automatic and ordinary manual
posting, not collection of existing dues. Preserve identity, placement start,
annual fees, receipts and earlier-placement balances. Resume explicitly reviews
return date, billing month, restored waived fees, missing monthly fees, optional
unbilled annual fees and available credit. Never duplicate or silently recreate
reversed fees/payments. Prepare a later-year placement before resuming there.
Certificates remain recorded and do not alone prohibit resume. Existing Left
records require individual reviewed adoption as a hold; never convert them all.

### BD-06

Departure: record a date and reason separately from the fee decision: use
cutoff, keep issued monthly fees, or waive from a chosen month. Reducing paid fees
requires an explicit advance/refund choice and Fees.Adjust. Keep annual/one-time
fees and older-placement dues. Departure prevents future posting even when issued
bills are retained. Historical departure/transfer dates must fit their recorded
placement/year; never silently choose today/year-end. Hold/resume requires a
placement covering today, including for backdated requests (migration 61).

### BD-07

Readmission (1 Nov; migration 68): an official former student in Left,
Inactive or legacy Withdrawn may re-admit after date, placement and fee review.
Active/Pending students, active enrollment or unresolved hold are ineligible.
Final class does not automatically mark Left or require an artificial next class.
Record departure/completion first. Keep departure, certificate and number history;
cancelled pending registrations use Reopen, held students use Resume.

### BD-08

Transfer (1 Nov; migration 69): route by campus, then class, then section.
Same campus/class with only a section change is Transfer Section, with no class
fee synchronization. Class transfer rejects the current class but may move to a
higher or lower class in the same year; next-class rules belong to promotion.
Historical/nonbilling transfers preserve fee plans and BillingReady=0. Do not
relabel past TRANSFER_CLASS events automatically. New-placement active choices
are a C# policy; later deactivation must not invalidate historical placements.

### BD-09

Annual promotion: select the immediate next class in the same program using
the full catalog's order and ID tie-breaker; do not skip an unconfigured class.
Repeat Class retains class/section in the next year; HOLD remains its stored
decision and history displays Repeated Class. New placements await Confirm Return
before billing. Carrying concessions is explicit, requires Fees.Adjust, excludes
expired/yearly/purchase concessions, caps fixed amounts at the new rate and lets
a full concession cover that rate. Keep promotion requests and undo history.

## Fees, Collection And Documents

### BD-10

Fee terms: class/future-plan changes must not silently rewrite posted bills.
Review agreement changes, issued-bill corrections or both, preserving terms outside
the chosen period and restoring the prior agreement after a temporary concession.
No bill is manufactured just to represent a concession. Correcting a closed
placement's bill never reopens it or rewrites its historical agreement.

### BD-11

Shared fee editor (31 Oct): Admissions and Collection use the same rules,
options, factory, permissions, snapshots and save recovery. Admissions has action
tabs; Collection preselects its fixed action. Collection deliberately defaults
both From/Through to the clicked/start month; Admissions keeps its normal range.
Do not make their defaults identical as a cosmetic consistency change.

### BD-12

Concessions and money: discount reduces the full fee; correcting a recorded
payment is distinct from returning cash. Reductions consume unpaid dues first;
paid differences become the explicitly selected advance or refund. Optional credit
use follows the reviewed order, oldest due tuition then other due fees; future
fees do not consume it early. Credit use is settlement, never new cash. Normal
later billing keeps its existing eligible-credit rules. No silent money rounding.

### BD-13

Collection: posting creates dues; collection settles them. Partial and family
collection keep each child's allocations, concessions and debt attached to that
child even on one family receipt. Do not replay a committed collection because
printing failed. Payable rows may offer fixed bill discount actions; newly waived,
not-yet-documented rows are read-only, unallocatable and offer details only.
An issued document consumes that waiver baseline; historical settlements do not
reappear. Finish/clear a payment draft before submitting a fee change or its note.

### BD-14

Charge cancellation (migration 60): an unpaid cancellation requires both
Fees.Collect and Fees.Adjust through every route and at SQL commit. Paid/partial
charges cannot take that shortcut. Keep the complete reason, actor and timestamp
separate from original notes; cancellation retains the charge/history. This is
bill cancellation, not the removed certificate cancel/void feature.

### BD-15

Transport (27 Oct; migration 58): its cutoff is independent of admission's
fee cutoff. Days 1–28 or blank are supported; before cutoff permits this month,
on/after cutoff starts next month, blank permits this month throughout. Paid or
part-paid months stay charged. Stop cancels wholly unpaid permitted future
transport charges and ends the plan, without refund/credit or erased allocations.
Start posts an eligible current month once; future starts await billing. Older
cancelled copies remain history. Require an Active placement, recheck cutoff,
payments and snapshot under the billing lock, and retain durable request recovery.

### BD-16

Monthly reversal: only the latest eligible posting for the selected year
can reverse, whole-school and all-or-nothing. Later posting, payment history or
ineligible placement may block the whole operation. Recheck the reviewed snapshot
and reason at SQL write; never erase paid transaction history or enable exclusions.

### BD-17

Daily closing: adjustment rows intentionally have no entered Actual Amount;
blank is not zero. Ordinary methods require whole rupees, rejecting fractions
without rounding. Preserve historical precision in display/print. Closing is a
revisable reconciliation snapshot, not a lock on backdated transactions, and does
not edit student payments. Recheck/report any changed closing position explicitly.

### BD-18

Receipt paper trail (migrations 55/56): visit-based version 3 supersedes the
earlier cumulative layout. Begin with the previous relevant receipt's closing
dues, adding later fees/adjustments/concessions only; settled fees stay in history.
Use saved monotonic visit/student-order metadata for backdated or same-second
events. Bills use that position without consuming the receipt baseline.
Show full fee amounts and plain fee names; deductions, payment and balance must
reconcile. Issued/annual reprints use saved participants, fees, plans and balances,
not today's family or terms. Disclose incomplete legacy evidence; do not invent it.
Retain original snapshots, payment corrections and later-change evidence.

### BD-19

Fee-change notes: use FeeChanges.rdlc and the existing numbered, zero-money
acknowledgement writer. Show saved before/change/after dues, signed changes and
reasons; unchanged unpaid fees reconcile separately. Saved plans are a separate
non-billed section. Omit unchanged plans; without prior evidence say Saved Fee
Plan, never invent old terms. Acknowledgements receive no cash or applied credit.
Ordinary outstanding bills still require positive dues.

### BD-20

Student Fee Account: investigation reuses existing readers, receipts,
correction form and recovery writers. Statement access needs Reports.View;
collection users with Fees.Collect may use Receipts without gaining statement
SQL/export access. Payment/credit readers also need Fees.View; corrections need
Fees.Adjust; annual receipts retain Fees.Statement.View. Include Siblings remains;
arbitrary group statements and duplicate standalone correction routes are retired.
Keep original family receipt totals distinct from per-child shares and later
adjustments. Label current all-date dues/advance separately from filtered totals.
The statement is not an immutable running ledger; do not fabricate a balance or
net outstanding against credit. Late or prior-user reads cannot enable actions.

## Certificates, Setup And Reports

### BD-21

Certificates (25 Oct; migrations 51/52): no cancel/void or replacement-from-
cancelled-copy flow without a new owner decision. Create, Save while uncollected,
Print, Mark Collected then lock; Issue Again creates a separate copy and reprint
uses the selected copy. Never undo departure/fees or silently mark collected.
Migration 52 removed cancellation columns only on the approved clean baseline;
refuse snapshots with cancelled copies rather than revive them. Preserve records,
issue/collection evidence and request markers. Leaving clearance is optional.
Leaving-copy duplicate checks are scoped to the recorded leaving episode, not
all past certificates; preserve earlier copies when a readmitted student leaves.

### BD-22

Certificate fields: do not reintroduce the removed Passed Exam gate.
Use saved eligible placement/year evidence and the writer's checks. Staff
designation and subjects may prefill from the selected staff record but remain
editable. Keep the school's finalized certificate wording and separate print
permission checks; creating/viewing a record alone does not authorize printing.

### BD-23

Academic years (31 Oct; migration 63): any enrollment, including historical
or left, and either side of promotion history locks Start/End dates. Name,
Remarks and Current remain editable; age alone does not lock an unused year.
Names use consecutive four-digit years. SQL must reject stale date changes too.
Never repair a historical year by renaming dates or deleting its usage.

### BD-24

Setup catalogs (2 Nov): Campus - Classes / Sections is the setup entry point;
dialogs retain the complete global class/section editors. Rename is school-wide;
creating a catalog row does not assign it to a campus. Apply Changes alone saves
assignments. Restore unsaved matrix differences by IDs after catalog refresh;
renames/reorder keep ticks, deleted keys disappear, new keys use saved baseline.
Campus switching and small setup drafts retain the approved prompt-free discard;
block closing during submitted writes. Bulk fee drafts keep their own guards.

### BD-25

Drafts/UI: explicit New Record in MasterPage deliberately discards its
draft without another confirmation; switching/leaving retain their own guards.
An untouched date default alone is not a substantive certificate draft. Preserve
the approved Collection/Admission layouts, warnings and critical financial
consequences while trimming routine guidance. Do not treat optional subtitles or
embedded headers as a reason to change workflows or introduce blanket prompts.

### BD-26

Reports (2 Nov): default order follows each report; optional sorting precedes
pagination and reaches print/export. Preserve groups/totals and typed amounts,
dates and textual references. Grid page preferences override shared alignment;
names/prose stay left, compact categories default centered. Direct PDFs use black
text/rules with centered titles, filters, headings and applicable data. Do not
change bill/receipt fonts or wording as a side effect of management-report styling.
Outstanding aging days cannot be negative; stored charge due dates are valid.

### BD-27

Report periods (2 Nov): custom dates or 1/3/6/12-month academic-year blocks
choose the same report date parameters, not two simultaneous filters. Clamp blocks
to saved year dates, preserve saved-view year/block identity, and return to custom
mode after manual date edits. Existing grouping and each report's date meaning
remain unchanged. Current-balance reports do not become period activity reports.

### BD-28

Receipt wording (2 Nov): print Cash + Credit and Date. Deductions name only
applicable Discount, Waived and Credit; hide the row when none apply. Free is the
stored sibling-concession kind and displays as Discount, not Waived. Unknown old
types use Discount; changed amounts cannot reuse stale waiver classification.
This changes captions only, never settlement arithmetic or stored money.

## Database, Access And Recovery

### BD-29

Release data: the owner supplies the matching detached MDF/LDF pair beside
the project for packaging; missing development snapshots are not release blockers.
Never overwrite an attached school's data with a DACPAC/template. Validate supplied
release assets, compatibility and recoverability for the actual install scenario.
Keep all required SQL changes in numbered migrations; do not apply unrecorded fixes.

### BD-30

Backup/restore: reject unknown/foreign identities and incompatible structure;
warn about loss of newer records when restoring an older verified backup. Keep
safety copies, rollback and failed-restore recovery. A dated test pass is not
current installer/operator/physical-printer acceptance. A historical approved target
was to refuse version mismatches without upgrade; it is not proof that the current
installer has retired migrations. Do not remove migration/compatibility safeguards
until that separate transition is actually implemented and verified.

### BD-31

Audit retirement: migration 59 was the explicitly approved batch exception
for 19 generic audit writers. Preserve historical audit data/readers (fee history
still uses them), operational histories, bills, allocations, credit, certificate
issues and recovery markers. Keep financial review hashes/JSON; only audit-only
snapshots were retired. Future procedure changes retire their own generic audit
writes, not unrelated procedures. Missing old evidence stays disclosed.

### BD-32

Access/actor: preserve Administrator's full authority and the approved
Accountant/grant model. Screens, shortcuts and direct handlers use destination
permissions, not sidebar visibility. Recheck actor/permission on write and after
async reads. Attribute opening charges/credit application to the authenticated
actor, never a previous operator. Do not rewrite historical attribution.

### BD-33

Unknown outcomes (migration 60): flush database/operator/request-bound
recovery intent before financial/identity writes. Only the bounded, verified
rollback signal (SQL state 60) is a correctable refusal; an enclosing transaction
retains rollback ownership. Timeouts, lost replies and failed reconciliation reads
stay uncertain: check saved outcome before resubmitting. Preserve entered details
and photos; committed identity/number/receipt outcomes are never replayed on print
failure. Preview is read-only; stale reviewed snapshots must fail at commit.

### BD-34

Accepted audit limits: the local SQL trust boundary, decoded-photo RAM
measurement, SQL photo-size policy and live-module semantic verification have
separate accepted/deferred scopes (BL-03, FR-07/08, F-07). Do not silently redesign
authentication, impose a new photo-size limit or claim those gates passed while
tidying comments. Preserve each dated finding's evidence and supersession status.

### BD-35

Communication deployment: the optional module has its own structural/module
contracts and version. Worker/webhook validation is independent of the main
migration version. Opening the page or rerunning installation does not install
the module, start dispatch or enable delivery. Preserve incompatible-module and
permission failures, exact-definition verification and existing message history;
never approve a changed module merely by synchronizing metadata hashes.

### BD-36

Communication delivery: saved school telephone numbers are candidates, not
verified ownership/consent. Review contacts, purpose/channel consent and templates.
Disabled/master-pause is the default. Simulation makes no external request and
must not become live when settings change; require a new live campaign. Explicit
submission, approval/limits, provider credentials and dispatcher start remain
separate. Queued/accepted is not delivered. Keep authenticated webhook evidence,
idempotency, delivery history and role checks; never add Accountant grants merely
to expose the page. Missing provider infrastructure is not proof of delivery.

### BD-37

Staff: hire dates cannot be future; an entered leaving date cannot precede
hire. Add Job and Edit Job use their own dialogs from the staff employment panels,
without restoring the redundant header Employment button. Saved designation and
subjects prefill certificates but remain operator-editable.

### BD-38

Global record search: the dashboard text box keeps feature/page navigation;
Ctrl+K separately searches students, saved bills/receipts and payment references.
Default to All; an optional permitted record type filters SQL before limiting results.
Name/admission lookups include saved document participants, never today's family;
open the matched child's account while keeping the original document unchanged.
Do not duplicate a saved receipt's linked payment aliases for a person lookup;
keep those aliases searchable by number and keep standalone legacy entries.
Limit results and cancel stale reads; recheck permissions, actor and database
endpoint before displaying/opening. Explicit selected-record IDs override saved
workspace selections and keep normal busy/unsaved-work guards. Open saved
snapshots/readers only; lookup must not issue a bill, allocate a number or record
money. Keep legacy payment entries distinct when no original receipt is available.

## Dated decision records

The following records retain their original decisions, corrections, dates and verification limits.
Historical deployment/version/status statements describe their reviewed revision; later clarifications take precedence.

- [Academic-year date lock — 31 October 2026](#academic-year-date-lock)
- [Admission and fee-flow fixes — 28 October 2026](#admission-fee-flow-fixes)
- [Admission owns registration — 31 October 2026](#admission-registration-simplification)
- [Decision: remove certificate cancellation](#certificate-cancellation-removed)
- [Class and section setup consolidation — 2 November 2026](#class-section-setup-consolidation)
- [Focused collection fee actions — 27 October 2026](#collection-fee-actions)
- [Fee-change note — 27 October 2026](#fee-change-note)
- [Shared fee management — 31 October 2026](#fee-management-shared-rules)
- [Fee documents and the parent paper trail — 26 October 2026](#fee-receipt-paper-trail)
- [Shared grid alignment — 29 October 2026](#grid-alignment)
- [Historical admission and return eligibility — 27 October 2026](#historical-admission-return-eligibility)
- [Historical lifecycle actions and individual outstanding bills — 29 October 2026](#historical-lifecycle-and-individual-bills)
- [Promotion choices — 27 October 2026](#promotion-repeat-class-and-choices)
- [Readmission eligibility — owner decision, 1 November 2026](#readmission-eligibility-2026-11-01)
- [Remaining generic audit writers — 28 October 2026](#remaining-audit-retirement)
- [Student Fee Account workspace](#student-fee-account-workspace)
- [Transfer Class vs Transfer Section — approved decision, 1 November 2026](#transfer-class-section-2026-11-01)
- [Transport policy — 27 October 2026](#transport-cutoff-policy)
- [Owner decisions and business invariants](#owner-decisions-and-invariants)

<a id="academic-year-date-lock"></a>
<!-- BEGIN preserved-source: Docs/Decisions/ACADEMIC_YEAR_DATE_LOCK.md -->
Original source: `Docs/Decisions/ACADEMIC_YEAR_DATE_LOCK.md`. Original SHA-256: `85cb7d0ca8ee06ff902a87b299cf7b6b1076f5e73d61c79a4b2c6ce2870d6f85`.

<a id="academic-year-date-lock-academic-year-date-lock--31-october-2026"></a>
## Academic-year date lock — 31 October 2026

The owner approved allowing Name/Remarks corrections and changing Current while
locking Start/End dates once an academic year has student or financial usage.
Any enrolment, including historical/left/legacy records, counts as usage. Promotion
history locks both its source and destination years. Financial activity retains
its connection through enrolment. Age alone does not lock an unused year.

Migration 63 adds `DatesLocked` to `GetAcademicYears` and enforces the same boundary
transactionally in `SaveAcademicYear`, including saves from stale or older forms.
The native page disables both date controls for used years and leaves Name,
Remarks and Current editable. New Record and unused-year selection unlock dates.
Names must contain consecutive four-digit years, such as `2021-2022`. The name
validator and date autofill use the same parser. No saved year or history is
automatically corrected or removed.

Verification on the working tree based on
`2db45ddc870fefc4034474034a52327ea9497735`:

- 25 portable name checks passed, including `2021-20224444` rejection.
- Eight native control checks passed without a database connection.
- `Tools/TestAcademicYearDateLock.ps1` passed against session-local synthetic
  tables: historical enrolment, promotion source/destination, used-date refusals,
  allowed corrections, unused-date edits, creation, newly added usage/stale saves,
  malformed names, permissions, duplicate names and overlaps.
- Application Release build passed with zero warnings/errors. The native test
  project build reported NU1900 because NuGet vulnerability data was unavailable;
  compilation and the focused native checks succeeded.
- `Tools/ApplyAcademicYearDateLock.ps1 -Apply` applied migration 63 to the authorized
  live schema-62 database after COPY_ONLY/CHECKSUM backup and RESTORE VERIFYONLY.
  Before/after hashes matched across all 79 existing business tables (excluding
  the version ledger and rowversion metadata). Final read-only verification found
  schema 63 and both installed guards. Backup and execution evidence remain in
  the ignored `artifacts/academic-year-upgrade/7839923b13e54ca3a4eae318fe5f7617/`.

The first apply attempt was rolled back because the preservation checker treated
an empty table's null hash as a mismatch. A read-only rolled-back diagnostic
confirmed this without running the migration. Explicit empty-array hashing fixed
the checker; all 79-table checks then passed before the successful retry.

Joint operator acceptance in the updated application remains pending. This is
focused evidence, not full application or release certification.

<!-- END preserved-source: Docs/Decisions/ACADEMIC_YEAR_DATE_LOCK.md -->


---

<a id="admission-fee-flow-fixes"></a>
<!-- BEGIN preserved-source: Docs/Decisions/ADMISSION_FEE_FLOW_FIXES.md -->
Original source: `Docs/Decisions/ADMISSION_FEE_FLOW_FIXES.md`. Original SHA-256: `27bc611608c05aa196f43205d4e8a08e6debc957cf42fb89bd3401c124e270de`.

<a id="admission-fee-flow-fixes-admission-and-fee-flow-fixes--28-october-2026"></a>
## Admission and fee-flow fixes — 28 October 2026

The owner requested fixes for all six findings in the admission/fee-flow audit. Migration 60 and the corresponding C# changes implement that scope.

| Finding | Resolution |
|---|---|
| F01 cancellation authorization | Main button, context menu, handler, dialog and leaving-fee review share the collection-plus-adjustment requirement. ReverseBillingCharge enforces both permissions at the write. Paid/partial charges and the fixed family counter cannot use the unpaid cancellation shortcut. |
| F02 long reason | BillingCharges gains nullable CancellationReason, CancelledAt and CancelledByUserId, with a user FK. New cancellations retain the complete reason and original Remarks separately. Existing rows remain null in the new fields; old cancellation metadata is not invented. Fee Change History displays the dedicated reason where present. |
| F03 pending transport | Start/Stop Transport requires an Active placement in the collection and fee-assignment interfaces. Pending registrations retain their existing registration fee-plan editing path; the assignment view explains this. SQL's existing Active-student rule is retained. |
| F04 actor attribution | Opening-charge posting accepts the authenticated actor and every production caller passes it. Activation also passes its actor. Credit application rejects a missing/invalid actor when credit is available; it no longer borrows the previous credit operator. Historical deposit/application attribution is not rewritten. |
| F05 aging account access | Open Student Account uses the destination access rule plus Fees.View, consistently in UI checks and the student lookup. Annual-receipt permission remains separate. |
| F06 false unknown outcome | Direct admission, historical admission and registration completion mark a business rejection with SQL state 60 only after their own verified rollback and before their own commit. An enclosing application transaction retains rollback ownership and is not tagged by this new contract. C# recognizes only the bounded verified signal, retains entered details/photo and enables an explicit corrected submission. Timeouts, lost replies, ordinary SQL errors and failed reconciliation reads remain uncertain and never trigger automatic retry. |

The application and installer require schema 60. Restore the owner's original snapshot through its supported migration chain, including 59 then 60 where applicable. No certificate cancellation was reintroduced, and no retiring generic audit writes were restored. These are bill cancellation metadata and normal recovery guards.

Validation completed before live application:

- 16 focused native flow-fix assertions.
- 221 existing admission, fee-selection, family-counter, collection-return, account/recovery and statement assertions.
- 28 existing transport assertions.
- 27 isolated SQL assertions plus full application startup/restore compatibility at schema 60. These cover migration rollback, retention of existing values, distinct restricted users, 500-character old notes and reasons, explicit credit actors, errors before and after number allocation, enclosing transaction rollback and a successful admission after refusal.
- Application/native project builds successfully; the native project reports only NU1900 for unavailable NuGet vulnerability data.

The SQL fixture owns a uniquely named private LocalDB instance, uses fictional records and accepts no school connection. Its source is Tools/TestAdmissionFeeFlowFixes.ps1; native cases are in Tests/ThemeSmokeChecks/Program.AdmissionFeeFlowFixes.cs.

Live deployment status: migration 60 is prepared and validated but has not been applied. Automatic approval review rejected live deployment because the owner's “fix all” request did not clearly authorize this exact production update. The database remains at schema 59; the new build requires migration 60 before use. No backup or live change was made by the rejected command.

Once explicitly approved, check live procedure definitions for drift, create and verify a private COPY_ONLY backup, apply the migration under one transaction and compare all original column values before committing. Newly added cancellation fields must remain null on pre-existing records. The only intended data write during deployment is the new migration-version marker.

<!-- END preserved-source: Docs/Decisions/ADMISSION_FEE_FLOW_FIXES.md -->


---

<a id="admission-registration-simplification"></a>
<!-- BEGIN preserved-source: Docs/Decisions/ADMISSION_REGISTRATION_SIMPLIFICATION.md -->
Original source: `Docs/Decisions/ADMISSION_REGISTRATION_SIMPLIFICATION.md`. Original SHA-256: `b0ea418cf08ab055f5b304e80e9b45505bf62489b67afa96d39bac4c68a463c5`.

<a id="admission-registration-simplification-admission-owns-registration--31-october-2026"></a>
## Admission owns registration — 31 October 2026

The owner approved using Admission and its sibling panel for pending registration,
confirmation and cancelled-registration reopening, retiring the separate Family
Admission navigation entry. This supersedes the registration-navigation portions
of the historical admission workflow and family-workspace rollout documents.

<a id="admission-registration-simplification-operator-flow"></a>
### Operator flow

- Admit Sibling requires a loaded student and opens one child editor using the
  established registration writer, fee quote and durable unknown-save recovery.
  Father name, phone, address and place of birth are inherited and read-only.
  Academic year is locked to the current year and rechecked inside the save
  transaction. It saves a pending child and retains the loaded student afterward.
- Admission search accepts pending and cancelled records. Daily Work → Pending Registrations
  opens the existing list, with Open and Review both loading the selected child in
  the existing Admission form. Opening a sibling loads that child in the same form.
- Student & Siblings includes the loaded student, including a child without a
  family. Pending rows offer Confirm & Save and Cancel; cancelled rows offer
  Reopen. These actions are absent from the main toolbar. Officially admitted
  students retain their ordinary lifecycle actions. Confirm loads/rechecks the
  target and validates required details before the existing official-number review.
- The existing Save button becomes Confirm & Save for pending children: completing
  details leads directly to official-number review and one confirmation transaction.
  Actions → Save Registration Draft is the optional non-confirming route. Save and
  registration confirmation retain the existing transaction/recovery contracts.
  Check Registration Save appears only when a saved outcome needs checking.
- Admission shows the number of pending siblings instead of a pending-list button.
  Reopen uses the same searchable PlacementPicker as Add Placement. Reopen and
  cancellation dialogs use compact field rows with short, single-line reasons.
- Admission messages occupy a compact full-width row below the action buttons.
  Long messages retain the existing tooltip; buttons can wrap at narrower widths.

<a id="admission-registration-simplification-reopening-and-history"></a>
### Reopening and history

Migration 64 adds `ReopenPendingRegistration` and `RegistrationReopenRequests`.
Reopening requires Students.View, Students.Admit, Students.Administer and Fees.View.
Only a cancelled pending identity without an official admission number qualifies.
The operator chooses a valid placement, academic year and joining date not before
the cancellation date, supplies a reason (up to 250 characters in the UI), and
reviews the outstanding amount and preservation notice before applying.

The same student identity and temporary reference receive a new pending placement;
the current attempt's admission date/campus match that placement. Earlier cancelled
placements, cancellation events, family links, fee agreements, bills, payments and
allocations remain recorded. No old fee agreement or cancelled bill is restored.
Review the pending child's fee plan in Admission before confirmation. New request
and operational lifecycle records preserve recovery and history independently of
the retiring generic audit system.

Reopening checks a fresh student/placement/financial snapshot inside the write
transaction. Identical request replay returns the original result; changed replay
is refused. An uncertain UI result disables retry until the student is reloaded.
Repeated cancellation permits previous cancelled episodes, while real attendance
and certificate-history refusals remain. Admitted former students use readmission.

<a id="admission-registration-simplification-verification-and-local-deployment"></a>
### Verification and local deployment

Working tree based on `c7f94141f01ae17914b3ecdc2fd2cd5f9e361fd3`.

- `Tools/TestRegistrationWorkflow.ps1` creates a uniquely named owned clone from a
  COPY_ONLY backup. It tests actual registration, sibling linking, cancellation,
  reopening, repeated cancel/reopen, confirmation and official numbers. Checks
  cover read-only preview, unauthorized access, stale review, request replay and
  changed payload refusal. Hashes verify the cancelled placement and all existing
  bill/payment/allocation/family-link rows remain unchanged during reopening.
  Fixture writes never run in the school database; the clone is dropped afterward.
- `ThemeSmokeChecks --registration-workflow` checks native row actions, pending
  search, required-detail refusal, permissions, sibling action visibility and the
  compact status row at 1600/1050 widths without connecting to a database.
- Migration 64 was applied locally after verified COPY_ONLY/CHECKSUM backup and
  RESTORE VERIFYONLY. All 79 previously existing business-table hashes matched.
  Final read-only verification found schema 64 and zero live reopening requests.
  The ignored backup/evidence directory is
  `artifacts/registration-workflow-upgrade/5c6028dcc3b9487aadda2ccf4d306c23/`.

Joint operator acceptance, including actual completion of a newly added sibling
through the updated Admission UI, remains pending. Focused checks do not certify
every application workflow or the release package.

<a id="admission-registration-simplification-owner-clarification--2-november-2026"></a>
### Owner clarification — 2 November 2026

The Siblings panel now lists only brothers and sisters, excluding the loaded student. This supersedes the inclusion described above. The loaded child uses Confirm & Save and Actions → Cancel Registration / Reopen Registration. Sibling row actions remain available.

<!-- END preserved-source: Docs/Decisions/ADMISSION_REGISTRATION_SIMPLIFICATION.md -->


---

<a id="certificate-cancellation-removed"></a>
<!-- BEGIN preserved-source: Docs/Decisions/CERTIFICATE_CANCELLATION_REMOVED.md -->
Original source: `Docs/Decisions/CERTIFICATE_CANCELLATION_REMOVED.md`. Original SHA-256: `9aca608a3c73e80dd64f23187b5e6e295357aa94a4044926d8affc9bf2f708ed`.

<a id="certificate-cancellation-removed-decision-remove-certificate-cancellation"></a>
## Decision: remove certificate cancellation

Date: 25 October 2026. Owner decision recorded from the certificate workflow discussion.

<a id="certificate-cancellation-removed-decision-and-reason"></a>
### Decision and reason

Remove the certificate cancel/void feature entirely from the normal application, rather than merely hiding its button. The user first requested hiding it, then preferred removal if the scope was manageable.

The action introduced unnecessary complexity. The user cancelled a row and could then issue another copy from that same row, making cancellation appear ineffective. More importantly, “cancel” or “void” can imply deleting the certificate record or undoing the student's leaving, enrollment or fee decisions, although the operation only invalidated a paper copy. Hiding cancelled rows alone would conceal that ambiguity and make replacement handling harder to discover.

The supported counter flow remains: **Create → Save Changes while uncollected → Print → Mark Collected → locked copy**. **Issue Again** prepares a separate copy while preserving the earlier one. Reprinting uses the selected saved copy. These existing actions remain otherwise unchanged.

<a id="certificate-cancellation-removed-implementation-and-historical-data"></a>
### Implementation and historical data

- Remove the shared cancellation button, event handler and command call from all ten certificate forms.
- Migration 51 drops `dbo.VoidCertificateCopy`; startup compatibility no longer requires that procedure.
- Keep existing historical cancellation markers, reasons and guards read-only. They preserve what already happened and do not offer a new cancellation operation. Removing the feature must not resurrect, delete or silently collect old cancelled copies.
- Do not undo student leaving, enrollment, bills, payments, allocations or fee decisions as part of this change.
- RDLC files are unchanged.

Do not reintroduce certificate cancellation or a “show cancelled / prepare replacement” workflow without a new explicit owner decision.

<a id="certificate-cancellation-removed-follow-up-permanent-column-removal"></a>
### Follow-up: permanent column removal

On 25 October 2026 the owner explicitly approved permanently dropping `VoidedAt` and `VoidReason` from code and SQL, then restored a clean schema-48 database containing students and leaving certificates. Migration 52 removes the columns and all active cancellation branches, and keeps collection locks, certificate records and recovery markers. It refuses a different snapshot containing historical cancelled copies to avoid silently reviving them. Old migrations remain immutable upgrade history. This supersedes the earlier instruction to retain the columns in the current schema.

<!-- END preserved-source: Docs/Decisions/CERTIFICATE_CANCELLATION_REMOVED.md -->


---

<a id="class-section-setup-consolidation"></a>
<!-- BEGIN preserved-source: Docs/Decisions/CLASS_SECTION_SETUP_CONSOLIDATION.md -->
Original source: `Docs/Decisions/CLASS_SECTION_SETUP_CONSOLIDATION.md`. Original SHA-256: `0c67ce798f9b714a917c10a9b6322e7b669b1fb70c078ea74b8f4976c6f9354e`.

<a id="class-section-setup-consolidation-class-and-section-setup-consolidation--2-november-2026"></a>
## Class and section setup consolidation — 2 November 2026

Campus - Classes / Sections is the single setup entry point. Manage Classes and
Manage Sections open the complete existing editors in separate dialogs. Programs,
class/section editing, ordering, usage checks and guarded deletion are retained.
The old Classes & Sections workspace/navigation wrapper is removed.

Catalog records are school-wide. Renames affect every campus; creation does not
assign the new record to any campus. Apply Changes remains the only assignment
save action. Existing SQL procedures, permission requirements and history remain
unchanged; no migration is required.

Closing a catalog dialog refreshes the current campus matrix. Pending differences
are restored by ClassId/SectionId after loading the fresh saved baseline. Renamed
and reordered records retain their ticks, deleted keys are dropped, and new
combinations start from the database baseline (normally unassigned). Refresh
Catalogs retries a failed read while retaining the pending differences. Reset
Changes restores the fresh saved baseline. Campus switching retains the existing
owner-approved prompt-free discard policy. Small editor drafts remain prompt-free;
closing a dialog during a submitted write is blocked.

Verification: build the native ThemeSmokeChecks project and run
`--class-section-setup` on Windows. The assignment fixture
checks ID-based draft restoration across catalog changes and Reset behavior;
the workspace fixture checks the existing full editors and in-flight save guard.
Existing SQL delete/usage protections are reused, not replaced by UI-only checks.

The shared master busy contract now includes asynchronous deletion as well as
saving, so catalog dialogs cannot close during either write. Two existing
shortcut-permission test callbacks were updated to accept the dashboard reader's
cancellation token, resolving pre-existing native harness compilation errors.

<!-- END preserved-source: Docs/Decisions/CLASS_SECTION_SETUP_CONSOLIDATION.md -->


---

<a id="collection-fee-actions"></a>
<!-- BEGIN preserved-source: Docs/Decisions/COLLECTION_FEE_ACTIONS.md -->
Original source: `Docs/Decisions/COLLECTION_FEE_ACTIONS.md`. Original SHA-256: `28c4374e5382720af65c68be8d4f38aee196986e592be38aceee2b48a716bb9a`.

<a id="collection-fee-actions-focused-collection-fee-actions--27-october-2026"></a>
## Focused collection fee actions — 27 October 2026

4 November clarification: ordinary Fee Collection uses only the main selected
student's advance, against that student's own listed fees. Selecting siblings
does not pool balances or opt in their advance. To use another child's money,
record an explicit Transfer in Advance Balance and select the recipient as main.
The checkbox amount shows advance usable against the listed fees; Advance left
tracks the main student's remaining balance. A short note explains when sibling
fees need a transfer. Blank/zero cash during allocation is valid, and a credit-only
collection can save with zero new cash when the owner has fees to settle. The
separate fixed family counter retains its per-child advance handling without
crossing account ownership. These are C# allocation/display changes; the SQL
ownership checks, credit ledger and explicit transfer workflow are unchanged.

28 October follow-up: cancelling an unpaid charge requires both collection and adjustment permission through every UI route and at the SQL write. Migration 60 retains a full cancellation reason, actor and timestamp separately from the bill's existing notes; it does not delete the bill or its history. See [the admission/fee flow fixes](BUSINESS_DECISIONS.md#admission-fee-flow-fixes).

The collection grid shows unpaid fees and newly waived posted fees awaiting a
receipt or fee-change acknowledgement. A zero balance alone cannot distinguish
payment from waiver: the existing visit/snapshot baseline supplies that evidence.
Once a saved document records a waiver, it leaves the collection grid. Historical
payments, credit applications and already documented waivers do not reappear.

Waived rows are read-only, clearly labelled, excluded from allocation, and offer
only View Fee Details. On payable rows, Give/Change Discount and Remove Discount
open separate, fixed tasks for the selected bill. These dialogs cannot switch to
waiver, rate correction, fee-plan changes or a different period. Waive Remaining
Dues keeps its direct-save path and preserves earlier payments. Future fee-plan
and fee-rate management remain in the fee-management workflow.

View Fee Details is read-only. Uncertain saves retain a separate Check Fee Change
Save recovery action, shown when recovery is pending, without reopening general
editing choices. Payment drafts must be finished or cleared before a saved fee
change or its acknowledgement is submitted.

When only pending waivers remain, the main print action becomes Print Fee Changes
and uses the established zero-payment acknowledgement. After printing, the grid
reloads from saved documents. With dues remaining, Print Outstanding retains the
existing bill flow and pending waivers accompany the next payment receipt.
This does not change the financial Outstanding/Aging report's debt-only meaning.

No schema or stored-procedure changes are required: selection uses the existing
schema-56 FeeDocumentAccounts and FeeVisitLines functions. The acknowledgement
print layout is documented separately in [Fee-change note](BUSINESS_DECISIONS.md#fee-change-note).

<!-- END preserved-source: Docs/Decisions/COLLECTION_FEE_ACTIONS.md -->


---

<a id="fee-change-note"></a>
<!-- BEGIN preserved-source: Docs/Decisions/FEE_CHANGE_NOTE.md -->
Original source: `Docs/Decisions/FEE_CHANGE_NOTE.md`. Original SHA-256: `d6b245503104d0759ad850d5883169ea4f287a131ffc360675ae36e275da7129`.

<a id="fee-change-note-fee-change-note--27-october-2026"></a>
## Fee-change note — 27 October 2026

Print Fee Changes uses the dedicated FeeChanges.rdlc template for both individual
and family acknowledgements. Established bills and payment receipts retain their
existing layouts. The stored receipt reference and issue date remain unchanged.

Each affected posted fee shows its saved fee/dues before the change, the signed
change (waiver, discount, withdrawn discount, fee correction or settlement
adjustment), and dues now. A recorded reason appears below the fee. Unchanged
unpaid fees are listed separately and reconcile to the saved outstanding total.
An acknowledgement never claims a payment was received.

Fee-plan figures are a separate section and are not added to posted dues. Plans
identical to the previous saved document for that child are omitted. When no
earlier plan snapshot exists, terms are labelled Saved Fee Plan; the note does
not invent old terms or claim a particular change. Ended plans are identified.
All comparisons use saved snapshots, including on historical reprints, rather
than today's fee assignments, payments or balances.

This is presentation and read selection only. No schema upgrade, financial
mutation or migration is needed. Issuing the acknowledgement continues to use
the existing collection writer and its visit/recovery markers.

<!-- END preserved-source: Docs/Decisions/FEE_CHANGE_NOTE.md -->


---

<a id="fee-management-shared-rules"></a>
<!-- BEGIN preserved-source: Docs/Decisions/FEE_MANAGEMENT_SHARED_RULES.md -->
Original source: `Docs/Decisions/FEE_MANAGEMENT_SHARED_RULES.md`. Original SHA-256: `a90886d4cc19e968927b03b599c970e83b77bade3d0fc8183d9eacf1ae8222e6`.

<a id="fee-management-shared-rules-shared-fee-management--31-october-2026"></a>
## Shared fee management — 31 October 2026

The owner confirmed that Admissions fee management and Fee Collection fee changes
must use the same rules and options through `ChangeFeeDiscountForm`.

<a id="fee-management-shared-rules-decision-1-different-entry-points-shared-editor-behavior"></a>
### Decision 1: Different entry points, shared editor behavior

- Admissions opens one fee-management window with four action tabs: Give Discount,
  Waive Fee, Correct Fee Amount and Remove Discount.
- Fee Collection opens the corresponding action in a separate window from each
  right-click menu entry. The action is preselected; the action tabs are hidden.
- Both show the same applicable amounts, concession types, scopes, date limits,
  optional reason, and paid-money treatment. Both use enter details → confirm on
  the same screen. Financial validation and snapshot checks run internally before
  saving; there is no subsequent review screen.
- Both load the current saved fee terms through the shared assignment factory.
  Permissions, payment protection, stale-data checks and save recovery are shared.

<a id="fee-management-shared-rules-decision-2-collection-intentionally-starts-with-a-single-month-period"></a>
### Decision 2: Collection intentionally starts with a single-month period

- Fee Collection resets **both From month and Through month to its start month**.
  The start month is the clicked charge's month, or the current month when no
  charge month is supplied, constrained to the fee's valid period.
- The operator can still change the period using the same rules and limits.
- Admissions keeps its normal period default, including its normal through month.

These entry-point and initial-period differences are intentional. Do not align
Collection's initial Through month with Admissions or introduce other differences
in fee rules/options without a new owner decision.

The C# implementation records these decisions at `ConfigureCollectionAction`.

<!-- END preserved-source: Docs/Decisions/FEE_MANAGEMENT_SHARED_RULES.md -->


---

<a id="fee-receipt-paper-trail"></a>
<!-- BEGIN preserved-source: Docs/Decisions/FEE_RECEIPT_PAPER_TRAIL.md -->
Original source: `Docs/Decisions/FEE_RECEIPT_PAPER_TRAIL.md`. Original SHA-256: `d0f4120a35503cf16767a3d468465945fb7ae75799e3f91d35b324396b3af5bc`.

<a id="fee-receipt-paper-trail-fee-documents-and-the-parent-paper-trail--26-october-2026"></a>
## Fee documents and the parent paper trail — 26 October 2026

<a id="fee-receipt-paper-trail-visit-based-clarification--27-october-2026"></a>
### Visit-based clarification — 27 October 2026

Migration 56 supersedes the cumulative presentation below. A visit begins with
each fee's closing outstanding on the previous relevant receipt. Only subsequent
fees, rate/payment adjustments and further concessions affect the new document.
Settled fees and their earlier concessions remain in history. Bills use the same
position as receipts; printing a bill does not consume the receipt baseline.

New version-3 snapshots save both the visit's printable lines and the underlying
account state. A monotonic visit sequence makes same-second and backdated visits
unambiguous. Existing version-2 snapshots are projected from their saved data and
earlier receipts when read; their stored financial records are never rewritten.

Document student order follows the existing allocator exactly: configured class
catalog order, then numeric admission number and suffix. It does not change cash
or credit already allocated to children. New student-order metadata is saved with
the receipt, keeping its order stable on reprint.

The owner's actual receipts FR-2026-00012, 00013 and 00014 verify family due
14,400 -> 7,400 -> 0. Visit three shows Shayan's opening 600 less a further 200,
payment 400; Abdullah's opening 6,800 less a further 5,200, payment 1,600. Faryal's
fees, settled on visit two, no longer appear.

`Tools/TestFeeVisits.ps1` tests migration preservation, the actual three visits,
new fees, partial payments, additional discounts, waivers, rate increases,
withdrawn concessions, bill issuance and concurrent acknowledgements in an owned
COPY_ONLY clone. It exports actual reader data for RDLC rendering checks.
`Tools/ApplyFeeVisits.ps1 -Apply` verifies a backup and unchanged business records
before committing schema 55 -> 56.

<a id="fee-receipt-paper-trail-earlier-cumulative-snapshot-implementation"></a>
### Earlier cumulative snapshot implementation

The receipt previously saved only charges receiving cash, credit or an additional
collection discount. A waiver saved before collection could disappear; an existing
discount appeared only as an already reduced fee. Admission 17 exposed both cases.

Migration 55 adds complete version-2 collection snapshots. SQL validates the actual
payment allocations, then independently captures all selected children's billed
charges, including zero-payable waivers, prior settlements and cancelled-charge
notices. Correction charges are consolidated into their original fee. Agreed fee
schedules are also captured, including effective periods and cancelled agreements.

On paper, each fee shows its plain name and full amount. Per-fee concession
explanations and reasons are omitted; the combined concession/waiver appears in
the summary deduction. The complete saved breakdown is retained. A visible
"Less: Previously paid" line reconciles partial and repeated collections. The
summary remains: Less: Discount / Waived, Net payable, Payment received, Balance
due. Printed cumulative concessions are separate from discounts newly applied in
this transaction, preventing repeated collection-report totals.

Every new receipt retains its issued amounts and recorded balances on reprint,
including annual reprints. Later refunds/corrections are flagged; the original
amounts are not replaced by today's fees. Old snapshots are not silently repaired:
their existing numbers and data remain intact and a legacy limitation is disclosed.

Fee Collection's Student Fees menu includes **Print Fee Changes**. It saves and
prints a numbered acknowledgement with no payment, so a waiver clearing all dues
or an agreement applying to future months still has parent-visible documentation.
Its saved agreement notes do not contribute to billed amounts. It cannot receive
cash or apply credit. Normal Print Outstanding still requires a positive due.

Collection reads a fee/account review token before and after loading each child.
The transaction validates those tokens under the billing lock before changing any
fees, credit or payments. Bills validate gross fees and concessions as well as due.

Verification:

- `Tools/TestReceiptPaperTrail.ps1`: migration and financial sequences in an owned
  COPY_ONLY clone, including admission 17, partial payment, waiver, fee increase,
  credit, refund, concession reduction, immutable originals and stale review.
- ThemeSmokeChecks `--fee-documents`: individual and family RDLC PNG/PDF output,
  complete fee breakdowns, previous-paid deductions and zero-money acknowledgements.
- `Tools/ApplyReceiptPaperTrail.ps1 -Apply`: verified backup, schema 54 -> 55 in a
  transaction, hashes of all existing business columns before/after. No existing
  receipt, charge, allocation, payment, fee agreement or correction is rewritten.

<!-- END preserved-source: Docs/Decisions/FEE_RECEIPT_PAPER_TRAIL.md -->


---

<a id="grid-alignment"></a>
<!-- BEGIN preserved-source: Docs/Decisions/GRID_ALIGNMENT.md -->
Original source: `Docs/Decisions/GRID_ALIGNMENT.md`. Original SHA-256: `24eb45f5d660ed6f709619a57caa71a82f64bb9a85f2e8f1f9e3e0ea17155ee0`.

<a id="grid-alignment-shared-grid-alignment--29-october-2026"></a>
## Shared grid alignment — 29 October 2026

Headings and displayed values must use the same alignment, including after
binding, resizing, theme refresh and sorting. The shared grid renderer owns
these defaults; individual pages can deliberately choose another alignment.

- Names, descriptions and other prose retain their existing left alignment.
- Compact dates, periods, admission numbers, numeric values, checkboxes and
  category badges default to centered alignment when the page has no preference.
- Explicit page choices remain authoritative. For example, financial statements,
  future-fee comparisons and receipt breakdowns retain right-aligned money.
- Fee Collection and its recent-payment amounts use centered headings and values.
  Fee-rate copy and bulk-change previews center their formatted currency strings
  without changing those strings or their underlying numeric plans.
- Last Posted/Last Billed Month and period columns receive date-style alignment.

Sortable native headers reserve an arrow slot that shifts their text relative
to body cells. The shared renderer paints centered/right-aligned sortable header
text using the full padded column bounds, and retains the sort indicator and
native sorting interaction. Unsortable and left-aligned headers keep native
painting. Existing header accents, wrapping and theme colors remain.

The implementation changes presentation only. It does not rewrite values,
change number formats, alter payment allocations or require a SQL migration.
Regression coverage includes real fee-assignment and payment-history grids,
typed student records, right-aligned statements, sorting, refreshes and resizing.

<!-- END preserved-source: Docs/Decisions/GRID_ALIGNMENT.md -->


---

<a id="historical-admission-return-eligibility"></a>
<!-- BEGIN preserved-source: Docs/Decisions/HISTORICAL_ADMISSION_RETURN_ELIGIBILITY.md -->
Original source: `Docs/Decisions/HISTORICAL_ADMISSION_RETURN_ELIGIBILITY.md`. Original SHA-256: `0396e3df45f73d1bf9e92390efc762822b3c01dc403ed1b6fb5421306a1ef82c`.

<a id="historical-admission-return-eligibility-historical-admission-and-return-eligibility--27-october-2026"></a>
## Historical admission and return eligibility — 27 October 2026

An admission entered into a completed academic year is an active, confirmed
historical placement. Its BillingReady=0 deliberately prevents historical fees;
it does not mean that the child has been promoted and is awaiting return.

Family Admission identifies this placement as Previous Academic Year and uses
the existing-child editor. Historical posted dues remain collectible. Individual
Admission and Confirm Return omit expired placements from their pending-return
list. Future-year placements are likewise not offered for early return.

Promotion eligibility is preserved: these students can be selected from their
previous-year source class. The normal promotion/hold batch creates the new
placement. A confirmed active placement in the current academic year with billing
not yet enabled may then use Confirm Return. Holds retain their separate reviewed
Lift Hold / Resume workflow.

Migration 57 corrects GetPendingReturnAdmissions and independently guards
ConfirmPromotedAdmission against expired/future years, legacy setup and draft
placements. It preserves all existing student, enrollment, fee, payment, credit,
history and recovery records. Reapply the versioned migration after restoring an
earlier snapshot; do not repair this classification by setting historical
BillingReady=1 or by changing the student's original admission/placement dates.

<!-- END preserved-source: Docs/Decisions/HISTORICAL_ADMISSION_RETURN_ELIGIBILITY.md -->


---

<a id="historical-lifecycle-and-individual-bills"></a>
<!-- BEGIN preserved-source: Docs/Decisions/HISTORICAL_LIFECYCLE_AND_INDIVIDUAL_BILLS.md -->
Original source: `Docs/Decisions/HISTORICAL_LIFECYCLE_AND_INDIVIDUAL_BILLS.md`. Original SHA-256: `e8703afcaedf3b3e7daaf53ef0a9ca675c31d17ee449c03adf9b83a15c08ea7b`.

<a id="historical-lifecycle-and-individual-bills-historical-lifecycle-actions-and-individual-outstanding-bills--29-october-2026"></a>
## Historical lifecycle actions and individual outstanding bills — 29 October 2026

The owner approved closing the historical-placement gaps while preserving useful
history-building operations. The rule concerns the placement's academic year,
not how long ago the student was originally admitted.

- Historical transfers remain available for Active placements. Their effective
  date must fit the recorded academic year and follow the enrollment start. A
  reason is required. The UI requires an explicit historical date selection.
- Class/campus transfers preserve existing fee plans for historical or nonbilling
  placements. The transfer writer and internal fee-sync writer independently
  enforce this. Historical transfers never enable BillingReady.
- Historical departure remains available with an explicit leaving date within
  the recorded placement/year and the existing reason/financial review. Its date
  is not silently assumed to be today or the year-end date.
- Hold, resume and the exceptional Left-as-hold review require a placement
  covering today. Prepare the current placement before resuming attendance;
  earlier factual corrections remain in Placement History. SQL also enforces
  the current-year requirement, including for backdated requests.
- Personal details, photos, history maintenance, promotion/repeat and genuine
  readmission keep their existing paths and permissions.

Migration 61 delivers all SQL changes and exposes year boundaries to Admission
and the transfer editor. Application and installer require schema 61. Existing
students, financial records, operational histories and recovery markers remain.

Fee Collection retains its existing selected-family bill behavior and grouping.
The adjacent buttons are labelled Print Family Bill and Print Student Bill, with
matching widths, heights and centered captions. Print Student Bill always passes
only the main selected admission to the existing outstanding-bill preview and
uses individual layout. It does not change checked siblings or bill grouping.
Family printing retains its fee-change acknowledgement fallback when applicable;
the individual outstanding button always uses the outstanding-bill preview.

Validation uses a new disposable schema-only SQL database with fictional records,
plus UI checks for menu/date restrictions, print scopes, permissions, stale
previews and rendered button alignment. No school database is used by these tests.

<!-- END preserved-source: Docs/Decisions/HISTORICAL_LIFECYCLE_AND_INDIVIDUAL_BILLS.md -->


---

<a id="promotion-repeat-class-and-choices"></a>
<!-- BEGIN preserved-source: Docs/Decisions/PROMOTION_REPEAT_CLASS_AND_CHOICES.md -->
Original source: `Docs/Decisions/PROMOTION_REPEAT_CLASS_AND_CHOICES.md`. Original SHA-256: `59559e273e17735e43384952e538e25411394fee5608cbf4c0a9df88d1510991`.

<a id="promotion-repeat-class-and-choices-promotion-choices--27-october-2026"></a>
## Promotion choices — 27 October 2026

Promotion uses the selected source campus and academic year, followed by class
and section. Source class and section choices contain only active placements
returned by the existing promotion-candidate rules. Changing campus or year
reloads these choices; completing a batch removes sources that are now empty.

Promote advances to the immediate next class in the same academic program,
according to the full class catalog's display order and class-ID tie breaker.
That exact next class must have sections configured at the source campus. An
unconfigured intermediate class cannot be skipped in favor of a higher class.
The destination is checked again against fresh catalog and campus assignments
before processing. Destination sections may be empty of students.

Repeat Class moves a student into the destination academic year while keeping
the same class and section. Unticking excludes a student from the batch.
The form, tooltips, review, result and history use Repeat Class / Repeated Class.
Historical placements do not offer Confirm Return; placements in the current
year offer it for both promoted and repeating students before billing.

Existing HOLD decisions and the closed placement's Promoted status remain
unchanged in storage. Placement History reads the saved repeat decision to
display Repeated Class without changing lifecycle records or correction rules.
These are application changes; no database migration or record repair is needed.

<!-- END preserved-source: Docs/Decisions/PROMOTION_REPEAT_CLASS_AND_CHOICES.md -->


---

<a id="readmission-eligibility-2026-11-01"></a>
<!-- BEGIN preserved-source: Docs/Decisions/READMISSION_ELIGIBILITY_2026-11-01.md -->
Original source: `Docs/Decisions/READMISSION_ELIGIBILITY_2026-11-01.md`. Original SHA-256: `7b768ac681b4651b1975904f4cbac1b96aced924c1daa3a264ec997fed38e530`.

<a id="readmission-eligibility-2026-11-01-readmission-eligibility--owner-decision-1-november-2026"></a>
## Readmission eligibility — owner decision, 1 November 2026

Re-Admit is for an officially admitted student whose current status is Left or
Inactive (or legacy Withdrawn). Leaving through a School/College leaving
certificate and recording departure in Student Admissions both lead to the
canonical Left status. Original departure dates, certificates and history remain.

Reaching the highest class **does not automatically change the status**.
The existing Record Departure / Final-Class Completion action, or the leaving
certificate workflow, records the departure. That student can then be
readmitted to a valid placement if the normal academic-year, date and fee
checks pass. There is no artificial next-class rule on readmission.
A still-Active final-class student cannot be readmitted.

A cancelled pending registration without an official admission number uses
Reopen Registration, not Re-Admit. Active or Pending status, an active
enrollment, and an unresolved posting hold block readmission; held students
use Lift Hold / Resume. Readmission writes still require Students.Administer.

Migration 68 adds readmission status, official-number and hold guards to the
existing dbo.ReAdmitStudent writer, and aligns the C# screens. The lifecycle
enrollment reader permits Enrollment.Manage or Students.Administer to view
history; each actual writer retains its own required permission. The
individual UI keeps calling ReAdmitStudentWithNumberChoice, whose readmission
writer is governed by dbo.ReAdmitStudent's eligibility checks.

No existing student, enrollment, certificate, fee, bill, payment or history
rows are changed. The SQL change is a forward-only migration after schema 67.
Financial review, admission-number decisions, date checks and past cases
are preserved. Run targeted checks against an owned disposable database
before any production deployment.

<!-- END preserved-source: Docs/Decisions/READMISSION_ELIGIBILITY_2026-11-01.md -->


---

<a id="remaining-audit-retirement"></a>
<!-- BEGIN preserved-source: Docs/Decisions/REMAINING_AUDIT_RETIREMENT.md -->
Original source: `Docs/Decisions/REMAINING_AUDIT_RETIREMENT.md`. Original SHA-256: `d40b854f719e99be26a0dce751205fd585c11e367411bc1926420190acf0bb26`.

<a id="remaining-audit-retirement-remaining-generic-audit-writers--28-october-2026"></a>
## Remaining generic audit writers — 28 October 2026

Following the redundancy review and live schema-58 inventory, the owner explicitly requested work on the remaining audit writers and delivery as migrations. Migration 59 is the scoped exception to the earlier gradual-retirement policy: it revises the 19 identified procedures together, removing their generic audit writes and audit-only snapshots. Future unrelated procedure changes still follow AGENTS.md.

The migration retains original procedure signatures, permissions, business writes, returned results, errors and transaction handling. Preview hashes and JSON used to reject stale decisions remain. The certificate-link repair cursor existed solely to write audit rows; the set-based business repair remains.

No historical audit data is deleted. AuditLogs, AuditOperations, AuditValueLeaves and historical readers remain because StudentFeeChangeHistory still uses them. That report continues to disclose incomplete history; stopping generic audit writes does not create a complete replacement change ledger.

Operational student/enrollment history, certificate issue records, bills, payments, allocations, credits, request markers and recovery state remain. The communication module's separate operational history also remains; only its writes into the retiring dbo audit system are removed.

The application's required version and installer target are 59. A restored snapshot at schema 58 must apply Migrations/59.sql before running this build. Earlier supported snapshots must apply every missing numbered migration in sequence. The DACPAC remains the reviewed schema-only baseline, not a replacement for school data.

Validation:

- Validation/CompareRemainingAuditSql.ps1 parses the 19 baseline/migrated definitions and compares business DML, procedure calls, THROW statements, transactions and returns. Audit-only statements are explicitly excluded.
- Tools/TestRemainingAuditRetirement.ps1 creates a private, owned LocalDB instance, publishes the schema-only DACPAC and adds fictional data. It checks rollback, unchanged existing rows, parameter contracts, rejection behavior, successful sample business operations and retained historical readers. It accepts no school connection or backup.
- The application builds successfully with zero warnings and errors.
- Final results: 19 parsed business-SQL comparisons and 75 isolated database checks passed, including populated historical audit rows and rollback of all procedure definitions. The native test project also builds; its only warning is NU1900 because NuGet vulnerability data could not be fetched.

Deployment status: the owner explicitly approved live application with “apply it”. Migration 59 was applied successfully after a COPY_ONLY backup with checksums and RESTORE VERIFYONLY verification. The original procedure definitions were checked for drift before execution. All existing data-table fingerprints (excluding the version marker) were unchanged inside the migration transaction before commit. Live schema is now 59, and no procedure calls the retiring generic audit writer. Existing audit records/readers remain. Application time (UTC): 2026-10-28T13:53:28.5961021Z. The private backup path is recorded in ignored local evidence at artifacts/audit-retirement-live-result.json.

The earlier automatic approval rejection was resolved by the owner's explicit approval. No permission remains pending.

<!-- END preserved-source: Docs/Decisions/REMAINING_AUDIT_RETIREMENT.md -->


---

<a id="student-fee-account-workspace"></a>
<!-- BEGIN preserved-source: Docs/Decisions/STUDENT_FEE_ACCOUNT_WORKSPACE.md -->
Original source: `Docs/Decisions/STUDENT_FEE_ACCOUNT_WORKSPACE.md`. Original SHA-256: `13e29a7e7eb6f0a9588f128c7e84532d5395c4c3ae6c53c4fae9f25eb2ca6db9`.

<a id="student-fee-account-workspace-student-fee-account-workspace"></a>
## Student Fee Account workspace

<a id="student-fee-account-workspace-decision-and-scope"></a>
### Decision and scope

The principal daily workflows are Admission, Fee Collection and Student Fee
Account. The account workspace consolidates investigation and existing payment
corrections. Fee Collection, admission flows, financial procedures, migrations,
the DACPAC and receipt issuance/printing implementations are unchanged.

<a id="student-fee-account-workspace-existing-overlap-and-reuse"></a>
### Existing overlap and reuse

| Existing component | Account use |
|---|---|
| `StudentAccountStatementPage` / `GetFeeStatementTransactions` | Existing date filters, family selection, verified opening/period totals, fee balances and PDF/CSV export. Raw reader rows also appear chronologically; no running balance is invented. |
| `PaymentCorrectionReview` | Existing payment history reader, allocation details, eligibility, reduction/refund/reversal form and confirmation. A contextual constructor pins the original student and payment. |
| `PaymentAdjustmentState` | The same remaining-payment and eligible-action rules drive both account display and correction availability. |
| `RecoverableMutationWriter` / `MutationRecoveryJournal` | Existing authorization, transactions, exact request identity, recovery, rejected saves and unknown outcomes. No alternate financial writer is introduced. |
| `GetStudentCollectionReceipts` | Original document inventory; a family document is deduplicated by receipt number. Per-child figures are hidden in this list rather than presented as family totals. |
| `GetCollectionReceipt` / `FeeReceiptDocuments` | Saved header, original participants, fee lines, discounts and plans. Reprint uses the existing snapshot-aware report pipeline. |
| `CollectionReceiptPayments` | Read-only linkage from a saved receipt to its original payments and subsequent corrections, using the existing payment-history reader. Participants come from the original document, not today's family links. |
| `FeeReceiptQueries.QueryCurrentBalanceAsync` / `GetStudentCredit` | Current outstanding dues and available credit, separately labelled and never netted. Credit movement history remains read-only. |
| `StudentFeeChangeHistory` | Existing fee-plan/concession history report, retaining its permission requirements and incomplete-history disclosures. |

<a id="student-fee-account-workspace-workspace-structure"></a>
### Workspace structure

Search once; Include Siblings toggles the existing family selection in place.
Switching views preserves student, scope and statement dates.

- **Statement:** established Fee Balances presentation and export, Chronological
  Entries from the same SQL result, selected entry details, Related Payment and
  existing Fee Change History.
- **Receipts:** Issued Documents, Payments & Adjustments, and Account Credit.
  Opening a receipt shows original student shares, original fee details and plans,
  plus a separate Payments & Later Changes view. Eligible originals can open the
  established correction form without another student search.
- **Reports menu:** annual receipts and current
  unpaid-bill report shortcuts. Their scope and permissions remain distinct.

Current all-date dues/credit and receipt history are explicitly distinguished
from the date-filtered period totals above them. Individual fee balances are
not described as running ledger balances.

The account is available under Daily Work and the welcome shortcuts. The duplicate
Reports-hub account card is removed. Statement access uses existing `Reports.View`,
matching the old statement reader.
Receipt/payment/credit readers additionally require `Fees.View`. Corrections
require `Fees.Adjust` in the UI and the existing SQL/writer authorization.
Annual receipts retain `Fees.Statement.View` checks.

The standalone corrections destination and its page title are removed. Its form
is now the internal `PaymentCorrectionReview` component, opened only as a payment
review or recovery dialog. Operators with `Fees.Collect` retain account access
without gaining `Reports.View`: their account opens Receipts, and statement SQL,
date filters and export remain unavailable. An independent Check Save Outcome
action preserves recovery when the original payment is no longer eligible.
Family / Group Statements was retired on 28 October 2026. Arbitrary group selection is removed; Include Siblings remains within the student account.
The shared statement component and annual reporting remain live dependencies.
The obsolete standalone statement mode and its old page title have been removed;
the shared page always opens the consolidated account, with Statement and Receipts
tabs and the same permission checks. There is no alternate statement-only screen.
Fee Collection's contextual Student Fee Account dialog now hosts this same
consolidated workspace, with the student preselected. Closing it refreshes the
collection's existing child selection, dues, credit and payment history. The
dialog cannot close during an account operation; a pending collection draft
must be finished or cleared before opening it.
The aging report's contextual account link now opens the consolidated workspace.

<a id="student-fee-account-workspace-safeguards-and-tradeoffs"></a>
### Safeguards and tradeoffs

- No new financial procedure, schema change or migration is needed.
- Contextual corrections cannot switch to a different student/payment. Recovery
  for another student can be resolved without replacing the selected account.
- Account actions are invalidated immediately when selection/dates change. Late
  responses and responses from a previous signed-in user cannot enable actions.
- Reloading after a review includes cancelled reviews and recovery resolution;
  it does not replay writes. Payment/receipt selections are restored where found.
- Original receipt tables remain separate from current adjustment tables. The
  existing legacy snapshot warning is retained; missing historical details are
  not synthesized from today's balances or family membership.
- Busy/unsaved-change guards remain on correction dialogs; receipt preview has
  a busy-close guard. The form uses existing HumanUi, AppTheme and scaling helpers.
- The statement SQL exposes charges and completed payment allocations, not a
  complete immutable event ledger. Reversed originals are visible in Payments &
  Adjustments, credit movements in Account Credit, and concessions in the existing
  history/report views. Building a new running ledger is outside this change.
- Existing fee-plan/charge-changing workflows remain owned by their established
  pages/dialogs. This workspace adds no new way to edit a historical charge.

<a id="student-fee-account-workspace-validation"></a>
### Validation

Focused native checks are selected with `--fee-account`; existing statement,
payment-recovery, receipt-breakdown, receipt rendering and PDF export checks are
also run. The new `Run-Level2.ps1 -FeeAccountOnly` mode creates an ownership-checked,
private LocalDB instance, publishes the existing record-free schema fixture,
applies numbered migrations and creates fictional account records. It never
accepts a school connection string or restores school data.

The test preflight's stale schema-42 requirement and the reference fixture's
schema-44 constant are aligned to 58. A missing async reader is added to the
test-only database adapter so the current shared writer can compile. These are
test-infrastructure repairs, not production database changes.

Actual run results and remaining verification limits are recorded in
`Docs/StudentFeeAccountValidation.md`.

<!-- END preserved-source: Docs/Decisions/STUDENT_FEE_ACCOUNT_WORKSPACE.md -->


---

<a id="transfer-class-section-2026-11-01"></a>
<!-- BEGIN preserved-source: Docs/Decisions/TRANSFER_CLASS_SECTION_2026-11-01.md -->
Original source: `Docs/Decisions/TRANSFER_CLASS_SECTION_2026-11-01.md`. Original SHA-256: `3c71ab8dc5e49fa97b2c6638524ca344cf662cb7e6d22c8a321a9760a7888fd6`.

<a id="transfer-class-section-2026-11-01-transfer-class-vs-transfer-section--approved-decision-1-november-2026"></a>
## Transfer Class vs Transfer Section — approved decision, 1 November 2026

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

<!-- END preserved-source: Docs/Decisions/TRANSFER_CLASS_SECTION_2026-11-01.md -->


---

<a id="transport-cutoff-policy"></a>
<!-- BEGIN preserved-source: Docs/Decisions/TRANSPORT_CUTOFF_POLICY.md -->
Original source: `Docs/Decisions/TRANSPORT_CUTOFF_POLICY.md`. Original SHA-256: `ff1d9ae2bbc35a9bf57d5551810f4b8341ae24973501960f1060f1ae7c457293`.

<a id="transport-cutoff-policy-transport-policy--27-october-2026"></a>
## Transport policy — 27 October 2026

The owner chose one transport policy for admission and fee collection.

- School Profile has its own Transport Cutoff Day, independent of the general admission fee cutoff. It accepts days 1–28 or blank. Migration 58 initially copies the existing fee cutoff to preserve the previous transport-start boundary until the owner changes it.
- Before the transport cutoff, the current month is permitted. On or after it, changes begin next month. Blank permits current-month changes throughout the month.
- Any month with a recorded payment, including a partial payment, remains charged. Stopping begins after protected months. Earlier unpaid balances remain owed.
- Stopping from a permitted month cancels wholly unpaid posted transport charges from that month onward and ends the fee plan. Recorded payments and allocations are never cancelled, refunded or moved to credit by this action.
- Starting in the current permitted month posts its charge immediately without adding a duplicate. A future start waits for normal monthly billing. A new transport period may bill a month that has an older cancelled copy, while that cancelled copy remains historical.
- Start and Stop use the same dialog, with a selectable month, an optional reason, and the effect on posted fees explained before confirmation.
- Fee Collection has one context-sensitive Start/Stop Transport button immediately after Student Fees. Admission's Fee Discounts & Transport uses the same service.
- Writes recheck cutoff, payments, eligibility and the loaded snapshot under the monthly billing lock. A committed request marker prevents an uncertain retry from applying the action twice.

<!-- END preserved-source: Docs/Decisions/TRANSPORT_CUTOFF_POLICY.md -->


---

<a id="owner-decisions-and-invariants"></a>
<!-- BEGIN preserved-source: Audit/OWNER_DECISIONS_AND_INVARIANTS.md -->
Original source: `Audit/OWNER_DECISIONS_AND_INVARIANTS.md`. Original SHA-256: `a3753965d42427934598368436db7def0d9927907c073825266eb3be50587e78`.

<a id="owner-decisions-and-invariants-owner-decisions-and-business-invariants"></a>
## Owner decisions and business invariants

**Consolidated:** 21 October 2026  
**Purpose:** One locator for consequential policies recorded across the audit reports. The dated reports remain the evidence; this summary does **not** rerun tests or certify the current build.

Read [the master audit](../Audit/FMSC_MASTER_AUDIT.md) for release gates and dated findings, [the audit index](../Audit/README.md) for source reports, and [the UI style guide](Design/UI_STYLE_GUIDE.md) for presentation conventions. Comments beside the applicable C# code preserve especially easy-to-miss rules; **code, SQL and compatibility checks remain authoritative for current behavior**.

<a id="owner-decisions-and-invariants-enforced-or-recorded-application-behavior"></a>
### Enforced or recorded application behavior

**31 October 2026 — Registration simplification:** Admission owns pending child
registration and sibling confirmation. Confirm/Cancel/Reopen belong to the
student's row in Student & Siblings, not the main toolbar. Cancelled registrations
reuse the same student identity through a reviewed reopening with a new pending
placement, retaining cancelled episodes and all financial/family history. The
separate Family Admission navigation entry is retired. See the
[decision and implementation evidence](BUSINESS_DECISIONS.md#admission-registration-simplification).

**31 October 2026 — Academic-year editing:** The owner approved locking Start/End
dates once a year is referenced by student enrolment (including legacy/historical
records) or promotion history. Name/Remarks corrections and Current remain
editable. Implemented by `Migrations/63.sql` and `AcademicYearsPage`; see the
isolated SQL and native `--academic-year-date-lock` checks. This entry records the
decision and implementation, not complete release acceptance.

| Area | Rule to preserve | Source / implementation | Evidence boundary |
|---|---|---|---|
| Fee collection and family payments | Posting a fee creates dues; collecting records settlement. A family collection may use one family receipt, but each child's allocations, discounts and dues stay attached to that child. Student advance settles fees without new cash. A successful collection must not be repeated merely because receipt printing fails. | [Finance wording record](FINANCE_WORDING_REVIEW_2026-10-20.md), [FeeCollectionPage](../Pages/Finance/Collection/FeeCollectionPage.cs) | Reconfirm live SQL, lost-response and print/reprint routes for each release candidate. |
| Daily collection closing | Refund/correction adjustment rows have no operator-entered **Actual Amount**: blank is intentional and must not become zero. Ordinary methods require actual amounts in whole rupees; fractional values are rejected without silent rounding. Display and printing must preserve legitimate existing historical precision. Closing reconciles money; it does not edit student payments. | [DailyCollectionClosingPage](../Pages/Finance/Collection/DailyCollectionClosingPage.cs), [UFA-07/08](../Audit/REVIEW_HISTORY.md#fmsc-end-user-flow-audit-2026-10-14), [master correction status](../Audit/FMSC_MASTER_AUDIT.md) | Source-fixed in the recorded correction; physical print and disposable SQL/operator acceptance remain distinct gates. |
| Monthly posting reversal | A reversal is **whole-school and all-or-nothing** for the eligible latest posting in the academic year. Later posting, payment history or ineligible placement can block reversal. The current snapshot and reason are checked; reversal does not erase paid transaction history. | [FeePostingReversalPage](../Pages/Finance/Billing/FeePostingReversalPage.cs), [end-user audit flow coverage](../Audit/REVIEW_HISTORY.md#fmsc-end-user-flow-audit-2026-10-14) | Eligibility must still be enforced at the database write; UI preview is not permission to bypass it. |
| Quick Registration | Saving a pending quick registration does **not** itself confirm admission or allocate the final official admission number. Confirmation is a separate Pending Registrations/admission-review step; printing a bill and collecting payment are separate operations. On uncertain save results, check recorded outcome before retrying. | [QuickRegistrationFormPage](../Pages/Students/Admissions/QuickRegistrationFormPage.cs), [StudentAdmissionPage](../Pages/Students/Admissions/StudentAdmissionPage.cs), [historical audit index](../Audit/Archive/FMSC_HISTORICAL_AUDITS.md) | Preserve the request recovery safeguards and verify actual interrupted-save/restart scenarios on disposable data. |
| Student posting hold and resume | A temporary hold stops automatic and ordinary manual fee posting while retained dues remain collectible. Resume reuses the identity, preserves placement start, and explicitly reviews billing month, selected waived fees, unbilled annual fees and optional advance. Refunds and certificates remain recorded. Existing Left records require individual reviewed adoption; certificate presence is not a blanket resume barrier. | [Owner discussion](../Audit/REVIEW_HISTORY.md#fmsc-student-hold-resume-discussion-2026-10-21), [implementation/operator policy](STUDENT_POSTING_HOLDS.md), [migration 30](../Migrations/30.sql), [hold/resume dialog](../Pages/Students/Admissions/StudentPostingHoldDialog.cs) | Implemented from starting commit `c3184b6` on 21 October 2026; migration 30 applied to the authorized school database after a verified backup and unchanged-record checks. Disposable SQL/native desktop checks passed; school-operator acceptance remains separate. |
| Fees, discounts and plans | A class rate or future fee-plan change must not silently alter already posted bills. Discount is a reduction **from** the full fee; adjusting a recorded payment is different from refunding cash. Where a reduction affects paid amounts, the choice of advance versus refund must remain explicit. | [Finance wording record](FINANCE_WORDING_REVIEW_2026-10-20.md), [UiText.Fees](../Core/UiText.cs) | Do not simplify critical financial explanations while trimming routine UI guidance. |
| Setup draft discard | The shared master editor's explicit **New Record** action intentionally discards its current draft without a new confirmation. Record switching and leaving the page retain their existing safeguards. Small Classes/Sections setup drafts follow the owner-accepted exception; embedded bulk fee drafts do not. | [MasterPage owner-decision comment](../Core/MasterPage.cs), [UFA-03 and status](../Audit/FMSC_MASTER_AUDIT.md) | Do not introduce blanket unsaved-draft prompts as a cosmetic consistency change. |
| Backup and restore | Restoring school data is potentially destructive. Unknown or foreign backups must not be treated as compatible; older verified backups must warn of newer-record loss. Keep existing safety-copy, recovery and rollback precautions. Never use a DACPAC to overwrite a school's attached MDF/LDF. | [BackupRestorePage](../Pages/Administration/BackupRestorePage.cs), [master release gates](../Audit/FMSC_MASTER_AUDIT.md) | Installation, operator-account access, actual backup and rollback remain unverified release gates unless separately evidenced. |
| Roles and access | Preserve the owner-approved Administrator/Accountant role boundary and explicit granted permissions. Shortcut visibility and screen access must follow the same destination authorization, not sidebar visibility. | [Master audit](../Audit/FMSC_MASTER_AUDIT.md), [UFA-06/09](../Audit/REVIEW_HISTORY.md#fmsc-end-user-flow-audit-2026-10-14), [PermissionCatalog](../Core/PermissionCatalog.cs) | Do not broaden permissions as part of audit organization or UI cleanup. |

<a id="owner-decisions-and-invariants-approved-target-policy-that-is-not-yet-proven-implemented"></a>
### Approved target policy that is **not yet proven implemented**

**Database/installer transition — do not mark complete.** The owner's target is: fresh installations use a verified matching detached MDF/LDF pair supplied outside the private repository; already compatible school databases retain every record; older, newer or structurally incompatible databases are refused **without** upgrades or replacement. However, the [dated master migration-retirement review](../Audit/FMSC_MASTER_AUDIT.md) and [post-merge audit](../Audit/REVIEW_HISTORY.md#fmsc-post-merge-project-audit-2026-10-14) found that the installer still allowed older-schema upgrade paths and lacked equivalent full pre-activation compatibility verification at their reviewed revisions. **Do not delete migration files, startup compatibility requirements, recovery contracts or installation rollback until the independent detached-snapshot and installer verification gate passes.** This is an approved *target*, not a claim about current runtime behavior.

<a id="owner-decisions-and-invariants-ui-consistency--design-constraints-not-database-rules"></a>
### UI consistency — design constraints, not database rules

The visual design is almost final. Full-page workspaces retain the shared title treatment; short context/subtitles are **optional only when useful**. Embedded workspace headers may deliberately be hidden. Preserve Finance and Admissions workflows, the approved Fee Collection layout, warnings, consent, confirmations, status/error messages and accessibility. Propose copy-only improvements first; screenshots, scaling/theme checks and owner approval precede layout changes. See [UI guidance audit, G01–G17](../Audit/REVIEW_HISTORY.md#fmsc-ui-guidance-audit-2026-10-20) and the [style guide](Design/UI_STYLE_GUIDE.md).

<a id="owner-decisions-and-invariants-maintenance-rule"></a>
### Maintenance rule

When a decision is new or materially changes, update this locator **and** the nearest relevant code comment/test or owner policy document. State the date and reviewed commit; do not turn a historical pass into a current guarantee. Keep underlying audit evidence immutable, and do not repeat historical reports in new files.

<!-- END preserved-source: Audit/OWNER_DECISIONS_AND_INVARIANTS.md -->
