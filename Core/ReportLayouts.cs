namespace FMSC_SMS.Core;
/// <summary>How a report column is shown and exported.</summary>
public enum ReportColumnKind
{
    Text,
    Number,
    Money,
    Date
}
/// <summary>One column a report can show.</summary>
public sealed record ReportColumnDef(
    string Key,
    string Header,
    string Group = "",
    bool DefaultVisible = true,
    ReportColumnKind Kind = ReportColumnKind.Text);
/// <summary>Per-user, per-machine storage for report column layouts and saved views: <c>%AppData%\FMSC_SMS\ReportLayouts\user-{UserId}.json</c>.</summary>
public static class ReportLayoutStore
{
    private static readonly object Gate = new();
    private static string? PathForCurrentUser()
    {
        int userId = CurrentUser.UserId;
        if (userId <= 0)
            return null;
        return Path.Combine(
#if FMSC_LEVEL2
            FMSC_SMS.Core.Level2Target.Folder(Environment.SpecialFolder.ApplicationData),
#else
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
#endif
            "FMSC_SMS",
            "ReportLayouts",
            $"user-{userId}.json");
    }
    private static ReportSettingsFile? CurrentFile() => PathForCurrentUser() is { } path
        ? new ReportSettingsFile(path, AppExceptionHandler.LogNonFatal) : null;
    private static ReportUserData Load() => CurrentFile()?.Read() ?? new();
    private static ReportPersistenceResult Update(Func<ReportUserData, bool> change) => CurrentFile()?.Update(change)
        ?? new(false, Error: new InvalidOperationException("Sign in before saving report settings."));
    internal static string? GetPreference(string key)
    {
        lock (Gate) return Load().Preferences.GetValueOrDefault(key);
    }
    internal static ReportPersistenceResult SetPreference(string key, string value)
    {
        lock (Gate) return Update(data => { data.Preferences[key] = value; return true; });
    }
    /// <summary>The columns this user last chose for a layout, in order, or null when they never chose.</summary>
    public static IReadOnlyList<string>? GetColumns(string layoutKey)
    {
        lock (Gate)
        {
            return Load().Layouts.TryGetValue(layoutKey, out var keys) && keys.Count > 0
                ? keys.ToList()
                : null;
        }
    }
    public static ReportPersistenceResult SetColumns(string layoutKey, IEnumerable<string> keys)
    {
        lock (Gate) return Update(data => { data.Layouts[layoutKey] = keys.ToList(); return true; });
    }
    /// <summary>Forgets the user's choice so the report's own default columns apply again.</summary>
    public static ReportPersistenceResult ClearColumns(string layoutKey)
    {
        lock (Gate) return Update(data => data.Layouts.Remove(layoutKey));
    }
    public static IReadOnlyList<ReportSavedView> GetViews(string pageKey)
    {
        lock (Gate)
        {
            return Load().Views.TryGetValue(pageKey, out var views)
                ? views.OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).ToList()
                : new List<ReportSavedView>();
        }
    }
    /// <summary>Saves a view, replacing the one of the same name (case-insensitive) if it exists.</summary>
    public static ReportPersistenceResult SaveView(string pageKey, ReportSavedView view)
    {
        lock (Gate) return Update(data => {
            if (!data.Views.TryGetValue(pageKey, out var views))
                data.Views[pageKey] = views = new List<ReportSavedView>();
            views.RemoveAll(v => string.Equals(v.Name, view.Name, StringComparison.CurrentCultureIgnoreCase));
            views.Add(view);
            return true;
        });
    }
    public static ReportPersistenceResult DeleteView(string pageKey, string name)
    {
        lock (Gate) return Update(data => data.Views.TryGetValue(pageKey, out var views) &&
            views.RemoveAll(v => string.Equals(v.Name, name, StringComparison.CurrentCultureIgnoreCase)) > 0);
    }
}
