# Audit reference

- [Master audit and release status](FMSC_MASTER_AUDIT.md): recorded findings,
  baselines and remaining release gates. Read each entry's date and evidence scope.
- [Business-decision handbook](../Docs/BUSINESS_DECISIONS.md): current rules and
  the complete dated decision records, including the former owner-invariants summary.
- [Source-resident business rules](../Core/BusinessDecisions.cs): self-contained
  C# policy record with stable BD identifiers and comments at implementation sites.
- [Consolidated review history](REVIEW_HISTORY.md): original investigation text,
  finding IDs, fixes, commits, test results and limitations from the dated reviews.
- [Earlier Git-based archive](Archive/FMSC_HISTORICAL_AUDITS.md): older report
  revision links. Those external records are not a claim of present acceptance.

## Maintenance

Update the C# rule record and nearest invariant comment when a business decision
changes; update the handbook's current-rule section to match. Keep the historical
record, date and supersession explicit rather than rewriting past evidence.

Append new investigations to the review history with the inspected revision,
scope and verification limits. Do not create competing release-status summaries.
The existing filenames under Reviews and the older Docs audit paths are
compatibility redirects; their original section headings preserve incoming links.

Build/test success, real installer acceptance, operator acceptance and physical
printing are separate evidence. Reorganizing documentation proves none of them.
Never include school data, credentials or private backups in the audit record.
