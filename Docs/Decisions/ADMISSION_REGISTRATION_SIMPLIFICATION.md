# Admission owns registration — 31 October 2026

The owner approved using Admission and its sibling panel for pending registration,
confirmation and cancelled-registration reopening, retiring the separate Family
Admission navigation entry. This supersedes the registration-navigation portions
of the historical admission workflow and family-workspace rollout documents.

## Operator flow

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

## Reopening and history

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

## Verification and local deployment

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
