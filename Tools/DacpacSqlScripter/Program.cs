using System.Text.Json;
using Microsoft.SqlServer.Dac.Model;

// Deliberately works only with the schema-only DACPAC prepared by the Python
// exporter. The source FMSC.dacpac may contain school rows; never load it here.
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: DacpacSqlScripter <schema-only.dacpac> <output.json>");
    return 2;
}

try
{
    using var model = new TSqlModel(args[0]);
    var scripts = new List<SchemaScript>();
    foreach (var obj in model.GetObjects(DacQueryScopes.UserDefined))
    {
        if (!obj.Name.HasName) continue;
        var name = string.Join(".", obj.Name.Parts.Select(p => "[" + p.Replace("]", "]]") + "]"));
        string? sql = null;
        try
        {
            sql = obj.GetScript();
            if (string.IsNullOrWhiteSpace(sql)) sql = null;
        }
        catch (DacModelException)
        {
            // DacFx cannot render some standalone constraints and settings as
            // CREATE statements. The exporter retains full model XML for them.
        }
        scripts.Add(new SchemaScript(obj.ObjectType.Name, name, sql));
    }
    var options = new JsonSerializerOptions { WriteIndented = false };
    File.WriteAllText(args[1], JsonSerializer.Serialize(scripts, options));
    Console.WriteLine($"DacFx inspected {scripts.Count} named top-level objects (schema only).");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("DacFx schema script generation failed: " + ex.GetType().Name);
    return 1;
}

internal sealed record SchemaScript(string Type, string Name, string? Sql);
