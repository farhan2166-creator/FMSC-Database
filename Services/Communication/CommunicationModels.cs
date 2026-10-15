using System.Text.Json;
using System.Text.RegularExpressions;

namespace FMSC_SMS.Services.Communication;

public enum MessageChannel { Sms, WhatsApp }
public enum DeliveryState { Draft, AwaitingApproval, Scheduled, Queued, Submitting, Accepted, Sent, Delivered, Read, Failed, Rejected, DeliveryUnknown, Cancelled }
public sealed record MessageEnvelope(Guid Id, Guid ClaimToken, MessageChannel Channel, string Destination, string Body,
    string ProviderTemplate, string Language, string[] Parameters, int Attempt, bool Simulation);
public sealed record ProviderResult(DeliveryState State, string? Reference = null, string Code = "", bool RetrySafe = false, int RetryAfterSeconds = 0);
public interface IMessageProvider
{
    string Name { get; }
    MessageChannel Channel { get; }
    Task<ProviderResult> SubmitAsync(MessageEnvelope message, CancellationToken token);
    Task<ProviderResult> QueryAsync(string reference, CancellationToken token);
}
public static class CommunicationPhone
{
    public static string Normalize(string input, string countryCode = "+92")
    {
        if (!Regex.IsMatch(countryCode, @"^\+[1-9][0-9]{0,2}$")) throw new ArgumentException("Choose a valid default country code.");
        string text = input.Trim();
        if (text.Length == 0 || text.Any(c => !(c is >= '0' and <= '9' or '+' or ' ' or '-' or '(' or ')')) ||
            text.Count(c => c == '+') > 1 || text.Contains('+') && !text.StartsWith('+'))
            throw new ArgumentException("Enter one complete phone number.");
        int brackets = 0;
        foreach (char c in text) { if(c=='(' && ++brackets>1 || c==')' && --brackets<0) throw new ArgumentException("Unbalanced phone brackets."); }
        if(brackets!=0) throw new ArgumentException("Unbalanced phone brackets.");
        string digits = string.Concat(text.Where(c => c is >= '0' and <= '9'));
        if (countryCode == "+92")
        {
            if (text.StartsWith('+')) { if (!digits.StartsWith("92")) throw new ArgumentException("This contact requires a Pakistan mobile number."); digits = digits[2..]; }
            else if (digits.StartsWith("0092")) digits = digits[4..];
            else if (digits.StartsWith("92")) digits = digits[2..];
            else if (digits.StartsWith("03")) digits = digits[1..];
            else throw new ArgumentException("Use 03…, 92… or +92…; an unqualified number is ambiguous.");
            if (!Regex.IsMatch(digits, @"^3[0-6][0-9]{8}$")) throw new ArgumentException("Enter a full Pakistan mobile number; landlines cannot receive SMS.");
            return "+92" + digits;
        }
        if(!text.StartsWith('+') || !Regex.IsMatch(digits,@"^[1-9][0-9]{7,14}$"))
            throw new ArgumentException("For other countries, enter the full +country-code number.");
        return "+"+digits;
    }
}
public static class MessageTemplateService
{
    public static readonly string[] Fields = ["school", "student", "class", "year", "amount", "receipt", "dueDate", "announcement"];
    public static void Validate(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > 2000) throw new ArgumentException("Template text must contain 1–2000 characters.");
        string remaining = body;
        foreach (string field in Fields) remaining = remaining.Replace("{{"+field+"}}", "", StringComparison.Ordinal);
        if (remaining.Contains("{{") || remaining.Contains("}}")) throw new ArgumentException("Only the documented template fields are allowed.");
    }
    public static string Render(string body, IReadOnlyDictionary<string,string> values)
    {
        Validate(body);
        foreach (string field in Fields)
            if (body.Contains("{{"+field+"}}",StringComparison.Ordinal))
                body = body.Replace("{{"+field+"}}", values.TryGetValue(field,out var value) ? value : throw new ArgumentException("A required template value is missing."),StringComparison.Ordinal);
        return body;
    }
}
public sealed record SmsEstimate(string Encoding, int Units, int Segments);
public static class SmsSegments
{
    public const string Basic = "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";
    public const string Extension = "\f^{}\\[~]|€";
    public static SmsEstimate Estimate(string text)
    {
        bool gsm = text.All(c => Basic.Contains(c) || Extension.Contains(c));
        int units = gsm ? text.Sum(c => Extension.Contains(c) ? 2 : 1) : text.Length;
        int single = gsm ? 160 : 70, multipart = gsm ? 153 : 67;
        return new(gsm ? "GSM-7" : "Unicode UTF-16", units, units <= single ? 1 : (units+multipart-1)/multipart);
    }
}
public static class DeliveryStatusService
{
    public static bool CanAdvance(DeliveryState current, DeliveryState next)
    {
        if (current == next) return false;
        if (current is DeliveryState.Delivered or DeliveryState.Read) return current == DeliveryState.Delivered && next == DeliveryState.Read;
        if (next is DeliveryState.Draft or DeliveryState.Queued or DeliveryState.Submitting or DeliveryState.Cancelled) return false;
        if (next is DeliveryState.Failed or DeliveryState.Rejected) return current is DeliveryState.Accepted or DeliveryState.Sent or DeliveryState.DeliveryUnknown;
        int Rank(DeliveryState s) => s switch { DeliveryState.Accepted => 1, DeliveryState.Sent => 2, DeliveryState.Delivered => 3, DeliveryState.Read => 4, _ => 0 };
        return Rank(next)>Rank(current);
    }
    public static bool VerifyMetaSignature(byte[] body, string signature, string appSecret)
    {
        if(!signature.StartsWith("sha256=",StringComparison.Ordinal))return false;
        try { return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature[7..]),
            System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(appSecret),body)); }
        catch(FormatException){return false;}
    }
}
public sealed class SimulationProvider(MessageChannel channel) : IMessageProvider
{
    public string Name => "Simulation";
    public MessageChannel Channel => channel;
    public Task<ProviderResult> SubmitAsync(MessageEnvelope message,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!message.Simulation) throw new InvalidOperationException("Simulation cannot masquerade as live delivery.");
        return Task.FromResult(new ProviderResult(DeliveryState.Accepted,"simulation-"+message.Id.ToString("N"),"SIMULATED_NO_NETWORK"));
    }
    public Task<ProviderResult> QueryAsync(string reference,CancellationToken token) => Task.FromResult(new ProviderResult(DeliveryState.Accepted,reference,"SIMULATED_NO_DELIVERY"));
}
