# Operator-control revision readiness

Candidate: 1.1.0-rc.2, database schema 21.

Status: implementation and local verification; hosted Windows and complete
installer acceptance are pending. This is not a claim that every school-policy
exception has been finished. See [operator decisions](OPERATOR_CONTROL.md).

## Changes

- Concessions have chosen start/end months and a reason. A temporary concession
  restores the previous agreement afterward and retains scheduled rate changes.
- The operator explicitly chooses whether issued monthly bills change. Preview
  shows current/proposed bills and paid amounts released as credit or refund.
- Concession confirmation uses stale-review protection and the existing durable
  mutation recovery mechanism; uncertain saves can be checked without re-entry.
- Direct admission and Quick Registration expose admission-month versus following-
  month concessions instead of forcing a hidden next-month rule.
- Leaving offers the normal cutoff, retained issued bills, or waiving from a chosen
  month. Its financial preview includes the full bill correction chain.
- Revised procedures have no retiring audit writes or audit-only JSON preparation.
  Financial, student history and request/recovery records remain operational.

## Verification

Local Release builds pass with zero warnings/errors. Disposable SQL scenarios cover
paid/partial concessions, temporary periods, rate schedules, stale reviews,
transaction rollback, permission refusal, exact replay and lost acknowledgement,
and the departure choices. Hosted Windows controls and complete installation will
be linked after the revision is frozen. No school data is used.

The [previous RC1 evidence](Releases/1.1.0-rc.1.md) applies to its recorded code and
artifact only; that binary does not include this operator-control revision.

## Deployment and scope

Migration 20 upgrades schema 19; migration 21 upgrades schema 20. The installer
backs up and verifies before applying pending files. Existing school, payment and
permission rows are preserved. Schema 21 changes four business procedures, the
communication version guard and the recovery-kind constraint; it adds no tables.

Local operator/login, printing and school-data clone upgrade/recovery acceptance
remain necessary. Follow [INSTALLATION.md](INSTALLATION.md) and the established
owner-supplied detached MDF/LDF preparation. No live school database has been
accessed or changed.
