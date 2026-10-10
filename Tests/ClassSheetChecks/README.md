# Class marks and exam attendance sheets

Open Reports → Class Marks & Attendance Sheets. Choose one campus, academic year, class and section. The roster comes from the existing `GetStudentRegister` procedure with active student/enrollment filters; no balances are requested and no records are saved.

Both templates use portrait A4 with two identical copies side by side, 25 students per page, alphabetical names and continuous serial numbers across pages. Empty final-page rows stay blank. Each copy repeats school/campus/year/class/section, exam/subject/date, teacher name/signature and page information. Optional details can be entered before preview or left blank for handwriting. Marks use Written, Oral and Total; exam attendance uses Present, Absent and student Signature.

Run `dotnet run --project Tests/ClassSheetChecks/ClassSheetChecks.csproj`. The checks cover empty rosters, page boundaries, continuation numbering, excluded former/unenrolled students, blank slots, optional details, A4/page-break definitions and real RDLC HTML processing of both copies for a 51-student class.

HTML rendering was visually reviewed in the cloud. PDF/image rendering and physical A4 pagination depend on Windows graphics and remain manual checks: preview/print 1, 25, 26 and 51 students; confirm no extra blank pages, identical copies, legible long names and teacher fields on every page. Check increased display scaling, scoped selectors and a class with no active students. The shared viewer supplies zoom, pan, page selection and printing. No migration is required.
