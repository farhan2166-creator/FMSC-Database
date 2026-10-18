using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
using static FMSC_SMS.Core.CommunicationInstallation;

// Used only by the owned disposable SQL harness; never shipped as an installer entry point.
internal static class CommunicationFixture
{
    internal static async Task<int> InstallAsync(SqlConnection connection, string script)
    {
        using var acquire = new SqlCommand("DECLARE @R int; EXEC @R=sys.sp_getapplock @Resource=N'FMSC_CommunicationInstall', @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=0; SELECT @R;", connection);
        if (Convert.ToInt32(await acquire.ExecuteScalarAsync().ConfigureAwait(false)) < 0)
            throw new InvalidOperationException("Another communication installation is running.");
        try
        {
            int state = await CheckAsync(connection).ConfigureAwait(false);
            if (state != Missing) return state; // Includes an already verified installation: never recreate it.
            using var stream = typeof(CommunicationInstallation).Assembly.GetManifestResourceStream("FMSC_SMS.Communication.Schema.sql")
                ?? throw new InvalidOperationException("Communication installation definition is missing.");
            using var reader = new StreamReader(stream);
            string expected = await reader.ReadToEndAsync().ConfigureAwait(false);
            if (script.Replace("\r\n", "\n") != expected.Replace("\r\n", "\n")) return ScriptMismatch;
            // An outer server transaction keeps the script's nested COMMIT from
            // exposing changes until the SAME connection verifies all contracts.
            try
            {
                using var begin = new SqlCommand("SET XACT_ABORT ON; BEGIN TRANSACTION; EXEC sys.sp_set_session_context @key=N'FMSC_CommunicationInstall',@value=1;", connection);
                await begin.ExecuteNonQueryAsync().ConfigureAwait(false);
                foreach (string batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(batch)) continue;
                    using var command = new SqlCommand(batch, connection) { CommandTimeout = 60 };
                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                using var count = new SqlCommand("SELECT @@TRANCOUNT;", connection);
                if (Convert.ToInt32(await count.ExecuteScalarAsync().ConfigureAwait(false)) != 1 ||
                    await CheckAsync(connection).ConfigureAwait(false) != Ready)
                    throw new InvalidOperationException("Communication installation did not pass verification.");
                using var defaults = new SqlCommand("SELECT COUNT(*) FROM comm.Settings WHERE Id=1 AND Mode='Disabled' AND Paused=1;", connection);
                if (Convert.ToInt32(await defaults.ExecuteScalarAsync().ConfigureAwait(false)) != 1)
                    throw new InvalidOperationException("Communication installation must remain disabled and paused.");
                using var commit = new SqlCommand("COMMIT TRANSACTION;", connection);
                await commit.ExecuteNonQueryAsync().ConfigureAwait(false);
                return Ready;
            }
            catch
            {
                if (connection.State == System.Data.ConnectionState.Open)
                {
                    try
                    {
                        using var rollback = new SqlCommand("IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;", connection);
                        await rollback.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                    catch (SqlException) { SqlConnection.ClearPool(connection); }
                }
                throw; // An uncertain COMMIT is reported as failure; next run verifies instead of reinstalling.
            }
        }
        finally
        {
            if (connection.State == System.Data.ConnectionState.Open)
            {
                using var release = new SqlCommand("EXEC sys.sp_releaseapplock @Resource=N'FMSC_CommunicationInstall',@LockOwner=N'Session';", connection);
                await release.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }

}
