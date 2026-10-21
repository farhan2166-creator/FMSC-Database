using System.Collections;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;

internal static partial class Program
{
    private static void CheckCombinedTooltips()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var root=new Panel();
        using var shortcuts=new ToolTip();
        using var descriptions=new ToolTip();
        var controller=new PageActionShortcutController(root,shortcuts);
        var save=new ModernButton{Text="Save"};root.Controls.Add(save);
        ControlToolTips.SetDescription(descriptions,save,"Save the reviewed details.");
        controller.RefreshHints();
        Check((descriptions.GetToolTip(save)??"").Contains("Ctrl+S")&&string.IsNullOrEmpty(shortcuts.GetToolTip(save)),
            "Explanatory help and the shortcut use one tooltip provider");
        FinanceHelp.Attach(root,save,FinanceHelp.Topic.FeeAgreement);
        for(int i=0;i<5;i++)controller.RefreshHints();
        string text=FinanceHelp.Tooltip(root,save);
        Check(text.Contains("Example:")&&text.Split("Ctrl+S").Length==2&&string.IsNullOrEmpty(descriptions.GetToolTip(save))&&string.IsNullOrEmpty(shortcuts.GetToolTip(save)),
            "Finance examples replace competing providers and repeated refresh never duplicates the shortcut");
        controller.RegisterShortcut(save,Keys.Control|Keys.N,"Ctrl+N",()=>{});
        Check(FinanceHelp.Tooltip(root,save).Contains("Ctrl+N")&&FinanceHelp.Tooltip(root,save).Contains("Ctrl+S"),
            "Local and page shortcuts share the same explanatory tooltip");
        controller.ClearLocalShortcuts();
        Check(!FinanceHelp.Tooltip(root,save).Contains("Ctrl+N")&&FinanceHelp.Tooltip(root,save).Contains("Ctrl+S"),
            "Clearing a local shortcut removes its hint without removing the page shortcut");
        var other=new ModernButton{Text="Save"};root.Controls.Add(other);controller.RefreshHints();
        Check(!FinanceHelp.Tooltip(root,save).Contains("Ctrl+S")&&FinanceHelp.Tooltip(root,save).Contains("Example:"),
            "An ambiguous action loses its shortcut hint while retaining the explanatory help");
        root.Controls.Remove(other);other.Dispose();controller.RefreshHints();
        Check(FinanceHelp.Tooltip(root,save).Contains("Ctrl+S"),"The shortcut hint returns when its action is unique again");
        var zoom=new ModernComboBox();root.Controls.Add(zoom);
        controller.RegisterShortcut(zoom,Keys.Control|Keys.Oemplus,"Ctrl++",()=>{});
        controller.RegisterShortcut(zoom,Keys.Control|Keys.Add,"Ctrl++",()=>{});
        Check(shortcuts.GetToolTip(zoom)=="Ctrl++","Equivalent report-viewer keys appear once");
        var input=HumanUi.TextBox();root.Controls.Add(HumanUi.Field("Amount",input));
        FinanceHelp.Attach(root,input,FinanceHelp.Topic.Discount);
        using(var validator=new FormValidator())
        {
            validator.Fail(input,"Enter an amount.");
            var errors=(ToolTip)typeof(FormValidator).GetField("_tip",flags)!.GetValue(validator)!;
            Check(errors.GetToolTip(input)=="Enter an amount."&&string.IsNullOrEmpty(FinanceHelp.Tooltip(root,input)),
                "A validation error temporarily replaces the normal tooltip");
            validator.Clear(input);
            Check(string.IsNullOrEmpty(errors.GetToolTip(input))&&FinanceHelp.Tooltip(root,input).Contains("Example:"),
                "Clearing validation restores the original help");
            validator.Fail(input,"Enter an amount.");
        }
        Check(FinanceHelp.Tooltip(root,input).Contains("Example:"),"Disposing a validator also restores the original help");
        Check(input.Controls.Cast<Control>().All(child=>FinanceHelp.Tooltip(root,child).Contains("Example:")),
            "Wrapped native inputs recover their help after validation");

        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,CurrentUser.GetPermissions().ToArray());
        CurrentUser.SignIn(990,"Tooltip fixture","Administrator",false,Array.Empty<string>());
        try
        {
            using var dashboard=new FMSC_SMS.MainDashboardForm((_,_)=>{});
            var sidebar=(ToolTip)typeof(FMSC_SMS.MainDashboardForm).GetField("sidebarToolTip",flags)!.GetValue(dashboard)!;
            var shellTips=(ToolTip)typeof(ApplicationWindowForm).GetField("saveShortcutTip",flags)!.GetValue(dashboard)!;
            var targets=(IDictionary)typeof(FMSC_SMS.MainDashboardForm).GetField("navigationShortcutTargets",flags)!.GetValue(dashboard)!;
            Check(targets.Count>0,"Dashboard fixture registers navigation shortcuts");
            foreach(DictionaryEntry entry in targets)
            {
                var target=(Control)entry.Value!;
                var shortcut=AppHotKeys.DashboardNavigation.Single(item=>item.Keys==(Keys)entry.Key);
                string combined=sidebar.GetToolTip(target)??"";
                Check(combined.Contains(shortcut.Caption)&&combined.Split(shortcut.Hint).Length==2&&string.IsNullOrEmpty(shellTips.GetToolTip(target)),
                    "Dashboard navigation has one complete tooltip: "+shortcut.Caption);
            }
            Check(!dashboard.IsHandleCreated,"Tooltip checks never start dashboard database loading");
        }
        finally{CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Item5);}
    }
}
