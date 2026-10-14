namespace FMSC_SMS.Core;

internal static class DpiLayout
{
    internal static float Factor(int dpi) => Math.Max(1, dpi) / 96f;

    internal static float Density(Size clientSize, int dpi, float minimum, float maximum) =>
        Math.Clamp(Math.Min(clientSize.Width / Factor(dpi) / AppScale.BaselineWidth,
            clientSize.Height / Factor(dpi) / AppScale.BaselineHeight), minimum, maximum);

    internal static int Pixels(int logical, int dpi) => logical <= 0 ? logical :
        Math.Max(1, (int)Math.Round(logical * Factor(dpi)));

    internal static Size ChangeDpi(Size size, int from, int to) => new(
        (int)Math.Round(size.Width * (double)to / Math.Max(1, from)),
        (int)Math.Round(size.Height * (double)to / Math.Max(1, from)));
}
