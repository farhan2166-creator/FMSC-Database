using System.Text.Json;
using Microsoft.SqlServer.Dac.Model;

if (args.Length == 1 && args[0] == "--self-test")
{
    try { Regression.Run(); return 0; }
    catch (Exception ex) { Console.Error.WriteLine("Regression FAILED: " + ex); return 2; }
}
if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: CSharpSqlAudit <schema-only.dacpac> <repository> <report-directory>");
    Console.Error.WriteLine("       CSharpSqlAudit --self-test");
    return 2;
}
try
{
    var root = Path.GetFullPath(args[1]);
    var output = Path.GetFullPath(args[2]);
    if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
    using var model = new TSqlModel(args[0]); // Read schema model only: no SQL connections.
    var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
        .Where(p => !Path.GetRelativePath(root, p).Split(Path.DirectorySeparatorChar)
            .Any(s => s.Equals("bin", StringComparison.OrdinalIgnoreCase)
                   || s.Equals("obj", StringComparison.OrdinalIgnoreCase)
                   || s.Equals(".git", StringComparison.OrdinalIgnoreCase)))
        .Where(p => !Path.GetRelativePath(root, p).Replace('\\', '/')
            .StartsWith("Tools/CSharpSqlAudit/", StringComparison.OrdinalIgnoreCase))
        .OrderBy(p => p, StringComparer.Ordinal).ToArray();
    var findings = new List<Finding>();
    var audited = AuditExtensions.Scan(model, root, files, findings);
    Directory.CreateDirectory(output);
    var summary = new
    {
        audited,
        procedures = model.GetObjects(DacQueryScopes.UserDefined)
            .Count(p => p.ObjectType.Name == "Procedure"),
        sourceFiles = files.Length,
        failures = findings.Count(p => p.Status == "FAIL"),
        reviews = findings.Count(p => p.Status == "REVIEW_REQUIRED"),
        limitation = "Read-only static analysis. Dynamic procedure identifiers, indirect wrappers, SQL scope, table variables and implicit conversions may require manual review. A passing report is not proof of runtime database compatibility."
    };
    var report = new { summary, findings };
    File.WriteAllText(Path.Combine(output, "CompatibilityReport.json"),
        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    static string Safe(string text) => text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ")
        .Replace("<", "&lt;").Replace(">", "&gt;");
    var markdown = new List<string>
    {
        "# FMSC C# ↔ DACPAC compatibility audit", "",
        $"Audited calls: **{audited}**; C# files: **{files.Length}**; procedures: **{summary.procedures}**.",
        $"Definite failures: **{summary.failures}**; review required: **{summary.reviews}**.", "",
        "**Scope:** " + summary.limitation, "",
        "| Result | C# location | SQL object | Reason |",
        "|---|---|---|---|"
    };
    foreach (var f in findings.OrderBy(x => x.File).ThenBy(x => x.Line))
        markdown.Add($"| {Safe(f.Status)} | {Safe(f.File)}:{f.Line} | {Safe(f.Object)} | {Safe(f.Message)} |");
    File.WriteAllText(Path.Combine(output, "CompatibilityReport.md"),
        string.Join("\n", markdown) + "\n");
    Console.WriteLine($"Audited {audited} calls, {summary.failures} definite failures, {summary.reviews} reviews.");
    return summary.failures == 0 ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Compatibility analysis incomplete ({ex.GetType().Name}): {ex.Message}");
    return 2;
}
internal sealed record Finding(string Status, string File, int Line, string Caller, string Object, string Message);
