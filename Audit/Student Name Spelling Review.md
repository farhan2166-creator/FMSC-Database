# Student and father-name spelling review

Read-only review of the current FMSC_DB on 11 October 2026 (Asia/Karachi): 6,590 student records. Original review was read-only; the approved update is recorded below. Name spellings must be checked against the admission register/B-form or other authoritative record before normalization, especially when documents have already been issued.

## Muhammad-like candidate spellings

| Existing token | Student Name fields | Father Name fields |
|---|---:|---:|
| Muhmmad | 4 | 0 |
| Muhamad | 2 | 3 |
| Muhamamd | 2 | 1 |
| Muammad | 1 | 0 |
| Muhaamad | 1 | 0 |
| Muhhammad | 1 | 0 |
| Muahammad | 0 | 1 |
| Muhamamad | 0 | 1 |
| Muhmamad | 0 | 1 |
| Mhhammad | 1 | 0 |
| Muhammadf | 1 | 1 |
| Muhammas | 1 | 0 |

Total: 22 candidate fields across 22 students. Muhammad already appears as a standalone token in 832 Student Name fields and 559 Father Name fields. Counts are case-insensitive token counts, not an assumption that every name beginning with Mu is Muhammad.

## Ambiguous entries kept separate

- M: 20 Student Name fields and six Father Name fields. Do not expand initials automatically.
- KhanMuhammad: one Father Name field; confirm whether it should be Khan Muhammad.
- Mumamail: one Student Name field; cannot determine the intended name.
- Muhawir: one Student Name field; cannot determine whether it is a distinct name or a typo.
- Mohib, Muhib, Muhajir, Mohmand, Momand and similar names are not Muhammad replacements.

## Private row-level review files

Candidate before/after names with AdmissionId and AdmissionNumber: bin\SpellingAudit_20261011\Names\muhammad-review-proposals.csv.
Uncertain original names: bin\SpellingAudit_20261011\Names\ambiguous-name-records.csv.
These files and the raw extract contain personal data and remain in ignored bin output.

Any approved update must replace only the exact approved whole-word spellings, preserve the rest of each name, check current original values before writing, retain a backup/before-after trail, and test on an isolated copy first. Existing issued certificate snapshots are a separate historical record; no automatic rewriting of issued documents is proposed.

## Approved name corrections applied

The user confirmed Muhammad for all 22 listed candidate variants and Khan Muhammad for the one KhanMuhammad entry. Applied to live FMSC_DB after a fresh COPY_ONLY/CHECKSUM backup, RESTORE VERIFYONLY, separate-file restore, integrity check and successful isolated-copy test. Final result: 14 StudentName fields and nine FatherName fields corrected across 23 students. The remaining portions of each name were preserved.

Exact original-value checks guarded the live transaction. Verification confirmed unchanged student count and AdmissionNumber, and the SQL transaction compared Address, FatherOccupation and PlaceOfBirth against their pre-update values for every student. M, Mumamail and Muhawir remained unchanged, as requested. Issued certificate snapshots were not rewritten.

Private backup, before/after names, applied-corrections.csv and copy/live results: bin\SpellingAudit_20261011\Names\Applied. No application code or SQL definitions changed. Restart the app to refresh cached student names.
