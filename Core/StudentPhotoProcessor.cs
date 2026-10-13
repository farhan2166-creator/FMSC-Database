using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FMSC_SMS.Core;

/// <summary>Prepares a small JPEG of the full photo; no cropping or stretching.</summary>
internal static class StudentPhotoProcessor
{
    private static readonly Lazy<ImageCodecInfo> JpegCodec = new(() =>
        ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid));

    internal static byte[] Prepare(byte[] originalBytes, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (originalBytes.Length == 0 || originalBytes.Length > StudentPhotoInput.MaximumBytes)
            throw new StudentPhotoInputException("Choose a non-empty JPEG or PNG image no larger than 25 MB.");
        using var source = new MemoryStream(originalBytes, writable: false);
        using var original = Image.FromStream(source, useEmbeddedColorManagement: true, validateImageData: true);
        if (original.RawFormat.Guid != ImageFormat.Jpeg.Guid && original.RawFormat.Guid != ImageFormat.Png.Guid)
            throw new StudentPhotoInputException("Choose a JPEG or PNG image.");
        CorrectOrientation(original);
        Size size = StudentPhotoInput.FittedSize(original.Width, original.Height);
        using var resized = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
        resized.SetResolution(96, 96);
        using (var graphics = Graphics.FromImage(resized))
        {
            // JPEG has no alpha channel; a white background avoids black transparent areas.
            graphics.Clear(Color.White);
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.PageUnit = GraphicsUnit.Pixel;
            float scale = (float)Math.Min(1d, (double)StudentPhotoInput.MaximumDimension / Math.Max(original.Width, original.Height));
            // Apply the same transform to both axes; rounded canvas dimensions
            // must not introduce independent horizontal/vertical stretching.
            graphics.ScaleTransform(scale, scale);
            graphics.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
                0, 0, original.Width, original.Height, GraphicsUnit.Pixel);
        }
        for (int quality = 90; quality >= 30; quality -= 15)
        {
            token.ThrowIfCancellationRequested();
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
            using var encoded = new MemoryStream();
            resized.Save(encoded, JpegCodec.Value, parameters);
            if (encoded.Length <= StudentPhotoInput.MaximumStoredBytes)
            {
                byte[] bytes = encoded.ToArray();
                StudentPhotoInput.ValidateStoredData(bytes);
                return bytes;
            }
        }
        throw new StudentPhotoInputException("This image could not be prepared within 300 KB. Choose a simpler photo or resize the original first.");
    }

    private static void CorrectOrientation(Image image)
    {
        const int orientationId = 0x0112;
        if (!image.PropertyIdList.Contains(orientationId)) return;
        byte[]? value = image.GetPropertyItem(orientationId)?.Value;
        if (value is null || value.Length < 2) return;
        RotateFlipType rotation = value[0] switch {
            2 => RotateFlipType.RotateNoneFlipX,
            3 => RotateFlipType.Rotate180FlipNone,
            4 => RotateFlipType.Rotate180FlipX,
            5 => RotateFlipType.Rotate90FlipX,
            6 => RotateFlipType.Rotate90FlipNone,
            7 => RotateFlipType.Rotate270FlipX,
            8 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone
        };
        image.RotateFlip(rotation);
    }
}
