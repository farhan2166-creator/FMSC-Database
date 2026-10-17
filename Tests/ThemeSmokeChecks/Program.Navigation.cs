using System.Reflection;
using FMSC_SMS.Core;

internal static partial class Program
{
    private sealed class FailingAttachPage : Panel
    {
        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (Parent is not null) throw new InvalidOperationException("Fixture parenting failure");
        }
    }

    private static void CheckNavigationRecovery()
    {
        // Keep the real shell unshown. Its initial dashboard loader never starts;
        // injected reporting avoids opening a dialog or creating the shell handle.
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(FMSC_SMS.MainDashboardForm);
        int failures = 0;
        Action<string, Exception> report = (_, _) => failures++;
        using var shell = (Form)Activator.CreateInstance(type, flags, null, new object[] { report }, null)!;
        Check(!shell.IsHandleCreated, "SQL-free navigation fixture remains unshown");
        var content = (Panel)type.GetField("content", flags)!.GetValue(shell)!;
        var navigate = type.GetMethod("NavigateTo", flags)!;
        void Open(Func<Control> factory, string title) => navigate.Invoke(shell, new object[] { factory, "Fixture", title });
        var previous = new Panel();
        Func<Control> previousFactory = () => previous;
        Open(previousFactory, "Previous");
        void PreviousStillUsable(string scenario)
        {
            Check(!previous.IsDisposed && ReferenceEquals(previous.Parent, content) && content.Controls.Count == 1,
                scenario + ": the previous control remains attached and alive");
            Check(ReferenceEquals(type.GetField("_currentPageControl", flags)!.GetValue(shell), previous) &&
                ReferenceEquals(type.GetField("_currentFactory", flags)!.GetValue(shell), previousFactory) &&
                (string)type.GetField("_currentTitle", flags)!.GetValue(shell)! == "Previous",
                scenario + ": current page, route and factory agree");
            Check(!(bool)type.GetField("_navigationInProgress", flags)!.GetValue(shell)!, scenario + ": navigation guard is released");
        }
        Open(() => throw new InvalidOperationException("Fixture factory failure"), "Factory Failure");
        PreviousStillUsable("Factory failure");
        Open(() => null!, "Null Factory"); PreviousStillUsable("Null destination");

        var states = (Dictionary<string, Dictionary<string, string>>)type.GetField("_workspaceStates", flags)!.GetValue(shell)!;
        // Two spellings of the same tab index produce a real duplicate child-state
        // key inside WorkspacePage.RestoreWorkspaceState, simulating malformed state.
        states["Fixture|State Failure"] = new Dictionary<string, string> { ["tab:0:key"] = "one", ["tab:00:key"] = "two" };
        var failedWorkspace = new WorkspacePage("Fixture", "", ("First", () => new Panel()));
        Open(() => failedWorkspace, "State Failure");
        PreviousStillUsable("State restoration failure");
        Check(failedWorkspace.IsDisposed, "An unactivated workspace is disposed after state restoration fails");

        var failedAttach = new FailingAttachPage();
        Open(() => failedAttach, "Attach Failure");
        PreviousStillUsable("Parenting failure");
        Check(failedAttach.IsDisposed, "A partially parented failed destination is disposed");

        var failedLayout = new Panel();
        bool layoutFailed = false;
        LayoutEventHandler failLayout = (_, _) => {
            if (!layoutFailed && content.Controls.Contains(failedLayout))
            {
                layoutFailed = true;
                throw new InvalidOperationException("Fixture layout failure");
            }
        };
        content.Layout += failLayout;
        try { Open(() => failedLayout, "Layout Failure"); }
        finally { content.Layout -= failLayout; }
        Check(layoutFailed && failedLayout.IsDisposed, "A failed activation layout retires only its failed destination");
        PreviousStillUsable("Layout failure after activation begins");
        Check(failures == 4, "Failures are reported once; a null factory is not reported as an exception");

        var next = new Panel(); Open(() => next, "Next");
        Check(previous.IsDisposed && ReferenceEquals(next.Parent, content) && content.Controls.Count == 1,
            "Confirmed navigation retires the previous page only after the destination is ready");
        Check((string)type.GetField("_currentTitle", flags)!.GetValue(shell)! == "Next" && failures == 4,
            "A successful retry activates the requested route");
        Check(!shell.IsHandleCreated, "Navigation recovery checks never start the real dashboard SQL loader");
    }
}
