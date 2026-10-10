namespace FMSC_SMS.Core;
public static class UiDesignSystem
{
    public static class Spacing
    {
        public static int Xs => AppMetrics.SpacingXs;
        public static int Sm => AppMetrics.SpacingSm;
        public static int Md => AppMetrics.SpacingMd;
        public static int Lg => AppMetrics.SpacingLg;
        public static int Xl => AppMetrics.SpacingXl;
        public static int Xxl => AppMetrics.SpacingXxl;
    }
    public static class Typography
    {
        public static Font Body => AppTheme.NormalFont;
        public static Font Caption => AppTheme.FieldCaptionFont;
        public static Font Action => AppTheme.ButtonFont;
        public static Font Navigation => AppTheme.NavigationFont;
        public static Font Section => AppTheme.HeadingFont;
        public static Font PageTitle => AppTheme.PageTitleFont;
    }
    public static class Surfaces
    {
        public static Color Page => AppTheme.PageBackground;
        public static Color Card => AppTheme.Surface;
        public static Color Subtle => AppTheme.SurfaceSecondary;
        public static Color Border => AppTheme.Border;
    }
    public static class Actions
    {
        public const string Primary = "Primary";
        public const string Success = "Success";
        public const string Warning = "Warning";
        public const string Danger = "Danger";
        public const string Secondary = "Secondary";
        public const string Navigation = "Navigation";
        public const string TabInactive = "TabInactive";
    }
}
