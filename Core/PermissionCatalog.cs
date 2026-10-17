using System.Text.Json;

namespace FMSC_SMS.Core;

internal sealed record PermissionDefinition(string Code, string Name, string Description);

/// <summary>The permissions implemented by the application and migration 3.</summary>
internal static class PermissionCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, PermissionDefinition>> Definitions = new(() => {
        using var stream = typeof(PermissionCatalog).Assembly.GetManifestResourceStream("FMSC_SMS.Core.PermissionCatalog.json")
            ?? throw new InvalidDataException("The application's permission catalog is missing.");
        var entries = JsonSerializer.Deserialize<PermissionDefinition[]>(stream)
            ?? throw new InvalidDataException("The application's permission catalog is invalid.");
        return entries.ToDictionary(entry => entry.Code, StringComparer.OrdinalIgnoreCase);
    });
    internal static IReadOnlyDictionary<string, PermissionDefinition> All => Definitions.Value;
    internal static readonly string[] OptionalCommunication = ["Communication.View", "Communication.Compose", "Communication.Send", "Communication.Bulk", "Communication.Approve", "Communication.Configure", "Communication.History"];
    internal static bool Contains(string code) => All.ContainsKey(code) || OptionalCommunication.Contains(code, StringComparer.OrdinalIgnoreCase);
    internal static bool IsSupportedCatalog(IEnumerable<string> codes)
    {
        var actual = codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return All.Keys.All(actual.Contains) && actual.All(Contains)
            && (OptionalCommunication.All(actual.Contains) || OptionalCommunication.All(code => !actual.Contains(code)));
    }
}
