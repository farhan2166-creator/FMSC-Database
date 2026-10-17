namespace FMSC_SMS.Core
{
    public static class AppScale
    {
        // Adaptive layout baseline; geometry can shrink to 82%, fonts to 92%.
        public const int BaselineWidth = 1280;
        public const int BaselineHeight = 720;
        // Keep fonts readable while allowing spacing and non-text geometry to
        // become denser on smaller/high-DPI working areas.
        public const float MinRawFactor = 0.82f;
        public const float MaxRawFactor = 1.0f;
        private const float MinFontFactor = 0.92f;
        private static float _rawFactor = MaxRawFactor;
        private static int _dpi = 96;
        public static event EventHandler? ScaleChanged;
        public static float RawFactor => _rawFactor;
        public static int Dpi => _dpi;
        // Recompute the adaptive factor from the size of the actual per-page working area.
        public static void Recompute(Size clientSize, int dpi)
        {
            if (clientSize.Width <= 0 || clientSize.Height <= 0)
                return;
            dpi = dpi > 0 ? dpi : 96;
            // WinForms client sizes are device pixels under PerMonitorV2.
            // Density uses logical space; monitor scaling is applied exactly
            // once by the shared geometry helpers, independently of density.
            float factor = DpiLayout.Density(clientSize, dpi, MinRawFactor, MaxRawFactor);
            // A monitor-DPI change matters even when the adaptive factor stays the same:
            // point-sized fonts render at a different pixel height, so every text-derived
            // row/header measurement must be refreshed.
            bool dpiChanged = dpi != _dpi;
            if (!dpiChanged && Math.Abs(factor - _rawFactor) < 0.01f)
                return;
            _rawFactor = factor;
            _dpi = dpi;
            ScaleChanged?.Invoke(null, EventArgs.Empty);
        }
        // Font scaling - shallow floor (MinFontFactor) so text stays legible at every supported resolution.
        public static float TitleFontScale => Math.Max(MinFontFactor, _rawFactor);
        public static float HeadingFontScale => Math.Max(MinFontFactor, _rawFactor);
        public static float BodyFontScale => Math.Max(MinFontFactor, _rawFactor);
        public static float ButtonFontScale => Math.Max(MinFontFactor, _rawFactor);
        // Control/spacing scaling - follows the full adaptive range, since padding, gaps and control chrome are what actually needs to give up space on smaller screens.
        public static float ControlSizeScale => _rawFactor;
        public static float SpacingScale => _rawFactor;
        // Scaling helpers
        public static int Scale(int baseValue, float factor) =>
            baseValue <= 0
                ? baseValue
                : Math.Max(1, (int)Math.Round(baseValue * factor * DpiLayout.Factor(_dpi)));
        public static int Scale(int baseValue) =>
            Scale(baseValue, _rawFactor);
        // Short alias used throughout the application.
        public static int S(int baseValue) =>
            Scale(baseValue);
    }
}
