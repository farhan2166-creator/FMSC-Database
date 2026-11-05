using FMSC_SMS.Core.Updates;
using System.Text.Json;

internal static class LiveTransferChecks
{
    internal static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "fmsc-live-transfer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await using var source = await UpdateGitSession.OpenAsync(timeout.Token);
            var release = await UpdateReleasePreparer.ReadLatestAsync(source, timeout.Token)
                ?? throw new Exception("No approved update is available.");
            var archive = release.Manifest.Incremental ?? release.Manifest.Full;
            string version = release.Manifest.Version;
            if (archive.Parts[0].Length > 8 * 1048576)
            {
                using var pointer = JsonDocument.Parse(await source.ReadSmallAsync("channel/stable.json", 4096, timeout.Token));
                if (pointer.RootElement.TryGetProperty("previousVersion", out var previous) &&
                    previous.ValueKind == JsonValueKind.String && UpdateManifestPolicy.IsPublishedVersion(previous.GetString()))
                {
                    string prior = previous.GetString()!;
                    var old = UpdateManifestPolicy.ReadValidated(await source.ReadSmallAsync(
                        "versions/" + prior + "/update-manifest.json", 4 * 1048576, timeout.Token));
                    var oldArchive = old.Incremental ?? old.Full;
                    if (old.Version == prior && oldArchive.Parts[0].Length < archive.Parts[0].Length)
                    { version = prior; archive = oldArchive; }
                }
            }
            var part = archive.Parts[0];
            int reports = 0; long received = 0;
            var progress = new UpdateInlineProgress<long>(value => {
                if (reports++ == 0) Console.WriteLine("Receiving actual Git network data…");
                received = Math.Max(received, value);
            });
            var counted = new CountingSource(source, progress);
            var slice = new UpdateArchive { Name = archive.Name, Length = part.Length, Sha256 = part.Sha256,
                Parts = new() { part } };
            UpdateManifestPolicy.ValidateArchive(slice);
            var states = new List<UpdateDownloadProgress>();
            var meter = new UpdateDownloadMeter(new UpdateInlineProgress<UpdateDownloadProgress>(value => {
                lock (states) states.Add(value);
            }));
            string first = Path.Combine(root, "first"); Directory.CreateDirectory(first);
            string cache = Path.Combine(root, "cache");
            string file = Path.Combine(first, part.Name);
            try
            {
                await UpdatePartDownload.FetchAsync(counted, slice, version, first, cache, meter, timeout.Token);
            }
            catch (UpdateGitCommandException error)
            {
                // Explicit local diagnostic mode only; never part of the desktop UI.
                Console.Error.WriteLine(Convert.ToString(error.Data["GitDiagnostic"])?.Replace(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"));
                throw;
            }
            if (new FileInfo(file).Length != part.Length || !UpdateChunks.Sha256(file).Equals(part.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Real Git part failed integrity verification.");
            if (reports == 0 && part.Length > 65536) throw new Exception("Git did not emit network progress for this transfer.");
            string resumed = Path.Combine(root, "resumed"); Directory.CreateDirectory(resumed);
            await UpdatePartDownload.FetchAsync(counted, slice, version, resumed, cache, meter, timeout.Token);
            if (counted.Requests != 1 || meter.Snapshot().ReusedBytes != part.Length ||
                !UpdateChunks.Sha256(Path.Combine(resumed, part.Name)).Equals(part.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Actual verified part was not reused without network access.");
            lock (states)
                if (!states.Any(s => s.BytesPerSecond > 0 && s.ReceivedBytes > 0)) throw new Exception("Live coordinator did not report network speed.");
            Console.WriteLine($"PASS: real private Git part/coordinator verified ({part.Length} bytes); {reports} network progress reports, peak {received} bytes; second attempt reused verified cache with no second request. No installation or SQL.");
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class CountingSource(UpdateGitSession source, IProgress<long> observed) : IUpdateArtifactSource
    {
        internal int Requests { get; private set; }
        public Task<byte[]> ReadSmallAsync(string path, int maximum, CancellationToken token) => source.ReadSmallAsync(path, maximum, token);
        public Task FetchPartAsync(string path, string destination, long expected, CancellationToken token, IProgress<long>? progress = null)
        {
            Requests++;
            return source.FetchPartAsync(path, destination, expected, token, new UpdateInlineProgress<long>(bytes => {
                observed.Report(bytes); progress?.Report(bytes);
            }));
        }
    }
}
