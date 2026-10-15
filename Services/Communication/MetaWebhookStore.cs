using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Services.Communication;

// The external ingress performs signature/account/time validation itself. UI stores cannot invoke raw ingestion.
public sealed class MetaWebhookStore(Func<SqlConnection> connections,int actor,string secret,string businessAccount,string phoneId)
{
    public async Task<bool> ReceiveAsync(byte[] body,string signature,DateTimeOffset now,CancellationToken token)
    {
        if(!MetaCallbackVerifier.Validate(body,signature,secret,businessAccount,phoneId,now))return false;
        await using var connection=connections();await connection.OpenAsync(token).ConfigureAwait(false);
        if(!await CommunicationSchemaContract.VerifyAsync(connection,token).ConfigureAwait(false))throw new InvalidOperationException("Reviewed communication schema is required.");
        int impersonated;
        await using(var scope=new SqlCommand("""
            IF ISNULL(IS_ROLEMEMBER(N'FMSC_CommunicationWebhook'),0)<>1
            BEGIN
              IF USER_NAME()<>N'dbo' THROW 51003,'Configure the restricted communication webhook principal.',1;
              EXECUTE AS USER=N'FMSCCommunicationIngress';
              SELECT 1;
            END ELSE SELECT 0;
            """,connection))impersonated=Convert.ToInt32(await scope.ExecuteScalarAsync(token).ConfigureAwait(false));
        await using var command=new SqlCommand("comm.IngestMeta",connection){CommandType=CommandType.StoredProcedure,CommandTimeout=30};
        command.Parameters.AddWithValue("@Actor",actor);command.Parameters.AddWithValue("@Payload",Encoding.UTF8.GetString(body));
        try{await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);}
        finally
        {
            if(impersonated==1)
            {
                try{await using var revert=new SqlCommand("REVERT;",connection);await revert.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);}
                catch{SqlConnection.ClearPool(connection);throw;}
            }
        }
        return true;
    }
}
