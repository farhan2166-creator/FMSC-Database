namespace FMSC_SMS.Core;

/// <summary>One password-creation policy shared by every account screen.</summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 12;
    public static string Requirement =>
        $"Use at least {MinimumLength} characters (spaces alone are not allowed)";

    public static bool IsValid(string? password) =>
        password is not null &&
        password.Length >= MinimumLength &&
        !string.IsNullOrWhiteSpace(password);
}
