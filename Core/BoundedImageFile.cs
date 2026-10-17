namespace FMSC_SMS.Core;

/// <summary>Check source length before allocation and detect a changed file during the read.</summary>
internal static class BoundedImageFile
{
    internal static async Task<byte[]> ReadAsync(string path, int maximumBytes, CancellationToken token = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ReadAsync(file, maximumBytes, token);
    }

    internal static async Task<byte[]> ReadAsync(Stream file, int maximumBytes, CancellationToken token = default)
    {
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        long length = file.Length;
        if (length <= 0 || length > maximumBytes)
            throw new InvalidDataException($"Choose a non-empty image no larger than {maximumBytes / 1_000_000d:0.##} MB.");
        token.ThrowIfCancellationRequested();
        byte[] bytes = new byte[(int)length];
        try { await file.ReadExactlyAsync(bytes, token); }
        catch (EndOfStreamException ex)
        { throw new InvalidDataException("The image file changed while it was being read. Choose it again.", ex); }
        if (await file.ReadAsync(new byte[1], token) != 0)
            throw new InvalidDataException("The image file changed while it was being read. Choose it again.");
        return bytes;
    }
}
