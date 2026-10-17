using System.Text.Json;

namespace FMSC_SMS.Services.Communication;

public static class MetaCallbackVerifier
{
    public static bool Validate(byte[] body,string signature,string secret,string businessAccount,string phoneId,DateTimeOffset now)
    {
        if(body.Length>65536 || string.IsNullOrEmpty(secret) || !DeliveryStatusService.VerifyMetaSignature(body,signature,secret))return false;
        try
        {
            using var document=JsonDocument.Parse(body,new JsonDocumentOptions{MaxDepth=24});
            var root=document.RootElement;
            if(root.GetProperty("object").GetString()!="whatsapp_business_account")return false;
            int events=0;
            foreach(var entry in root.GetProperty("entry").EnumerateArray())
            {
                if(entry.GetProperty("id").GetString()!=businessAccount)return false;
                foreach(var change in entry.GetProperty("changes").EnumerateArray())
                {
                    var value=change.GetProperty("value");
                    if(value.GetProperty("metadata").GetProperty("phone_number_id").GetString()!=phoneId)return false;
                    foreach(string collection in new[]{"statuses","messages"})
                    {
                        if(!value.TryGetProperty(collection,out var rows))continue;
                        foreach(var row in rows.EnumerateArray())
                        {
                            if(++events>100)return false;
                            if(!long.TryParse(row.GetProperty("timestamp").GetString(),out long stamp))return false;
                            var age=now-DateTimeOffset.FromUnixTimeSeconds(stamp);
                            if(age>TimeSpan.FromDays(7)||age<TimeSpan.FromMinutes(-5))return false;
                        }
                    }
                }
            }
            return events>0;
        }
        catch(Exception ex) when(ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentOutOfRangeException){return false;}
    }
}
