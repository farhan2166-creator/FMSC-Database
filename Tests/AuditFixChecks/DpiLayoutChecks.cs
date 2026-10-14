using FMSC_SMS.Core;

internal static class DpiLayoutChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var original = (AppScale.Dpi, AppScale.RawFactor);
        try
        {
            foreach (int dpi in new[] { 96, 120, 144, 192 })
            {
                var equivalent = new Size(DpiLayout.Pixels(1280, dpi), DpiLayout.Pixels(720, dpi));
                AppScale.Recompute(equivalent, dpi);
                Check(AppScale.RawFactor == 1f, "Equivalent logical client areas have identical density at every DPI");
                Check(AppScale.Scale(40) == DpiLayout.Pixels(40, dpi), "Geometry applies Windows DPI exactly once");
                Check(AppScale.BodyFontScale == 1f, "Point-font density does not also multiply monitor DPI");
                Check(AppScale.Scale(40, AppScale.ControlSizeScale) == AppScale.Scale(40), "Explicit density and default geometry paths agree");
                foreach (var resolution in new[] { new Size(1280, 720), new Size(1366, 768), new Size(1920, 1080), new Size(2560, 1440), new Size(3840, 2160) })
                {
                    var available = new Size(resolution.Width, resolution.Height - DpiLayout.Pixels(40, dpi));
                    AppScale.Recompute(available, dpi);
                    Check(AppScale.RawFactor >= .82f && AppScale.RawFactor <= 1f, "Responsive spacing stays within its existing bounds");
                    Check(AppScale.BodyFontScale >= .92f, "Small clients retain the established readable font floor");
                    foreach (var origin in new[] { Point.Empty, new Point(-2560, 100) })
                    {
                        var work = new Rectangle(origin, available);
                        var desired = new Rectangle(origin.X - 50, origin.Y - 50, DpiLayout.Pixels(1000, dpi), DpiLayout.Pixels(800, dpi));
                        var minimum = new Size(DpiLayout.Pixels(850, dpi), DpiLayout.Pixels(650, dpi));
                        var fitted = WorkingAreaLayout.Fit(desired, minimum, work, dpi);
                        Check(work.Contains(fitted.Bounds), "Dialog fits synthetic work areas at every target resolution/DPI");
                        Check(fitted.MinimumSize.Width <= fitted.Bounds.Width && fitted.MinimumSize.Height <= fitted.Bounds.Height, "A dialog minimum cannot exceed its usable window");
                        Check(fitted.Bounds.Width <= available.Width - DpiLayout.Pixels(16, dpi), "Work-area inset follows DPI without clipping");
                    }
                }
            }
            AppScale.Recompute(new Size(1920, 1080), 192);
            Check(AppScale.RawFactor == .82f, "A 1920-pixel client at 200% does not masquerade as a large logical desktop");
            Check(DpiLayout.ChangeDpi(new Size(800, 600), 96, 192) == new Size(1600, 1200), "Window transitions use the DPI ratio, not an arbitrary zoom");
        }
        finally { AppScale.Recompute(new Size((int)(1280 * original.RawFactor * DpiLayout.Factor(original.Dpi)), (int)(720 * original.RawFactor * DpiLayout.Factor(original.Dpi))), original.Dpi); }
        return checks;
    }
}
