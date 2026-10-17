using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FMSC_SMS.Core;

/// <summary>Repairs only the SQL runtime. Never runs the FMSC installer or replaces school data.</summary>
internal static class SqlDependencyRepair
{
    internal const string PackageUrl = "https://download.microsoft.com/download/3/8/d/38de7036-2433-4207-8eae-06e247e17b25/SqlLocalDB.msi";

    internal static async Task<int> RepairAsync(IProgress<string> progress)
    {
        if (RuntimeInformation.OSArchitecture != Architecture.X64)
            throw new InvalidOperationException("This SQL dependency package requires 64-bit Intel/AMD Windows. Contact your administrator for a compatible SQL runtime.");
        string folder = Path.Combine(Path.GetTempPath(), "FMSC-SqlRepair-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string package = Path.Combine(folder, "SqlLocalDB.msi");
        try
        {
            progress.Report("Downloading SQL Server LocalDB from Microsoft...");
            await DownloadAsync(package);
            progress.Report("Verifying the Microsoft package signature...");
            await VerifySignatureAsync(package);
            progress.Report("Installing SQL Server LocalDB. Approve the Windows administrator prompt to continue.");
            return await Task.Run(() => {
                var start = new ProcessStartInfo("msiexec.exe") { UseShellExecute = true, Verb = "runas" };
                start.ArgumentList.Add("/i"); start.ArgumentList.Add(package);
                start.ArgumentList.Add("/passive"); start.ArgumentList.Add("/norestart");
                start.ArgumentList.Add("IACCEPTSQLLOCALDBLICENSETERMS=YES");
                if (LocalDatabaseEngine.FindTool() is not null)
                { start.ArgumentList.Add("REINSTALL=ALL"); start.ArgumentList.Add("REINSTALLMODE=vomus"); }
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows could not start SQL dependency repair.");
                process.WaitForExit();
                return process.ExitCode;
            });
        }
        finally
        {
            // Delete only the files created by this operation, never a shared folder.
            try { if (File.Exists(package)) File.Delete(package); if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); }
            catch (IOException fallbackException1)
            {
                AppExceptionHandler.LogNonFatal(fallbackException1, "SqlDependencyRepair: IOException recovery");
            }
            catch (UnauthorizedAccessException fallbackException2)
            {
                AppExceptionHandler.LogNonFatal(fallbackException2, "SqlDependencyRepair: UnauthorizedAccessException recovery");
            }
        }
    }

    internal static async Task DownloadAsync(string destination)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var response = await client.GetAsync(PackageUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        Uri? final = response.RequestMessage?.RequestUri;
        if (final?.Scheme != "https" || !string.Equals(final.Host, "download.microsoft.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The SQL package download did not come from the approved Microsoft host.");
        const long limit = 200L * 1024 * 1024;
        if (response.Content.Headers.ContentLength > limit) throw new InvalidOperationException("The SQL package exceeds the expected download size.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920]; long total = 0; int read;
        while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
        {
            total += read;
            if (total > limit) throw new InvalidOperationException("The SQL package exceeds the expected download size.");
            await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
        }
        if (total == 0) throw new InvalidOperationException("Microsoft returned an empty SQL package.");
    }

    internal static async Task VerifySignatureAsync(string package)
    {
        string path = package.Replace("'", "''");
        string script = "$s=Get-AuthenticodeSignature -LiteralPath '" + path + "'; " +
            "Write-Output ($s.Status.ToString() + ': ' + $s.StatusMessage); " +
            "if($s.Status -ne 'Valid' -or $null -eq $s.SignerCertificate -or $s.SignerCertificate.Subject -notmatch '(^|, *)O=Microsoft Corporation(,|$)'){exit 1};exit 0";
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var start = new ProcessStartInfo(Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        // A launch from PowerShell 7 can carry incompatible modules into Windows PowerShell.
        start.Environment["PSModulePath"] = Path.Combine(system, "WindowsPowerShell", "v1.0", "Modules");
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-OutputFormat", "Text", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The SQL package signature could not be verified.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { process.Kill(); } catch (InvalidOperationException) { } throw new InvalidOperationException("SQL package signature verification timed out. Try again when the internet connection is available."); }
        string diagnostic = (await output + " " + await error).Trim();
        if (process.ExitCode != 0) throw new InvalidOperationException("The SQL package could not be verified as Microsoft-signed. Installation was refused. " + diagnostic);
    }
}

