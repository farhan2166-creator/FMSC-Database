using System.Text;
using FMSC_SMS.Core;
namespace FMSC_SMS.Services.Backup;
public static class BackupSettings
{
    private static readonly string SettingsFolder =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FMSC_SMS");
    private static readonly string SettingsFile =
        Path.Combine(SettingsFolder, "BackupSettings.txt");
    public static string GetBackupFolder()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return string.Empty;
            string path =
                File.ReadAllText(SettingsFile, Encoding.UTF8).Trim();
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            return Directory.Exists(path)
                ? path
                : string.Empty;
        }
        catch (Exception nonFatalException77)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException77, "BackupSettings: GetBackupFolder fallback");
            return string.Empty;
        }
    }
    public static bool SetBackupFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            AtomicTextFile.Write(SettingsFile, path.Trim(), Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            /* this was an empty catch. It stays non-fatal (the caller keeps working) but is no longer invisible. */
            AppExceptionHandler.LogNonFatal(
                ex,
                $"Saving the selected backup folder to '{SettingsFile}' - the folder choice will not survive a restart");
            return false;
        }
    }
}
