namespace FMSC_SMS.Core;

/// <summary>Keep reasonable JPEG quality; reduce resolution proportionally for unusually detailed images.</summary>
internal static class StudentPhotoCompression
{
    internal const int MinimumQuality = 70;
    internal const int MinimumDimension = 320;
    private static readonly int[] Qualities = { 88, 82, 76, MinimumQuality };

    internal static byte[] Prepare(int width, int height, Func<Size, int, byte[]> encode, CancellationToken token = default)
    {
        int dimension = StudentPhotoInput.MaximumDimension;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            Size size = StudentPhotoInput.FittedSize(width, height, dimension);
            int lastLength = 0;
            foreach (int quality in Qualities)
            {
                token.ThrowIfCancellationRequested();
                byte[] bytes = encode(size, quality);
                lastLength = bytes.Length;
                if (lastLength > 0 && lastLength <= StudentPhotoInput.MaximumStoredBytes)
                {
                    StudentPhotoInput.ValidateStoredData(bytes);
                    return bytes;
                }
                if (lastLength == 0) throw new StudentPhotoInputException("The photo encoder returned an empty image. Choose another JPEG or PNG.");
            }
            int current = Math.Max(size.Width, size.Height);
            if (current <= MinimumDimension) break;
            // Estimate the next pixel budget instead of encoding many barely smaller images.
            double scale = Math.Min(0.85, Math.Sqrt((double)StudentPhotoInput.MaximumStoredBytes / lastLength) * 0.95);
            dimension = Math.Max(MinimumDimension, (int)Math.Floor(current * scale));
        }
        throw new StudentPhotoInputException("This image could not fit within 80 KB at acceptable quality. Choose a clearer, simpler student portrait.");
    }
}
