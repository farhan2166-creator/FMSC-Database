using FMSC_SMS.Services.Communication;
using Microsoft.Data.SqlClient;

internal static class CommunicationChecks
{
    internal static async Task RunAsync(string master)
    {
        string connection=new SqlConnectionStringBuilder(master){InitialCatalog="CommunicationFixture",Pooling=false}.ConnectionString;
        using var c=new SqlConnection(connection);c.Open();int checks=0;
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
        object? Scalar(string sql){using var q=new SqlCommand(sql,c){CommandTimeout=20};return q.ExecuteScalar();}
        void Execute(string sql){using var q=new SqlCommand(sql,c){CommandTimeout=20};q.ExecuteNonQuery();}
        int actor=Convert.ToInt32(Scalar("SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator';"));
        string original=Convert.ToString(Scalar("SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC;"))!;
        var store=new SqlCommunicationStore(()=>new SqlConnection(connection),actor);
        int messages=Convert.ToInt32(Scalar("SELECT COUNT(*) FROM comm.Message;"));
        try
        {
            foreach(int version in new[]{5,21,41,999})
            {
                Execute($"UPDATE dbo.DatabaseVersion SET VersionNumber=N'{version}' WHERE VersionId=(SELECT MAX(VersionId) FROM dbo.DatabaseVersion);");
                Check(await CommunicationSchemaContract.VerifyAsync(c),"Communication API contract remains valid across unrelated main migration numbers");
                Check(await store.IsInstalledAsync(),"The worker's actual readiness check does not reject the main migration number");
                using var callback=new SqlCommand("""
                    EXECUTE AS USER=N'FMSCCommunicationIngress';
                    BEGIN TRY
                        EXEC comm.IngestMeta @Actor=@Actor,@Payload=@Payload;
                        REVERT;
                    END TRY
                    BEGIN CATCH
                        REVERT; THROW;
                    END CATCH;
                    """,c){CommandTimeout=20};
                callback.Parameters.AddWithValue("@Actor",actor);
                callback.Parameters.AddWithValue("@Payload","{\"entry\":[],\"test\":\""+Guid.NewGuid().ToString("N")+"\"}");
                callback.ExecuteNonQuery();
                Check(true,"Restricted callback accepts its valid empty test envelope regardless of main migration number");
            }
        }
        finally
        {
            using var q=new SqlCommand("UPDATE dbo.DatabaseVersion SET VersionNumber=@Version WHERE VersionId=(SELECT MAX(VersionId) FROM dbo.DatabaseVersion);",c);
            q.Parameters.AddWithValue("@Version",original);q.ExecuteNonQuery();
        }
        Check(Convert.ToInt32(Scalar("SELECT COUNT(*) FROM comm.Message;"))==messages,"Version-independence checks never enqueue, send or modify messages");
        string claim=Convert.ToString(Scalar("SELECT OBJECT_DEFINITION(OBJECT_ID(N'comm.Claim'));"))!;
        try
        {
            Execute("DROP PROCEDURE comm.Claim;");
            Check(!await CommunicationSchemaContract.VerifyAsync(c)&&!await store.IsInstalledAsync(),"Removing a required API still prevents worker startup");
        }
        finally{Execute(claim);}
        Execute("CREATE USER FMSC_NoWebhook WITHOUT LOGIN;");
        try
        {
            Execute($"EXECUTE AS USER=N'FMSC_NoWebhook'; BEGIN TRY EXEC comm.IngestMeta @Actor={actor},@Payload=N'{{\"entry\":[]}}'; REVERT; END TRY BEGIN CATCH REVERT; THROW; END CATCH;");
            throw new Exception("A non-webhook principal was accepted.");
        }
        catch(SqlException ex) when(ex.Number is 229 or 51003){Check(true,"Webhook principal restrictions remain enforced");}
        Check(await store.IsInstalledAsync(),"The communication module remains ready after the negative checks");
        Console.WriteLine($"PASS: {checks} communication SQL checks; no dispatcher or external delivery ran.");
    }
}
