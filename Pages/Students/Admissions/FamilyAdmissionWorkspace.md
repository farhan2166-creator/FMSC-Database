# Family Admission & Fees

Daily Work workspace replacing the retired standalone Quick Registration screen.
Existing dashboard and pending-registration entry points open this workspace.
Pending Registrations remains available from its toolbar for discovery and review.

- Find any student to load the established family, or start a new family.
- Check the children being processed or billed. Selection for editing is separate
  from inclusion. Each child retains their own editor when another is selected.
- Add multiple new drafts. Parent details carry forward; the first saved child
  becomes the family anchor. Further new children use the existing sibling-link
  admission operation.
- New/pending children use the original registration writer, quote/snapshot
  checks and durable save-outcome recovery. New children remain pending until
  the established admission review confirms them and assigns official numbers.
- Promoted children awaiting return use the existing return-confirmation flow.
- Existing children use the existing fee-assignment and fee-term review controls.
  These reviews save through their normal explicit confirmation steps.
- Discounts can cover selected fees partially or fully. A new child or a return
  can have all included/opening fees waived. Standard amounts remain recorded.
  Fee-term review retains existing period and paid-bill correction protections.
- Process Selected operates per child, stops on the first unresolved outcome and
  keeps prior successful outcomes. It is not represented as an atomic family
  transaction. Retrying does not recreate an already saved child.
- Printing rechecks selected family membership/readiness and reads posted unpaid
  charges. Fully waived charges remain visible. Existing student/family RDLC
  templates and B/FB numbering are used; printing does not register or collect.
- Posted outstanding, new monthly plans and new one-time/yearly plans are shown
  separately. A recurring plan is not mislabeled as a posted bill total.

No database schema/procedure changes are required by this workspace. Any later
SQL changes must be delivered through versioned scripts in Migrations.

The old tabbed host was retired at the user's request on 19 October 2026 so
testing and refinement can continue in this workspace. Its internal child editor,
writer and durable recovery records remain in use. This navigation retirement
does not resolve the open functional findings in FamilyAdmissionWorkspace.Audit.md.
