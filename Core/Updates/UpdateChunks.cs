using System.Globalization;
using System.Security.Cryptography;

namespace FMSC_SMS.Core.Updates;

/// <summary>
/// Split ZIPs into signed, ordinary Git blobs under GitHub's 100 MiB per-blob
/// ceiling. Reconstruct only into a new staging file, hash checking each part
/// and then the complete archive. The original ZIP is a LOCAL build product,
/// not a distribution-repository object.
/// </summary>
internal static class UpdateChunks
{
    internal const long DefaultMaximumPartBytes = 90L * 1024 * 1024;

    internal static List<UpdatePart> SplitArchive(
        string zipPath, long maximumPartBytes = DefaultMaximumPartBytes)
    {
        if (maximumPartBytes is < 1 or > DefaultMaximumPartBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumPartBytes));
        string source = Path.GetFullPath(zipPath);
        var input = new FileInfo(source);
        if (!input.Exists || input.Length == 0)
            throw new InvalidDataException("The application archive is absent or empty.");
        long partCount = (input.Length + maximumPartBytes - 1) / maximumPartBytes;
        if (partCount is < 1 or > 2000)
            throw new InvalidDataException("Too many chunks for this signed update package.");

        var parts = new List<UpdatePart>((int)partCount);
        using var archive = input.OpenRead();
        for (int index = 1; index <= partCount; index++)
        {
            string name = MakeName(input.Name, index);
            string destination = Path.Combine(input.DirectoryName!, name);
            long remaining = Math.Min(maximumPartBytes, input.Length - archive.Position);
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[64 * 1024];
                while (remaining > 0)
                {
                    int next = archive.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (next == 0)
                        throw new EndOfStreamException("The update ZIP was truncated while splitting.");
                    output.Write(buffer, 0, next);
                    remaining -= next;
                }
            }
            parts.Add(new UpdatePart
            {
                Name = name,
                Length = new FileInfo(destination).Length,
                Sha256 = Sha256(destination)
            });
        }
        if (archive.Position != archive.Length)
            throw new InvalidDataException("The update ZIP changed during chunking.");
        return parts;
    }

    internal static void ReassembleVerified(UpdateArchive descriptor,
        string partsDirectory, string outputArchivePath)
    {
        UpdateManifestPolicy.ValidateArchive(descriptor);
        string folder = Path.GetFullPath(partsDirectory);
        string destination = Path.GetFullPath(outputArchivePath);
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("The verified output archive must not already exist.");
        string prefix = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        if (destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            destination.Equals(folder, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Reconstruction must not overwrite private repository files.");
        string? destFolder = Path.GetDirectoryName(destination);
        if (destFolder is null || !Directory.Exists(destFolder))
            throw new DirectoryNotFoundException("The isolated archive staging folder is missing.");
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                foreach (var part in descriptor.Parts)
                {
                    string source = Path.Combine(folder, part.Name);
                    var sourceInfo = new FileInfo(source);
                    if (!sourceInfo.Exists || sourceInfo.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                        sourceInfo.Length != part.Length)
                        throw new InvalidDataException("A signed update archive part is missing or changed.");
                    if (!StringComparer.OrdinalIgnoreCase.Equals(Sha256(source), part.Sha256))
                        throw new InvalidDataException("An update archive part failed its signed SHA-256 hash.");
                    using var input = sourceInfo.OpenRead();
                    input.CopyTo(output);
                }
                output.Flush(flushToDisk: true);
            }
            var finished = new FileInfo(temporary);
            if (finished.Length != descriptor.Length ||
                !StringComparer.OrdinalIgnoreCase.Equals(Sha256(temporary), descriptor.Sha256))
                throw new InvalidDataException("The reconstructed update archive failed its signed hash.");
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static string MakeName(string archiveName, int index) =>
        archiveName + ".part" + index.ToString("D4", CultureInfo.InvariantCulture);

    internal static string Sha256(string fileName)
    {
        using var input = File.OpenRead(fileName);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }
}
