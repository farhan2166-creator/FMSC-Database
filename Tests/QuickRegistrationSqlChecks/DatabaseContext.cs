using Microsoft.Data.SqlClient;

namespace FMSC_SMS
{
    // Test connection only; never use the production app's auto-attach path.
    public static class DatabaseHelper
    {
        public const string RequiredDatabaseSchemaVersion="23";
        internal static string ConnectionString = "";
        public static SqlConnection GetConnection()=>new(ConnectionString);
        public static void OpenConnection(SqlConnection connection)=>connection.Open();
        public static System.Data.DataTable QuerySql(string sql,params SqlParameter[] args)
        {
            using var connection=GetConnection();using var command=new SqlCommand(sql,connection);command.Parameters.AddRange(args);
            using var adapter=new SqlDataAdapter(command);var result=new System.Data.DataTable();adapter.Fill(result);return result;
        }
        public static SqlParameter P(string name,object? value)=>new(name,Core.WholeRupees.ValidateParameter(name,value)??DBNull.Value);
        public static SqlParameter Int(string name,int value)=>new(name,SqlDbType.Int){Value=value};
        public static SqlParameter Bit(string name,bool value)=>new(name,SqlDbType.Bit){Value=value};
        public static SqlParameter Date(string name,DateTime value)=>new(name,SqlDbType.Date){Value=value.Date};
    }
}

namespace FMSC_SMS.Pages.Security
{
    public static class CurrentUser
    {
        public static int UserId { get; internal set; }
    }
}
