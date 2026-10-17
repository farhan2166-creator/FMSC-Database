using FMSC_SMS.Core;
using System.Data;
using System.Diagnostics;

internal static class WorkflowLoadingChecks
{
    internal static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        Check(NavigationLocation.Format("Finance", "Fee Posting", "Monthly Fees") == "Finance › Fee Posting › Monthly Fees", "Location must include the active workspace section");
        Check(NavigationLocation.Format(" Setup ", " Setup ", "") == "Setup", "Location must trim and avoid repeated segments");
        Check(NavigationLocation.Format("", "Student Profile") == "Student Profile", "Standalone pages retain their identity");
        async Task Cancelled(Task operation)
        {
            try { await operation.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("Cancellation expected"); }
            catch (OperationCanceledException) { checks++; }
        }
        var source = new DataTable(); source.Columns.Add("Id", typeof(int)); source.Columns.Add("Name", typeof(string));
        source.PrimaryKey = new[] { source.Columns["Id"]! };
        for (int i = 0; i < 10003; i++) source.Rows.Add(i, $"Student {i}");
        source.AcceptChanges();
        foreach (int requested in new[] { -1, 0, 1, 19, 20, int.MaxValue })
        {
            var page = ReportPaginator.Build(source, requested, 500);
            int index = Math.Clamp(requested, 0, 20);
            int start = index * 500, count = Math.Min(500, source.Rows.Count - start);
            Check(page.Index == index && page.PageCount == 21 && page.TotalRows == 10003 && page.Rows.Rows.Count == count, "Page metadata must preserve limits");
            Check(page.HasPrevious == (index > 0) && page.HasNext == (index < 20), "Pager actions must preserve boundaries");
            for (int i = 0; i < count; i++) Check((int)page.Rows.Rows[i]["Id"] == start + i && page.Rows.Rows[i].RowState == DataRowState.Unchanged, "Page rows must preserve order and state");
            page.Rows.Rows[0]["Name"] = "Edited";
            Check((string)source.Rows[start]["Name"] == $"Student {start}", "Detached pages must not alter export source");
        }
        Check(ReportPaginator.Build(source.Clone(), 10, 500).RangeText == "No records", "Empty page retains its message");
        Check(ReportPaginator.Build(source, 0, int.MaxValue).Rows.Rows.Count == source.Rows.Count, "Large page sizes must not overflow");
        try { ReportPaginator.Build(source, 0, 0); throw new Exception("Page size must be positive"); }
        catch (ArgumentOutOfRangeException) { checks++; }

        using (var coordinator = new ReportLoadCoordinator())
        {
            var active = coordinator.Begin(); await coordinator.EnterAsync(active);
            var queued = coordinator.Begin(); var waiter = coordinator.EnterAsync(queued);
            var latest = coordinator.Begin();
            await Cancelled(waiter);
            await Cancelled(Task.Delay(Timeout.Infinite, active.Token));
            Check(!coordinator.IsCurrent(active) && !coordinator.IsCurrent(queued) && coordinator.IsCurrent(latest), "Only latest generation may bind");
            coordinator.Exit(); await coordinator.EnterAsync(latest);
            coordinator.Cancel(); await Cancelled(Task.Delay(Timeout.Infinite, latest.Token));
            Check(!coordinator.IsCurrent(latest), "Filter changes must invalidate active results");
            coordinator.Exit();
            var finished = coordinator.Begin(); await coordinator.EnterAsync(finished);
            coordinator.Exit(); coordinator.Complete(finished);
            Check(!coordinator.Cancel(), "Cancelling without a pending read must not disturb completed report actions");
        }
        var closing = new ReportLoadCoordinator();
        var running = closing.Begin(); await closing.EnterAsync(running);
        var pending = closing.Begin(); var closingWaiter = closing.EnterAsync(pending);
        closing.Dispose(); await Cancelled(closingWaiter); closing.Exit();
        Check(!closing.IsCurrent(pending), "Disposed pages must reject results without a semaphore disposal race");

        // Search session callbacks simulate the UI context without connecting to SQL.
        var session = new StudentPickerSearchSession();
        var context = new QueuedContext();
        var firstLoad = new TaskCompletionSource<DataTable>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newerApplied = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int staleApplied = 0, failures = 0;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            session.Begin(null, "old", false, () => "old", () => false,
                (_, _) => Interlocked.Increment(ref staleApplied), _ => failures++, loadSource: () => firstLoad.Task);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        session.Begin(source, "new", false, () => "new", () => false,
            (_, term) => newerApplied.SetResult(term), _ => failures++);
        firstLoad.SetResult(source);
        Check(await newerApplied.Task == "new", "Newest cached search must apply immediately");
        await context.Ready.Task.WaitAsync(TimeSpan.FromSeconds(2));
        context.Drain();
        Check(staleApplied == 0 && failures == 0, "Older list loads must not replace a newer search");
        return checks;
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        internal readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Action? continuation;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            continuation = () => callback(state);
            Ready.SetResult();
        }
        internal void Drain() => continuation!();
    }

    internal static void ProfilePagination()
    {
        using var source = new DataTable(); source.Columns.Add("Id", typeof(int));
        source.BeginLoadData();
        for (int i = 0; i < 200000; i++) source.Rows.Add(i);
        source.EndLoadData(); source.AcceptChanges();
        DataTable Baseline()
        {
            var rows = source.Clone();
            foreach (DataRow row in source.Rows.Cast<DataRow>().Skip(199500).Take(500)) rows.ImportRow(row);
            return rows;
        }
        DataTable Improved() => ReportPaginator.Build(source, 399, 500).Rows;
        foreach (var run in new Func<DataTable>[] { Baseline, Improved }) { using var warm = run(); }
        foreach (var (label, run) in new[] { ("Before", (Func<DataTable>)Baseline), ("After", (Func<DataTable>)Improved) })
        {
            var samples = new List<double>();
            for (int i = 0; i < 9; i++)
            {
                var watch = Stopwatch.StartNew(); using var page = run(); watch.Stop();
                if ((int)page.Rows[0]["Id"] != 199500 || page.Rows.Count != 500) throw new Exception("Profile page changed");
                samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            samples.Sort();
            Console.WriteLine($"PROFILE pagination {label}: median {samples[4]:F2} ms for last 500 of 200,000 synthetic rows. Excludes SQL and WinForms.");
        }
    }
}
