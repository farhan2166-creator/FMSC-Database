# FMSC C# ↔ DACPAC compatibility audit (first pass)

This read-only CI tool analyzes C# syntax with Roslyn and compares statically resolvable stored-procedure identifiers with the latest **candidate** `FMSC.dacpac` model using Microsoft DacFx. It never connects to SQL Server or reads exported school data.

Workflow: `.github/workflows/fmsc-csharp-sql-audit.yml` runs on DACPAC changes, C# changes, or manually. A private temporary, data-free package is derived using the existing validated exporter. The job writes `CompatibilityReport.md` and `CompatibilityReport.json` as short-retention artifacts and a job summary.

**Strict scope boundary:** This first increment checks procedure **existence** for literal `DatabaseHelper` calls and selected explicit `SqlCommand` constructions; it does not yet validate procedure parameters, raw SQL columns, SQL mappings, or complete dynamic procedure resolution. Any recognized unknown procedure identifier produces `REVIEW_REQUIRED`; definite absent literal procedure names produce `FAIL`. The report explicitly states this scope. Passing this job does **not** establish end-to-end application/database compatibility.

The tool is excluded from `FMSC_SMS.csproj` compilation. No baseline fingerprints, schema approvals, migrations, installers or production files are changed.

Run locally (with .NET 8 and Python 3.12):

```sh
python Tools/export_dacpac_schema.py --prepare-model-only ./temp-schema-only.dacpac
dotnet run --project Tools/CSharpSqlAudit/CSharpSqlAudit.csproj -- ./temp-schema-only.dacpac . ./compatibility-report
```

Next increments for independent Codex verification: add robust syntactic/semantic constant propagation, DacFx parameter signature comparisons (including defaults/output/TVPs), and ScriptDom-based raw SQL table/column checks with synthetic positive/negative fixtures. Do not claim those checks are present until tested.
