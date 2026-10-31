namespace FMSC_SMS.Core;

internal static class SchoolContactValidation
{
    internal const string PhoneMessage = "Enter full Pakistan phone numbers separated by commas.";
    internal const string EmailMessage = "Enter a valid email address, such as name@example.com.";
    internal static bool IsPhoneCharacter(char value) => value is >= '0' and <= '9' or '+' or '-' or ' ' or '(' or ')' or ',';
    internal static bool IsValidPhoneList(string? text) => string.IsNullOrWhiteSpace(text) ||
        text.Split(',').All(number => !string.IsNullOrWhiteSpace(number) &&
            number.All(IsPhoneCharacter) && PakistanPhoneNumber.IsValidOptional(number));
}
