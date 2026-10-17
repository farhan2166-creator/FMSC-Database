using FMSC_SMS.Core;
using FMSC_SMS.Services.Communication;
using Microsoft.Data.SqlClient;

if(!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FMSC_COMM_WORKER_ENABLED")!="yes")
{Console.Error.WriteLine("Worker disabled. Explicit Windows-user deployment and configuration are required.");return 1;}
try
{
    var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FMSC_COMMUNICATION_CONNECTION")??throw new InvalidOperationException());
    if(connection.AttachDBFilename.Length>0 || string.IsNullOrWhiteSpace(connection.InitialCatalog))throw new InvalidOperationException();
    int actor=int.Parse(Environment.GetEnvironmentVariable("FMSC_COMMUNICATION_ACTOR")??throw new InvalidOperationException());
    using(var c=new SqlConnection(connection.ConnectionString)){c.Open();DatabaseCompatibility.Validate(c);}
    var store=new SqlCommunicationStore(()=>new SqlConnection(connection.ConnectionString),actor);
    if(!await store.IsInstalledAsync())throw new InvalidOperationException();
    var gateways=new WindowsGatewayStore();using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(30)};
    var dispatcher=new MessageDispatcher(store,c=>gateways.Provider(c,http));
    using var stop=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop.Cancel();};
    await dispatcher.RunAsync(stop.Token,Console.WriteLine,Environment.GetEnvironmentVariable("FMSC_COMM_EVALUATE_RULES")=="yes");
    return 0;
}
catch(OperationCanceledException){return 0;}
catch(Exception){Console.Error.WriteLine("Communication worker stopped. Verify explicit database, actor, permissions and protected gateway configuration; review uncertain outbox submissions before retrying.");return 1;}
