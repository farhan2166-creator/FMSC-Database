using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FMSC_SMS.Core.Updates;

/// <summary>
/// User-initiated, read-only private Git repository probe.
/// Does not download update packages, run migrations, change any app files or
/// start automatically on startup. GitHub's SSH host key is pinned so an
/// unknown host cannot impersonate the private distribution repository.
/// </summary>
internal static class UpdateGitReader
{
    private const string Remote = "git@github.com:farhan2166-creator/FMSC-Updates.git";

    // Published by GitHub: https://docs.github.com/en/authentication/
    // keeping-your-account-and-data-secure/githubs-ssh-key-fingerprints
    private const string KnownHosts = "github.com ssh-ed25519 " +
        "AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl\n";

    internal sealed record CheckResult(bool Authorized, bool FeedPublished, string? Version, string Message);

    internal static async Task<CheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!UpdateDeviceKey.IsEnrolled)
            return new(false, false, null, "Authorize this computer before checking for updates.");

        string session = Path.Combine(UpdateDeviceKey.StateDirectory, "probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(session);
        byte[] privatePem = Array.Empty<byte>();
        try
        {
            privatePem = UpdateDeviceKey.OpenPrivateKey();
            File.WriteAllBytes(Path.Combine(session, "device.pem"), privatePem);
            File.WriteAllText(Path.Combine(session, "known_hosts"), KnownHosts, Encoding.ASCII);
            // No credentials, private keys or passwords are written to stdout.
            string lsRemote = await GitAsync(session, "device.pem", cancellationToken,
                "ls-remote", "--heads", Remote, "refs/heads/main");
            if (!lsRemote.Split('\n').Any(line => line.EndsWith("\trefs/heads/main", StringComparison.Ordinal)))
                return new(false, false, null, "GitHub did not return the approved update branch.");

            // Partial, no-checkout, shallow fetch: only metadata objects at this
            // stage. Full/delta blobs must never be downloaded just to check a
            // version. Later downloads will be separately signed/hash-verified.
            await GitAsync(session, "device.pem", cancellationToken,
                "clone", "--quiet", "--no-checkout", "--depth=1", "--filter=blob:none",
                "--single-branch", "--branch", "main", Remote, "feed");

            string pointer;
            try
            {
                pointer = await GitAsync(session, "../device.pem", cancellationToken,
                    "-C", "feed", "show", "HEAD:channel/stable.json");
            }
            catch (UpdateGitCommandException error)
                when (error.Reason.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                      error.Reason.Contains("exists on disk", StringComparison.OrdinalIgnoreCase))
            {
                return new(true, false, null,
                    "Computer authorized. No approved update package has been published yet.");
            }

            if (Encoding.UTF8.GetByteCount(pointer) is < 1 or > 4096)
                throw new InvalidDataException("GitHub update feed exceeds its permitted metadata size.");
            using var feed = JsonDocument.Parse(pointer);
            var root = feed.RootElement;
            if (root.GetProperty("format").GetString() != "fmsc-git-updates/v1")
                throw new InvalidDataException("Unsupported GitHub update feed format.");
            string version = root.GetProperty("latestVersion").GetString() ?? "";
            if (!UpdateManifestPolicy.IsPublishedVersion(version))
                throw new InvalidDataException("Invalid version in GitHub distribution feed.");
            return new(true, true, version,
                "Private update repository connected. Publisher signature verification " +
                "and installation are not yet enabled; no files were changed.");
        }
        catch (UpdateGitCommandException exception)
        {
            return new(false, false, null,
                "Unable to access the private distribution repository. " +
                "Check internet connectivity, deploy-key approval and Git/SSH availability. " +
                exception.Reason);
        }
        catch (Win32Exception)
        {
            return new(false, false, null,
                "Git for Windows is not installed. The final installer must bundle " +
                "the approved Git/SSH helper; no update was attempted.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privatePem);
            try { Directory.Delete(session, recursive: true); }
            catch (IOException) { /* A locked temp file can be removed on next maintenance. */ }
            catch (UnauthorizedAccessException) { /* No live application files are touched. */ }
        }
    }

    private static async Task<string> GitAsync(string workingFolder, string privateKey,
        CancellationToken cancellationToken, params string[] arguments)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var start = new ProcessStartInfo("git.exe")
        {
            WorkingDirectory = workingFolder,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_LFS_SKIP_SMUDGE"] = "1";
        // Relative safe names make the SSH shell command independent of the
        // computer's username or profile path, which may contain spaces.
        string hosts = privateKey.StartsWith("../", StringComparison.Ordinal)
            ? "../known_hosts" : "known_hosts";
        start.Environment["GIT_SSH_COMMAND"] =
            "ssh -F none -o IdentitiesOnly=yes -o BatchMode=yes " +
            "-o StrictHostKeyChecking=yes -o PasswordAuthentication=no " +
            "-o UserKnownHostsFile=" + hosts + " -i " + privateKey;
        foreach (var item in arguments) start.ArgumentList.Add(item);
        using var process = new Process { StartInfo = start };
        process.Start();
        try
        {
            Task<string> standard = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            string output = await standard;
            string error = await errors;
            if (process.ExitCode != 0)
            {
                // Never echo arbitrary Git error output; it may contain
                // local file paths, proxy details or information unsuitable
                // for a visible school-user dialog.
                string reason = error.Contains("path 'channel/stable.json' does not exist",
                    StringComparison.OrdinalIgnoreCase)
                    ? "path 'channel/stable.json' does not exist"
                    : "Git request was rejected or the network is unavailable.";
                throw new UpdateGitCommandException(reason);
            }
            if (output.Length > 65536)
                throw new InvalidDataException("Unexpectedly large private Git metadata result.");
            return output;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new UpdateGitCommandException("GitHub did not respond before the timeout.");
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }
}

internal sealed class UpdateGitCommandException : Exception
{
    internal string Reason { get; }
    internal UpdateGitCommandException(string reason) : base(reason) => Reason = reason;
}
