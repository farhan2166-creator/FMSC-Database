using FMSC_SMS.Services.Backup;

namespace FMSC_SMS.Services;

internal static class SetupReadiness
{
    internal static string NavigationPermission => CurrentUser.HasPermission("System.Setup") ? "System.Setup" :
        CurrentUser.HasPermission("Calendar.Manage") ? "Calendar.Manage" : "Fees.Configuration.Manage";
    internal static async Task<DataTable> CheckAsync()
    {
        var issues = await DatabaseHelper.QueryUserAsync("GetSetupReadiness");
        if (CurrentUser.HasPermission("Backup.Create"))
        {
            if (string.IsNullOrWhiteSpace(BackupSettings.GetBackupFolder()))
                issues.Rows.Add("Backup", "Select an available backup folder, preferably on another drive.", "Backup");
            if (!CurrentUser.HasPermission("Backup.View"))
            {
                issues.Rows.Add("Backup", "Ask an administrator to confirm that an available recovery backup exists.", "Backup");
                return issues;
            }
            var history = await Task.Run(DatabaseBackupManager.GetHistory);
            bool available = history.AsEnumerable().Any(row =>
                string.Equals(Convert.ToString(row["Status"]), "Success", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Convert.ToString(row["Operation"]), "BACKUP", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Convert.ToString(row["FilePath"])));
            if (!available) issues.Rows.Add("Backup", "Create a successful backup and keep a copy off this computer.", "Backup");
        }
        return issues;
    }
}
