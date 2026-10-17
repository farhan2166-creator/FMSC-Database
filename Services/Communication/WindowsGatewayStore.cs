using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FMSC_SMS.Core;

namespace FMSC_SMS.Services.Communication;

public sealed record GatewayCredentials(string AccountOrPhoneId,string Secret,string Sender,string GraphVersion);
public sealed class WindowsGatewayStore
{
    private static readonly byte[] Entropy=Encoding.UTF8.GetBytes("FMSC_SMS.Communication.Gateway.v1");
#if FMSC_LEVEL2
    private readonly string root=Path.Combine(FMSC_SMS.Core.Level2Target.Folder(Environment.SpecialFolder.LocalApplicationData),"FMSC_SMS","Communication");
#else
    private readonly string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FMSC_SMS","Communication");
#endif
    private string PathFor(MessageChannel channel)=>Path.Combine(root,channel+".protected");
    public bool IsConfigured(MessageChannel channel)=>File.Exists(PathFor(channel));
    public void Save(MessageChannel channel,GatewayCredentials credentials)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Gateway secrets require Windows user protection.");
        byte[] plain=JsonSerializer.SerializeToUtf8Bytes(credentials);
        try{byte[] encrypted=ProtectedData.Protect(plain,Entropy,DataProtectionScope.CurrentUser);
            AtomicTextFile.Write(PathFor(channel),Convert.ToBase64String(encrypted));}
        finally{CryptographicOperations.ZeroMemory(plain);}
    }
    public IMessageProvider Provider(MessageChannel channel,HttpClient http)
    {
        #if FMSC_LEVEL2
        throw new InvalidOperationException("Live gateways are disabled in Level 2.");
#else
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")=="true")throw new InvalidOperationException("Live gateways are disabled in CI.");
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        byte[] plain=ProtectedData.Unprotect(Convert.FromBase64String(File.ReadAllText(PathFor(channel))),Entropy,DataProtectionScope.CurrentUser);
        try
        {
            var c=JsonSerializer.Deserialize<GatewayCredentials>(plain)??throw new InvalidOperationException("Gateway configuration is missing.");
            return channel==MessageChannel.Sms?new TwilioSmsProvider(http,c.AccountOrPhoneId,c.Secret,c.Sender):new WhatsAppCloudProvider(http,c.AccountOrPhoneId,c.Secret,c.GraphVersion);
        }
        finally{CryptographicOperations.ZeroMemory(plain);}
#endif
    }
}
