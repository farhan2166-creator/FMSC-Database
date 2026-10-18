using System.Drawing;
using FMSC_SMS.Core;

internal static class ThemeAppearanceChecks
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        Check((int)AppThemeType.OceanBlue==0&&(int)AppThemeType.Amber==6&&(int)AppThemeType.ProfessionalSimple==7,"Existing saved theme IDs retain their meaning");
        Check(ThemeAppearance.Selectable.Count==9&&ThemeAppearance.Selectable.Distinct().Count()==9,"Four standard and five lightweight options are distinct");
        Check(ThemeAppearance.Selectable.Count(ThemeAppearance.IsLightweight)==5,"All lightweight options use one rendering policy");
        foreach(var (oldTheme,current) in new[]{(AppThemeType.Rose,AppThemeType.Violet),(AppThemeType.Teal,AppThemeType.Emerald),(AppThemeType.Amber,AppThemeType.ProfessionalOrange)})
            Check(ThemeAppearance.Normalize(oldTheme)==current,"Retired saved preferences retain their established migration");
        foreach(var unknown in new[]{(AppThemeType)(-1),(AppThemeType)12,(AppThemeType)999})
            Check(ThemeAppearance.Normalize(unknown)==AppThemeType.OceanBlue,"Unsupported preferences fall back safely");
        var light=ThemeAppearance.Selectable.Where(ThemeAppearance.IsLightweight).ToArray();
        Check(light.Select(theme=>ThemeAppearance.Palette(theme).Primary).Distinct().Count()==5,"Lightweight colors retain distinct identities");
        var text=Color.FromArgb(31,41,55);var muted=Color.FromArgb(95,107,122);
        foreach(var theme in light)
        {
            var p=ThemeAppearance.Palette(theme);
            Check(Enum.TryParse<AppThemeType>(theme.ToString(),out var loaded)&&ThemeAppearance.Normalize(loaded)==theme,"Every lightweight preference round-trips by its stable name");
            Check(ThemeAppearance.Name(theme).Contains("Lightweight"),"Appearance choices advertise the rendering mode");
            Check(ThemeAppearance.Contrast(Color.White,p.Primary)>=4.5&&ThemeAppearance.Contrast(Color.White,p.Hover)>=4.5,"Primary and hover action text meets readable contrast");
            Check(ThemeAppearance.Contrast(text,p.Page)>=4.5&&ThemeAppearance.Contrast(muted,p.Secondary)>=4.5&&ThemeAppearance.Contrast(text,p.Selection)>=4.5,"Page, guidance and selected-row text stays readable");
            var boundary=Color.FromArgb((p.Border.R+muted.R)/2,(p.Border.G+muted.G)/2,(p.Border.B+muted.B)/2);
            Check(ThemeAppearance.Contrast(boundary,Color.White)>=3,"Lightweight input boundaries remain distinguishable on white");
            Check(ThemeAppearance.Luminance(p.Hover)<ThemeAppearance.Luminance(p.Primary),"Hover feedback retains the same family with a darker accent");
        }
        foreach(var (theme,standard) in new[]{(AppThemeType.OceanBlueLightweight,AppThemeType.OceanBlue),(AppThemeType.EmeraldLightweight,AppThemeType.Emerald),(AppThemeType.ProfessionalOrangeLightweight,AppThemeType.ProfessionalOrange),(AppThemeType.VioletLightweight,AppThemeType.Violet)})
            Check(ThemeAppearance.BaseTheme(theme)==standard&&!ThemeAppearance.IsLightweight(standard),"Standard and lightweight versions share their family without sharing rendering mode");
        // Include the green/orange action colors that failed the former linear threshold,
        // and grayscale colors around the crossover where dark-gray and white can both fail.
        foreach(var color in new[]{Color.FromArgb(5,150,105),Color.FromArgb(234,88,12),Color.FromArgb(22,163,74),Color.FromArgb(180,83,9),Color.FromArgb(220,38,38),Color.FromArgb(123,123,123),Color.FromArgb(130,130,130),Color.White,Color.Black})
            Check(ThemeAppearance.Contrast(ThemeAppearance.ReadableText(color),color)>=4.5,"Solid action text is chosen by actual luminance contrast");
        int good=0,errors=0;
        EventHandler listeners=(_,_)=>good++;
        listeners+=(_,_)=>throw new InvalidOperationException("Injected failed theme listener");
        listeners+=(_,_)=>good++;
        ThemeChangeDelivery.Notify(listeners,_=>errors++);
        Check(good==2&&errors==1,"One failed refresh listener is recorded without blocking remaining listeners");
        ThemeChangeDelivery.Notify(null,_=>errors++);
        Check(errors==1,"An empty refresh subscription is safe");
        return checks;
    }
}
