Run `dotnet run --project Tests/StudentSearchChecks/StudentSearchChecks.csproj`.

These checks compile the production matcher, admission-number logic and picker result builder. They cover exact and prefix name ordering, admission normalization and natural ordering, mixed student/father tokens, optional fields, scoped limits, source preservation, picker caps and ambiguous highlights. UI shells are stubbed and database access throws; these checks do not exercise native WinForms or SQL.
