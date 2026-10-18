using System.Reflection;
using FMSC_SMS.Core;

internal static partial class Program
{
    private sealed class FailingThemeLabel : Label
    {
        internal bool FailOnce;
        protected override void OnForeColorChanged(EventArgs e)
        {
            base.OnForeColorChanged(e);
            if(FailOnce){FailOnce=false;throw new InvalidOperationException("Injected control theme failure");}
        }
    }
    private static void CheckLightweightResources(Form owner)
    {
        var saved=AppTheme.CurrentTheme;
        try
        {
            using var loading=new LoadingState("Reading records","Please wait") {Dock=DockStyle.None,Bounds=new Rectangle(360,215,260,60)};
            owner.Controls.Add(loading);Application.DoEvents();
            var timer=typeof(LoadingState).GetField("_timer",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var alerts=typeof(StatusAttention).GetField("Active",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
            var findAlert=alerts.GetType().GetMethod("TryGetValue")!;
            using var status=new Label{AutoSize=false,Bounds=new Rectangle(360,300,260,32),Text="Review this entry"};
            using var tip=new ToolTip();owner.Controls.Add(status);Application.DoEvents();
            foreach(var theme in AppTheme.SelectableThemes)
            {
                AppTheme.SetTheme(theme);Application.DoEvents();
                bool animationAllowed=!AppTheme.UseSimpleRendering&&!SystemInformation.TerminalServerSession&&SystemInformation.IsMenuAnimationEnabled;
                Check((timer.GetValue(loading) is not null)==animationAllowed,"Loading animation follows rendering mode and Windows motion settings");
                loading.Hide();Check(timer.GetValue(loading) is null,"Hidden loading panels release their timers");
                loading.Show();Application.DoEvents();
                if(AppTheme.UseSimpleRendering)
                {
                    Check(timer.GetValue(loading) is null,"Every lightweight variant avoids loading timer allocation");
                    Check(Contrast(AppTheme.MutedText,AppTheme.SurfaceSecondary)>=4.5&&Contrast(AppTheme.InputBorder,AppTheme.Surface)>=3,"Guidance and input boundaries stay legible");
                    Check(Contrast(AppTheme.ThemeOnPrimaryText,AppTheme.Primary)>=4.5&&Contrast(AppTheme.MenuText,AppTheme.MenuBackground)>=4.5,"Selected student rows and menus retain readable text");
                    using var flat=new UiPaintShape(new Rectangle(0,0,100,30),8);
                    Check(typeof(UiPaintShape).GetField("path",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(flat) is null,"Every lightweight variant avoids rounded drawing paths");
                }
                int restores=0;
                StatusAttention.Show(owner,status,tip,"Check this entry",()=>restores++);
                if(animationAllowed)
                {
                    object?[] arguments={status,null};Check((bool)findAlert.Invoke(alerts,arguments)!,"Decorated warning starts one bounded attention session");
                    var old=arguments[1]!;
                    StatusAttention.Show(owner,status,tip,"New message",()=>restores++);
                    object?[] replacement={status,null};Check((bool)findAlert.Invoke(alerts,replacement)!&&!ReferenceEquals(old,replacement[1]),"New warning replaces the old attention timer");
                    AppTheme.SetTheme(AppThemeType.OceanBlueLightweight);
                    object?[] stopped={status,null};Check(!(bool)findAlert.Invoke(alerts,stopped)!&&restores==1,"Switching to lightweight rendering cancels attention and restores current status");
                }
                else
                {
                    object?[] arguments={status,null};Check(!(bool)findAlert.Invoke(alerts,arguments)!&&restores==1,"Lightweight warnings preserve their message without a flash timer");
                }
            }
            // Verify subscriber isolation and prevent recursive theme-change callbacks.
            AppTheme.SetTheme(AppThemeType.OceanBlue);
            using var failedControl=new FailingThemeLabel{ForeColor=AppTheme.Primary,Text="Preserved label",Bounds=new Rectangle(360,345,200,28)};
            using var goodControl=new Label{ForeColor=AppTheme.Primary,Text="Other label",Bounds=new Rectangle(360,380,200,28)};
            owner.Controls.Add(failedControl);owner.Controls.Add(goodControl);
            failedControl.FailOnce=true;
            AppTheme.SetTheme(AppThemeType.VioletLightweight);
            Check(!failedControl.FailOnce&&goodControl.ForeColor==AppTheme.Primary&&failedControl.Text=="Preserved label", "A faulty control cannot block sibling refresh or change its text");
            AppTheme.SetTheme(AppThemeType.OceanBlue);
            bool continued=false;
            EventHandler failed=(_,_)=>throw new InvalidOperationException("Injected theme callback failure");
            EventHandler recursive=(_,_)=>{AppTheme.SetTheme(AppThemeType.Violet);continued=true;};
            AppTheme.ThemeChanged+=failed;AppTheme.ThemeChanged+=recursive;
            try
            {
                AppTheme.SetTheme(AppThemeType.EmeraldLightweight);
                Check(continued&&AppTheme.CurrentTheme==AppThemeType.EmeraldLightweight&&!AppTheme.IsChangingTheme,"One failed listener and a recursive request do not abort or corrupt the chosen theme");
            }
            finally{AppTheme.ThemeChanged-=failed;AppTheme.ThemeChanged-=recursive;}
            loading.Dispose();Check(timer.GetValue(loading) is null,"Loading disposal releases its timer");
        }
        finally{AppTheme.SetTheme(saved);}
    }
}
