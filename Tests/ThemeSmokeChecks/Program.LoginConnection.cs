using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;

internal static partial class Program
{
    private static void CheckLoginConnection()
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        // Visible alone is insufficient: an opaque sibling can cover a control.
        bool Exposed(Control control)
        {
            if (!control.Visible || control.Width <= 0 || control.Height <= 0) return false;
            Point center = control.PointToScreen(new Point(control.Width / 2, control.Height / 2));
            for (Control? child = control; child?.Parent is { } parent; child = parent)
                if (parent.GetChildAtPoint(parent.PointToClient(center)) != child) return false;
            return true;
        }

        foreach (var (found, title) in new[] { ("1", "Database update required"), ("3", "Application update required") })
        {
            using var release = new ManualResetEventSlim();
            int attempts = 0, firstRuns = 0;
            var failure = new DatabaseCompatibilityException(new(DatabaseCompatibilityIssue.SchemaVersion, "2", found));
            using var form = new LoginForm((_, _) => (false, "unused", null), true,
                _ => {
                    Interlocked.Increment(ref attempts);
                    if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Fixture was not released");
                    return (attempts > 1, failure.Message, attempts > 1 ? null : failure);
                }, () => { firstRuns++; return true; });
            form.Show();
            Application.DoEvents();
            var view = Descendants(form).OfType<DatabaseConnectionView>().Single();
            var reconnect = (ModernButton)view.AcceptAction;
            var status = (Label)typeof(DatabaseConnectionView).GetField("_status", fields)!.GetValue(view)!;
            var message = (Label)typeof(DatabaseConnectionView).GetField("_message", fields)!.GetValue(view)!;
            Check(view.Busy && Exposed(status) && Exposed(message), "Connection progress is exposed above the hidden login shell");
            Check(!reconnect.Enabled && firstRuns == 0, "Checking disables reconnect and cannot start first-run setup");
            release.Set();
            PumpUntil(() => !view.Busy, "Compatibility check finishes");
            Check(status.Text == title && Exposed(status) && Exposed(message), "Compatibility guidance remains exposed after startup layout");
            Check(message.Text.Contains($"version: {found}") && message.Text.Contains("application: 2"), "Main guidance identifies found and required versions");
            Check(reconnect.Enabled && Exposed(reconnect), "Reconnect is visible and available");
            Check(!((Control)typeof(DatabaseConnectionView).GetField("_restart", fields)!.GetValue(view)!).Enabled &&
                !((Control)typeof(DatabaseConnectionView).GetField("_repair", fields)!.GetValue(view)!).Visible,
                "Version failures cannot suggest engine restart or repair");
            Check(firstRuns == 0 && !Descendants(form).OfType<ModernButton>().Single(b => b.Text == "Sign In").Visible,
                "An incompatible database cannot proceed to setup or sign-in");
            var details = Descendants(view).OfType<LinkLabel>().Single();
            typeof(LinkLabel).GetMethod("OnLinkClicked", fields)!.Invoke(details,
                new object[] { new LinkLabelLinkClickedEventArgs(details.Links[0]) });
            Application.DoEvents();
            Check(Exposed(status) && Exposed(reconnect), "Expanded details preserve the visible connection stage");
            reconnect.PerformClick();
            PumpUntil(() => view.IsDisposed, "Successful reconnect switches to sign-in");
            Application.DoEvents();
            var signIn = Descendants(form).OfType<ModernButton>().Single(b => b.Text == "Sign In");
            Check(firstRuns == 1 && form.Text == "FMSC - Sign In" && Exposed(signIn), "Same window exposes sign-in after readiness succeeds");
            Check(ReferenceEquals(form.AcceptButton, signIn) && form.CancelButton is null, "Sign-in restores keyboard actions after connection view disposal");
            form.Close();
        }
    }
}
