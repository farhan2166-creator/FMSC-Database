using Microsoft.Data.SqlClient;

internal static class Identity
{
    internal static Guid Owner => Guid.Parse(Environment.GetEnvironmentVariable("FMSC_LEVEL2_OWNER")
        ?? throw new InvalidOperationException("Missing owner."));
    internal static string Catalog => "FMSC_Level2_" + Owner.ToString("N");
    internal static string Endpoint => @"(localdb)\" + Catalog;
    internal static string Root
    {
        get
        {
            string root = Path.GetFullPath(Environment.GetEnvironmentVariable("FMSC_LEVEL2_ROOT")
                ?? throw new InvalidOperationException("Missing private directory."));
            if (Path.GetFileName(root) != Catalog ||
                File.ReadAllText(Path.Combine(root, "owner.txt")).Trim() != Owner.ToString("D"))
                throw new InvalidOperationException("Directory ownership differs.");
            return root;
        }
    }
    internal static string ConnectionString => new SqlConnectionStringBuilder {
        DataSource=Endpoint, InitialCatalog=Catalog, IntegratedSecurity=true,
        TrustServerCertificate=true, ConnectTimeout=15, ConnectRetryCount=0
    }.ConnectionString;
    internal static SqlConnection Open()
    {
        var c = new SqlConnection(ConnectionString);
        try
        {
            c.Open();
            using var cmd = new SqlCommand("""
                SELECT DB_NAME(),CONVERT(nvarchar(36),value),
                    (SELECT physical_name FROM sys.database_files WHERE file_id=1)
                FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_Level2Owner';
                """, c);
            using var r=cmd.ExecuteReader();
            if (!r.Read() || r.GetString(0)!=Catalog || r.GetString(1)!=Owner.ToString("D") ||
                !Path.GetFullPath(r.GetString(2)).StartsWith(Path.Combine(Root,"database")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Database ownership differs.");
            return c;
        }
        catch { c.Dispose(); throw; }
    }
    internal static object? Scalar(string sql, params SqlParameter[] parameters)
    {
        using var c=Open(); using var cmd=new SqlCommand(sql,c);
        cmd.Parameters.AddRange(parameters); return cmd.ExecuteScalar();
    }
    internal static int Count(string sql, params SqlParameter[] parameters)=>Convert.ToInt32(Scalar(sql,parameters));
    internal static decimal Money(string sql, params SqlParameter[] parameters)=>Convert.ToDecimal(Scalar(sql,parameters));
    internal static void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
}
