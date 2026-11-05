using System.Diagnostics;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace FMSC_SMS.Core.Updates;

internal static class UpdateInstallerHandoff
{
    internal static void Validate(PreparedUpdate prepared)
    {
        var manifest = UpdateManifestPolicy.ReadValidated(File.ReadAllBytes(
            Path.Combine(prepared.ManifestDirectory, "update-manifest.json")));
        if (manifest.Version != prepared.Version) throw new InvalidDataException("Prepared version changed.");
        if (!manifest.Files.Any(file => file.Path.StartsWith("UpdateSupport/Migrations/", StringComparison.Ordinal) &&
            file.Path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The update does not include numbered installer migrations.");
        var entries = Directory.GetFileSystemEntries(prepared.StageDirectory, "*", SearchOption.AllDirectories);
        if (entries.Any(path => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)))
            throw new InvalidDataException("Links are forbidden in prepared updates.");
        if (entries.Count(File.Exists) != manifest.Files.Count)
            throw new InvalidDataException("Prepared application contains unlisted files.");
        foreach (var file in manifest.Files)
        {
            if (file.Path.EndsWith(".mdf", StringComparison.OrdinalIgnoreCase) || file.Path.EndsWith(".ldf", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Database files are forbidden in updates.");
            string path = Path.Combine(prepared.StageDirectory, file.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path) || new FileInfo(path).Length != file.Length ||
                !UpdateChunks.Sha256(path).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Prepared application changed: " + file.Path);
        }
        string installer = File.ReadAllText(Path.Combine(prepared.StageDirectory, "UpdateSupport", "core.ps1"));
        string version = Regex.Match(installer, @"(?m)^\$AppVersion = '([^']+)'\r?$").Groups[1].Value;
        string schema = Regex.Match(installer, @"(?m)^\$DatabaseVersion = '([0-9]+)'\r?$").Groups[1].Value;
        if (version != manifest.Version || schema != manifest.RequiredDatabaseSchema.ToString())
            throw new InvalidDataException("Update installer does not match the package version/schema.");
    }

    internal static void Launch(PreparedUpdate prepared)
    {
#if FMSC_LEVEL2
        throw new InvalidOperationException("Disposable test builds cannot activate school updates.");
#else
        Validate(prepared);
        if (!Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory).Equals(
            @"C:\FMSC School Management", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Automatic update installation supports the existing standard installation folder only.");
        using var original = Process.GetCurrentProcess();
        string script = Path.Combine(prepared.StageDirectory, "UpdateSupport", "core.ps1");
        string root = Directory.GetParent(prepared.StageDirectory)!.FullName;
        // Windows PowerShell joins ArgumentList for shell execution. Reject quotes
        // and use quoted paths; no command string or expression is evaluated.
        if ((script + root).Contains('"')) throw new InvalidDataException("Invalid update directory.");
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe")) { UseShellExecute = true, Verb = "runas" };
        start.Arguments = "-NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File \"" + script + "\" -PreparedRoot \"" + root +
            "\" -OriginalProcessId " + original.Id + " -OriginalStartTicks " + original.StartTime.ToUniversalTime().Ticks +
            " -OriginalUserSid " + WindowsIdentity.GetCurrent().User!.Value;
        Process.Start(start)?.Dispose();
#endif
    }
}
