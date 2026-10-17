using FMSC_SMS.Core;
namespace FMSC_SMS;
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
#if FMSC_LEVEL2
        if (args.SequenceEqual(new[]{"--verify-level2-guards"}))
        {
            Environment.ExitCode = Level2Target.VerifyRejections();
            return;
        }
        // Fail before any UI or operational file/engine access. Normal startup,
        // first-run checks, authentication and operator forms remain unchanged.
        Level2Target.ValidateSchema();
        if (args.Length != 0) throw new InvalidOperationException("Installation commands are disabled in Level 2.");
#else
        // A normal binary must reject the test-only probe before normal startup
        // can access its operational database. This also detects a wrong build.
        if (args.SequenceEqual(new[]{"--verify-level2-guards"}))
        {
            Environment.ExitCode = 2;
            return;
        }
#endif
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
