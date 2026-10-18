using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

internal static class Sql
{
    internal static void ValidateTarget()
    {
        using var c = Identity.Open();
        DatabaseHelper.ConnectionString = Identity.ConnectionString;
    }

    internal static SqlParameter P(string name, object? value) => new(name, value ?? DBNull.Value);
    internal static SqlParameter TvpParameter(string name, string type, DataTable value) =>
        new(name, SqlDbType.Structured) { TypeName = "dbo." + type, Value = value };
    internal static DataTable Query(string text, params SqlParameter[] args)
    {
        using var connection = Identity.Open();
        using var command = new SqlCommand(text, connection) { CommandTimeout = 90 };
        command.Parameters.AddRange(args);
        var result = new DataTable(); using var adapter = new SqlDataAdapter(command); adapter.Fill(result); return result;
    }
    internal static object? Scalar(string text, params SqlParameter[] args)
    {
        using var connection = Identity.Open();
        using var command = new SqlCommand(text, connection) { CommandTimeout = 90 }; command.Parameters.AddRange(args);
        return command.ExecuteScalar();
    }
    internal static int Count(string text, params SqlParameter[] args) => Convert.ToInt32(Scalar(text, args));
    internal static decimal Money(string text, params SqlParameter[] args) => Convert.ToDecimal(Scalar(text, args));
    internal static DataTable Proc(string name, params SqlParameter[] args)
    {
        using var connection = Identity.Open();
        using var command = new SqlCommand(name, connection) { CommandType = CommandType.StoredProcedure, CommandTimeout = 90 };
        command.Parameters.AddRange(args);
        using var result = new DataSet(); using var adapter = new SqlDataAdapter(command); adapter.Fill(result);
        // Admission with a family link emits nested family results before the
        // final authoritative admission result. Never treat that first row as
        // the newly saved student's identity.
        if (result.Tables.Count == 0) return new DataTable();
        var last = result.Tables[^1]; result.Tables.Remove(last); return last;
    }
    internal static int Key(string procedure, params SqlParameter[] args) => Convert.ToInt32(Proc(procedure, args).Rows[0][0]);
}

namespace FMSC_SMS.Core
{
    // Only the logging sink is replaced. SQL, validation, transaction and recovery code are linked unchanged.
    internal static class AppExceptionHandler
    {
        internal static void LogNonFatal(Exception ex, string context) => Console.WriteLine("NONFATAL: recovery-file cleanup requires review.");
    }
}
