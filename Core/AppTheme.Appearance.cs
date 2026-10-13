using System.Drawing;

namespace FMSC_SMS.Core;

// Append values: saved numeric preferences must keep their original meaning.
public enum AppThemeType
{
    OceanBlue = 0, Emerald = 1, ProfessionalOrange = 2, Violet = 3,
    Rose = 4, Teal = 5, Amber = 6, ProfessionalSimple = 7,
    OceanBlueLightweight = 8, EmeraldLightweight = 9,
    ProfessionalOrangeLightweight = 10, VioletLightweight = 11
}

internal readonly record struct LightweightPalette(Color Primary, Color Hover, Color Selection,
    Color Page, Color Secondary, Color Border);

/// <summary>Appearance policy and lightweight colors, independent of Windows controls.</summary>
internal static class ThemeAppearance
{
    internal static IReadOnlyList<AppThemeType> Selectable { get; } = Array.AsReadOnly(new[] {
        AppThemeType.OceanBlue, AppThemeType.OceanBlueLightweight,
        AppThemeType.Emerald, AppThemeType.EmeraldLightweight,
        AppThemeType.ProfessionalOrange, AppThemeType.ProfessionalOrangeLightweight,
        AppThemeType.Violet, AppThemeType.VioletLightweight,
        AppThemeType.ProfessionalSimple
    });
    internal static bool IsLightweight(AppThemeType theme) => theme is AppThemeType.ProfessionalSimple or
        AppThemeType.OceanBlueLightweight or AppThemeType.EmeraldLightweight or
        AppThemeType.ProfessionalOrangeLightweight or AppThemeType.VioletLightweight;
    internal static AppThemeType Normalize(AppThemeType theme) => theme switch {
        AppThemeType.Rose => AppThemeType.Violet,
        AppThemeType.Teal => AppThemeType.Emerald,
        AppThemeType.Amber => AppThemeType.ProfessionalOrange,
        _ => Selectable.Contains(theme) ? theme : AppThemeType.OceanBlue
    };
    internal static AppThemeType BaseTheme(AppThemeType theme) => theme switch {
        AppThemeType.OceanBlueLightweight => AppThemeType.OceanBlue,
        AppThemeType.EmeraldLightweight => AppThemeType.Emerald,
        AppThemeType.ProfessionalOrangeLightweight => AppThemeType.ProfessionalOrange,
        AppThemeType.VioletLightweight => AppThemeType.Violet,
        AppThemeType.Rose => AppThemeType.Violet,
        AppThemeType.Teal => AppThemeType.Emerald,
        AppThemeType.Amber => AppThemeType.ProfessionalOrange,
        AppThemeType.OceanBlue or AppThemeType.Emerald or AppThemeType.ProfessionalOrange or
            AppThemeType.Violet or AppThemeType.ProfessionalSimple => theme,
        _ => AppThemeType.OceanBlue
    };
    internal static string Name(AppThemeType theme) => theme switch {
        AppThemeType.OceanBlue => "Ocean Blue (Standard)", AppThemeType.Emerald => "Emerald Green (Standard)",
        AppThemeType.ProfessionalOrange => "Professional Orange (Standard)", AppThemeType.Violet => "Royal Violet (Standard)",
        AppThemeType.ProfessionalSimple => "Professional Simple (Lightweight)",
        AppThemeType.OceanBlueLightweight => "Ocean Blue (Lightweight)",
        AppThemeType.EmeraldLightweight => "Emerald Green (Lightweight)",
        AppThemeType.ProfessionalOrangeLightweight => "Professional Orange (Lightweight)",
        AppThemeType.VioletLightweight => "Royal Violet (Lightweight)",
        AppThemeType.Rose => "Rose", AppThemeType.Teal => "Teal", AppThemeType.Amber => "Amber Gold",
        _ => theme.ToString()
    };
    private static Color C(int r,int g,int b) => Color.FromArgb(r,g,b);
    private static readonly LightweightPalette Blue = new(C(37,99,235),C(29,78,216),C(222,233,252),C(245,248,252),C(239,245,252),C(160,180,207));
    private static readonly LightweightPalette Green = new(C(4,120,87),C(6,95,70),C(219,239,230),C(245,250,247),C(236,246,241),C(150,173,161));
    private static readonly LightweightPalette Orange = new(C(194,65,12),C(154,52,18),C(251,230,216),C(252,248,244),C(252,242,234),C(196,171,153));
    private static readonly LightweightPalette Purple = new(C(109,40,217),C(91,33,182),C(235,226,251),C(248,246,252),C(242,237,251),C(180,167,205));
    private static readonly LightweightPalette Professional = new(C(36,90,157),C(28,69,122),C(221,234,249),C(244,247,251),C(238,243,249),C(165,181,203));
    internal static LightweightPalette Palette(AppThemeType theme) => BaseTheme(theme) switch {
        AppThemeType.Emerald => Green, AppThemeType.ProfessionalOrange => Orange,
        AppThemeType.Violet => Purple, AppThemeType.ProfessionalSimple => Professional, _ => Blue
    };

    internal static double Luminance(Color color)
    {
        static double Channel(byte value) { double v=value/255d;return v<=0.04045?v/12.92:Math.Pow((v+0.055)/1.055,2.4); }
        return 0.2126*Channel(color.R)+0.7152*Channel(color.G)+0.0722*Channel(color.B);
    }
    internal static double Contrast(Color first,Color second)
    {
        double a=Luminance(first),b=Luminance(second);
        return (Math.Max(a,b)+0.05)/(Math.Min(a,b)+0.05);
    }
    private static readonly Color DarkText=C(17,24,39);
    private static readonly double DarkTextLuminance=Luminance(DarkText);
    internal static Color ReadableText(Color background)
    {
        double luminance=Luminance(background);
        double lightContrast=1.05/(luminance+0.05);
        double darkContrast=(Math.Max(luminance,DarkTextLuminance)+0.05)/(Math.Min(luminance,DarkTextLuminance)+0.05);
        if(darkContrast>=4.5&&darkContrast>=lightContrast)return DarkText;
        if(lightContrast>=4.5)return Color.White;
        return Color.Black;
    }
}

/// <summary>A faulty theme listener must not block the remaining controls' refresh.</summary>
internal static class ThemeChangeDelivery
{
    internal static void Notify(EventHandler? handlers,Action<Exception> report)
    {
        if(handlers is null)return;
        foreach(EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(null,EventArgs.Empty); }
            catch(Exception ex) { report(ex); }
        }
    }
}
