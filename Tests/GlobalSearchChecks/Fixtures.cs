using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Security
{
    internal static class CurrentUser
    {
        internal static int UserId=1;
        internal static HashSet<string> Grants=new();
        internal static bool HasPermission(string value)=>Grants.Contains(value);
    }
}
namespace FMSC_SMS
{
    internal static class DatabaseHelper
    {
        internal static string DatabaseEndpoint="synthetic-endpoint";
        internal static Func<string,CancellationToken,SqlParameter[],Task<DataTable>> Read=(_,_,_)=>throw new InvalidOperationException("No fixture reader configured");
        internal static SqlParameter Int(string name,int value)=>new(name,SqlDbType.Int){Value=value};
        internal static SqlParameter P(string name,object value)=>new(name,value);
        internal static SqlParameter Bit(string name,bool value)=>new(name,SqlDbType.Bit){Value=value};
        internal static Task<DataTable> QuerySqlAsync(string sql,CancellationToken token,params SqlParameter[] parameters)=>Read(sql,token,parameters);
    }
}
