using System.Text.Json;

namespace FMSC_SMS.Core;

internal sealed record ReportScopeSelection(int Campus, int Year, int Class, int Section)
{
    internal static ReportScopeSelection? Read(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var scope = JsonSerializer.Deserialize<ReportScopeSelection>(value);
            return scope is not null && scope.Campus >= 0 && scope.Year >= 0 && scope.Class >= 0 && scope.Section >= 0
                ? scope : null;
        }
        catch (JsonException) { return null; }
    }

    internal string Serialize() => JsonSerializer.Serialize(this);
}
