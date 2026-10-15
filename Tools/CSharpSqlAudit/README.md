# FMSC C# ↔ schema-only DACPAC compatibility audit (incremental, read-only)

**This is a static compatibility audit, not a runtime or release-readiness certificate.** It never connects to SQL Server, modifies database files, exports school rows, or changes installer/approval fingerprints. It opens the **candidate schema-only DACPAC** through Microsoft DacFx and parses C# and SQL text in memory. It writes only Markdown/JSON findings under the *requested report directory*. The separate audit project is excluded from the WinForms project.

## What is checked

- Roslyn scans C# `DatabaseHelper.Query`, `QueryUser`, `QueryUserSet`, `Execute`, `ExecuteUser`, `Scalar`, `ScalarUser`, and supported `Async` variants. It checks semantic method identity when binding succeeds (including aliases) and conservatively falls back to known helper syntax where the app has unavailable references.
- Literal, concatenated, `const`, and limited locally initialized immutable procedure names can be resolved. Dynamic, mutable or forwarded names generate `REVIEW_REQUIRED`.
- DacFx supplies stored-procedure existence and generates each CREATE PROCEDURE script; Microsoft's ScriptDom parses declared parameter names, SQL types, optional defaults and table-valued parameter type names. Model/parse failures generate review findings, **not** invented empty signatures.
- Supplied `DatabaseHelper` parameter constructors, `new SqlParameter(...)` and simple local parameter arrays are compared with procedure signatures. `*User*` wrappers automatically add `@UserId`. Explicit nonexistent parameter names and mismatched TVP names are definite failures. Unknown parameter arrays, missing required arguments and possibly convertible SQL type differences require review.
- `DatabaseHelper.QuerySql` / `QuerySqlAsync` with statically recoverable SQL are parsed using ScriptDom. Explicit schema-qualified references to absent DACPAC tables/views are definite failures; unqualified/default-schema resolution and unresolved columns are reviews. CTE and temporary identifiers are not treated as physical tables.
- Explicit `new SqlCommand(...){ CommandType = CommandType.StoredProcedure }` calls are checked. Post-construction `CommandType` mutations are reported for review rather than treated as certain execution paths; default `CommandType.Text` is not a stored procedure.

## Synthetic regression tests

`dotnet run -c Release --project Tools/CSharpSqlAudit/CSharpSqlAudit.csproj -- --self-test`

The self-test builds an in-memory fictional DacFx schema and fictional C# caller fixture (no external SQL server). It exercises positive and negative cases for known/missing procedures, sync/async wrappers and user injection, constant and dynamic names, aliases, named arguments, timeout-independent parameter names, declared defaults, supplied/missing parameters, mismatched types, correct/incorrect TVP types, raw SQL table/column references, `SqlCommand` construction and mutation, unrelated helper lookalikes, and exact reported source line/file. Each assertion fails CI if a regression is introduced.

**Normal audit:**

```sh
python Tools/export_dacpac_schema.py --prepare-model-only ./temp-schema-only.dacpac
dotnet run -c Release --project Tools/CSharpSqlAudit/CSharpSqlAudit.csproj -- ./temp-schema-only.dacpac . ./compatibility-report
```

The GitHub workflow `.github/workflows/fmsc-csharp-sql-audit.yml` prepares data-free metadata, runs synthetic regressions, runs the real source audit, and publishes short-lived `CompatibilityReport.json` / `CompatibilityReport.md` artifacts. Definite failures and incomplete execution block this audit check; reviews must be triaged separately.

## Important limitations

- **Semantic precision:** This is not a full solution-level MSBuild compilation. Missing external assemblies, reflection, delegates, extension wrappers, cross-method constant propagation, complex control/data flow, and dynamic SQL/procedure strings can evade reliable resolution. Reports explicitly separate verified paths from `REVIEW_REQUIRED`.
- **Parameter types:** Not all helper constructors specify `SqlDbType`. SQL length/precision/scale, nullability, direction/output semantics, coercion, overload binding and values computed at runtime are not fully proven. TVP type identity is compared; TVP row values, schema compatibility, column precision and complete `DataTable`/record shapes are not proven.
- **Raw SQL:** SQL parser checks are deliberately conservative. CTEs, temp tables, synonyms, SQL functions, aliases in nested scopes, default schemas, unqualified columns, wildcard projections, computed expressions and dynamic statement construction can require manual review. This analyzer does not infer runtime SQL Server name-resolution settings.
- **`SqlCommand` and wrapper flow:** Not all `SqlCommand.CommandText` assignments, command execution paths or indirect wrappers are covered. Post-construction command-type assignments require review.
- **No operational proof:** Successful static analysis does **not** validate database permissions, transactions, result-set shapes, business logic, runtime data, installer behavior or end-to-end schema compatibility. Independent DB lifecycle checks are separate.

Never merge this PR or change operational schema approvals as a side effect of this tool. Review and authorization are required.
