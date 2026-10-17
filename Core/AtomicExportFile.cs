namespace FMSC_SMS.Core;

/// <summary>Build and verify a complete export before replacing a user's existing file.</summary>
internal static class AtomicExportFile
{
    internal static void Publish(string path, Action<string> generate, Action<string>? validate = null)
    {
        string destination = Path.GetFullPath(path);
        string folder = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        bool created = false;
        try
        {
            using (var reserved = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { created = true; }
            generate(temporary);
            using (var output = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                if (output.Length == 0) throw new InvalidDataException("The export produced an empty file. The previous file was not replaced.");
                output.Flush(flushToDisk: true);
            }
            validate?.Invoke(temporary);
            // Same-directory rename: no truncation/copy fallback if replacement is refused.
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (created)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { System.Diagnostics.Trace.TraceWarning("Could not remove temporary export: {0}", ex.Message); }
            }
        }
    }
}
