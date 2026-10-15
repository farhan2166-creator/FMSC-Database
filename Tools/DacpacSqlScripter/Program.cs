using System.Text.Json;
using Microsoft.SqlServer.Dac.Model;

// The caller must supply a PRIVATE TEMPORARY schema-only DACPAC, never a raw
// database export containing school data. No connection to SQL Server is used.
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: DacpacSqlScripter <schema-only.dacpac> <private-script-result.json>");
    return 2;
}
try
{
    using var model = new TSqlModel(args[0]);
    var results = new List<ScriptedObject>();
    foreach (var obj in model.GetObjects(DacQueryScopes.UserDefined))
    {
        if (!obj.Name.HasName) continue;
        var identifier = obj.Name.ToString();
        var kind = obj.ObjectType.Name;
        string? sql = null;
        try
        {
            if (obj.TryGetScript(out var candidate) && !string.IsNullOrWhiteSpace(candidate))
                sql = candidate;
        }
        catch (DacModelException)
        {
            // Inline constraints and model settings are not independently
            // scriptable. The Python exporter records exact XML for these.
        }
        results.Add(new ScriptedObject(kind, identifier, sql));
    }
    File.WriteAllText(args[1], JsonSerializer.Serialize(results));
    Console.WriteLine($"Inspected {results.Count} user-defined schema objects via DacFx.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"DacFx SQL scripting failed ({ex.GetType().Name}).");
    return 1;
}
internal sealed record ScriptedObject(string Type, string Name, string? Sql);
