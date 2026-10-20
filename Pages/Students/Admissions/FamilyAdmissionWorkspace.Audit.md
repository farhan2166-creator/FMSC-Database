# Family admission rollout audit — 19 October 2026

Initial decision: retain Quick Registration during rollout pending verification.
Subsequent user decision: retire the standalone host now and test/refine the
family workspace afterward. Its old navigation was removed, pending-registration
discovery relocated to the family toolbar, and existing registration links routed
to the family workspace. Shared writer/recovery infrastructure remains in use.
This is the user's rollout decision, not a claim that the findings below passed.

## Follow-up — 20 October 2026

The counter implementation now isolates recovery from loaded child identities,
includes future fully waived monthly plans and saves all-zero individual bills.
Existing collection opens over the retained family with a fixed selection and an
editable proposed split. The actual host saves, partial retry, return confirmation,
collection, saved snapshots, recovery and RDLC paths passed the owned 45-check run.
The detailed [current verification](../../../Docs/QUICK_COUNTER_VERIFICATION_2026-10-20.md)
records native checks, migration 27, the verified live backup/upgrade and remaining
operator/physical-printer acceptance. The initial findings below are retained as
the historical rollout record, not a statement that the corrections remain undone.

## Checked

- Application and focused UI fixture compile successfully.
- Thirteen focused checks cover loading siblings, retaining drafts when changing
  children, inherited parent details, selection, print gating and permissions.
- The workspace delegates registration, return confirmation and fee changes to
  the established operations. Processing is explicitly per child, not one atomic
  family transaction. No new schema/procedure changes are introduced.
- Bill printing uses the established individual/family RDLC reports and numbering.
- The existing `Tools/TestAutomaticBilling.ps1` suite ran against its uniquely
  owned schema-only LocalDB fixture. Billing, credit allocations, sibling
  transfers and several waiver checks passed. It then stopped at **"A free
  assignment is reportable without manufacturing a zero-value bill."** This is
  a concession-report assertion, not an end-to-end workspace result; its cause
  has not been classified. Later admission/return checks were not reached.
  The disposable instance and its artifacts were cleaned up by the harness.

## Retirement gates

1. **Recovery must retain the correct child/family context.** Every lazily created
   registration editor calls `ReadRecoveryRecord`, which reads one profile-wide
   pending intent. `CheckSaveOutcomeAsync` can then replace that editor's student
   with the recovered request's student. The family host adopts this new ID without
   first verifying its membership against an already loaded family. Reproduce with
   an unresolved save for family A, then opening a pending child in family B.
   Recovery needs a dedicated, verified context before processing or printing can
   resume; it must not relabel an unrelated loaded child.
2. **Fully waived future monthly fees need a complete bill preview.** The shared
   preview synthesizes monthly waiver rows only for the current month. The new
   supplemental query synthesizes only nonmonthly assignments. A fully waived
   monthly assignment that starts next month and has no posted charge is therefore
   absent. Verify an entirely free child alongside a paying sibling, using the
   actual saved bill snapshot and RDLC output, before claiming full waiver support.
3. **Pending-registration discovery relocated during retirement.** The family
   toolbar now opens the established searchable Pending Registrations list,
   retaining Review / Confirm. Open Registration loads the selected child's family
   in the new workspace, with its existing unsaved-draft guard.
4. **Exercise the new host against a disposable database.** UI fixtures do not prove
   saves or report rendering. Verify a new family, existing plus new siblings,
   promoted returns, partial/full waivers, a failure after the first child saves,
   retry/recovery, and individual/family bill output through this host. Established
   SQL checks alone do not certify these new orchestration paths.

These are rollout gates, not a request to alter historical student or financial
records. Any SQL changes arising from follow-up work belong in versioned
`Migrations` scripts.
