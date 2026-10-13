using Microsoft.Data.SqlClient;

// SQL-free tests exercise the production cache/loader/sorting logic. Any accidental
// database access fails rather than connecting to a school database.
namespace FMSC_SMS.Core;
internal static class StudentSearchCache
{
    internal static int Invalidations;
    internal static void Invalidate() => Invalidations++;
}
internal static class DatabaseHelper
{
    internal static SqlConnection GetConnection() => throw new InvalidOperationException("Unexpected SQL access");
    internal static void OpenConnection(SqlConnection connection) => throw new InvalidOperationException("Unexpected SQL access");
    internal static SqlParameter Int(string name, int value) => new(name, SqlDbType.Int) { Value = value };
    internal static DataTable Query(string procedure, params SqlParameter[] parameters) => throw new InvalidOperationException("Unexpected SQL access");
}
