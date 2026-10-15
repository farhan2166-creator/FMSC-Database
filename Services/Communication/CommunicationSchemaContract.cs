using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Services.Communication;

public static class CommunicationSchemaContract
{
    public static async Task<bool> VerifyAsync(SqlConnection connection,CancellationToken token=default)
    {
        using var stream=typeof(CommunicationSchemaContract).Assembly.GetManifestResourceStream("FMSC_SMS.Communication.Schema.sql")
            ??throw new InvalidOperationException("Communication schema contract is missing.");
        using var reader=new StreamReader(stream);string script=await reader.ReadToEndAsync(token).ConfigureAwait(false);
        var expected=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(string batch in Regex.Split(script,@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase))
        {
            var match=Regex.Match(batch,@"^\s*CREATE (?:PROCEDURE|FUNCTION) comm\.\[?(\w+)\]?",RegexOptions.IgnoreCase);
            if(match.Success)expected[match.Groups[1].Value]=batch.Replace("\r\n","\n").Trim();
        }
        await using var command=new SqlCommand("SELECT O.name,M.definition FROM sys.objects O JOIN sys.sql_modules M ON M.object_id=O.object_id WHERE O.schema_id=SCHEMA_ID(N'comm');",connection);
        using(var result=await command.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            while(await result.ReadAsync(token).ConfigureAwait(false))
            {
                if(!expected.Remove(result.GetString(0),out var definition) || result.IsDBNull(1) || definition!=result.GetString(1).Replace("\r\n","\n").Trim())return false;
            }
        }
        if(expected.Count!=0)return false;
        await using var structure=new SqlCommand("""
            SELECT CASE WHEN (SELECT COUNT(*) FROM comm.Module WHERE Version=1)=1
            AND COL_LENGTH(N'comm.Message',N'ClaimToken')=16 AND COL_LENGTH(N'comm.Message',N'Simulation')=1
            AND COL_LENGTH(N'comm.Message',N'ProviderReference')=200 AND COL_LENGTH(N'comm.Contact',N'ConsentSource')=400
            AND COL_LENGTH(N'comm.Contact',N'Revision')=8 AND COL_LENGTH(N'comm.Campaign',N'ContentHash')=32
            AND OBJECT_ID(N'comm.PendingReceipt',N'U') IS NOT NULL AND COL_LENGTH(N'comm.Message',N'EnrollmentId')=4 AND OBJECT_ID(N'comm.RuleBaseline',N'U') IS NOT NULL
            AND COL_LENGTH(N'comm.Campaign',N'RequestHash')=32 AND COL_LENGTH(N'comm.Settings',N'NextSendUtc') IS NOT NULL
            AND (SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'comm.Message') AND is_unique=1 AND name IN(N'UX_CommMessage_Recipient',N'UX_CommMessage_Provider'))=2
            AND EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'comm.Campaign') AND name=N'UX_CommCampaign_Event' AND is_unique=1)
            AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE schema_id=SCHEMA_ID(N'comm') AND (is_disabled=1 OR is_not_trusted=1))
            AND NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE schema_id=SCHEMA_ID(N'comm') AND (is_disabled=1 OR is_not_trusted=1))
            THEN 1 ELSE 0 END;
            """,connection);
        return Convert.ToInt32(await structure.ExecuteScalarAsync(token).ConfigureAwait(false))==1;
    }
}
