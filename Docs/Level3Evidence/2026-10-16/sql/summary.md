# FMSC_SMS Level 1 automated evidence

Commit: `84d019cb3ec9a904f97312b16cf80d21f1ba7b67`
Run: `c49c3fb1-2d4d-4d64-8a53-4954f4a78757`
Engine: SQL Server 2022 (16.0.4295.3); required schema: 6

PASS: 65 · FAIL: 0 · BLOCKED: 0

| Scenario | Result | Assertions | Seconds |
|---|---|---:|---:|
| Environment and tooling | PASS | 0 | 13.80 |
| SQL container readiness | PASS | 0 | 10.00 |
| Database provisioning | PASS | 0 | 11.30 |
| Fixture: isolated target and synthetic school | PASS | 4 | 0.83 |
| A: admission, official identities and opening bill | PASS | 8 | 0.35 |
| B: class rates, monthly posting and duplicate protection | PASS | 6 | 0.15 |
| C: partial/final collection and reconciliation | PASS | 12 | 0.21 |
| D: manual workbook charge and collection | PASS | 4 | 0.08 |
| E: bulk promotion and original-year preservation | PASS | 9 | 0.11 |
| F: return confirmation and new-year fee/payment cycle | PASS | 8 | 0.06 |
| Negative: admission fields, permissions and whole-rupee boundary | PASS | 8 | 0.02 |
| Recovery: committed adjustment, lost acknowledgement and exact retry | PASS | 5 | 0.11 |
| Recovery: pending collection blocks ambiguous resubmission | PASS | 3 | 0.00 |
| Negative: stale collection edit and unauthorized finance | PASS | 3 | 0.04 |
| G: complete two-year history and database invariants | PASS | 11 | 0.04 |
| Lifecycle SQL | PASS | 0 | 2.47 |
| Fixture: isolated target and synthetic school | PASS | 4 | 0.87 |
| Quick Registration SQL | PASS | 72 | 17.39 |
| Fixture: isolated target and synthetic school | PASS | 4 | 0.87 |
| Fixture: existing mutation-suite synthetic prerequisites | PASS | 1 | 0.48 |
| Mutation SQL | PASS | 46 | 19.21 |
| Fixture: isolated target and synthetic school | PASS | 4 | 1.28 |
| Administration SQL | PASS | 67 | 14.30 |
| Level 3 database provisioning | PASS | 0 | 16.12 |
| Fixture: isolated target and synthetic school | PASS | 4 | 0.91 |
| L3 fixture: production admission for controlled faults | PASS | 1 | 0.25 |
| L3-REC-01 / uncommitted production collection rollback | PASS | 1 | 0.20 |
| L3-REC-03 / terminate owned SQL session during uncommitted write | PASS | 2 | 0.03 |
| L3-REC-04 / command timeout, rollback and deliberate retry | PASS | 3 | 1.04 |
| L3-REC-05 / killed process retains collection journal across restart | PASS | 6 | 0.43 |
| L3-REC-06 / cancelled SQL command rolls back staged collection | PASS | 2 | 0.17 |
| L3-FIN-07 / concurrent production posting and competing collections | PASS | 3 | 0.15 |
| L3-FIN-05 / exact correction and reversal ledger persistence | PASS | 4 | 0.11 |
| L3-SEC-04 / SQL permissions rechecked after role removal | PASS | 2 | 0.01 |
| L3-DB-01 / checksum backup and VERIFYONLY | PASS | 2 | 0.08 |
| L3-DB-02 / restore to absent owned catalog and new files | PASS | 5 | 0.34 |
| L3-DB-03 / restored compatibility, complete ledger and CHECKDB | PASS | 1 | 0.60 |
| L3-DB-04 / old, new and structurally incompatible schema refusal | PASS | 4 | 0.31 |
| L3 database target guards / wrong owner and non-test catalog refusal | PASS | 2 | 0.00 |
| L3-PRF-01 / 1000 production admissions and repeated real searches | PASS | 21 | 7.26 |
| L3-PRF-04 / repeated full register and financial report queries | PASS | 21 | 0.23 |
| L3 restored catalog cleanup / ownership checked before DROP | PASS | 1 | 0.04 |
| Level 3 SQL reliability | PASS | 0 | 12.62 |
| Communication database provisioning | PASS | 0 | 5.56 |
| Fixture: isolated target and synthetic school | PASS | 4 | 0.85 |
| Communication: additive schema, explicit consent and permission isolation | PASS | 16 | 1.80 |
| Communication: durable claims, unknown acknowledgement and restart | PASS | 1 | 0.02 |
| Communication: idempotent submissions, consent recheck and no blind retry | PASS | 7 | 0.13 |
| Communication: delivery monotonicity, pause/cancel and approval | PASS | 4 | 0.04 |
| Communication: financial reminder revalidation and original ledger | PASS | 3 | 0.24 |
| Communication: future-only automation, approval and template revocation | PASS | 6 | 0.23 |
| Communication: live-mode isolation, budgets, bounded retry and throttling | PASS | 7 | 0.06 |
| Communication: committed promotion, return and payment event context | PASS | 5 | 0.24 |
| Communication: rendered text limits and Urdu segment parity | PASS | 6 | 0.06 |
| Communication: signed Meta receipts, replay isolation and inbound opt-out | PASS | 9 | 0.17 |
| Communication: interrupted bulk campaign and readable history | PASS | 5 | 0.04 |
| Communication: family unlink preserves existing workflow and message history | PASS | 4 | 0.06 |
| Communication: contact privacy and financial placeholder authorization | PASS | 10 | 0.11 |
| Communication: compose-only draft and authorized sender handoff | PASS | 3 | 0.02 |
| Communication: expired lease and schema tamper refusal | PASS | 3 | 0.18 |
| Communication SQL | PASS | 0 | 4.70 |
| Portable regressions | PASS | 832 | 0.67 |
| Performance checks | PASS | 32183 | 0.96 |
| Communication provider checks | PASS | 42 | 0.47 |
| Cleanup | PASS | 0 | 0.50 |

Expected refusals verified: 35

## Coverage limits

- Linux real-SQL lifecycle/integration tests; existing desktop CI separately runs Windows control contracts and PDF export.
- No interactive visual, printing, installer, Windows LocalDB/operator-account or actual network-loss acceptance.
- No real school rows are imported: pinned data-free SQL test fixture only, fresh synthetic records. This test does not approve or check the current repository DACPAC.
- Test-only seed sets schema version 6 and repairs DacFx recovery-scope type; this does not prove the owner's distributed database snapshot.
- Legacy PowerShell scripts that back up FMSC_DB or hardcode school IDs are inspected but never executed.
