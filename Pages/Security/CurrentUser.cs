namespace FMSC_SMS.Pages.Security;
public static class CurrentUser
{
    public static int UserId { get; private set; }
    public static string Username { get; internal set; } = "";
    public static string RoleName { get; private set; } = "";
    public static bool IsAdministrator { get; private set; }
    public static bool MustChangePassword { get; internal set; }
    private static readonly HashSet<string> Permissions =
        new(StringComparer.OrdinalIgnoreCase);
    public static void SignIn(int userId, string username, string roleName, bool mustChange, IEnumerable<string> permissions)
    {
        FMSC_SMS.Core.LookupDataCache.Invalidate();
        FMSC_SMS.Core.StudentSearchCache.Invalidate();
        UserId = userId;
        Username = username;
        RoleName = roleName;
        // Owner policy: any Administrator membership has full authority, matching dbo.RequirePermission.
        IsAdministrator = roleName
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(role => role.Equals("Administrator", StringComparison.OrdinalIgnoreCase));
        MustChangePassword = mustChange;
        Permissions.Clear();
        foreach (var permission in permissions)
        {
            if (!string.IsNullOrWhiteSpace(permission))
                Permissions.Add(permission);
        }
    }
    public static bool HasPermission(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;
        return IsAdministrator
            || Permissions.Contains(code);
    }
    public static IReadOnlyCollection<string> GetPermissions()
        => Permissions;
    public static void SignOut()
    {
        FMSC_SMS.Core.LookupDataCache.Invalidate();
        FMSC_SMS.Core.StudentSearchCache.Invalidate();
        FMSC_SMS.Core.UiTextWidthCache.Clear();
        UserId = 0;
        Username = "";
        RoleName = "";
        IsAdministrator = false;
        MustChangePassword = false;
        Permissions.Clear();
    }
}
