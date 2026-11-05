using FMSC_SMS.Core.Updates;

internal static class UpdateDownloadTests
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "fmsc-download-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string zip = Path.Combine(root, "fixture.zip");
            File.WriteAllBytes(zip, Enumerable.Range(0, 3072).Select(i => (byte)(i % 251)).ToArray());
            var archive = new UpdateArchive { Name = "fixture.zip", Length = 3072, Sha256 = UpdateChunks.Sha256(zip),
                Parts = UpdateChunks.SplitArchive(zip, 1024) };
            var blobs = archive.Parts.ToDictionary(p => p.Name, p => File.ReadAllBytes(Path.Combine(root, p.Name)));
            string NewFolder(string name) { string p = Path.Combine(root, name); Directory.CreateDirectory(p); return p; }
            var received = new List<UpdateDownloadProgress>();
            var sink = new UpdateInlineProgress<UpdateDownloadProgress>(p => received.Add(p));
            var waits = new List<TimeSpan>();
            Task Delay(TimeSpan span, CancellationToken token) { token.ThrowIfCancellationRequested(); waits.Add(span); return Task.CompletedTask; }
            void Fetch(DownloadSource source, string parts, string cache, CancellationToken token = default,
                Func<TimeSpan, CancellationToken, Task>? delay = null) => UpdatePartDownload.FetchAsync(source, archive,
                    "1.2.0", NewFolder(parts), Path.Combine(root, cache), new UpdateDownloadMeter(sink), token, delay ?? Delay)
                    .GetAwaiter().GetResult();

            var flaky = new DownloadSource(blobs, (name, attempt) => name.EndsWith("0002") && attempt < 3
                ? new UpdateGitCommandException("interrupted") : null);
            Fetch(flaky, "flaky-parts", "flaky-cache");
            Require(flaky.Calls[archive.Parts[1].Name] == 3 && waits.Select(w => w.TotalSeconds).SequenceEqual(new[] { 2d, 4d }),
                "Transient connection failure must retry the unfinished part with bounded backoff.");
            Require(received.Any(p => p.Phase == UpdateDownloadPhase.Retrying) && received.Any(p => p.ReceivedBytes > 0 && p.BytesRemaining > 0),
                "Real progress and reconnect status must be reported.");

            var interrupted = new DownloadSource(blobs, (name, _) => name.EndsWith("0002") ? new OperationCanceledException() : null);
            try { Fetch(interrupted, "cancelled-parts", "resume-cache"); throw new Exception("Cancellation accepted."); }
            catch (OperationCanceledException) { }
            var resumed = new DownloadSource(blobs);
            received.Clear(); Fetch(resumed, "resumed-parts", "resume-cache");
            Require(!resumed.Calls.ContainsKey(archive.Parts[0].Name) && resumed.Calls.Count == 2 && received.Last().ReusedBytes == 1024,
                "A separate retry must reuse only previously hash-verified chunks.");
            UpdateChunks.ReassembleVerified(archive, Path.Combine(root, "resumed-parts"), Path.Combine(root, "resumed.zip"));

            string damagedCache = Path.Combine(root, "resume-cache", archive.Sha256, archive.Parts[1].Name);
            File.WriteAllText(damagedCache, "damage");
            var repaired = new DownloadSource(blobs);
            Fetch(repaired, "repaired-parts", "resume-cache");
            Require(repaired.Calls.Count == 1 && repaired.Calls.ContainsKey(archive.Parts[1].Name),
                "Damaged cache chunks must be fetched again; intact chunks must remain reusable.");

            waits.Clear();
            var offline = new DownloadSource(blobs, (_, _) => new UpdateGitCommandException("offline"));
            try { Fetch(offline, "offline-parts", "offline-cache"); throw new Exception("Offline transfer accepted."); }
            catch (UpdateGitCommandException) { }
            Require(offline.Calls.Values.Single() == 5 && waits.Count == 4, "Offline retries must stop after five attempts.");
            Require(received.Last().Phase == UpdateDownloadPhase.Failed && received.Last().ReceivedBytes == 0 &&
                received.Last().BytesPerSecond == 0 && received.Last().TimeRemaining is null,
                "Exhausted retries must not show partial unverified bytes or a stale transfer rate as saved progress.");
            waits.Clear();
            var revoked = new DownloadSource(blobs, (_, _) => new UpdateGitCommandException("authorization revoked", false));
            try { Fetch(revoked, "revoked-parts", "revoked-cache"); throw new Exception("Revoked transfer accepted."); }
            catch (UpdateGitCommandException) { }
            Require(revoked.Calls.Values.Single() == 1 && waits.Count == 0, "Authorization refusal must not enter a network retry loop.");

            using var cancel = new CancellationTokenSource();
            var cancelledWait = new DownloadSource(blobs, (_, _) => new UpdateGitCommandException("offline"));
            try
            {
                Fetch(cancelledWait, "wait-cancel-parts", "wait-cancel-cache", cancel.Token,
                    (_, token) => { cancel.Cancel(); return Task.FromCanceled(token); });
                throw new Exception("Backoff cancellation accepted.");
            }
            catch (OperationCanceledException) { }
            Require(cancelledWait.Calls.Values.Single() == 1, "Cancelling during backoff must not launch another request.");
            Require(!Directory.GetFiles(root, "*.incoming", SearchOption.AllDirectories).Any(), "Incomplete files must never become resumable chunks.");

            TimeSpan clock = TimeSpan.Zero;
            var meter = new UpdateDownloadMeter(null, () => clock);
            meter.Begin(archive); meter.StartPart(1, 1024, 1); clock = TimeSpan.FromSeconds(2); meter.NetworkBytes(512);
            var measured = meter.Snapshot();
            Require(measured.ReceivedBytes == 512 && measured.BytesRemaining == 2560 && measured.BytesPerSecond == 256 &&
                measured.Elapsed == clock && measured.TimeRemaining == TimeSpan.FromSeconds(10), "Speed, elapsed time and ETA must derive from network bytes and time.");
            clock = TimeSpan.FromSeconds(9);
            Require(meter.Snapshot().BytesPerSecond == 0 && meter.Snapshot().TimeRemaining is null,
                "A stalled connection must not display a stale speed or fictional ETA.");
            UpdateManifestPolicy.ValidateArchive(new UpdateArchive { Name = "legacy.zip", Length = UpdateChunks.DefaultMaximumPartBytes,
                Sha256 = new string('a', 64), Parts = new() { new UpdatePart { Name = "legacy.zip.part0001",
                    Length = UpdateChunks.DefaultMaximumPartBytes, Sha256 = new string('a', 64) } } });
            Console.WriteLine("PASS: download metrics, interrupted chunk retry, persistent verified resume, damaged cache, offline/revoked access and cancellation checks.");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Require(bool valid, string message) { if (!valid) throw new Exception(message); }

    private sealed class DownloadSource(Dictionary<string, byte[]> blobs, Func<string, int, Exception?>? fault = null) : IUpdateArtifactSource
    {
        internal Dictionary<string, int> Calls { get; } = new();
        public Task<byte[]> ReadSmallAsync(string path, int max, CancellationToken token) => throw new NotSupportedException();
        public Task FetchPartAsync(string path, string destination, long expected, CancellationToken token, IProgress<long>? progress = null)
        {
            token.ThrowIfCancellationRequested();
            string name = path.Split('/').Last();
            Calls.TryGetValue(name, out int count); Calls[name] = ++count;
            var data = blobs[name];
            File.WriteAllBytes(destination, data[..(data.Length / 2)]); progress?.Report(data.Length / 2);
            if (fault?.Invoke(name, count) is { } error) throw error;
            File.WriteAllBytes(destination, data); progress?.Report(data.Length);
            return Task.CompletedTask;
        }
    }
}
