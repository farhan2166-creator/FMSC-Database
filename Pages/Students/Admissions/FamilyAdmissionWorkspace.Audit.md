# Family admission rollout audit — 19 October 2026

Decision: retain Quick Registration during rollout. The family workspace is
implemented, but replacement readiness has not been established. Do not remove
the old entry points or the shared registration writer/recovery infrastructure.

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
3. **Keep pending-registration discovery and completion accessible.** The old host
   contains the searchable Pending Registrations tab. The family workspace can
   review an already loaded pending child, but does not replace that discovery
   list. Preserve or relocate it before retiring the old host and its navigation.
4. **Exercise the new host against a disposable database.** UI fixtures do not prove
   saves or report rendering. Verify a new family, existing plus new siblings,
   promoted returns, partial/full waivers, a failure after the first child saves,
   retry/recovery, and individual/family bill output through this host. Established
   SQL checks alone do not certify these new orchestration paths.

These are rollout gates, not a request to alter historical student or financial
records. Any SQL changes arising from follow-up work belong in versioned
`Migrations` scripts.
