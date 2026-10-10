using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Core;
public static class DbMessage
{
    public static string UserMessage(Exception ex)
    {
        if (ex is SqlException sql)
            return SqlMessage(sql);
        if (ex is TimeoutException)
            return "The operation took too long to finish. Please try again.";
        /* exceptions this application raises itself carry a message written for the operator: SecretUnavailableException explains that the stored secret cannot be read, BalanceUnavailableException that a balance could not be loaded. */
        if (IsOperatorFacing(ex))
            return ex.Message.Trim();
        return "The operation could not be completed. Please try again. If the problem continues, contact the administrator.";
    }
    /// <summary>True for exceptions this application raises itself. Identified by namespace rather than by a list of types, so a new application exception is covered without being registered here.</summary>
    private static bool IsOperatorFacing(Exception ex)
    {
        string? ns = ex.GetType().Namespace;
        return ns is not null &&
               ns.StartsWith("FMSC_SMS", StringComparison.Ordinal) &&
               !string.IsNullOrWhiteSpace(ex.Message);
    }
    public static string UserMessage(Exception ex, string action)
    {
        string message = UserMessage(ex);
        return string.IsNullOrWhiteSpace(action)
            ? message
            : $"{action}\r\n\r\n{message}";
    }
    /// <summary>True when the operator can do something about the failure: a business rule one of our own procedures raised, or a validation problem the database reported…</summary>
    public static bool IsActionable(Exception ex)
    {
        if (ex is TimeoutException)
            return false;
        if (ex is not SqlException sql)
            return false;
        if (sql.Number >= 50000 && sql.Number < 60000)
            return true;
        return sql.Number switch {
            2601 or 2627 => true,   // duplicate record
            547 => true,            // linked to other records
            515 => true,            // required value missing
            8152 or 2628 => true,   // value too long for the column
            8114 or 245 or 241 => true,  // invalid format
            1205 => true,           // deadlock - retry
            _ => false
        };
    }
    private static string SqlMessage(SqlException sql)
    {
        if (sql.Number >= 50000 && sql.Number < 60000)
        {
            string custom = string.Join(Environment.NewLine, sql.Errors.Cast<SqlError>()
                    .Select(e => e.Message.Trim())
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(custom)
                ? "The database rejected this operation. Please review the information entered and try again."
                : MakeBusinessText(custom);
        }
        return sql.Number switch {
            -1 or 0 or 2 or 20 or 26 or 40 or 52 or 53 or 64 or 233 or 10053 or 10054 or 10060 or 10061 or 17142 =>
                "FMSC could not reach the database. Please make sure the database is installed and available, then try again." + Code(sql),
            18456 =>
                "The database login was not accepted. Please contact the administrator.",
            4060 =>
                "The FMSC database could not be opened. Please contact the administrator.",
            229 =>
                "Your account does not have permission to perform this operation.",
            208 =>
                "The required database object is missing. Please contact the administrator.",
            2812 =>
                "The requested database operation is not available. Please contact the administrator.",
            2601 or 2627 =>
                "This record already exists. Please review the information and use the existing record if appropriate.",
            547 =>
                "This operation cannot be completed because the record is linked to other information. Please review the related records first.",
            515 =>
                "Some required information is missing. Please complete all required fields and try again.",
            8152 or 2628 =>
                "Some information is too long for the database field. Please shorten the value and try again.",
            8114 or 245 or 241 =>
                "One of the entered values has an invalid format. Please check the numbers, dates, and other fields and try again.",
            1205 =>
                "The database was busy with another operation. No changes were intentionally lost. Please wait a moment and try again.",
            -2 =>
                "The database operation took too long to finish. Please try again.",
            1105 or 9002 =>
                "The database is currently unable to complete this operation because of a storage problem. Please contact the administrator.",
            _ =>
                "The database could not complete this operation. Please review the information and try again. If the problem continues, contact the administrator." + Code(sql)
        };
    }
    /* An unmapped or connection-level failure carries its error number so a support call (or the log) can identify it; the friendly sentence alone cannot. */
    private static string Code(SqlException sql) => $" (Error {sql.Number})";
    private static string MakeBusinessText(string message)
    {
        string text = message
            .Replace("SQL Server", "database", StringComparison.OrdinalIgnoreCase)
            .Replace("SqlException", "database error", StringComparison.OrdinalIgnoreCase);
        text = text
            .Replace("Violation of PRIMARY KEY constraint", "This record already exists", StringComparison.OrdinalIgnoreCase)
            .Replace("Violation of UNIQUE KEY constraint", "This record already exists", StringComparison.OrdinalIgnoreCase)
            .Replace("The INSERT statement conflicted with the FOREIGN KEY constraint", "This record is linked to another record and cannot be changed this way", StringComparison.OrdinalIgnoreCase)
            .Replace("The DELETE statement conflicted with the REFERENCE constraint", "This record is still being used by another record", StringComparison.OrdinalIgnoreCase)
            .Replace("String or binary data would be truncated", "Some information is too long for the database field", StringComparison.OrdinalIgnoreCase);
        return text.Trim();
    }
}
