using FMSC_SMS.Core;
namespace FMSC_SMS;
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "--verify-installation" or "--install-communication")
        {
            Environment.ExitCode = CommunicationInstallation.Run(args);
            return;
        }
        ApplicationConfiguration.Initialize();
        AppExceptionHandler.Register();
        MainDashboardForm.PrimeInitialScale();
        using (var connection = new DatabaseConnectionForm(startup: true))
            if (connection.ShowDialog() != DialogResult.OK) return;
        try
        {
            var state = DatabaseHelper.Query("GetFirstRunStatus");
            if (state.Rows.Count > 0 && Convert.ToBoolean(state.Rows[0]["IsFirstRun"]))
            {
                using var setup = new FirstRunSetupForm();
                if (setup.ShowDialog() != DialogResult.OK)
                    return;
            }
        }
        catch (Exception ex)
        {
            ModernMessageDialog.ShowError(null, Core.DbMessage.UserMessage(ex), "FMSC Startup");
            return;
        }
        using var login = new LoginForm();
        if (login.ShowDialog() != DialogResult.OK)
            return;
        if (CurrentUser.MustChangePassword)
        {
            using var change = new ChangePasswordForm();
            if (change.ShowDialog() != DialogResult.OK)
            {
                ModernMessageDialog.ShowInfo(
                    null,
                    "Your account requires a password change before you can continue. " +
                    "FMSC will now close - please sign in again to set a new password.",
                    "Password Change Required");
                return;
            }
        }
        Application.Run(new MainDashboardForm());
    }
}
