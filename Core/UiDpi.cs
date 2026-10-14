namespace FMSC_SMS.Core;

/// <summary>One DPI boundary for the existing programmatic, AutoScaleMode.None UI.</summary>
internal static class UiDpi
{
    private static readonly Dictionary<(Font Reference, int Dpi), Font> fonts = new();
    private static readonly HashSet<Font> scaledFonts = new(ReferenceEqualityComparer.Instance);
    private static int? systemDpi;
    static UiDpi()
    {
        Application.ApplicationExit += (_, _) => {
            foreach (Font font in fonts.Values) font.Dispose();
            fonts.Clear(); scaledFonts.Clear();
        };
    }
    internal static int SystemDpi
    {
        get
        {
            if (systemDpi is null)
            {
                using var probe = new Control();
                systemDpi = probe.DeviceDpi;
            }
            return systemDpi.Value;
        }
    }

    internal static Font ForControl(Control control, Font reference) =>
        ForDpi(reference, control.IsHandleCreated ? control.DeviceDpi : SystemDpi);

    internal static Font ForDpi(Font reference, int dpi)
    {
        if (dpi == SystemDpi) return reference;
        var key = (reference, dpi);
        if (!fonts.TryGetValue(key, out var font))
        {
            // WinForms' native point-font handles use the system-DPI reference.
            // Use a separate cache so a scaled body font cannot masquerade as
            // an unscaled theme title font of the same point size.
            font = new Font(reference.FontFamily, reference.Size * dpi / SystemDpi, reference.Style, reference.Unit);
            fonts.Add(key, font);
            scaledFonts.Add(font);
        }
        return font;
    }

    internal static Font MeasurementFont(Font font) =>
        !scaledFonts.Contains(font) && UiTheme.IsReferenceFont(font)
            ? ForDpi(font, AppScale.Dpi) : font;

    internal static bool Matches(Control control, Font font, Font reference) =>
        font.FontFamily.Equals(reference.FontFamily) && font.Style == reference.Style && font.Unit == reference.Unit &&
        Math.Abs(font.Size - ForControl(control, reference).Size) < 0.02f;
}
