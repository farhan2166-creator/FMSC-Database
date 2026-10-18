using FMSC_SMS.Core;
using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;

internal static class RecordOpeningChecks
{
    private static int LegacyCompare(string left, string right)
    {
        var a = Regex.Match(left, @"^([0-9]+)(.*)$");
        var b = Regex.Match(right, @"^([0-9]+)(.*)$");
        if (a.Success && b.Success && long.TryParse(a.Groups[1].Value, out long av) && long.TryParse(b.Groups[1].Value, out long bv))
        {
            int main = av.CompareTo(bv);
            return main != 0 ? main : StringComparer.OrdinalIgnoreCase.Compare(a.Groups[2].Value, b.Groups[2].Value);
        }
        return StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    internal static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        var values = new List<string> { "", "1", "01", "2", "10a", "10A", "10b", "123 suffix", "QR-1", " 2", "-2", "0", "٠١", "1\n", "2\r\n", "1\nx", "3\n\n", "9223372036854775807", "9223372036854775808", "999999999999999999999a" };
        var random = new Random(42);
        for (int i = 0; i < 80; i++) values.Add(random.Next(1, 100000).ToString() + (i % 2 == 0 ? "a" : ""));
        foreach (string left in values)
            foreach (string right in values)
                Check(Math.Sign(AdmissionNumbers.Compare(left, right)) == Math.Sign(LegacyCompare(left, right)), "Optimized comparison must preserve register ordering");
        var table = new DataTable(); table.Columns.Add("AdmissionNumber", typeof(string));
        foreach (string value in values) table.Rows.Add(value);
        AdmissionNumbers.AddSortColumns(table);
        foreach (DataRow row in table.Rows)
        {
            string text = (string)row["AdmissionNumber"];
            var match = Regex.Match(text, @"^([0-9]+)(.*)$");
            long expectedBase = match.Success && long.TryParse(match.Groups[1].Value, out long parsed) ? parsed : long.MaxValue;
            string expectedSuffix = match.Success ? match.Groups[2].Value : text;
            Check((long)row["__AdmissionNumberBase"] == expectedBase && (string)row["__AdmissionNumberSuffix"] == expectedSuffix, "Report sort columns must preserve overflow and suffix handling");
        }
        table.AcceptChanges(); int events = 0; table.RowChanged += (_, _) => events++;
        AdmissionNumbers.AddSortColumns(table); Check(events == 0, "Repeated derived-key preparation must remain idle");

        using var scope = new LatestReadScope();
        var first = scope.Begin(); var other = scope.Begin(); var returned = scope.Begin();
        Check(!scope.IsCurrent(first) && !scope.IsCurrent(other) && scope.IsCurrent(returned), "Returning to the same student must not revive the first request");
        Check(first.Token.IsCancellationRequested && other.Token.IsCancellationRequested, "Superseded profile reads must receive cancellation");
        using var fiveReadsStarted = new CountdownEvent(5);
        async Task ReadAsync()
        {
            fiveReadsStarted.Signal();
            await Task.Delay(Timeout.Infinite, returned.Token);
        }
        var reads = Enumerable.Range(0, 5).Select(_ => ReadAsync()).ToArray();
        Check(fiveReadsStarted.CurrentCount == 0, "Independent reads can start together");
        scope.Cancel();
        try { await Task.WhenAll(reads).WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { checks++; }
        Check(!scope.IsCurrent(returned), "Clearing a selection must invalidate all profile results");
        var closing = scope.Begin(); scope.Dispose();
        Check(closing.Token.IsCancellationRequested && !scope.IsCurrent(closing), "Disposal must cancel reads and reject their replies");
        try { scope.Begin(); throw new Exception("Disposed scope cannot restart"); }
        catch (ObjectDisposedException) { checks++; }
        return checks;
    }

    internal static void ProfileSorting()
    {
        var random = new Random(42);
        string[] source = Enumerable.Range(1, 10000).Select(i => i.ToString() + (i % 3 == 0 ? "a" : ""))
            .OrderBy(_ => random.Next()).ToArray();
        foreach (var compare in new Comparison<string>[] { LegacyCompare, AdmissionNumbers.Compare })
        { var warm = (string[])source.Clone(); Array.Sort(warm, compare); }
        string[]? expected = null;
        foreach (var (label, compare) in new[] { ("Before", (Comparison<string>)LegacyCompare), ("After", (Comparison<string>)AdmissionNumbers.Compare) })
        {
            var times = new List<double>(); var allocations = new List<long>();
            for (int i = 0; i < 5; i++)
            {
                var rows = (string[])source.Clone(); long before = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
                Array.Sort(rows, compare); watch.Stop();
                times.Add(watch.Elapsed.TotalMilliseconds); allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
                if (expected is null) expected = rows;
                else if (!rows.SequenceEqual(expected)) throw new Exception("Profile register ordering changed");
            }
            times.Sort(); allocations.Sort();
            Console.WriteLine($"PROFILE register sorting {label}: median {times[2]:F2} ms, {allocations[2]/1024:N0} KiB allocated for 10,000 synthetic numbers. Excludes SQL and WinForms.");
        }
    }
}
