using System.Text.Json;
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
/// <summary>A named snapshot of a report's settings that one user saved for themselves.</summary>
public sealed class ReportSavedView
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> State { get; set; } = new();
}
internal sealed class ReportUserData
{
    public Dictionary<string, string> Preferences { get; set; } = new();
    public Dictionary<string, List<string>> Layouts { get; set; } = new();
    public Dictionary<string, List<ReportSavedView>> Views { get; set; } = new();
}
/// <summary>Per-user, per-machine storage for report column layouts and saved views: <c>%AppData%\FMSC_SMS\ReportLayouts\user-{UserId}.json</c>.</summary>
public static class ReportLayoutStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true
    };
    private static string? PathForCurrentUser()
    {
        int userId = CurrentUser.UserId;
        if (userId <= 0)
            return null;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FMSC_SMS",
            "ReportLayouts",
            $"user-{userId}.json");
    }
    private static ReportUserData Load()
    {
        string? path = PathForCurrentUser();
        if (path is null || !File.Exists(path))
            return new ReportUserData();
        try
        {
            return JsonSerializer.Deserialize<ReportUserData>(File.ReadAllText(path), JsonOptions)
                   ?? new ReportUserData();
        }
        catch (Exception ex)
        {
            AppExceptionHandler.LogNonFatal(ex, "Reading the saved report layouts - defaults are used instead");
            return new ReportUserData();
        }
    }
    private static void Save(ReportUserData data)
    {
        string? path = PathForCurrentUser();
        if (path is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppExceptionHandler.LogNonFatal(ex, "Saving the report layouts - the choice applies to this session only");
        }
    }
    internal static string? GetPreference(string key)
    {
        lock (Gate) return Load().Preferences.GetValueOrDefault(key);
    }
    internal static void SetPreference(string key, string value)
    {
        lock (Gate) {
            var data = Load(); data.Preferences[key] = value; Save(data);
        }
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
    public static void SetColumns(string layoutKey, IEnumerable<string> keys)
    {
        lock (Gate)
        {
            var data = Load();
            data.Layouts[layoutKey] = keys.ToList();
            Save(data);
        }
    }
    /// <summary>Forgets the user's choice so the report's own default columns apply again.</summary>
    public static void ClearColumns(string layoutKey)
    {
        lock (Gate)
        {
            var data = Load();
            if (data.Layouts.Remove(layoutKey))
                Save(data);
        }
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
    public static void SaveView(string pageKey, ReportSavedView view)
    {
        lock (Gate)
        {
            var data = Load();
            if (!data.Views.TryGetValue(pageKey, out var views))
                data.Views[pageKey] = views = new List<ReportSavedView>();
            views.RemoveAll(v => string.Equals(v.Name, view.Name, StringComparison.CurrentCultureIgnoreCase));
            views.Add(view);
            Save(data);
        }
    }
    public static bool DeleteView(string pageKey, string name)
    {
        lock (Gate)
        {
            var data = Load();
            if (!data.Views.TryGetValue(pageKey, out var views))
                return false;
            bool removed = views.RemoveAll(v => string.Equals(v.Name, name, StringComparison.CurrentCultureIgnoreCase)) > 0;
            if (removed)
                Save(data);
            return removed;
        }
    }
}

