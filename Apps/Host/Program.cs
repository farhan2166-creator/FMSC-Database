using System.Security.Cryptography;
using System.Text;
using FMSC_SMS.Services.Communication;
using Microsoft.Data.SqlClient;

var builder=WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();builder.Logging.AddConsole();builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.ConfigureKestrel(options=>options.Limits.MaxRequestBodySize=65536);
string Required(string key)=>Environment.GetEnvironmentVariable(key) is {Length:>0} value?value:throw new InvalidOperationException("A required webhook configuration value is missing: "+key);
string secret=Required("FMSC_META_APP_SECRET"),verify=Required("FMSC_META_VERIFY_TOKEN"),account=Required("FMSC_META_BUSINESS_ACCOUNT"),phone=Required("FMSC_META_PHONE_ID");
var connection=new SqlConnectionStringBuilder(Required("FMSC_COMMUNICATION_CONNECTION"));
if(connection.AttachDBFilename.Length>0 || string.IsNullOrWhiteSpace(connection.InitialCatalog))throw new InvalidOperationException("Use an explicitly configured existing database; attachment is forbidden.");
int actor=int.Parse(Required("FMSC_COMMUNICATION_ACTOR"));
var store=new MetaWebhookStore(()=>new SqlConnection(connection.ConnectionString),actor,secret,account,phone);
using(var c=new SqlConnection(connection.ConnectionString)){c.Open();if(!await CommunicationSchemaContract.VerifyAsync(c))throw new InvalidOperationException("Reviewed communication schema required.");}
var app=builder.Build();
// Deploy behind a trusted HTTPS reverse proxy, or use Kestrel HTTPS directly.
using var ingressGate=new SemaphoreSlim(8,8);
app.Use(async(context,next)=>{if(!await ingressGate.WaitAsync(0,context.RequestAborted)){context.Response.StatusCode=429;return;}try{await next(context);}finally{ingressGate.Release();}});
app.UseExceptionHandler(handler=>handler.Run(async context=>{context.Response.StatusCode=503;await context.Response.WriteAsync("Receipt persistence unavailable; retry later.");}));
app.MapGet("/whatsapp",(HttpRequest request)=>{
    string supplied=request.Query["hub.verify_token"].ToString();
    bool valid=request.Query["hub.mode"]=="subscribe" && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied),Encoding.UTF8.GetBytes(verify));
    return valid?Results.Text(request.Query["hub.challenge"].ToString()):Results.Unauthorized();
});
app.MapPost("/whatsapp",async(HttpRequest request,CancellationToken token)=>{
    using var memory=new MemoryStream();byte[] block=new byte[8192];int read;
    while((read=await request.Body.ReadAsync(block,token))>0){if(memory.Length+read>65536)return Results.StatusCode(413);memory.Write(block,0,read);}
    byte[] body=memory.ToArray();
    return await store.ReceiveAsync(body,request.Headers["X-Hub-Signature-256"].ToString(),DateTimeOffset.UtcNow,token)?Results.Ok():Results.Unauthorized();
});
app.Run();
