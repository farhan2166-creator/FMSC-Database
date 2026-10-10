using System.Net.Mail;
namespace FMSC_SMS.Core;
/// <summary>Email syntax checking for the two optional email fields in the application (School Profile and the SMTP "From Address"). of the release-readiness register.</summary>
internal static class EmailAddressValidator
{
    /// <summary>True when a value is either left blank or is a usable address.</summary>
    public static bool IsBlankOrValid(string? value) =>
        string.IsNullOrWhiteSpace(value) || IsValid(value);
    /// <summary>False for blank. See the class comment for the exact rule.</summary>
    public static bool IsValid(string? value)
    {
        string address = (value ?? string.Empty).Trim();
        if (address.Length == 0)
            return false;
        // SMTP limits. The database column limits are still authoritative.
        if (address.Length > 254)
            return false;
        // A display-name form such as Name <a@b.com> is not what these fields ask for, and whitespace is never valid inside an address.
        if (address.Any(char.IsWhiteSpace))
            return false;
        int at = address.IndexOf('@');
        if (at < 0 || at != address.LastIndexOf('@'))
            return false;
        string local = address[..at];
        string domain = address[(at + 1)..];
        if (local.Length == 0 || local.Length > 64)
            return false;
        // A dot may separate words in the local part but cannot lead, trail or double up.
        if (local.StartsWith('.') || local.EndsWith('.') || local.Contains(".."))
            return false;
        if (domain.Length == 0)
            return false;
        // The domain must be dotted and its labels must be non-empty, so "a.@b.com", "a@.b.com" and "a@b..com" are all out.
        if (!domain.Contains('.'))
            return false;
        if (domain.StartsWith('.') || domain.EndsWith('.') || domain.Contains(".."))
            return false;
        foreach (string label in domain.Split('.'))
        {
            if (label.Length == 0 || label.Length > 63)
                return false;
        }
        // A single-character top-level domain is always a typo rather than a real TLD, and "abc@x" is the case the register asks to be defined.
        string tld = domain[(domain.LastIndexOf('.') + 1)..];
        if (tld.Length < 2)
            return false;
        // The framework's own parser catches the remaining structural cases (quoted strings, angle brackets, illegal characters) so this does not have to enumerate them.
        if (!MailAddress.TryCreate(address, out _))
            return false;
        return true;
    }
}
