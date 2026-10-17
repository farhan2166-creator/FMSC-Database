using System.Text;
namespace FMSC_SMS.Core;
internal static class AppExceptionHandler
{
    static readonly object Sync = new();
    const long MaxLogBytes = 512L * 1024;
    public static string LogPath
    {
        get
        {
#if FMSC_LEVEL2
            string dir = Path.Combine(FMSC_SMS.Core.Level2Target.Folder(Environment.SpecialFolder.LocalApplicationData), "FMSC", "Logs");
#else
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FMSC", "Logs");
#endif
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "FMSC_errors.log");
        }
    }
    public static void Register()
    {
        // Keep the previous run's diagnostic evidence. WriteBoundedLog already
        // limits growth when the next error is recorded.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Handle(e.Exception, "UI Thread");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Handle(
                e.ExceptionObject as Exception ??
                new Exception(Convert.ToString(e.ExceptionObject)),
                "AppDomain");
        TaskScheduler.UnobservedTaskException += (_, e) => {
            Handle(e.Exception, "Unobserved Task");
            e.SetObserved();
        };
    }
    static void Handle(Exception ex, string source)
    {
        try
        {
            Log(ex, source);
            if (Application.MessageLoop && DatabaseConnectionForm.IsConnectionProblem(ex))
            {
                using var connection = new DatabaseConnectionForm(startup: false);
                connection.ShowDialog();
                return;
            }
            if (Application.MessageLoop)
                ModernMessageDialog.ShowError(null, DbMessage.UserMessage(ex), "FMSC Unexpected Error");
        }
        catch (Exception handlerException)
        {
            LogNonFatal(handlerException, "Unexpected-error UI handler");
        }
    }
    internal static void LogNonFatal(Exception ex, string source)
    {
        Log(ex, source);
    }
    internal static void Log(Exception ex, string source)
    {
        try
        {
            string entry =
                $"{Environment.NewLine}" +
                $"============================================================{Environment.NewLine}" +
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}" +
                $"{ex}{Environment.NewLine}" +
                $"============================================================{Environment.NewLine}";
            WriteBoundedLog(LogPath, entry);
        }
        catch (Exception loggingException)
        {
            // Logging failure must never prevent recovery or recursively log itself.
            try { System.Diagnostics.Trace.TraceError($"FMSC logging failed: {loggingException.Message}; {source}: {ex}"); }
            catch (Exception traceException)
            {
                OutputDebugString($"FMSC diagnostic fallback: {traceException.Message}; {source}: {ex.Message}");
            }
        }
    }
    static void WriteBoundedLog(string path, string entry)
    {
        const int keepBytes = 256 * 1024;
        lock (Sync)
        {
            long entryBytes = Encoding.UTF8.GetByteCount(entry);
            if (File.Exists(path) &&
                new FileInfo(path).Length + entryBytes > MaxLogBytes)
            {
                string existing = File.ReadAllText(path, Encoding.UTF8);
                int start = Math.Max(0, existing.Length - keepBytes);
                File.WriteAllText(path, existing[start..] + entry, Encoding.UTF8);
                return;
            }
            File.AppendAllText(path, entry, Encoding.UTF8);
        }
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern void OutputDebugString(string message);
}
