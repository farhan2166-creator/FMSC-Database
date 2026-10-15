#if FMSC_LEVEL2
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace FMSC_SMS.Core;

// Compiled ONLY for the dedicated Level 2 binary. No discovery, attach, repair,
// credentials or fallback to FMSC_DB. Every connection validates ownership.
internal static class Level2Target
{
    internal static Guid Owner => Guid.Parse(Environment.GetEnvironmentVariable("FMSC_LEVEL2_OWNER")
        ?? throw new InvalidOperationException("Level 2 owner is required."));
    internal static string Catalog => "FMSC_Level2_" + Owner.ToString("N");
    internal static string Endpoint => @"(localdb)\" + Catalog;
    internal static string Root
    {
        get
        {
            string root = Path.GetFullPath(Environment.GetEnvironmentVariable("FMSC_LEVEL2_ROOT")
                ?? throw new InvalidOperationException("Level 2 private root is required."));
            if (!string.Equals(Path.GetFileName(root), Catalog, StringComparison.Ordinal) ||
                !File.Exists(Path.Combine(root, "owner.txt")) ||
                File.ReadAllText(Path.Combine(root, "owner.txt")).Trim() != Owner.ToString("D"))
                throw new InvalidOperationException("Level 2 directory ownership mismatch.");
            return root;
        }
    }
    internal static string Folder(Environment.SpecialFolder folder) => Path.Combine(Root, "profile", folder.ToString());
    internal static string InputId(string? name)=>"Level2Input_"+Regex.Replace(name??"Input",@"[^A-Za-z0-9]+","_");
    internal static void Open(SqlConnection connection)
    {
        var target = new SqlConnectionStringBuilder(connection.ConnectionString);
        if (target.DataSource != Endpoint || target.InitialCatalog != Catalog ||
            !target.IntegratedSecurity || target.AttachDBFilename.Length != 0)
            throw new InvalidOperationException("Refusing a non-Level-2 connection.");
        connection.Open();
        try
        {
            using var cmd = new SqlCommand("""
                SELECT DB_NAME(),CONVERT(nvarchar(36),value),
                    (SELECT physical_name FROM sys.database_files WHERE file_id=1)
                FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_Level2Owner';
                """, connection);
            using var r = cmd.ExecuteReader();
            string prefix = Path.Combine(Root, "database") + Path.DirectorySeparatorChar;
            if (!r.Read() || r.GetString(0) != Catalog || r.GetString(1) != Owner.ToString("D") ||
                !Path.GetFullPath(r.GetString(2)).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Level 2 database identity mismatch.");
        }
        catch { connection.Close(); throw; }
    }
    internal static void Validate()
    {
        using var connection = new SqlConnection(DatabaseHelper.GetConnectionString(Catalog));
        Open(connection);
    }
    internal static void ValidateSchema()
    {
        using var connection = new SqlConnection(DatabaseHelper.GetConnectionString(Catalog));
        Open(connection);
        DatabaseCompatibility.Validate(connection);
    }
    internal static int VerifyRejections()
    {
        // Offline regression: every rejected input must fail before SqlConnection.Open.
        // A SqlException (including endpoint unavailable) therefore fails this check.
        string? previous=Environment.GetEnvironmentVariable("FMSC_LEVEL2_OWNER");
        Environment.SetEnvironmentVariable("FMSC_LEVEL2_OWNER","f1b9556d-e54a-4f20-96e7-714934f02472");
        try
        {
            var safe=new SqlConnectionStringBuilder {DataSource=Endpoint,InitialCatalog=Catalog,IntegratedSecurity=true};
            var candidates=new List<string>();
            var bad=new SqlConnectionStringBuilder(safe.ConnectionString){DataSource=@"(localdb)\MSSQLLocalDB"};candidates.Add(bad.ConnectionString);
            bad=new(safe.ConnectionString){InitialCatalog="FMSC_DB"};candidates.Add(bad.ConnectionString);
            bad=new(safe.ConnectionString){AttachDBFilename=@"C:\ProgramData\FMSC_Database\FMSC_DB.mdf"};candidates.Add(bad.ConnectionString);
            bad=new(safe.ConnectionString){IntegratedSecurity=false};candidates.Add(bad.ConnectionString);
            foreach(string candidate in candidates)
            {
                using var c=new SqlConnection(candidate);bool rejected=false;
                try {Open(c);} catch(InvalidOperationException ex) when(ex.Message=="Refusing a non-Level-2 connection."){rejected=true;}
                if(!rejected)return 1;
            }
            return 0;
        }
        finally {Environment.SetEnvironmentVariable("FMSC_LEVEL2_OWNER",previous);}
    }
}
#endif
