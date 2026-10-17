using FMSC_SMS.Services.Communication;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace FMSC_SMS.Core;

// Installer-only entry points: explicit connections, no LocalDB attachment,
// no logged-in school actor, provider configuration or message dispatcher.
internal static class CommunicationInstallation
{
    internal const int Ready = 0, Missing = 10, BaseIncompatible = 20,
        ModuleIncompatible = 21, ScriptMismatch = 22, Failed = 23;

    internal static async Task<int> CheckAsync(SqlConnection connection)
    {
        try { DatabaseCompatibility.Validate(connection); }
        catch (Exception ex) when (ex is SqlException or DatabaseCompatibilityException)
        { return BaseIncompatible; }
        using var exists = new SqlCommand("SELECT CASE WHEN SCHEMA_ID(N'comm') IS NULL THEN 0 ELSE 1 END;", connection);
        if (Convert.ToInt32(await exists.ExecuteScalarAsync().ConfigureAwait(false)) == 0) return Missing;
        try { return await CommunicationSchemaContract.VerifyAsync(connection).ConfigureAwait(false) ? Ready : ModuleIncompatible; }
        catch (SqlException) { return ModuleIncompatible; }
    }

    internal static int Run(string[] args)
    {
        if (args.Length != 3 || args[0] != "--verify-installation") return Failed;
        // Production installer connections are Windows-authenticated LocalDB only.
        // Do not route through DatabaseHelper.GetConnection/auto-attach.
        if (args[2] != "FMSC_DB" ||
            !(args[1].Equals(@"(localdb)\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase) ||
              Regex.IsMatch(args[1], "^" + Regex.Escape(@"np:\\.\pipe\LOCALDB#") + @"[A-Za-z0-9]+" + Regex.Escape(@"\tsql\query") + "$", RegexOptions.IgnoreCase))) return Failed;
        try
        {
            var builder = new SqlConnectionStringBuilder { DataSource = args[1], InitialCatalog = args[2],
                IntegratedSecurity = true, TrustServerCertificate = true, ConnectTimeout = 15 };
            using var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();
            return CheckAsync(connection).GetAwaiter().GetResult();
        }
        catch (Exception)
        { return Failed; } // Installer explains exit codes; no credentials/raw SQL are logged.
    }
}
