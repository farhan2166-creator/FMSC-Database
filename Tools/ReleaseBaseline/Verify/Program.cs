global using System.Data;
using Microsoft.Data.SqlClient;
using FMSC_SMS.Core;

// Read-only validation of the explicitly owned first-release working copy.
if (args.Length != 3 || !System.Text.RegularExpressions.Regex.IsMatch(args[1], @"^FMSC_Baseline1_[a-f0-9]{32}$"))
    throw new ArgumentException("Supply the owned LocalDB endpoint, unique baseline-copy database, and working-database directory.");
string directory = Path.GetFullPath(args[2]);
using var connection = new SqlConnection(new SqlConnectionStringBuilder {
    DataSource = args[0], InitialCatalog = args[1], IntegratedSecurity = true,
    TrustServerCertificate = true, Pooling = false, ConnectTimeout = 10
}.ConnectionString);
connection.Open();
using (var command = new SqlCommand("SELECT physical_name FROM sys.database_files;", connection))
using (var rows = command.ExecuteReader())
{
    var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        Path.Combine(directory, "FMSC_DB.mdf"), Path.Combine(directory, "FMSC_DB_log.ldf") };
    while (rows.Read())
        if (!expected.Remove(Path.GetFullPath(rows.GetString(0)))) throw new InvalidOperationException("Unexpected database file; refusing verification.");
    if (expected.Count != 0) throw new InvalidOperationException("The intended working pair was not attached.");
}
DatabaseCompatibility.Validate(connection);
Console.WriteLine("PASS: baseline 1 satisfies the complete application database compatibility contract (read-only).");

namespace FMSC_SMS {
    internal static class DatabaseHelper { public const string RequiredDatabaseSchemaVersion = "1"; }
}
