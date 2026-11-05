using FMSC_SMS.Core;
using FMSC_SMS.Core.Updates;
using FMSC_SMS.Pages.Security;

internal static partial class Program
{
    private static void CheckStartupDialogSize()
    {
        // Construction-only smoke test: never open a database connection or
        // start an installer just to inspect the four startup window sizes.
        Size reference = AppMetrics.StartupDialogClientSize;
        Check(reference == new Size(AppScale.Scale(760), AppScale.Scale(500)),
            "The login window establishes the 760 x 500 logical-size standard");

        using var login = new LoginForm((_, _) => (false, "fixture", null));
        using var connection = new DatabaseConnectionForm(startup: true);
        using var update = new UpdateConnectionForm(() => (true, "ssh-rsa fixture"));
        using var unapprovedUpdate = new UpdateConnectionForm(() => (false, ""));

        Check(login.ClientSize == reference, "Login uses the standard window size");
        Check(connection.ClientSize == reference, "Startup connection uses the login window size");
        Check(update.ClientSize == reference, "Authorized update checking uses the login window size");
        Check(unapprovedUpdate.ClientSize.Width == reference.Width &&
            unapprovedUpdate.ClientSize.Height >= reference.Height,
            "Computer enrollment retains its extra vertical space without narrowing the window");

        // The installer PowerShell UI intentionally shares this baseline for
        // both first-time installation and prepared updates (core.ps1).
    }
}
