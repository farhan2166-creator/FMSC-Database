using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace FMSC_SMS.Core.Updates;

/// <summary>
/// One temporary, read-only Git connection to the private FMSC distribution.
/// Fetches the channel and signed manifest first; individual ZIP chunks are
/// materialized only on explicit request. No source repo or school database
/// is accessed and no Git credential is embedded in the application.
/// </summary>
internal sealed class UpdateGitSession : IUpdateArtifactSource, IAsyncDisposable
{
    internal const string DistributionRemote =
        "git@github.com:farhan2166-creator/FMSC-Updates.git";

    // Pin the published GitHub ed25519 host key, not a host key acquired from
    // the network at enrollment time.
    internal const string GitHubKnownHosts = "github.com ssh-ed25519 " +
        "AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl\n";

    private readonly string _workspace;
    private bool _isOpen;
    private bool _disposed;

    private UpdateGitSession(string workspace) => _workspace = workspace;

    internal static async Task<UpdateGitSession> OpenAsync(
        CancellationToken cancellationToken = default)
    {
        if (!UpdateDeviceKey.IsEnrolled)
            throw new InvalidOperationException("This computer has not yet generated an update authorization key.");
        string root = Path.Combine(UpdateDeviceKey.StateDirectory,
            "git-read-" + Guid.NewGuid().ToString("N"));
        var session = new UpdateGitSession(root);
        byte[] privateKey = Array.Empty<byte>();
        try
        {
            Directory.CreateDirectory(root);
            privateKey = UpdateDeviceKey.OpenPrivateKey();
            string pemPath = Path.Combine(root, "device.pem");
            using (var file = new FileStream(pemPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await file.WriteAsync(privateKey, cancellationToken);
                file.Flush(flushToDisk: true);
            }
            File.WriteAllText(Path.Combine(root, "known_hosts"), GitHubKnownHosts, Encoding.ASCII);
            // This never checks out the release ZIPs or source. Git will fetch
            // individual lazy blobs only when ReadSmall/FetchPart requests them.
            await session.RunGitAsync(
                new[] { "clone", "--quiet", "--no-checkout", "--depth=1",
                    "--filter=blob:none", "--single-branch", "--branch", "main",
                    DistributionRemote, "feed" },
                isRepoCommand: false, destination: Stream.Null,
                maxOutputBytes: 65536, cancellationToken,
                TimeSpan.FromSeconds(60));
            session._isOpen = true;
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    internal async Task<bool> HasStableFeedAsync(CancellationToken token)
    {
        EnsureOpen();
        // A missing feed means no release has been published yet, not a broken
        // school database or a reason to block application startup.
        var result = await RunGitAsync(
            new[] { "-C", "feed", "cat-file", "-e", "HEAD:channel/stable.json" },
            isRepoCommand: true, destination: Stream.Null, maxOutputBytes: 4096,
            token, TimeSpan.FromSeconds(30), allowFailure: true);
        return result.ExitCode == 0;
    }

    public async Task<byte[]> ReadSmallAsync(string relativePath, int maximumBytes,
        CancellationToken cancellationToken)
    {
        EnsureOpen();
        if (maximumBytes is <= 0 or > 4 * 1024 * 1024 ||
            !IsAllowedMetadataPath(relativePath))
            throw new InvalidDataException("Unexpected or excessive update metadata request.");
        using var output = new MemoryStream();
        await RunGitAsync(
            new[] { "-C", "feed", "show", "HEAD:" + relativePath },
            isRepoCommand: true, destination: output, maxOutputBytes: maximumBytes,
            cancellationToken, TimeSpan.FromSeconds(40));
        return output.ToArray();
    }

    public async Task FetchPartAsync(string relativePath, string destinationFile,
        long expectedBytes, CancellationToken cancellationToken)
    {
        EnsureOpen();
        if (!IsAllowedPartPath(relativePath) ||
            expectedBytes is < 1 or > UpdateChunks.DefaultMaximumPartBytes)
            throw new InvalidDataException("Invalid signed update chunk request.");
        if (File.Exists(destinationFile) || Directory.Exists(destinationFile))
            throw new IOException("The destination update chunk must be a new file.");
        try
        {
            using (var output = new FileStream(destinationFile, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536, useAsync: true))
            {
                await RunGitAsync(
                    new[] { "-C", "feed", "show", "HEAD:" + relativePath },
                    isRepoCommand: true, destination: output,
                    maxOutputBytes: expectedBytes, cancellationToken,
                    TimeSpan.FromMinutes(12));
                await output.FlushAsync(cancellationToken);
            }
            if (new FileInfo(destinationFile).Length != expectedBytes)
                throw new InvalidDataException("Private Git returned an incomplete chunk.");
        }
        catch
        {
            if (File.Exists(destinationFile)) File.Delete(destinationFile);
            throw;
        }
    }

    private static bool IsAllowedMetadataPath(string value)
    {
        if (value == "channel/stable.json") return true;
        var pieces = value.Split('/');
        return pieces.Length == 3 && pieces[0] == "versions" &&
            UpdateManifestPolicy.IsPublishedVersion(pieces[1]) &&
            (pieces[2] == "update-manifest.json" || pieces[2] == "update-manifest.sig");
    }

    private static bool IsAllowedPartPath(string value)
    {
        var pieces = value.Split('/');
        if (pieces.Length != 3 || pieces[0] != "versions" ||
            !UpdateManifestPolicy.IsPublishedVersion(pieces[1]))
            return false;
        return (pieces[2].StartsWith("FMSC_SMS-app-full.zip.part", StringComparison.Ordinal) ||
                pieces[2].StartsWith("FMSC_SMS-app-incremental.zip.part", StringComparison.Ordinal)) &&
            pieces[2].Length is >= 31 and <= 49 &&
            UpdateManifestPolicy.IsSafeRelativePath(pieces[2]) &&
            pieces[2].Split(".part").Last().Length == 4 &&
            pieces[2].Split(".part").Last().All(char.IsAsciiDigit);
    }

    private void EnsureOpen()
    {
        if (_disposed || !_isOpen)
            throw new ObjectDisposedException(nameof(UpdateGitSession));
    }

    private async Task<(int ExitCode, long Bytes)> RunGitAsync(
        string[] arguments, bool isRepoCommand, Stream destination,
        long maxOutputBytes, CancellationToken cancellationToken,
        TimeSpan maximumDuration, bool allowFailure = false)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(maximumDuration);
        var start = new ProcessStartInfo("git.exe")
        {
            WorkingDirectory = _workspace,
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_LFS_SKIP_SMUDGE"] = "1";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["GIT_SSH_COMMAND"] = isRepoCommand
            ? GitSsh("../device.pem", "../known_hosts")
            : GitSsh("device.pem", "known_hosts");
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start();
        try
        {
            var errorTask = process.StandardError.ReadToEndAsync(linked.Token);
            long bytes = 0;
            var buffer = new byte[65536];
            int read;
            while ((read = await process.StandardOutput.BaseStream.ReadAsync(buffer, linked.Token)) != 0)
            {
                bytes += read;
                if (bytes > maxOutputBytes)
                    throw new InvalidDataException("Git emitted data larger than the allowed signed update file.");
                await destination.WriteAsync(buffer.AsMemory(0, read), linked.Token);
            }
            await process.WaitForExitAsync(linked.Token);
            _ = await errorTask; // Deliberately never display stderr; it may contain paths.
            if (process.ExitCode != 0 && !allowFailure)
                throw new UpdateGitCommandException(
                    "The private Git request was refused or network access failed.");
            return (process.ExitCode, bytes);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new UpdateGitCommandException("The private Git operation timed out.");
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static string GitSsh(string pem, string knownHosts) =>
        "ssh -F none -o BatchMode=yes -o IdentitiesOnly=yes " +
        "-o StrictHostKeyChecking=yes -o PasswordAuthentication=no " +
        "-o UserKnownHostsFile=" + knownHosts + " -i " + pem;

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _isOpen = false;
        try
        {
            if (Directory.Exists(_workspace))
                Directory.Delete(_workspace, recursive: true);
        }
        catch (IOException) { /* Never delete a live application file. */ }
        catch (UnauthorizedAccessException) { /* Temporary user-only folder. */ }
        return ValueTask.CompletedTask;
    }
}
