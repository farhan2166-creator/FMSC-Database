using System.Text.Json;

namespace FMSC_SMS.Core;

// Presentation metadata only; balances continue to come from the advance ledger.
internal static class FamilyAdvanceLayout
{
    internal const string Column="__AdvanceFamily";
    internal const string Key="FMSC.AdvanceFamilies";
    internal sealed record Child(string Name,string Placement,decimal Advance);
    internal sealed record Family(string Parent,Child[] Children);
    internal static Family Read(string json)=>JsonSerializer.Deserialize<Family>(json)??throw new InvalidDataException("Missing family presentation.");
    internal static void Copy(DataTable source,DataTable target)
    {
        if(source.Columns.Contains(Column))target.ExtendedProperties[Key]=source.AsEnumerable().Select(row=>Convert.ToString(row[Column])!).ToArray();
    }
}
