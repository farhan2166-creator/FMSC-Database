using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Core;

/// <summary>Bounded, user-scoped copies of stable lookup data. Never caches balances.</summary>
internal static class LookupDataCache
{
    private sealed record Entry(Task<DataTable> Loading, DateTime Created);
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Entry> Entries = new();
    private static readonly HashSet<string> Procedures = new(StringComparer.OrdinalIgnoreCase)
    {
        "GetCampuses", "GetAcademicYears", "GetAcademicPrograms", "GetClasses",
        "GetSections", "GetClassSections", "GetSubjects", "GetFeeTypes", "GetSchoolOfficials",
        "ReportClassSectionLookup"
    };
    private static int generation;
    private const int Capacity = 64;
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);

    internal static void Invalidate()
    {
        lock (Sync) { generation++; Entries.Clear(); }
    }

    internal static void OperationCompleted(string procedure)
    {
        // Conservative invalidation covers writes returning IDs/tables as well as Execute.
        if (procedure.StartsWith("Get", StringComparison.OrdinalIgnoreCase)) return;
        Invalidate();
        StudentSearchCache.Invalidate();
    }

    private static string? Key(string procedure, SqlParameter[] parameters)
    {
        if (!Procedures.Contains(procedure) ||
            !parameters.Any(p => p.ParameterName.Equals("@UserId", StringComparison.OrdinalIgnoreCase)) ||
            parameters.Any(p => p.Direction != ParameterDirection.Input || p.SqlDbType == SqlDbType.Structured)) return null;
        return procedure.ToUpperInvariant() + JsonSerializer.Serialize(parameters
            .OrderBy(p => p.ParameterName, StringComparer.OrdinalIgnoreCase)
            .Select(p => new[] { p.ParameterName.ToUpperInvariant(), p.SqlDbType.ToString(),
                p.Value is null or DBNull ? null : Convert.ToString(p.Value, CultureInfo.InvariantCulture) }));
    }
    internal static bool CanCache(string procedure, SqlParameter[] parameters) => Key(procedure, parameters) is not null;

    internal static DataTable Get(string procedure, SqlParameter[] parameters, Func<DataTable> load) =>
        GetAsync(procedure, parameters, () => Task.FromResult(load()), CancellationToken.None).GetAwaiter().GetResult();

    internal static async Task<DataTable> GetAsync(string procedure, SqlParameter[] parameters,
        Func<Task<DataTable>> load, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string? key = Key(procedure, parameters);
        if (key is null) return await load().ConfigureAwait(false);
        while (true)
        {
            Entry entry;
            int observed;
            lock (Sync)
            {
                observed = generation;
                if (!Entries.TryGetValue(key, out entry!) || DateTime.UtcNow - entry.Created >= MaxAge)
                {
                    if (Entries.Count >= Capacity)
                        Entries.Remove(Entries.OrderBy(pair => pair.Value.Created).First().Key);
                    entry = new Entry(Task.Run(async () =>
                        await LookupDisplayOrder.EnrichAsync(procedure, parameters, await load().ConfigureAwait(false)).ConfigureAwait(false)), DateTime.UtcNow);
                    Entries[key] = entry;
                }
            }
            DataTable table;
            try { table = await entry.Loading.WaitAsync(token).ConfigureAwait(false); }
            catch
            {
                lock (Sync)
                    if (entry.Loading.IsCompleted && Entries.TryGetValue(key, out var current) && current == entry)
                        Entries.Remove(key);
                throw;
            }
            lock (Sync)
            {
                if (observed != generation) continue;
                // Consumers bind/filter/edit independently; shared cached tables stay private.
                return table.Copy();
            }
        }
    }
}
