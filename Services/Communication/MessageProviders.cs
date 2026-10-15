using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FMSC_SMS.Services.Communication;

// No provider response bodies, credentials, destinations or message text are included in exceptions/logs.
public abstract class HttpMessageProvider(HttpClient http) : IMessageProvider
{
    public abstract string Name { get; }
    public abstract MessageChannel Channel { get; }
    protected abstract HttpRequestMessage Submission(MessageEnvelope message);
    protected abstract ProviderResult Parse(JsonElement json);
    protected static async Task<byte[]> ReadBoundedAsync(HttpContent content,CancellationToken token)
    {
        await using var stream=await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var memory=new MemoryStream();byte[] block=new byte[8192];int read;
        while((read=await stream.ReadAsync(block,token).ConfigureAwait(false))>0)
        {if(memory.Length+read>65536)throw new InvalidDataException("Provider response exceeded its bounded size.");memory.Write(block,0,read);}
        return memory.ToArray();
    }
    public async Task<ProviderResult> SubmitAsync(MessageEnvelope message,CancellationToken token)
    {
        if (message.Simulation) throw new InvalidOperationException("A simulated message cannot use a live provider.");
        using var request = Submission(message);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            using var response = await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false);
            if(response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retry=response.Headers.RetryAfter;
                int seconds=(int)Math.Clamp(Math.Ceiling((retry?.Delta??(retry?.Date is {} date?date-DateTimeOffset.UtcNow:TimeSpan.FromSeconds(60))).TotalSeconds),1,86400);
                return new(DeliveryState.Rejected,Code:"HTTP_429",RetrySafe:true,RetryAfterSeconds:seconds);
            }
            if((int)response.StatusCode>=500) return new(DeliveryState.DeliveryUnknown,Code:"HTTP_5XX_UNCERTAIN");
            if(!response.IsSuccessStatusCode)
            {
                string code="HTTP_"+(int)response.StatusCode;
                try
                {
                    using var error=JsonDocument.Parse(await ReadBoundedAsync(response.Content,timeout.Token).ConfigureAwait(false));
                    var root=error.RootElement.TryGetProperty("error",out var nested)?nested:error.RootElement;
                    if(root.TryGetProperty("code",out var providerCode)&&providerCode.TryGetInt32(out int number))code+="_PROVIDER_"+number;
                }
                catch(Exception ex) when(ex is JsonException or InvalidOperationException or IOException or InvalidDataException or HttpRequestException or OperationCanceledException){}
                return new(DeliveryState.Rejected,Code:code);
            }
            var bytes = await ReadBoundedAsync(response.Content,timeout.Token).ConfigureAwait(false);
            if(bytes.Length>65536) return new(DeliveryState.DeliveryUnknown,Code:"RESPONSE_TOO_LARGE");
            using var json = JsonDocument.Parse(bytes);
            return Parse(json.RootElement);
        }
        catch(OperationCanceledException){return new(DeliveryState.DeliveryUnknown,Code:"SUBMISSION_TIMEOUT_OR_CANCELLED");}
        catch(InvalidDataException){return new(DeliveryState.DeliveryUnknown,Code:"RESPONSE_TOO_LARGE");}
        catch(IOException){return new(DeliveryState.DeliveryUnknown,Code:"ACKNOWLEDGEMENT_READ_FAILURE");}
        catch(HttpRequestException){return new(DeliveryState.DeliveryUnknown,Code:"NETWORK_ACKNOWLEDGEMENT_UNKNOWN");}
        catch(JsonException){return new(DeliveryState.DeliveryUnknown,Code:"UNREADABLE_ACKNOWLEDGEMENT");}
        catch(InvalidOperationException){return new(DeliveryState.DeliveryUnknown,Code:"INVALID_ACKNOWLEDGEMENT");}
    }
    public virtual Task<ProviderResult> QueryAsync(string reference,CancellationToken token) =>
        Task.FromResult(new ProviderResult(DeliveryState.DeliveryUnknown,reference,"SIGNED_WEBHOOK_OR_PROVIDER_REVIEW_REQUIRED"));
}
public sealed class TwilioSmsProvider : HttpMessageProvider
{
    private readonly HttpClient http;
    private readonly string account, secret, sender;
    public TwilioSmsProvider(HttpClient http,string account,string secret,string sender) : base(http)
    {
        if(!Regex.IsMatch(account,@"^AC[a-fA-F0-9]{32}$") || string.IsNullOrWhiteSpace(secret) || secret.Contains('\r') || secret.Contains('\n'))
            throw new ArgumentException("Twilio account configuration is incomplete.");
        if(!Regex.IsMatch(sender,@"^(\+[1-9][0-9]{7,14}|[A-Za-z][A-Za-z0-9 ]{0,10})$")) throw new ArgumentException("Configure a provider-authorized sender.");
        this.http=http;this.account=account;this.secret=secret;this.sender=sender;
    }
    public override string Name=>"Twilio";
    public override MessageChannel Channel=>MessageChannel.Sms;
    private HttpRequestMessage Request(HttpMethod method,string suffix)
    {
        var request=new HttpRequestMessage(method,"https://api.twilio.com/2010-04-01/Accounts/"+account+"/Messages"+suffix);
        request.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(account+":"+secret)));
        return request;
    }
    protected override HttpRequestMessage Submission(MessageEnvelope message)
    {
        var request=Request(HttpMethod.Post,".json");
        request.Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"To",message.Destination},{"From",sender},{"Body",message.Body}});
        return request;
    }
    protected override ProviderResult Parse(JsonElement json)
    {
        string? id=json.TryGetProperty("sid",out var value)?value.GetString():null;
        return id is not null && Regex.IsMatch(id,@"^SM[a-fA-F0-9]{32}$")
            ?new(DeliveryState.Accepted,id):new(DeliveryState.DeliveryUnknown,Code:"MISSING_PROVIDER_REFERENCE");
    }
    public override async Task<ProviderResult> QueryAsync(string reference,CancellationToken token)
    {
        if(!Regex.IsMatch(reference,@"^SM[a-fA-F0-9]{32}$"))throw new ArgumentException("Invalid provider reference.");
        using var request=Request(HttpMethod.Get,"/"+reference+".json");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false);
            if(!response.IsSuccessStatusCode)return new(DeliveryState.DeliveryUnknown,reference,"STATUS_UNAVAILABLE");
            using var json=JsonDocument.Parse(await ReadBoundedAsync(response.Content,timeout.Token).ConfigureAwait(false));
            if(json.RootElement.GetProperty("sid").GetString()!=reference)return new(DeliveryState.DeliveryUnknown,reference,"STATUS_REFERENCE_MISMATCH");
            var state=json.RootElement.GetProperty("status").GetString() switch {
                "delivered"=>DeliveryState.Delivered,"sent"=>DeliveryState.Sent,"failed" or "undelivered"=>DeliveryState.Failed,_=>DeliveryState.Accepted};
            return new(state,reference);
        }
        catch(Exception ex) when(ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException){return new(DeliveryState.DeliveryUnknown,reference,"STATUS_UNAVAILABLE");}
    }
}
public sealed class WhatsAppCloudProvider : HttpMessageProvider
{
    private readonly string phoneId,secret,version;
    public WhatsAppCloudProvider(HttpClient http,string phoneId,string secret,string version):base(http)
    {
        if(!Regex.IsMatch(phoneId,@"^[0-9]{5,30}$") || string.IsNullOrWhiteSpace(secret) || !Regex.IsMatch(version,@"^v[0-9]{2}\.0$"))
            throw new ArgumentException("Configure the authorized Meta business phone, access token and supported Graph version.");
        this.phoneId=phoneId;this.secret=secret;this.version=version;
    }
    public override string Name=>"MetaCloud";
    public override MessageChannel Channel=>MessageChannel.WhatsApp;
    protected override HttpRequestMessage Submission(MessageEnvelope message)
    {
        if(!Regex.IsMatch(message.ProviderTemplate,@"^[a-z0-9_]{1,512}$") || !Regex.IsMatch(message.Language,@"^[a-z]{2,3}(_[A-Z]{2})?$"))
            throw new ArgumentException("Choose an approved WhatsApp template and language.");
        var request=new HttpRequestMessage(HttpMethod.Post,"https://graph.facebook.com/"+version+"/"+phoneId+"/messages");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",secret);
        request.Content=new StringContent(JsonSerializer.Serialize(new {
            messaging_product="whatsapp",recipient_type="individual",to=message.Destination.TrimStart('+'),type="template",
            template=new {name=message.ProviderTemplate,language=new {code=message.Language},
                components=message.Parameters.Length==0?Array.Empty<object>():new object[]{new {type="body",parameters=message.Parameters.Select(text=>new {type="text",text}).ToArray()}}}
        }),Encoding.UTF8,"application/json");
        return request;
    }
    protected override ProviderResult Parse(JsonElement json)
    {
        if(json.TryGetProperty("messages",out var messages) && messages.GetArrayLength()==1 && messages[0].TryGetProperty("id",out var id))
        {
            string? value=id.GetString();
            if(value is {Length:>0 and <=200} && value.StartsWith("wamid.",StringComparison.Ordinal))return new(DeliveryState.Accepted,value);
        }
        return new(DeliveryState.DeliveryUnknown,Code:"MISSING_PROVIDER_REFERENCE");
    }
}
