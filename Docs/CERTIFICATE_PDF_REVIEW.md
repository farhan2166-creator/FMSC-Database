# Certificates — PDF and Print Review

Use GitHub Actions **08 - Review - Certificates (PDF and Print)** to produce one bookmarked PDF of realistic, fictional certificate combinations, similar to the two existing fee-document and dashboard-report review workflows.

## Download

Run the workflow manually with **Run workflow**, or review its result when a certificate template, letterhead, printing helper or certificate review fixture changes on a pull request or on `main`. On a passing Windows run, download **FMSC-Certificates-Review.pdf** from the workflow summary or artifact. This is a single PDF download without a ZIP of separate samples or a folder of PNGs. The artifact is kept for seven days.

The bookmarks use the actual certificate names and meaningful variants rather than placeholder scenario identifiers. The combined PDF retains each original sheet and its page content. The summary reports the source commit, ten template types, number of combinations, pages and SHA-256 checksum.

## Certificates and combinations

The generator runs the **actual shipped RDLC templates** with `CertificateUi.PrintData` and `CertificateLetterhead.Definition`, not recreated visual layouts.

| Certificate | Covered variations |
| --- | --- |
| Character | SSC and HSSC, male/female/unspecified gender, year, roll number and conduct |
| Date of Birth | Male and female students with different dates, plus unspecified gender |
| Regular Student | Middle and higher secondary classes, plus a longer recorded name |
| School Leaving | Original, duplicate, unchanged/promoted class, and reason wording |
| College Leaving | Original/duplicate, HSSC Part I/II, and birth dates |
| Sports | Cricket, athletics, and volleyball team leadership with realistic achievement narratives |
| No Objection | Male and female student wording |
| Provisional | SSC, HSSC Part I and Part II, grades, marks, board details and short/extended subject lists |
| Regular Teacher / Lecturer | Teacher and lecturer, female and male wording, subjects |
| Bank Account Opening | Teacher and lecturer, salary, bank, branch and purpose |

The fictional recipient and parent names use ordinary Pakistani naming conventions. Identifiers and contact details are invented or non-operational. **No database is read, no certificate is issued or collected, and no physical print job runs.**

## What is checked

The Windows renderer verifies that all ten templates are present; source data is appropriate to each RDLC field; each certificate contains the selected person and parent; every PDF has readable content and fits one physical sheet; duplicate leaving certificates are visibly identified; and the review has one bookmark per combination. After merging, checks compare each page's original geometry and extracted text so no content, page order, orientation or paper size is silently changed. A failed run must not publish an older successful PDF.

The generator keeps individual intermediate PDFs and a provenance JSON under `artifacts/certificate-pdf-review` in the runner, but the workflow intentionally uploads **only the final combined PDF**, as with the fee-document and dashboard workflows.

For local Windows verification:

```powershell
dotnet run --project Tests/ThemeSmokeChecks/ThemeSmokeChecks.csproj -c Release -- --certificate-pdf-review
```

A passing CI rendering check demonstrates production RDLC rendering with fictional data, **not** physical-printer or school-computer acceptance. Operators must still inspect the letterhead, spacing, printed text and preprinted-pad alignment on their actual printer.
