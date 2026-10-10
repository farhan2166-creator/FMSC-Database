namespace FMSC_SMS.Core
{
    public enum AppThemeType
    {
        OceanBlue,
        Emerald,
        ProfessionalOrange,
        Violet,
        Rose,
        Teal,
        Amber
    }
    public static class AppTheme
    {
        // Deliberately small, visually distinct professional set shown to the operator.
        // The remaining enum values stay readable only so older saved preferences migrate cleanly.
        public static IReadOnlyList<AppThemeType> SelectableThemes { get; } =
            new[]
            {
                AppThemeType.OceanBlue,
                AppThemeType.Emerald,
                AppThemeType.ProfessionalOrange,
                AppThemeType.Violet
            };
        public static AppThemeType CurrentTheme =>
            _currentTheme;
        public static string CurrentThemeName =>
            GetThemeName(_currentTheme);
        private static readonly string ThemeFilePath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FMSC_SMS", "theme.txt");
        private static AppThemeType _currentTheme =
            LoadSavedTheme();
        public static event EventHandler? ThemeChanged;
        internal static bool IsChangingTheme { get; private set; }
        static AppTheme()
        {
            Application.Idle += (_, _) => UiTheme.InitializeOpenForms();
            ThemeChanged +=
                (sender, e) => {
                    UiHelper.RefreshApplicationTheme();
                };
        }
        private static AppThemeType LoadSavedTheme()
        {
            try
            {
                if (File.Exists(ThemeFilePath))
                {
                    string savedTheme =
                        File.ReadAllText(ThemeFilePath).Trim();
                    if (Enum.TryParse(savedTheme, true, out AppThemeType theme))
                    {
                        return NormalizeSelectableTheme(theme);
                    }
                }
            }
            catch (Exception nonFatalException1)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException1, "AppTheme: LoadSavedTheme fallback");
            }
            return AppThemeType.OceanBlue;
        }
        private static AppThemeType NormalizeSelectableTheme(AppThemeType theme) =>
            theme switch {
                AppThemeType.Rose => AppThemeType.Violet,
                AppThemeType.Teal => AppThemeType.Emerald,
                AppThemeType.Amber => AppThemeType.ProfessionalOrange,
                _ => theme
            };
        private static void SaveTheme(AppThemeType theme)
        {
            try
            {
                string? folder =
                    Path.GetDirectoryName(ThemeFilePath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(ThemeFilePath, theme.ToString());
                }
            }
            catch (Exception nonFatalException2)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException2, "AppTheme: SaveTheme fallback");
            }
        }
        public static string GetThemeName(
            AppThemeType theme) =>
            theme switch {
                AppThemeType.OceanBlue =>
                    "Ocean Blue",
                AppThemeType.Emerald =>
                    "Emerald Green",
                AppThemeType.ProfessionalOrange =>
                    "Professional Orange",
                AppThemeType.Violet =>
                    "Royal Violet",
                AppThemeType.Rose =>
                    "Rose",
                AppThemeType.Teal =>
                    "Teal",
                AppThemeType.Amber =>
                    "Amber Gold",
                _ => theme.ToString()
            };
        public static void SetTheme(AppThemeType theme)
        {
            theme = NormalizeSelectableTheme(theme);
            if (_currentTheme == theme)
                return;
            UiTheme.CaptureOpenForms();
            IsChangingTheme = true;
            try
            {
                _currentTheme = theme;
                SaveTheme(theme);
                ThemeChanged?.Invoke(null, EventArgs.Empty);
            }
            finally { IsChangingTheme = false; }
        }
        // Primary colors
        public static Color Primary
        {
            get
            {
                return _currentTheme switch {
                    AppThemeType.Emerald =>
                        Color.FromArgb(5, 150, 105),
                    AppThemeType.ProfessionalOrange =>
                        Color.FromArgb(234, 88, 12),
                    AppThemeType.Violet =>
                        Color.FromArgb(124, 58, 237),
                    AppThemeType.Rose =>
                        Color.FromArgb(225, 29, 72),
                    AppThemeType.Teal =>
                        Color.FromArgb(13, 148, 136),
                    AppThemeType.Amber =>
                        Color.FromArgb(202, 138, 4),
                    _ =>
                        Color.FromArgb(37, 99, 235)
                };
            }
        }
        public static Color PrimaryHover
        {
            get
            {
                return _currentTheme switch {
                    AppThemeType.Emerald =>
                        Color.FromArgb(4, 120, 87),
                    AppThemeType.ProfessionalOrange =>
                        Color.FromArgb(194, 65, 12),
                    AppThemeType.Violet =>
                        Color.FromArgb(109, 40, 217),
                    AppThemeType.Rose =>
                        Color.FromArgb(190, 18, 60),
                    AppThemeType.Teal =>
                        Color.FromArgb(15, 118, 110),
                    AppThemeType.Amber =>
                        Color.FromArgb(161, 98, 7),
                    _ =>
                        Color.FromArgb(29, 78, 216)
                };
            }
        }
        public static Color PrimaryLight
        {
            get
            {
                return _currentTheme switch {
                    AppThemeType.Emerald =>
                        Color.FromArgb(236, 253, 245),
                    AppThemeType.ProfessionalOrange =>
                        Color.FromArgb(255, 247, 237),
                    AppThemeType.Violet =>
                        Color.FromArgb(245, 243, 255),
                    AppThemeType.Rose =>
                        Color.FromArgb(255, 241, 242),
                    AppThemeType.Teal =>
                        Color.FromArgb(240, 253, 250),
                    AppThemeType.Amber =>
                        Color.FromArgb(255, 251, 235),
                    _ =>
                        Color.FromArgb(239, 246, 255)
                };
            }
        }
        // Status colors
        public static Color Success
        {
            get
            {
                return _currentTheme switch {
                    AppThemeType.Emerald =>
                        Color.FromArgb(16, 185, 129),
                    AppThemeType.ProfessionalOrange =>
                        Color.FromArgb(22, 163, 74),
                    _ =>
                        Color.FromArgb(22, 163, 74)
                };
            }
        }
        public static Color SuccessHover
        {
            get
            {
                return _currentTheme switch {
                    AppThemeType.Emerald =>
                        Color.FromArgb(5, 150, 105),
                    AppThemeType.ProfessionalOrange =>
                        Color.FromArgb(21, 128, 61),
                    _ =>
                        Color.FromArgb(21, 128, 61)
                };
            }
        }
        public static Color Warning
        {
            get
            {
                return _currentTheme switch {
                    AppThemeType.Emerald =>
                        Color.FromArgb(245, 158, 11),
                    AppThemeType.ProfessionalOrange =>
                        Color.FromArgb(249, 115, 22),
                    _ =>
                        Color.FromArgb(234, 179, 8)
                };
            }
        }
        public static Color WarningHover
        {
            get
            {
                return _currentTheme switch {
                    AppThemeType.Emerald =>
                        Color.FromArgb(217, 119, 6),
                    AppThemeType.ProfessionalOrange =>
                        Color.FromArgb(194, 65, 12),
                    _ =>
                        Color.FromArgb(202, 138, 4)
                };
            }
        }
        public static Color Danger =>
            Color.FromArgb(220, 38, 38);
        public static Color DangerHover =>
            Color.FromArgb(185, 28, 28);
        // Surfaces
        public static Color PageBackground
        {
            get
            {
                return _currentTheme switch {
                    AppThemeType.Emerald =>
                        Color.FromArgb(246, 250, 248),
                    AppThemeType.ProfessionalOrange =>
                        Color.FromArgb(252, 249, 247),
                    AppThemeType.Violet =>
                        Color.FromArgb(248, 247, 253),
                    AppThemeType.Rose =>
                        Color.FromArgb(253, 247, 248),
                    AppThemeType.Teal =>
                        Color.FromArgb(245, 250, 249),
                    AppThemeType.Amber =>
                        Color.FromArgb(252, 250, 244),
                    _ =>
                        Color.FromArgb(246, 248, 251)
                };
            }
        }
        public static Color Surface =>
            Color.White;
        public static Color SurfaceSecondary
        {
            get
            {
                return _currentTheme switch {
                    AppThemeType.Emerald =>
                        Color.FromArgb(248, 252, 250),
                    AppThemeType.ProfessionalOrange =>
                        Color.FromArgb(253, 250, 248),
                    AppThemeType.Violet =>
                        Color.FromArgb(250, 249, 254),
                    AppThemeType.Rose =>
                        Color.FromArgb(254, 249, 250),
                    AppThemeType.Teal =>
                        Color.FromArgb(248, 252, 251),
                    _ =>
                        Color.FromArgb(250, 251, 253)
                };
            }
        }
        public static Color SurfaceElevated =>
            Surface;
        // Text
        public static Color Text =>
            Color.FromArgb(31, 41, 55);
        public static Color HeadingText =>
            Color.FromArgb(17, 24, 39);
        public static Color MutedText =>
            Color.FromArgb(107, 114, 128);
        public static Color MenuText =>
            Color.FromArgb(243, 244, 246);
        public static Color MenuMutedText =>
            Color.FromArgb(156, 163, 175);
        public static Color ThemeOnPrimaryText =>
            Color.FromArgb(225, 240, 255);
        // Borders
        public static Color Border
        {
            get
            {
                return _currentTheme switch {
                    AppThemeType.Emerald =>
                        Color.FromArgb(220, 232, 227),
                    AppThemeType.ProfessionalOrange =>
                        Color.FromArgb(235, 226, 220),
                    AppThemeType.Violet =>
                        Color.FromArgb(231, 227, 244),
                    AppThemeType.Rose =>
                        Color.FromArgb(244, 224, 228),
                    AppThemeType.Teal =>
                        Color.FromArgb(216, 234, 231),
                    _ =>
                        Color.FromArgb(226, 232, 240)
                };
            }
        }
        public static Color BorderFocus =>
            Primary;
        public static Color InputBorder
        {
            get
            {
                var b = Border;
                var m = MutedText;
                return Color.FromArgb((b.R + m.R) / 2, (b.G + m.G) / 2, (b.B + m.B) / 2);
            }
        }
        public static Color InputBorderHover =>
            Blend(InputBorder, Primary, 0.28f);
        // Quiet hierarchy on white cards, with readable theme-related text.
        public static Color SectionHeaderBackground => Blend(Surface, Primary, 0.055f);
        public static Color PageTitleBackground => Blend(Surface, Primary, 0.035f);
        public static Color SectionHeadingText => Blend(Text, Primary, 0.30f);
        public static Color SectionGuidanceText => Blend(MutedText, Primary, 0.15f);
        private static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            return Color.FromArgb(
                from.R +
                    (int)((to.R - from.R) * amount),
                from.G +
                    (int)((to.G - from.G) * amount),
                from.B +
                    (int)((to.B - from.B) * amount));
        }
        // Navigation colors
        public static Color MenuSeparator =>
            Border;
        public static Color MenuBackground =>
            Surface;
        public static Color MenuCardBackground =>
            Surface;
        public static Color MenuHover =>
            SurfaceSecondary;
        public static Color MenuActive =>
            PrimaryLight;
        /// <summary>Darkens a colour by mixing it toward black.</summary>
        public static Color Shade(Color color, float amount = 0.50f)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            return Color.FromArgb(
                (int)(color.R * (1f - amount)),
                (int)(color.G * (1f - amount)),
                (int)(color.B * (1f - amount)));
        }
        /// <summary>Readable text colour for a status line drawn on a light tint of <paramref name="statusColor"/> (about 6:1 or better for every theme).</summary>
        public static Color StatusText(Color statusColor) =>
            Shade(statusColor, 0.50f);
        public static Color Tint(Color color, float amount = 0.90f)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            int r =
                color.R +
                (int)((255 - color.R) * amount);
            int g =
                color.G +
                (int)((255 - color.G) * amount);
            int b =
                color.B +
                (int)((255 - color.B) * amount);
            return Color.FromArgb(r, g, b);
        }
        public static Color ModuleAccent(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return Primary;
            return title.Trim().ToLowerInvariant() switch {
                "dashboard" =>
                    Primary,
                "students" =>
                    Color.FromArgb(37, 99, 235),
                "attendance" =>
                    Color.FromArgb(16, 185, 129),
                "finance" =>
                    Color.FromArgb(245, 158, 11),
                "staff" =>
                    Color.FromArgb(6, 182, 212),
                "reports" =>
                    Color.FromArgb(14, 116, 144),
                "certificates" =>
                    Color.FromArgb(168, 85, 247),
                "administration" =>
                    Color.FromArgb(71, 85, 105),
                _ =>
                    Primary
            };
        }
        // Shared fonts avoid repeated GDI handle allocation as pages are rebuilt.
        private static readonly object FontCacheLock = new();
        private static readonly Dictionary<(string Family, float Size, FontStyle Style, GraphicsUnit Unit), Font> FontCache = new();
        public static Font SharedFont(string family, float size, FontStyle style = FontStyle.Regular, GraphicsUnit unit = GraphicsUnit.Point)
        {
            float normalizedSize = (float)Math.Round(size, 2, MidpointRounding.AwayFromZero);
            var key = (family, normalizedSize, style, unit);
            lock (FontCacheLock)
            {
                if (FontCache.TryGetValue(key, out Font? font))
                    return font;
                font = new Font(family, normalizedSize, style, unit);
                FontCache.Add(key, font);
                return font;
            }
        }
        public static Font NormalFont =>
            SharedFont("Segoe UI", 12F * AppScale.BodyFontScale);
        public static Font BodyBoldFont =>
            SharedFont("Segoe UI", 12F * AppScale.BodyFontScale, FontStyle.Bold);
        public static Font ButtonFont =>
            SharedFont("Segoe UI", 12F * AppScale.ButtonFontScale, FontStyle.Bold);
        public static Font FieldCaptionFont =>
            SharedFont("Segoe UI", 12F * AppScale.ButtonFontScale, FontStyle.Bold);
        public static Font NavigationFont =>
            SharedFont("Segoe UI", 12F * AppScale.BodyFontScale);
        public static Font HeadingFont =>
            SharedFont("Segoe UI", 14F * AppScale.HeadingFontScale, FontStyle.Bold);
        public static Font PageTitleFont =>
            SharedFont("Segoe UI", 18F * AppScale.TitleFontScale, FontStyle.Bold);
        public static Font DisplayFont =>
            SharedFont("Segoe UI", 26F * AppScale.HeadingFontScale, FontStyle.Bold);
        public static Font SmallFont =>
            SharedFont("Segoe UI", 9F * AppScale.BodyFontScale);
        public static Font FontSmallBold =>
            SharedFont("Segoe UI", 9F * AppScale.BodyFontScale, FontStyle.Bold);
        public static Font FontTitle =>
            SharedFont("Segoe UI", 24F * AppScale.TitleFontScale, FontStyle.Bold);
        public static Font SymbolFont(float size, FontStyle style = FontStyle.Regular) =>
            SharedFont("Segoe UI Symbol", size, style);
        public static Font IconFont(float size) =>
            SharedFont("Segoe MDL2 Assets", size, FontStyle.Regular);
        // Shape metrics
        public const int ControlRadius = 8;
        public const int ButtonRadius = 8;
        public const int CardRadius = 10;
        // Disabled / shadow colors
        public static Color ButtonDisabledBackground =>
            Color.FromArgb(Math.Max(0, Surface.R - 12), Math.Max(0, Surface.G - 12), Math.Max(0, Surface.B - 12));
        public static Color ButtonDisabledText =>
            MutedText;
        public static Color ButtonShadow =>
            Color.FromArgb(42, Math.Max(0, Border.R - 18), Math.Max(0, Border.G - 18), Math.Max(0, Border.B - 18));
        // Control heights IMPORTANT: Scale(int) already applies ControlSizeScale. The previous implementation applied ControlSizeScale twice: Scale(40, ControlSizeScale) This has been corrected to: Scale(40)
        public static int InputHeight =>
            AppScale.Scale(40);
        public static int ButtonHeight =>
            AppScale.Scale(40);
        public static int LargeButtonHeight =>
            AppScale.Scale(42);
        public static int CompactButtonHeight =>
            AppScale.Scale(34);
    }
}
