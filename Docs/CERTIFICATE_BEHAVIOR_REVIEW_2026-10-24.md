# Certificate identity, correction and reprint review

Reviewed the application pages, RDLC field bindings and local schema-46 stored procedures. Character-certificate changes are delivered in migration 47. No student placement or lifecycle data changes are required for those changes.

## Character certificates: fixed

- SSC/HSSC used to be a print-only dropdown. Neither the certificate table nor its issue history stored it, and the print action injected the current dropdown value. The same saved certificate could therefore print with either qualification.
- Migration 47 saves the exam type on the certificate identity and on every issue snapshot. A student can have separate SSC and HSSC identities, with one identity per type. A second copy of the same type uses Issue Again, rather than Create.
- Print passes the exact selected issue ID and uses its saved wording and details. The database rejects a mismatched certificate/issue pair, an uncollected copy, an unknown saved type and missing print permission.
- Correct Details edits only the entered exam type, certificate date, roll number and remarks. It requires a reason and creates a linked replacement issue. Names, gender and the derived examination year are not editable here. Earlier copies remain printable with their original details.
- Issue Again reviews and saves current Admission names and gender with today's certificate date. The saved exam type, examination year, roll number and remarks remain certificate-specific history. Personal-detail mistakes must be corrected in Admission. A source fingerprint rejects changes made after review.
- Correcting the exam type is an explicit replacement operation. It cannot collide with an existing certificate identity of the target type. The older copy retains its original qualification.
- Corrections can replace an incorrectly entered uncollected copy without forcing collection of the incorrect copy. The replacement has its own collection state.
- Reviewed latest-issue IDs prevent stale/concurrent corrections. Create, reissue and correction use durable request markers and outcome checking, so replaying a save cannot add another copy.
- The user identified the existing local character certificates as test data and authorized their removal. The release migration itself preserves unknown legacy records and offers explicit review instead of assuming SSC or HSSC.

## Other certificate types: concrete findings

| Certificate | Type and duplicate behavior | Existing correction behavior | Remaining gap |
|---|---|---|---|
| Provisional | Persists SSC, HSSC1 or HSSC2; printing reads the saved type. Create permits multiple records. | Update edits the saved certificate and replaces its subject rows. | Updates overwrite the old printable details, including exam type; no retained correction version or reason. |
| Regular Student | Saves class and personal details; the UI checks for an existing certificate for the class. | Migration 47 makes Issue Again review current Admission names and DOB, with a fingerprint check and a new issue snapshot. Old issue reprints are preserved. | A separate correction of an entered certificate date is not available; reissue dates the new copy today. Saved certificate class is retained. |
| Date of Birth | One certificate identity per student; individual copies have snapshots. | Migration 47 reissues using reviewed current Admission names, gender and DOB after collecting the current copy. Old issue reprints are preserved. | No separate certificate-date correction; an incorrect uncollected original cannot yet be replaced directly. |
| School Leaving | Persists School certificate type and leaving episode; issues save Original/Duplicate. | Migration 47 snapshots every issue and refreshes reviewed Admission names, DOB and admission date on reissue, while preserving the original leaving episode. | No separate correction UI for manually entered leaving-certificate content. Invalid explicit issue IDs are now rejected instead of falling back. |
| College Leaving | Persists College certificate type and leaving episode; uses the same issue system. | Same per-issue preservation and fresh Admission reissue as School Leaving, including existing collection and duplicate limits. | No separate correction UI for manually entered leaving-certificate content. |
| Sports | Multiple certificates are allowed. | Update edits sport, performance and date in place. | No retained replacement/correction history. |
| No Objection | Multiple certificates are allowed. | Update edits the entered certificate date in place. Fresh personal details come from Admission when a new certificate is created. | No retained replacement history. |
| Regular Teacher / Lecturer | Staff snapshot and role/subject wording are saved. | Update edits role, subject and date in place. | No retained replacement/correction history. |
| Bank Account Opening | Staff and bank details are saved; creation has outcome recovery. | Update edits bank/designation/income/purpose/date in place. | Updates do not preserve earlier printable details as separate copies. |

The remaining-gap column identifies work not included in migration 47. Existing Update forms edit fields originally entered when creating those certificates; they do not offer direct personal-detail edits. Retained correction history is still inconsistent across those types. A common follow-up should use reasoned replacements, retain the old copy and reject stale edits. A certificate correction must not change student leaving/enrollment/financial history.

## Validation

- Isolated database restore: migration 47, compatibility contract, duplicate-type rejection, separate SSC/HSSC identities, exact-issue printing, correction snapshots, collection requirements, stale/concurrent changes, save replay, permission refusal and lost-response recovery.
- Existing regular/DOB reissue SQL checks remain included in the same isolated test run.
- Offline UI checks cover saved-type selection, disabled print-time type editing, latest-copy reissue rules, correction dialog fields, window resizing and keyboard order.
