using Microsoft.Data.SqlClient;
namespace FMSC_SMS
{
    public static class DatabaseHelper
    {
        public const string RequiredDatabaseSchemaVersion="46";
        internal static string ConnectionString="";
        public static SqlConnection GetConnection()=>new(ConnectionString);
        public static void OpenConnection(SqlConnection connection)=>connection.Open();
        public static SqlParameter P(string name,object? value)=>new(name,Core.WholeRupees.ValidateParameter(name,value)??DBNull.Value);
        public static SqlParameter Int(string name,int value)=>new(name,SqlDbType.Int){Value=value};
        public static SqlParameter Date(string name,DateTime value)=>new(name,SqlDbType.Date){Value=value.Date};
    }
}
namespace FMSC_SMS.Pages.Security
{
    public static class CurrentUser { public static int UserId { get; internal set; } }
}
