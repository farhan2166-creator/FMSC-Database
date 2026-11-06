# Communication Center redesign — staged implementation

Status: **UI-first development branch; not production-ready.** Scope is the optional Communication Center, not the admissions or finance transaction workflows.

## Product contract

FMSC is the primary school-management application. Communication must remain optional; saving an admission, collecting a fee, promoting a student or performing any other business operation must never require communication setup or a successful message. Only an explicit send action may send a manual message. Optional automation is opt-in and starts disabled.

**Everyday operator journey:** choose recipient scope (campus, class/section, outstanding fees or individual contact), select an appropriate reviewed template, preview the actual eligible recipients and personalized text, explicitly confirm sending, and inspect results in History. Sending must be refused or clearly deferred if consent, approval, gateway, cost, permissions, limits or execution prerequisites are missing.

**Top-level screen contract:**
1. **Send Message** — primary operator workflow. Scope selection replaces the default full contact administration grid. Additional channel, academic-year, scheduling and draft controls are available through More Options.
2. **History** — readable provider evidence and pending work. More Actions contains approval, queue, retry and dispatcher controls for authorized users.
3. **Settings** — administrator-only contact/consent management, template reviews, sending/gateway setup and a limited existing-automation review/disable area. No independent application/dashboard inside FMSC.

## Backend reuse

Keep the current SQL preview/rendering, immutable campaign snapshots, delivery evidence, permission checks, costs, simulation boundaries, idempotency, provider adapters, audit and safe-retry protections. Preserve the existing optional `comm` module and its schema contract; never recreate or overwrite it during ordinary application startup.

Never interpret `Accepted` as `Delivered`. Treat `DeliveryUnknown` as a reconciliation case rather than a retry instruction. Explicit confirmation must show channel, live/simulation mode, eligible recipient count, exclusions and estimated cost/unknown cost before dispatch.

Direct reviewed sending can use the existing `MessageDispatcher.DispatchOneAsync` for supported channels; draft/scheduled campaigns retain their existing approval/queue constraints. Canceling the confirmation must not create a draft. A change between Simulation and Live mode during confirmation must never dispatch unexpectedly; a resulting saved draft remains pending for review.

## Identified integration gaps — do not guess around them

1. **Guardian consent:** Admission currently records `dbo.StudentGuardianCommunication` (guardian email/phone/WhatsApp preference). The Communication module independently validates `comm.Contact` (verified ownership, WhatsApp/SMS consent, financial/announcement purpose consent, opt-out status and a consent source). Those are not a unified authoritative model. A phone number or WhatsApp preference alone does not satisfy the module's verified contact/purpose consent checks. Design a permission-checked, idempotent enrollment/reconciliation flow, with explicit handling for number changes, revoked preferences, existing opt-outs, row versions and historic message snapshots. Avoid silently upgrading consent or registering contacts automatically as verified. Do not modify Admission in this first UI pass.
2. **Unattended delivery:** `Apps/Worker` is opt-in and not provisioned by the current installer. An enabled rule or a scheduled message therefore must not promise automatic execution unless a supported worker/dispatcher is actually configured. Keep rules disabled by default. A complete worker lifecycle, permissions, credentials, diagnostics and updater strategy must be reviewed before offering a simple automation switch.
3. **Provider templates:** Approved local templates and actual provider-side WhatsApp template approvals are separate. Do not allow arbitrary live WhatsApp text without provider mapping.

## Proposed phases

### Phase A — task-oriented UI (this branch, first pass)

- Operator-focused recipient mode selection and reviewed preview, with only matching template scopes visible; outstanding reminders require the outstanding template.
- One Send Now flow for supported SMS/WhatsApp channels.
- Concise History statuses with specialist actions behind More Actions.
- Three top-level sections, with Settings shown only for authorized users. Previously enabled automation can be inspected and disabled, but the redesign offers no one-click enablement until worker reliability is addressed.
- Preserve existing SQL and provider safety boundaries.

### Phase B — complete functional readiness

- Verify campus/class/outstanding selection, recipient exclusions, matching limit, and appropriate template/category/scope checks.
- Formalize guardian preference to communication-contact reconciliation without weakening verification or consent rules.
- Resolve sender readiness, approved WhatsApp templates, trial/simulation/live warnings and actual dispatch availability.
- Decide whether optional scheduled delivery can be made reliable without deploying a new always-on component. If not, make limitations explicit and leave automation unavailable.
- Review status naming, empty states, keyboard navigation, theming, scaling and small-screen layouts.
- Replace stale Help and operational documentation.

### Phase C — contextual page actions (only after central engine acceptance)

- Admission: after successful commit, optional **Send Welcome Message** action with identified student and preview. Closing it has no effect on admission.
- Fee Collection: after successful payment commit, optional **Send Payment Confirmation** action bound to the verified payment identity and preview. Closing it has no effect on payment.
- Class/fee workflows: optional bulk reminder with filtered recipient count and amount snapshots.
- Use one shared engine; no duplicate sending logic, direct provider requests or hidden automatic sends inside business writers.

## Deployment and acceptance gates

- Preserve installed databases, school records, guardian preferences and existing communication history. No destructive migration or hidden first-run installation.
- If schema changes become necessary, use the existing numbered migration / DACPAC compatibility process and approved updater/release pipeline; support data-preserving upgrades and explicit failure recovery.
- Run native ThemeSmoke checks, Communication SQL lifecycle tests, build/publish checks, permissions and role isolation, mode/simulation/live invariants, provider failure and timeout handling, duplicate-send checks and upgrade regression tests.
- Validate both administrator and limited operator UX at relevant DPI/window sizes.
- Keep the work on a review branch until these gates pass. **Do not ship this first-pass UI without a successful Windows build and runtime smoke test.**

## Non-goals for Phase A

No automatic send from admission/payment pages, no schema replacement, no silent migration, no service auto-install, no live-provider testing on real guardians, no guaranteed schedule execution, and no unreviewed enablement of automation.
