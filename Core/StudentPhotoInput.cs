namespace FMSC_SMS.Core;

internal static class StudentPhotoInput
{
    internal const int MaximumBytes = 5_000_000;

    internal static async Task<byte[]> ReadAsync(string path, CancellationToken token = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        long length = file.Length;
        if (length > MaximumBytes)
            throw new StudentPhotoInputException("Please choose an image no larger than 5 MB.");
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
