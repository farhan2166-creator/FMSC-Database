namespace FMSC_SMS.Core;

internal static class StudentPhotoInput
{
    // Owner policy: new photos fit within 80 KB, preserving proportions without upscaling.
    // This C# limit does not rewrite existing photos or impose a new SQL constraint.
    internal const int MaximumBytes = 25_000_000;
    internal const int MaximumStoredBytes = 80 * 1024;
    internal const int MaximumDimension = 600;

    internal static Size FittedSize(int width, int height, int maximumDimension = MaximumDimension)
    {
        if (width <= 0 || height <= 0 || maximumDimension <= 0)
            throw new StudentPhotoInputException("The photo has invalid dimensions. Choose another JPEG or PNG.");
        double scale = Math.Min(1d, (double)maximumDimension / Math.Max(width, height));
        // Round the canvas outward so a fractional edge of the scaled image is retained.
        return new Size(Math.Clamp((int)Math.Ceiling(width * scale), 1, maximumDimension),
            Math.Clamp((int)Math.Ceiling(height * scale), 1, maximumDimension));
    }

    internal static void ValidateStoredData(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaximumStoredBytes)
            throw new StudentPhotoInputException("The prepared photo must be no larger than 80 KB. Choose the photo again to resize it before saving.");
    }

    internal static async Task<byte[]> ReadAsync(string path, CancellationToken token = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        long length = file.Length;
        if (length > MaximumBytes)
            throw new StudentPhotoInputException("Please choose an original JPEG or PNG no larger than 25 MB. It will be resized to fit within 80 KB before saving.");
        token.ThrowIfCancellationRequested();
        byte[] bytes = new byte[(int)length];
        try { await file.ReadExactlyAsync(bytes, token); }
        catch (EndOfStreamException)
        { throw new StudentPhotoInputException("The photo file changed while it was being read. Choose it again."); }
        if (await file.ReadAsync(new byte[1], token) != 0)
            throw new StudentPhotoInputException("The photo file changed while it was being read. Choose it again.");
        return bytes;
    }
}

internal sealed class StudentPhotoInputException(string message) : IOException(message);
