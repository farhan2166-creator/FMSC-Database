using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Core;

/// <summary>Official register numbers; database student keys never become display numbers.</summary>
public static class AdmissionNumbers
{
    private static readonly object Sync = new();
    private static Dictionary<int, string>? _numbers;
    private static DateTime _loadedAt;
    private static int _generation;

    public static void Invalidate() { lock (Sync) { _numbers = null; _generation++; } }

    public static bool TryNormalize(string? text, out string number)
    {
        number = string.Empty;
        var match = Regex.Match((text ?? string.Empty).Trim(), @"^([0-9]+)([a-zA-Z]*)$");
        if (!match.Success || !long.TryParse(match.Groups[1].Value, out long main) || main <= 0 || main == long.MaxValue)
            return false;
        number = main.ToString(CultureInfo.InvariantCulture) + match.Groups[2].Value.ToLowerInvariant();
        return number.Length <= 40;
    }

    public static string ForId(int id)
    {
        if (id <= 0) return string.Empty;
        lock (Sync)
            if (_numbers is not null && DateTime.UtcNow - _loadedAt < TimeSpan.FromSeconds(45))
                return _numbers.GetValueOrDefault(id, string.Empty);
        using var cn = DatabaseHelper.GetConnection();
        DatabaseHelper.OpenConnection(cn);
        return Numbers(cn).GetValueOrDefault(id, string.Empty);
    }

    private static Dictionary<int, string> Numbers(SqlConnection cn)
    {
        int generation;
        lock (Sync)
        {
            if (_numbers is not null && DateTime.UtcNow - _loadedAt < TimeSpan.FromSeconds(45))
                return _numbers;
            generation = _generation;
        }
        using var cmd = new SqlCommand("SELECT AdmissionId, COALESCE(AdmissionNumber,QuickReference,'') FROM dbo.Students;", cn);
        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<int, string>();
        while (reader.Read()) result[reader.GetInt32(0)] = reader.GetString(1);
        return PublishNumbers(result, generation);
    }

