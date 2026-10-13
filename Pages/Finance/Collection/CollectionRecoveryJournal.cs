using System.Text.Json;

namespace FMSC_SMS.Pages.Finance.Collection;

// A write intent is flushed before collecting and retained until commit or rollback
// is confirmed. Closing a page or restarting the app must not forget an unknown payment.
internal sealed class CollectionRecoveryJournal : IDisposable
{
    internal sealed record Pending(Guid Id, string Summary, DateTime StartedUtc);
    private static readonly Mutex Gate = new(false, @"Local\FMSC_SMS.CollectionRecovery");
    internal static string JournalPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FMSC_SMS", "PendingCollection.json");
    private readonly string path;
    private FileStream? stream;
    private readonly Guid id;
    private bool completed;

    private CollectionRecoveryJournal(string path, FileStream stream, Guid id)
    { this.path = path; this.stream = stream; this.id = id; }

    internal static CollectionRecoveryJournal Begin(string summary, string? testPath = null) => Locked(() => {
        string path = testPath ?? JournalPath;
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FileStream file;
        try { file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) when (File.Exists(path)) {
            throw new CollectionRecoveryException("A previous collection still needs review, or another window is recording a payment. Check Payment History before collecting again.", ex);
        }
        try {
            Guid id = Guid.NewGuid();
            JsonSerializer.Serialize(file, new Pending(id, summary, DateTime.UtcNow));
            file.Flush(flushToDisk: true);
            return new CollectionRecoveryJournal(path, file, id);
        }
        catch { file.Dispose(); File.Delete(path); throw; } // No database write has started.
    });

    internal static Pending? ReadPending(string? testPath = null) => Locked(() => {
        string path = testPath ?? JournalPath;
        if (!File.Exists(path)) return null;
        try {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            var pending = JsonSerializer.Deserialize<Pending>(file)
                ?? throw new CollectionRecoveryException("The previous collection's recovery record cannot be read. Contact the administrator before collecting again.");
            if (pending.Id == Guid.Empty || string.IsNullOrWhiteSpace(pending.Summary) || pending.StartedUtc == default)
                throw new CollectionRecoveryException("The previous collection's recovery record is incomplete. Contact the administrator before collecting again; do not repeat the payment.");
            return pending;
        }
        catch (IOException ex) {
            throw new CollectionRecoveryException("Another window is still recording a collection. Wait for its result before collecting again.", ex);
        }
        catch (JsonException ex) {
            throw new CollectionRecoveryException("The previous collection's recovery record is damaged. Contact the administrator before collecting again.", ex);
        }
    });

    internal static void ClearReviewed(Guid expectedId, string? testPath = null) => Locked(() => {
        string path = testPath ?? JournalPath;
        var pending = ReadPending(path);
        if (pending?.Id != expectedId)
            throw new CollectionRecoveryException("The pending collection changed. Review it again before clearing its recovery record.");
        File.Delete(path);
        return true;
    });

    internal void Complete() => Locked(() => {
        if (completed) return true;
        stream?.Dispose(); stream = null;
        var pending = ReadPending(path);
        if (pending is not null && pending.Id != id)
            throw new CollectionRecoveryException("The collection recovery record changed. Its payment must be reviewed before clearing it.");
        File.Delete(path);
        completed = true;
        return true;
    });

    // Disposing without Complete deliberately preserves the intent for recovery.
    public void Dispose() { stream?.Dispose(); stream = null; }

    private static T Locked<T>(Func<T> action)
    {
        try { Gate.WaitOne(); }
        catch (AbandonedMutexException) { /* The previous process died; inspect its retained intent. */ }
        try { return action(); }
        finally { Gate.ReleaseMutex(); }
    }
}

internal sealed class CollectionRecoveryException(string message, Exception? inner = null)
    : Exception(message, inner);

internal sealed class CollectionOutcomeUnknownException(Exception inner)
    : Exception("The collection result could not be confirmed. It may already have been recorded. Check Payment History and the receipt before collecting again; do not repeat the payment.", inner);
