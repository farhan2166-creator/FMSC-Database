using System.Text.Json;

namespace FMSC_SMS.Core;

/// <summary>A named snapshot of one user's report settings.</summary>
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
public sealed record ReportPersistenceResult(bool Succeeded, bool Changed = false,
    string? RecoveryPath = null, Exception? Error = null);

/// <summary>Reads can use defaults; writes must preserve unreadable or corrupt originals.</summary>
internal sealed class ReportSettingsFile(string path, Action<Exception, string>? log = null,
    Action<string, string>? publish = null, Action<string, byte[]>? preserve = null)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private sealed record Snapshot(ReportUserData Data, byte[]? CorruptBytes = null);

    internal ReportUserData Read()
    {
        try { return Load().Data; }
        catch (Exception ex)
        {
            log?.Invoke(ex, "Reading report settings; using defaults without changing the original");
            return new();
        }
    }

    private Snapshot Load()
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (FileNotFoundException) { return new(new()); }
        catch (DirectoryNotFoundException) { return new(new()); }
        try
        {
            // Support earlier UTF-8 files with a BOM while preserving their exact bytes on corruption.
            int offset = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
            var data = JsonSerializer.Deserialize<ReportUserData>(bytes.AsSpan(offset), Options)
                ?? throw new JsonException("Report settings contain no object.");
            Validate(data);
            return new(data);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            log?.Invoke(ex, "Corrupt report settings; defaults are used and the original is retained");
            return new(new(), bytes);
        }
    }

    private static void Validate(ReportUserData data)
    {
        if (data.Preferences is null || data.Layouts is null || data.Views is null ||
            data.Preferences.Any(item => item.Value is null) ||
            data.Layouts.Any(item => item.Value is null || item.Value.Any(column => column is null)) ||
            data.Views.Any(item => item.Value is null || item.Value.Any(view => view is null ||
                string.IsNullOrWhiteSpace(view.Name) || view.State is null || view.State.Any(value => value.Value is null))))
            throw new InvalidDataException("Report settings contain incomplete entries.");
    }

    internal ReportPersistenceResult Update(Func<ReportUserData, bool> change)
    {
        string? recoveryPath = null;
        try
        {
            var snapshot = Load(); // I/O/access errors are never treated as an empty file for writes.
            if (!change(snapshot.Data)) return new(true);
            Validate(snapshot.Data);
            string json = JsonSerializer.Serialize(snapshot.Data, Options);
            if (snapshot.CorruptBytes is { } bytes)
            {
                string candidate = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N") + ".bak";
                if (preserve is not null) preserve(candidate, bytes);
                else Preserve(candidate, bytes);
                recoveryPath = candidate; // Report a backup only after its write and flush completed.
            }
            if (publish is not null) publish(path, json);
            else AtomicTextFile.Write(path, json);
            return new(true, true, recoveryPath);
        }
        catch (Exception ex)
        {
            log?.Invoke(ex, "Report settings were not persisted; the original was not replaced");
            return new(false, RecoveryPath: recoveryPath, Error: ex);
        }
    }

    private static void Preserve(string path, byte[] bytes)
    {
        bool created = false;
        try
        {
            using var backup = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created = true;
            backup.Write(bytes); backup.Flush(flushToDisk: true);
        }
        catch
        {
            if (created)
            {
                try { File.Delete(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { System.Diagnostics.Trace.TraceWarning("Could not remove incomplete report-settings backup: {0}", ex.Message); }
            }
            throw;
        }
    }
}
