# Certificate workflow — schema 51

Applies to Character, Date of Birth, Regular Student, School Leaving, College Leaving, Sports, No Objection, Provisional, Regular Teacher/Lecturer and Bank Account Opening certificates. The existing RDLC files are unchanged.

## Normal counter flow

1. Select the student or staff member. Open an existing certificate when the request is for that saved copy, or choose **New Certificate** for a different request where that certificate type allows it.
2. Complete the certificate details and choose **Create Certificate**. Required recorded birth dates must be available for DOB, Regular Student, Provisional and Leaving certificates; Leaving also requires the admission date. Refresh reloads corrected date information.
3. A saved, uncollected copy is ready for **Print**. Review the preview and print the paper. Printing does not collect it, create another copy, or consume a Leaving duplicate allowance.
4. Correct entered details on the same form using **Save Changes** while the copy is uncollected. Personal-record corrections belong in Admission or Staff.
5. Hand over the paper and choose **Mark Collected**. Enter the collector's name. The collection prompt explains that this permanently locks the copy.
6. The collected copy remains selectable, readable and printable. Its details cannot be edited, deleted or uncollected through the certificate workflows. Database guards also reject changes to collected copies and their subject/snapshot rows.

## Subsequent requests

| Situation | Action |
|---|---|
| Printer jam, spoiled paper, or another print of the saved document | Select the saved copy and use Print. No extra issuance. |
| Another physical copy is needed after collection | Select the latest copy for that certificate and use Issue Again. Review the new uncollected copy, print it and collect it separately. |
| A handed-over certificate has incorrect entered details | Issue Again, correct the new copy on the form, then print and collect it. The original stays locked. |
| Student/staff name, parent name, gender or birth date needs correction | Correct the person record first. Issue Again takes the current personal details; reprinting an earlier copy retains its saved details. |
| A save/reissue reply is lost | Refresh/check the existing outcome before repeating it. Existing recovery actions remain available. The new linked-copy operation has a request marker and refuses a second child copy from the same source. |
| Another operator changed the copy after it was opened | Refresh and review it again. Reviewed-version checks protect the revised save and collection paths. |

## Certificate-specific rules

- **Character:** separate SSC and HSSC certificate identities remain. Passing year, roll number, conduct, type and certificate date can be corrected on the latest uncollected copy. Earlier or collected copies are locked.
- **DOB:** one identity per student; each issued copy retains its saved personal details. The latest uncollected certificate date can be corrected inline.
- **Regular Student:** new certificates require an active placement. New identities are scoped to the enrollment rather than just the class name, allowing another enrollment in the same class. Saved copies retain the class they certify. The latest uncollected certificate date can be corrected inline.
- **School/College Leaving:** creation retains the existing leaving and fee-review workflow. A selected historical episode has its own Issue Again action and duplicate allowance. Copy corrections cover promoted class, academic record, attendance and remarks; the recorded leaving date and lifecycle remain unchanged. Reprinting, collection or issuing another copy does not repeat withdrawal or reverse fees. The existing maximum of two non-void duplicate copies remains.
- **Sports, NOC, Provisional and staff letters:** separate legitimate requests remain possible. Matching create requests reuse the existing record rather than silently adding an identical one. Issue Again creates a linked new record with current personal details and the saved certificate-specific information. Provisional subject snapshots retain their saved names and ordering.

Collection status and collector details are shown in certificate histories and the certificate issue register for every type.

Sports certificates offer six optional performance sentences covering sportsmanship, participation, teamwork, representation, leadership and achievement. Choosing one fills the editable Performance field; operators can adapt it or enter their own wording. The suggestions remain available even when there are no saved sports certificates. Collected copies remain locked.

## Database delivery and checks

Cancellation is removed from the forms and database command in schema 51. The decision and rationale are recorded in [the cancellation decision](Decisions/CERTIFICATE_CANCELLATION_REMOVED.md). Existing cancelled copies remain protected as historical data.

`Migrations/51.sql` retires the command after schema 50. `Migrations/50.sql` contains the complete schema/procedure/trigger change from schema 49. Apply it through the established transactional migration mechanism after restoring a schema-49 database. Older databases must first receive their intervening migrations. Existing collection timestamps are retained; collection is not invented for previously untracked certificates.

`Tools/TestCertificateWorkflow.ps1` restores a uniquely owned clone, applies pending migrations 50 and 51, checks the startup/restore contract and executes `Validation/CertificateWorkflowChecks.sql`. The checks cover all ten types and roll back their test scenarios. `Tools/TestCertificateReissues.ps1` additionally verifies snapshot preservation, duplicate limits, concurrent reissues and lost-response recovery.
