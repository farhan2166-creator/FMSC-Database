using System.Diagnostics;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;

namespace FMSC_SMS.Core.Updates;

internal static class UpdateStartupGuard
{
    // Local recovery only: no feed checks, downloads or database access at startup.
    internal static bool AllowStartup()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FMSC_Updater");
        try
        {
            if (Mutex.TryOpenExisting("Global\\FMSCInstallerActivation", out var activation))
            {
                using (activation)
                {
                    bool acquired;
                    try { acquired = activation.WaitOne(0); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new InvalidOperationException("FMSC is being updated. Wait for installation to finish.");
                    activation.ReleaseMutex();
                }
            }
            string journal = Path.Combine(root, "recovery.json");
            if (!File.Exists(journal)) return true;
            ValidateRecoveryStorage(root);
            if (new FileInfo(journal).Length > 32768) throw new InvalidDataException("The update recovery record is invalid. Contact support.");
            if (!RequiresRecovery(File.ReadAllBytes(journal))) return true;
            string launcher = Path.Combine(root, "Launch-FMSC.ps1");
            if (!File.Exists(launcher)) throw new IOException("FMSC needs update recovery. Contact support; do not replace the school database.");
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe")) { UseShellExecute = true };
            start.Arguments = "-NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File \"" + launcher + "\"";
            Process.Start(start)?.Dispose();
            return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or
            JsonException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            ModernMessageDialog.ShowWarning(null, error.Message, "FMSC - Update Recovery");
            return false;
        }
    }

    internal static bool RequiresRecovery(byte[] journal)
    {
        if (journal.Length > 32768) throw new InvalidDataException("The update recovery record is invalid. Contact support.");
        using var record = JsonDocument.Parse(journal);
        if (record.RootElement.ValueKind != JsonValueKind.Object ||
            !record.RootElement.TryGetProperty("Format", out var format) || format.ValueKind != JsonValueKind.String ||
            format.GetString() != "fmsc-recovery/v1" || !record.RootElement.TryGetProperty("Phase", out var phase) ||
            phase.ValueKind != JsonValueKind.String || phase.GetString() is not ("Prepared" or "Migrating" or "Activating" or "Verified" or "Completed"))
            throw new InvalidDataException("The update recovery record is invalid. Contact support.");
        return phase.GetString() != "Completed";
    }

    private static void ValidateRecoveryStorage(string root)
    {
        foreach (string path in new[] { root, Path.Combine(root, "recovery.json"),
            Path.Combine(root, "Launch-FMSC.ps1"), Path.Combine(root, "UpdateRecovery.ps1") })
        {
            FileSystemInfo item = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            for (FileSystemInfo? ancestor = item; ancestor is not null;
                ancestor = ancestor is DirectoryInfo directory ? directory.Parent : ((FileInfo)ancestor).Directory)
                if (ancestor.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException("Linked update recovery files are not supported. Contact support.");
            FileSystemSecurity acl = item is DirectoryInfo folder ? folder.GetAccessControl() : ((FileInfo)item).GetAccessControl();
            static bool Trusted(string sid) => sid is "S-1-5-18" or "S-1-5-32-544";
            if (acl.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !Trusted(owner.Value))
                throw new InvalidDataException("Update recovery files have an untrusted owner. Contact support.");
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow && !Trusted(rule.IdentityReference.Value) &&
                    ((int)rule.FileSystemRights & 0xD0156) != 0)
                    throw new InvalidDataException("Update recovery files permit unsafe modification. Contact support.");
        }
    }
}
