using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Core;

/// <summary>Resolves a live endpoint each time the local engine is refreshed.</summary>
internal static class LocalDatabaseEngine
{
    private static readonly object Sync = new();
    private static string? _endpoint;
    internal static string? CurrentEndpoint { get { lock (Sync) return _endpoint; } }
    internal static Func<string?> ToolLocator = FindTool;
    internal static Func<string?> NativeEndpointResolver = ResolveNativeEndpoint;
    internal static Func<string, string[], CommandResult> CommandRunner = Run;
    internal sealed record CommandResult(int ExitCode, string Output);

    internal static string Endpoint(bool refresh = false)
    {
        lock (Sync)
        {
            if (!refresh && _endpoint is not null) return _endpoint;
            _endpoint = null;
            string? tool = ToolLocator();
            if (tool is null)
                return _endpoint = NativeEndpointResolver() ?? throw MissingEngine();
            var listed = CommandRunner(tool, ["info"]);
            if (listed.ExitCode == 0 && !listed.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Any(line => line.Trim().Equals("MSSQLLocalDB", StringComparison.OrdinalIgnoreCase)))
            {
                var created = CommandRunner(tool, ["create", "MSSQLLocalDB"]);
                if (created.ExitCode != 0) throw Failed(created.Output);
            }
            var started = CommandRunner(tool, ["start", "MSSQLLocalDB"]);
            var info = CommandRunner(tool, ["info", "MSSQLLocalDB"]);
            string? pipe = ParsePipe(info.Output);
            if (info.ExitCode == 0 && pipe is not null) return _endpoint = pipe;
            AppExceptionHandler.LogNonFatal(new InvalidOperationException(started.Output + "\n" + info.Output), "Local database engine resolution");
            // An API failure can coexist with a healthy running engine. Its own
            // log supplies a candidate; the subsequent SQL connection validates it.
            string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Microsoft SQL Server Local DB", "Instances", "MSSQLLocalDB", "error.log");
            if (File.Exists(log))
            {
                using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                var matches = Regex.Matches(reader.ReadToEnd(), @"ready to accept connection on \[ (.*?) \]");
                if (matches.Count > 0 && ParsePipe(matches[^1].Groups[1].Value) is string loggedPipe)
                    return _endpoint = loggedPipe;
            }
            throw Failed(info.Output + "\n" + started.Output);
        }
    }

    internal static void Forget() { lock (Sync) _endpoint = null; }

    internal static void Restart()
    {
        lock (Sync)
        {
            string previous = Endpoint();
            _endpoint = null;
            string? tool = ToolLocator();
            if (tool is null)
            {
                StopStaleEngine(previous);
                _endpoint = NativeEndpointResolver() ?? throw MissingEngine();
                return;
            }
            // Never force-kill the engine or delete/recreate its instance.
            var stopped = CommandRunner(tool, ["stop", "MSSQLLocalDB"]);
            if (stopped.ExitCode != 0) throw new DatabaseRecoveryException("EngineBusy",
                "The database engine could not stop safely. Close other programs using the school database, then try Restart Connection again.", stopped.Output);
            StopStaleEngine(previous);
            Endpoint(refresh: true);
        }
    }

