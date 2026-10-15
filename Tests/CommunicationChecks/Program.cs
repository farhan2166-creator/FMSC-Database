using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using FMSC_SMS.Services.Communication;
using FMSC_SMS.Core;

int count=0;
void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);count++;}
void Refused(Action action,string name){try{action();}catch(ArgumentException){count++;return;}throw new InvalidOperationException(name);}
foreach(string number in new[]{"03001234567","923001234567","+92 300 1234567","00923001234567"})Check(CommunicationPhone.Normalize(number)=="+923001234567","Pakistan prefixes normalize without changing subscriber identity");
foreach(string invalid in new[]{"3001234567","0300","+9203001234567","+923001234567/+923001234567","++923001234567","(03001234567","+922112345678"})Refused(()=>CommunicationPhone.Normalize(invalid),"Ambiguous, multiple, incomplete and landline destinations are rejected");
Check(SmsSegments.Estimate(new string('a',160)).Segments==1 && SmsSegments.Estimate(new string('a',161)).Segments==2,"GSM7 boundaries");
Check(SmsSegments.Estimate(new string('^',81)).Segments==2,"GSM extension characters count twice");
Check(SmsSegments.Estimate(new string('ا',70)).Segments==1 && SmsSegments.Estimate(new string('ا',71)).Segments==2,"Urdu Unicode boundaries");
Check(SmsSegments.Estimate(string.Concat(Enumerable.Repeat("😀",36))).Segments==2,"Supplementary Unicode uses UTF16 units");
Refused(()=>MessageTemplateService.Validate("{{arbitrary.sql}}"),"Unrestricted placeholders are rejected");
Check(MessageTemplateService.Render("{{student}}: {{amount}}",new Dictionary<string,string>{{"student","Fictional child"},{"amount","500"}})=="Fictional child: 500","Whitelisted personalization");
Check(PermissionCatalog.IsSupportedCatalog(PermissionCatalog.All.Keys),"Existing permission catalogs remain valid without installing communication");
Check(PermissionCatalog.IsSupportedCatalog(PermissionCatalog.All.Keys.Concat(PermissionCatalog.OptionalCommunication)),"The complete reviewed optional permission catalog is supported");
Check(!PermissionCatalog.IsSupportedCatalog(PermissionCatalog.All.Keys.Concat(PermissionCatalog.OptionalCommunication.Take(1))),"Partially installed communication permissions are refused");
Check(!PermissionCatalog.IsSupportedCatalog(PermissionCatalog.All.Keys.Skip(1)),"Missing existing permissions remain a hard failure");
Check(!PermissionCatalog.IsSupportedCatalog(PermissionCatalog.All.Keys.Append("Unknown.Permission")),"Unknown authority cannot pass catalog validation");
var envelope=new MessageEnvelope(Guid.NewGuid(),Guid.NewGuid(),MessageChannel.Sms,"+923001234567","السلام علیکم","approved_notice","ur",["فرضی طالب علم"],1,false);
string account="AC"+new string('1',32);string secret=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
var handler=new FixtureHandler();using var http=new HttpClient(handler);
var sms=new TwilioSmsProvider(http,account,secret,"FMSC");
handler.Response=()=>new(HttpStatusCode.Created){Content=new StringContent(JsonSerializer.Serialize(new{sid="SM"+new string('2',32),status="queued"}))};
var accepted=await sms.SubmitAsync(envelope,default);
Check(accepted.State==DeliveryState.Accepted && handler.Uri!.Host=="api.twilio.com" && handler.Content.Contains("To=%2B923001234567"),"Twilio authenticates a scoped official API submission and records acceptance only");
Check(handler.Authorization=="Basic" && Uri.UnescapeDataString(handler.Content.Replace("+"," ")).Contains(envelope.Body),"Urdu survives authenticated form encoding");
handler.Response=()=>new(HttpStatusCode.TooManyRequests);var limited=await sms.SubmitAsync(envelope,default);
Check(limited.RetrySafe && limited.State==DeliveryState.Rejected,"Only explicit nonacceptance permits a bounded retry");
handler.Response=()=>new(HttpStatusCode.InternalServerError);Check((await sms.SubmitAsync(envelope,default)).State==DeliveryState.DeliveryUnknown,"Server errors do not prove that a submission failed");
handler.Throw=true;Check((await sms.SubmitAsync(envelope,default)).State==DeliveryState.DeliveryUnknown,"Lost acknowledgements are retained for reconciliation");handler.Throw=false;
handler.Response=()=>new(HttpStatusCode.Unauthorized);Check((await sms.SubmitAsync(envelope,default)).State==DeliveryState.Rejected,"Invalid credentials are a permanent refusal");
handler.Response=()=>new(HttpStatusCode.Created){Content=new StringContent("not JSON")};Check((await sms.SubmitAsync(envelope,default)).State==DeliveryState.DeliveryUnknown,"Corrupt acknowledgements never cause automatic replay");
var wa=new WhatsAppCloudProvider(http,"123456789",secret,"v23.0");
handler.Response=()=>new(HttpStatusCode.OK){Content=new StringContent("{\"messages\":[{\"id\":\"wamid.synthetic\"}]}")};
var result=await wa.SubmitAsync(envelope with{Channel=MessageChannel.WhatsApp},default);
using(var json=JsonDocument.Parse(handler.Content))Check(result.State==DeliveryState.Accepted && handler.Uri!.Host=="graph.facebook.com" && json.RootElement.GetProperty("type").GetString()=="template" && json.RootElement.GetProperty("template").GetProperty("language").GetProperty("code").GetString()=="ur","Official WhatsApp request uses the approved template and language");
handler.Response=()=>new(HttpStatusCode.BadRequest);Check((await wa.SubmitAsync(envelope with{Channel=MessageChannel.WhatsApp},default)).State==DeliveryState.Rejected,"WhatsApp template refusal remains rejected");
Check(!DeliveryStatusService.CanAdvance(DeliveryState.Delivered,DeliveryState.Sent) && DeliveryStatusService.CanAdvance(DeliveryState.Delivered,DeliveryState.Read),"Delivery callbacks cannot regress or duplicate terminal state");
Check(!DeliveryStatusService.CanAdvance(DeliveryState.Queued,DeliveryState.Delivered) && !DeliveryStatusService.CanAdvance(DeliveryState.Failed,DeliveryState.Sent),"Unsubmitted and terminal failures cannot advance through unrelated callbacks");
byte[] body=Encoding.UTF8.GetBytes("synthetic callback");string signature="sha256="+Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),body));
Check(DeliveryStatusService.VerifyMetaSignature(body,signature,secret) && !DeliveryStatusService.VerifyMetaSignature(Encoding.UTF8.GetBytes("altered"),signature,secret),"Meta signatures bind the exact request bytes");
handler.Response=()=>{var response=new HttpResponseMessage(HttpStatusCode.TooManyRequests);response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(240));return response;};
Check((await sms.SubmitAsync(envelope,default)).RetryAfterSeconds==240,"Provider Retry-After is honored without an immediate replay");
handler.Response=()=>new(HttpStatusCode.OK){Content=new StringContent(new string('x',70000))};
Check((await sms.SubmitAsync(envelope,default)).State==DeliveryState.DeliveryUnknown,"Oversized provider responses are bounded and remain uncertain");
byte[] Callback(string owner,string phone,long stamp)=>JsonSerializer.SerializeToUtf8Bytes(new{ @object="whatsapp_business_account",entry=new[]{new{id=owner,changes=new[]{new{value=new{metadata=new{phone_number_id=phone},statuses=new[]{new{id="wamid.synthetic",status="delivered",timestamp=stamp.ToString()}}}}}}}});
string Sign(byte[] bytes)=>"sha256="+Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),bytes));
var now=DateTimeOffset.UtcNow;var callback=Callback("account","phone",now.ToUnixTimeSeconds());
Check(MetaCallbackVerifier.Validate(callback,Sign(callback),secret,"account","phone",now),"A signed scoped timely callback is accepted");
Check(!MetaCallbackVerifier.Validate(callback,Sign(callback),secret,"other","phone",now),"A valid signature cannot cross business-account boundaries");
var stale=Callback("account","phone",now.AddDays(-8).ToUnixTimeSeconds());Check(!MetaCallbackVerifier.Validate(stale,Sign(stale),secret,"account","phone",now),"Stale callbacks are refused before SQL access");
Check(!MetaCallbackVerifier.Validate(callback,"sha256=invalid",secret,"account","phone",now),"Malformed signatures are rejected");
var simulation=new SimulationProvider(MessageChannel.Sms);Check((await simulation.SubmitAsync(envelope with{Simulation=true},default)).Code=="SIMULATED_NO_NETWORK","Simulation has an explicit no-network result");
Console.WriteLine($"PASS: {count} communication provider, phone, Unicode, template and recovery checks. No external request was made.");

sealed class FixtureHandler:HttpMessageHandler
{
 internal Func<HttpResponseMessage> Response=()=>new(HttpStatusCode.OK);internal bool Throw;
 internal string Content="",Authorization="";internal Uri? Uri;
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
 {Uri=request.RequestUri;Authorization=request.Headers.Authorization?.Scheme??"";Content=request.Content is null?"":await request.Content.ReadAsStringAsync(token);if(Throw)throw new HttpRequestException("Synthetic lost acknowledgement");return Response();}
}