    private static Dictionary<int, string> PublishNumbers(Dictionary<int, string> result, int generation)
    {
        lock (Sync)
        {
            // A lookup started before invalidation cannot repopulate the cache.
            if (generation != _generation) return result;
            // Concurrent cold lookups may finish together; keep the first fresh snapshot.
            if (_numbers is not null && DateTime.UtcNow - _loadedAt < TimeSpan.FromSeconds(45))
                return _numbers;
            _numbers = result;
            _loadedAt = DateTime.UtcNow;
            return result;
        }
    }
    public static string Read(DataRow row, string column = "AdmissionNumber") =>
        row.Table.Columns.Contains(column) && row[column] is not DBNull
            ? Convert.ToString(row[column], CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;

    internal static void Enrich(DataTable table, SqlConnection cn)
    {
        var keys = table.Columns.Cast<DataColumn>()
            .Where(c => Regex.IsMatch(c.ColumnName, @"AdmissionId[12]?$", RegexOptions.IgnoreCase)).ToArray();
        if (keys.Length == 0) return;
        Dictionary<int, string>? numbers = null;
        foreach (var key in keys)
        {
            string name = Regex.Replace(key.ColumnName, "AdmissionId", "AdmissionNumber", RegexOptions.IgnoreCase);
            if (table.Columns.Contains(name) && table.AsEnumerable().All(row => row[name] is not DBNull && !string.IsNullOrWhiteSpace(Convert.ToString(row[name])))) continue;
            if (!table.Columns.Contains(name)) table.Columns.Add(name, typeof(string));
            numbers ??= Numbers(cn);
            foreach (DataRow row in table.Rows)
                if ((row[name] is DBNull || string.IsNullOrWhiteSpace(Convert.ToString(row[name]))) && int.TryParse(Convert.ToString(row[key]), out int id)) row[name] = numbers.GetValueOrDefault(id, string.Empty);
        }
        AddSortColumns(table);
    }

    internal static async Task EnrichAsync(DataTable table, SqlConnection cn, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var keys = table.Columns.Cast<DataColumn>()
            .Where(c => Regex.IsMatch(c.ColumnName, @"AdmissionId[12]?$", RegexOptions.IgnoreCase)).ToArray();
        if (keys.Length == 0) return;
        Dictionary<int, string>? numbers = null;
        foreach (var key in keys)
        {
            string name = Regex.Replace(key.ColumnName, "AdmissionId", "AdmissionNumber", RegexOptions.IgnoreCase);
            if (table.Columns.Contains(name) && table.AsEnumerable().All(row => row[name] is not DBNull && !string.IsNullOrWhiteSpace(Convert.ToString(row[name])))) continue;
            numbers ??= await NumbersAsync(cn, token).ConfigureAwait(false);
            if (!table.Columns.Contains(name)) table.Columns.Add(name, typeof(string));
            foreach (DataRow row in table.Rows)
            {
                token.ThrowIfCancellationRequested();
                if ((row[name] is DBNull || string.IsNullOrWhiteSpace(Convert.ToString(row[name]))) && int.TryParse(Convert.ToString(row[key]), out int id))
                    row[name] = numbers.GetValueOrDefault(id, string.Empty);
            }
        }
        AddSortColumns(table);
    }

    private static async Task<Dictionary<int, string>> NumbersAsync(SqlConnection cn, CancellationToken token)
    {
        int generation;
        lock (Sync)
        {
            if (_numbers is not null && DateTime.UtcNow - _loadedAt < TimeSpan.FromSeconds(45))
                return _numbers;
            generation = _generation;
        }
        using var cmd = new SqlCommand("SELECT AdmissionId, COALESCE(AdmissionNumber,QuickReference,'') FROM dbo.Students;", cn);
        using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new Dictionary<int, string>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result[reader.GetInt32(0)] = reader.GetString(1);
        token.ThrowIfCancellationRequested();
        return PublishNumbers(result, generation);
    }
    public static void AddSortColumns(DataTable table)
    {
        if (!table.Columns.Contains("AdmissionNumber")) return;
        if (!table.Columns.Contains("__AdmissionNumberBase")) table.Columns.Add("__AdmissionNumberBase", typeof(long));
        if (!table.Columns.Contains("__AdmissionNumberSuffix")) table.Columns.Add("__AdmissionNumberSuffix", typeof(string));
        foreach (DataRow row in table.Rows)
        {
            string number = Read(row);
            var match = Regex.Match(number, @"^([0-9]+)(.*)$");
            long main = match.Success && long.TryParse(match.Groups[1].Value, out long value) ? value : long.MaxValue;
            string suffix = match.Success ? match.Groups[2].Value : number;
            // Binding and sorting revisit this table. Avoid row-change events and
            // index maintenance when derived values have not changed.
            if (!Equals(row["__AdmissionNumberBase"], main)) row["__AdmissionNumberBase"] = main;
            if (!Equals(row["__AdmissionNumberSuffix"], suffix)) row["__AdmissionNumberSuffix"] = suffix;
        }
    }

    public static int Compare(string left, string right)
    {
        var a = Regex.Match(left, @"^([0-9]+)(.*)$");
        var b = Regex.Match(right, @"^([0-9]+)(.*)$");
        if (a.Success && b.Success && long.TryParse(a.Groups[1].Value, out long av) && long.TryParse(b.Groups[1].Value, out long bv))
        {
            int main = av.CompareTo(bv);
            if (main != 0) return main;
            return StringComparer.OrdinalIgnoreCase.Compare(a.Groups[2].Value, b.Groups[2].Value);
        }
        return StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    public static DataTable Sort(DataTable table)
    {
        AddSortColumns(table);
        return new DataView(table) { Sort = "__AdmissionNumberBase ASC, __AdmissionNumberSuffix ASC" }.ToTable();
    }
}
