using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.SqlServer.Dac.Model;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: CSharpSqlAudit <schema-only.dacpac> <repository> <report-directory>");
    return 2;
}
try
{
    using var model = new TSqlModel(args[0]);
    var root = Path.GetFullPath(args[1]);
    var output = Path.GetFullPath(args[2]);
    var procedures = model.GetObjects(DacQueryScopes.UserDefined)
        .Where(x => x.ObjectType.Name == "Procedure" && x.Name.HasName)
        .Select(x => Normalize(x.Name.ToString())).ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (procedures.Count == 0) throw new InvalidOperationException("DACPAC has no stored procedures; refusing false success");
    var found = new List<Finding>();
    var audited = 0;
    var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
        .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj" or ".git"))
        .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}Tools{Path.DirectorySeparatorChar}CSharpSqlAudit{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .OrderBy(p => p, StringComparer.Ordinal).ToList();
    foreach (var path in files)
    {
        var code = File.ReadAllText(path);
        var tree = CSharpSyntaxTree.ParseText(code, path: path);
        var doc = tree.GetRoot();
        foreach (var invoke in doc.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var method = invoke.Expression.ToString();
            var isHelper = method.StartsWith("DatabaseHelper.", StringComparison.Ordinal)
                && new[] { "Query", "QueryUser", "QueryUserAsync", "Execute", "ExecuteUser", "ExecuteUserAsync", "Scalar", "ScalarUser", "ScalarUserAsync" }
                    .Any(n => method.Split('.').Last().Equals(n, StringComparison.Ordinal) || method.Split('.').Last().StartsWith(n + "Async", StringComparison.Ordinal));
            var isConstructor = false;
            if (!isHelper) continue;
            var argument = invoke.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            if (argument is null) continue;
            audited++;
            var position = tree.GetLineSpan(invoke.Span).StartLinePosition;
            var file = Path.GetRelativePath(root, path).Replace('\\', '/');
            var literal = Resolve(argument);
            if (literal is null)
            {
                found.Add(new("REVIEW_REQUIRED", file, position.Line + 1, method, "", "Procedure identifier is not a literal or compile-time concatenation; manual review required."));
                continue;
            }
            var name = Normalize(literal);
            if (!procedures.Contains(name))
                found.Add(new("FAIL", file, position.Line + 1, method, name, "Referenced stored procedure is absent from the uploaded DACPAC."));
        }
        // Direct new SqlCommand("dbo.Name", ...) requires checking CommandType.
        foreach (var creation in doc.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Where(x => x.Type.ToString().EndsWith("SqlCommand", StringComparison.Ordinal)))
        {
            var argument = creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
            if (argument is null) continue;
            var text = Resolve(argument);
            var parent = creation.Parent?.ToString() ?? "";
            var outer = creation.Ancestors().OfType<StatementSyntax>().FirstOrDefault()?.ToString() ?? "";
            var explicitlyProcedure = parent.Contains("CommandType.StoredProcedure", StringComparison.Ordinal)
                || outer.Contains("CommandType.StoredProcedure", StringComparison.Ordinal);
            if (!explicitlyProcedure) continue;
            audited++;
            var position = tree.GetLineSpan(creation.Span).StartLinePosition;
            var file = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (text is null)
                found.Add(new("REVIEW_REQUIRED", file, position.Line + 1, "SqlCommand", "", "Dynamic stored-procedure command text requires review."));
            else if (!procedures.Contains(Normalize(text)))
                found.Add(new("FAIL", file, position.Line + 1, "SqlCommand", Normalize(text), "Stored procedure is not defined in DACPAC."));
        }
    }
    Directory.CreateDirectory(output);
    var counts = new { audited, procedures = procedures.Count, sourceFiles = files.Count,
        failures = found.Count(f => f.Status == "FAIL"),
        reviews = found.Count(f => f.Status == "REVIEW_REQUIRED"),
        limitation = "First pass: procedure existence for literal DatabaseHelper calls and explicitly typed SqlCommand constructions. Parameter, raw SQL, constant propagation and full dynamic-call coverage are not claimed." };
    var report = new { summary = counts, findings = found };
    File.WriteAllText(Path.Combine(output, "CompatibilityReport.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    var lines = new List<string> { "# FMSC C# ↔ DACPAC compatibility audit", "",
        $"Audited calls: **{audited}**; C# source files: **{files.Count}**; known stored procedures: **{procedures.Count}**.",
        $"Definite failures: **{counts.failures}**; review required: **{counts.reviews}**.", "",
        "**Scope:** " + counts.limitation, "",
        "| Result | C# location | Procedure | Reason |", "|---|---|---|---|" };
    foreach (var f in found.OrderBy(x => x.File).ThenBy(x => x.Line))
        lines.Add($"| {f.Status} | `{f.File}:{f.Line}` | `{f.Object}` | {f.Message} |");
    File.WriteAllText(Path.Combine(output, "CompatibilityReport.md"), string.Join("\n", lines) + "\n");
    Console.WriteLine($"Audited {audited} calls, {counts.failures} failures, {counts.reviews} requiring review.");
    return counts.failures > 0 ? 1 : 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"Compatibility analysis incomplete ({e.GetType().Name}): {e.Message}");
    return 2;
}
static string Normalize(string name)
{
    var parts = name.Replace("[", "").Replace("]", "").Trim().Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return parts.Length == 1 ? "dbo." + parts[0] : string.Join('.', parts);
}
static string? Resolve(ExpressionSyntax syntax)
{
    if (syntax is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        return literal.Token.ValueText;
    if (syntax is ParenthesizedExpressionSyntax paren) return Resolve(paren.Expression);
    if (syntax is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
    {
        var a = Resolve(binary.Left); var b = Resolve(binary.Right);
        return a is not null && b is not null ? a + b : null;
    }
    return null;
}
internal sealed record Finding(string Status, string File, int Line, string Caller, string Object, string Message);
