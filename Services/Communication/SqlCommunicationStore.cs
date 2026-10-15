using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Services.Communication;

public sealed class SqlCommunicationStore(Func<SqlConnection> connections, int actor)
{
    public int Actor => actor;
    public static SqlParameter P(string name,object? value)=>new(name,value??DBNull.Value);
    public async Task<DataTable> CallAsync(string operation,CancellationToken token=default,params SqlParameter[] parameters)
    {
        if(!new[]{"Read","SaveContact","SaveTemplate","Preview","CreateCampaign","Transition","Claim","Complete","StatusUpdate","SaveSettings","SaveRule","Evaluate","Retry","Details","CampaignSummary","RevokeTemplate"}.Contains(operation))
            throw new ArgumentException("Unknown communication operation.");
        await using var connection=connections();
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command=new SqlCommand("comm."+operation,connection){CommandType=CommandType.StoredProcedure,CommandTimeout=60};
        command.Parameters.Add(P("@Actor",actor));command.Parameters.AddRange(parameters);
        using var reader=await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var table=new DataTable();
        if(reader.FieldCount>0){for(int i=0;i<reader.FieldCount;i++)table.Columns.Add(reader.GetName(i),reader.GetFieldType(i));
            while(await reader.ReadAsync(token).ConfigureAwait(false)){var values=new object[reader.FieldCount];reader.GetValues(values);table.Rows.Add(values);}}
        while(await reader.NextResultAsync(token).ConfigureAwait(false))while(await reader.ReadAsync(token).ConfigureAwait(false)){}
        return table;
    }
    public async Task<bool> IsInstalledAsync(CancellationToken token=default)
    {
        await using var connection=connections();await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command=new SqlCommand("SELECT CASE WHEN OBJECT_ID(N'comm.Module',N'U') IS NOT NULL AND OBJECT_ID(N'comm.Claim',N'P') IS NOT NULL AND OBJECT_ID(N'comm.CreateCampaign',N'P') IS NOT NULL AND OBJECT_ID(N'comm.Contact',N'U') IS NOT NULL THEN 1 ELSE 0 END",connection);
        if(Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false))!=1)return false;
        await using var version=new SqlCommand("SELECT COUNT(*) FROM comm.Module WHERE Version=1;",connection);
        return Convert.ToInt32(await version.ExecuteScalarAsync(token).ConfigureAwait(false))==1 && await CommunicationSchemaContract.VerifyAsync(connection,token).ConfigureAwait(false);
    }
    public Task<DataTable> ReadAsync(string view,int page=0,string search="",string filter="",CancellationToken token=default)=>
        CallAsync("Read",token,P("@View",view),P("@Page",page),P("@Search",search),P("@Filter",filter));
    public async Task<MessageEnvelope?> ClaimAsync(CancellationToken token)
    {
        var rows=await CallAsync("Claim",token).ConfigureAwait(false);
        if(rows.Rows.Count==0)return null;
        var r=rows.Rows[0];
        using var parameters=JsonDocument.Parse(Convert.ToString(r["Parameters"])!);
        return new((Guid)r["MessageId"],(Guid)r["ClaimToken"],Enum.Parse<MessageChannel>((string)r["Channel"]),(string)r["Destination"],(string)r["Body"],
            (string)r["ProviderTemplate"],(string)r["Language"],parameters.RootElement.EnumerateArray().Select(x=>x.GetProperty("text").GetString()??"").ToArray(),(int)r["Attempts"],(bool)r["Simulation"]);
    }
    public Task CompleteAsync(MessageEnvelope message,string provider,ProviderResult result)=>CallAsync("Complete",CancellationToken.None,
        P("@Id",message.Id),P("@Token",message.ClaimToken),P("@State",result.State.ToString()),P("@Provider",provider),P("@Reference",result.Reference),P("@Code",result.Code),P("@RetrySafe",result.RetrySafe),P("@RetryDelay",result.RetryAfterSeconds));
    public Task<DataTable> PreviewAsync(Guid template,MessageChannel channel,IEnumerable<Guid> contacts,string announcement="",int? payment=null,CancellationToken token=default)=>
        CallAsync("Preview",token,P("@TemplateId",template),P("@Channel",channel.ToString()),P("@ContactIds",ContactJson(contacts)),P("@Announcement",announcement),P("@PaymentId",payment));
    private static string ContactJson(IEnumerable<Guid> ids)=>JsonSerializer.Serialize(ids.Distinct().Order().ToArray());
    public Task CreateAsync(Guid request,Guid template,MessageChannel channel,IEnumerable<Guid> contacts,string announcement,DateTime dueUtc,string? eventKey=null,int? payment=null,CancellationToken token=default)=>
        CallAsync("CreateCampaign",token,P("@Id",request),P("@TemplateId",template),P("@Channel",channel.ToString()),P("@ContactIds",ContactJson(contacts)),P("@Announcement",announcement),P("@DueUtc",dueUtc),P("@EventKey",eventKey),P("@PaymentId",payment));
    public Task TransitionAsync(Guid campaign,string action,CancellationToken token=default)=>CallAsync("Transition",token,P("@Id",campaign),P("@Action",action));
}
public sealed class CommunicationService(SqlCommunicationStore store)
{
    public SqlCommunicationStore Store=>store;
    public async Task<Guid> PrepareAsync(Guid request,Guid template,MessageChannel channel,IEnumerable<Guid> contacts,string announcement,DateTime dueUtc,CancellationToken token=default)
    {
        var selection=contacts.Distinct().Order().ToArray();
        if(selection.Length is <1 or >500)throw new ArgumentException("Select 1–500 contacts.");
        var preview=await store.PreviewAsync(template,channel,selection,announcement,token:token).ConfigureAwait(false);
        if(preview.Rows.Count!=selection.Length || preview.AsEnumerable().Any(r=>!string.IsNullOrEmpty(Convert.ToString(r["Exclusion"]))))
            throw new InvalidOperationException("Recipient eligibility changed. Refresh the preview before creating a campaign.");
        await store.CreateAsync(request,template,channel,selection,announcement,dueUtc,token:token).ConfigureAwait(false);
        return request;
    }
}
public sealed class MessageDispatcher(SqlCommunicationStore store,Func<MessageChannel,IMessageProvider> liveProviders)
{
    private readonly SemaphoreSlim single=new(1,1);
    private int submitting;
    public bool IsSubmitting=>Volatile.Read(ref submitting)!=0;
    public async Task<bool> DispatchOneAsync(CancellationToken token)
    {
        if(!await single.WaitAsync(0,token).ConfigureAwait(false))return false;
        try
        {
            var message=await store.ClaimAsync(token).ConfigureAwait(false);
            if(message is null)return false;
            Interlocked.Exchange(ref submitting,1);
            IMessageProvider provider;
            try{provider=message.Simulation?new SimulationProvider(message.Channel):liveProviders(message.Channel);}
            catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or System.Security.Cryptography.CryptographicException or IOException or FormatException)
            {await store.CompleteAsync(message,"Unconfigured",new(DeliveryState.Rejected,Code:"LOCAL_GATEWAY_CONFIGURATION",RetrySafe:true)).ConfigureAwait(false);return true;}
            ProviderResult result;
            try{result=await provider.SubmitAsync(message,token).ConfigureAwait(false);}
            catch(Exception ex) when(ex is OperationCanceledException or HttpRequestException or InvalidOperationException or ArgumentException)
            {result=new(DeliveryState.DeliveryUnknown,Code:"UNEXPECTED_SUBMISSION_RESULT_RECONCILE");}
            await store.CompleteAsync(message,provider.Name,result).ConfigureAwait(false);
            return true;
        }
        finally{Interlocked.Exchange(ref submitting,0);single.Release();}
    }
    // No timer wakes the application when this task is not explicitly running.
    public async Task RunAsync(CancellationToken token,Action<string> operational,bool evaluateAutomation=false)
    {
        DateTime evaluateAfter=DateTime.MinValue;
        while(!token.IsCancellationRequested)
        {
            try { if(evaluateAutomation && DateTime.UtcNow>=evaluateAfter){await store.CallAsync("Evaluate",token).ConfigureAwait(false);evaluateAfter=DateTime.UtcNow.AddMinutes(1);}
                bool claimed=await DispatchOneAsync(token).ConfigureAwait(false);operational(claimed?"Dispatcher operational; submission recorded":"Dispatcher operational; waiting for eligible work"); }
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch(SqlException){operational("Dispatcher paused by database error; inspect Communication Center");throw;}
            await Task.Delay(TimeSpan.FromSeconds(5),token).ConfigureAwait(false);
        }
    }
}
