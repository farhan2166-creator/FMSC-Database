# Generic grid report checks

Run `dotnet run --project Tests/GridReportChecks/GridReportChecks.csproj`.

These checks build the production report definition and render it with the real RDLC engine. They cover complete student names, financial precision, dates, numeric alignment, blank remarks, repeated headings, school identity, filters, page-number expressions and A4 orientation. The test preview host only substitutes opening the Windows window.

On Windows, print-preview a multi-page generic report and inspect the first, middle and last pages. Review long names, large amounts, timestamps with page-specific formats, totals, school identity and filter text. Print or export a small sample. Existing receipt, reminder and class-sheet layouts have separate rendering checks.
