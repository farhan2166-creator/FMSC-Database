# Review of the prioritized C# / SQL audit

The user approved three rules: reversal covers charges assigned to the selected billing month; posting bills only enrollments in the selected academic year; occupied campus/class/section placements cannot become inactive until active students move. The user separately approved repairing the missing-enrollment audit formatting error while retaining existing auditing.

| Finding | Resolution |
| --- | --- |
| H01 | Preserve monthly, annual and manual reversal eligibility for the month, including existing payment/status protections. Show charge type and explain that only monthly fees use monthly reposting. |
| H02 | Filter month discovery and preview by enrollment year; enforce the year again during posting, rejecting a mixed-year selection atomically. Both application posting paths supply the year. |
| H03 | Serialize confirmed missing-enrollment details and the reason as JSON for the existing audit procedure. Keep auditing and existing lifecycle rules. |
| H04 | Reject deactivation of an active placement containing active enrollments inside the save transaction. Unused and historical-only placements may still retire. |
| M01 | Clarify that the report counts students with at least one posted monthly fee. Counting logic remains unchanged; no new definition of fully billed is introduced. |
| M02 | Restrict reversal month discovery to active enrollments, matching existing preview/save eligibility. |
| M03 | Retain confirmed results for completed fee-rate groups. On interruption identify the pending group as unknown, show groups not started, and require a fresh review before retrying. No automatic retry or claim of rollback. |
| L01 | Allow long historical recursion in both reports while preserving the existing 24-month future projection cap. |
| L02 | The finding misidentifies inline Info/Warn updates as two modal messages. Bulk promotion is unchanged. |

## Deployment and validation

Apply `Migrations/18.sql` before deploying the updated application. It changes procedure definitions and adds required academic-year parameters to monthly preview/posting; the application compatibility requirements include those parameters. The schema compatibility baseline remains 3. No tables, audit switches or reference database files are changed.

Validation uses a disposable SQL Server database copy and transactional synthetic fixtures. See `Tests/AuditChecks/README.md`. The Windows interface, physical printers and simultaneous multi-user contention require Windows/manual verification; the cloud checks do not establish those outcomes.