    private static void StopStaleEngine(string previous)
    {
        var builder = new SqlConnectionStringBuilder {
            DataSource = previous, InitialCatalog = "master", IntegratedSecurity = true,
            TrustServerCertificate = true, Pooling = false, ConnectTimeout = 2, ConnectRetryCount = 0
        };
        using var cn = new SqlConnection(builder.ConnectionString);
        try { cn.Open(); }
        catch (SqlException ex) when (DatabaseHelper.IsConnectionFailure(ex)) { return; }
        using (var check = new SqlCommand("SELECT physical_name FROM sys.database_files WHERE file_id=1;", cn))
        {
            string? master = Convert.ToString(check.ExecuteScalar());
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Microsoft SQL Server Local DB", "Instances", "MSSQLLocalDB", "master.mdf");
            if (master is null || !Path.GetFullPath(master).Equals(Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
                throw new DatabaseRecoveryException("WrongEngine", "This connection belongs to a different database engine. FMSC will not restart it.");
        }
        // A stale LocalDB controller can report a successful stop while its
        // verified engine remains alive. SHUTDOWN checkpoints the databases;
        // never use NOWAIT or kill the process.
        try { using var stop = new SqlCommand("SHUTDOWN;", cn) { CommandTimeout = 30 }; stop.ExecuteNonQuery(); }
        catch (SqlException ex) when (DatabaseHelper.IsConnectionFailure(ex)) { }
        cn.Close();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            using var probe = new SqlConnection(builder.ConnectionString);
            try { probe.Open(); }
            catch (SqlException ex) when (DatabaseHelper.IsConnectionFailure(ex)) { return; }
            Thread.Sleep(300);
        }
        throw new DatabaseRecoveryException("EngineBusy", "The database engine is still finishing work. Close other database programs, then try Restart Connection again.");
    }

    internal static string? ParsePipe(string output)
    {
        string pattern = "(?:np:)?(" + Regex.Escape(@"\\.\pipe\LOCALDB#") + "[A-Za-z0-9]+" + Regex.Escape(@"\tsql\query") + ")";
        var match = Regex.Match(output, pattern, RegexOptions.IgnoreCase);
        return match.Success ? "np:" + match.Groups[1].Value : null;
    }

    private static DatabaseRecoveryException Failed(string details) => new("EngineUnavailable",
        "The database engine could not start. Try Restart Connection. If it still fails, contact your administrator to check SQL Server LocalDB.", details);

    private static DatabaseRecoveryException MissingEngine() => new("EngineMissing",
        "SQL Server LocalDB could not be reached and its management utility is missing. " +
        "Ask your administrator to restore SQL Server LocalDB, then choose Reconnect. Your school data has not been replaced.");

    private static string? ResolveNativeEndpoint()
    {
        // SqlClient can start the automatic instance through LocalDB's native API
        // even when the management executable is missing. Never attach or replace
        // school files here; DatabaseHelper still verifies the school database.
        var builder = new SqlConnectionStringBuilder {
            DataSource = DatabaseHelper.LocalDbInstance, InitialCatalog = "master",
            IntegratedSecurity = true, TrustServerCertificate = true, Pooling = false,
            ConnectTimeout = 15, ConnectRetryCount = 0
        };
        try
        {
            using var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();
            using var command = new SqlCommand("SELECT physical_name FROM sys.database_files WHERE file_id=1;", connection);
            string? master = Convert.ToString(command.ExecuteScalar());
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Microsoft SQL Server Local DB", "Instances", "MSSQLLocalDB", "master.mdf");
            if (string.IsNullOrEmpty(master) || !Path.GetFullPath(master).Equals(Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
                throw new DatabaseRecoveryException("WrongEngine", "The fallback connection belongs to a different database engine. FMSC will not use it.");
            return DatabaseHelper.LocalDbInstance;
        }
        catch (SqlException ex)
        {
            AppExceptionHandler.LogNonFatal(ex, "Native LocalDB recovery without management utility");
            return null;
        }
    }

    internal static string? FindTool()
    {
        var candidates = new List<string>();
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (!string.IsNullOrWhiteSpace(folder)) candidates.Add(Path.Combine(folder.Trim('"'), "SqlLocalDB.exe"));
        foreach (string programRoot in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Distinct())
        {
            string root = Path.Combine(programRoot, "Microsoft SQL Server");
            if (!Directory.Exists(root)) continue;
            candidates.AddRange(Directory.GetDirectories(root).OrderByDescending(path => path, StringComparer.Ordinal)
                .Select(path => Path.Combine(path, "Tools", "Binn", "SqlLocalDB.exe")));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    private static CommandResult Run(string tool, string[] arguments)
    {
        var start = new ProcessStartInfo(tool) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the database engine utility.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            // Terminate only this utility process, never its SQL Server child.
            try { process.Kill(); } catch (InvalidOperationException) { }
            throw new DatabaseRecoveryException("EngineTimeout", "The database engine is taking too long to respond. Try Restart Connection. If it still fails, contact your administrator.");
        }
        return new(process.ExitCode, stdout.GetAwaiter().GetResult() + "\n" + stderr.GetAwaiter().GetResult());
    }

    internal static string? FindSetupAsset(string fileName)
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        for (int level = 0; folder is not null && level < 6; level++, folder = folder.Parent)
            foreach (string child in new[] { "", "Tools", "Installer" })
            {
                string candidate = Path.Combine(folder.FullName, child, fileName);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }
}
