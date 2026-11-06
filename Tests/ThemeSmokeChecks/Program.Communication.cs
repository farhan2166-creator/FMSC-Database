using System.Reflection;
using FMSC_SMS;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Communication;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Services.Communication;

internal static partial class Program
{
    private static void CheckCommunicationWorkspace()
    {
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,CurrentUser.GetPermissions().ToArray());
        var theme=AppTheme.CurrentTheme;const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        try
        {
            foreach(var access in new[]{(Role:"Accountant",Permissions:new[]{"Students.View"},Allowed:false),(Role:"Operator",Permissions:new[]{"Communication.View"},Allowed:true),(Role:"Administrator",Permissions:Array.Empty<string>(),Allowed:true)})
            {
                CurrentUser.SignIn(919,"Communication fixture",access.Role,false,access.Permissions);
                using var shell=new MainDashboardForm((_,_)=>{});
                var commands=(List<NavigationCommand>)typeof(MainDashboardForm).GetField("navigationCommands",flags)!.GetValue(shell)!;
                Check(commands.Count(c=>c.Caption=="Communication Center")== (access.Allowed?1:0),"Communication navigation uses explicit permission and preserves Administrator authority without expanding Accountant access");
            }
            CurrentUser.SignIn(919,"Communication fixture","Administrator",false,Array.Empty<string>());
            int connections=0;var store=new SqlCommunicationStore(()=>{connections++;throw new Exception("Native control fixture must not connect to SQL.");},919);
            using var page=new CommunicationCenterPage(store,false);
            T Field<T>(string name)=>(T)typeof(CommunicationCenterPage).GetField(name,flags)!.GetValue(page)!;
            using var parent=new Form{ClientSize=new Size(1000,620)};parent.Controls.Add(page);parent.Show();Application.DoEvents();
            Check(!page.IsBusy&&!page.HasUnsavedChanges,"An unopened communication draft starts clean and idle");
            var tabs=Field<TabControl>("tabs");
            Check(tabs.TabPages.Count==3&&!tabs.Enabled,"Administrator sees Send Message, History and Settings pending schema verification");
            var settingsButtons=Descendants(tabs.TabPages[2]).OfType<ModernButton>().Select(button=>button.Text).ToArray();
            Check(settingsButtons.Contains("Existing Automations") && settingsButtons.Contains("Disable Selected Rule"),
                "Previously configured rules remain reviewable and explicitly disableable without a new automation enable switch");
            Check(!settingsButtons.Contains("New / Edit Rule"),
                "New unattended automation is not enabled from the simplified Settings interface");
            Check(settingsButtons.Contains("Try a Sample"),
                "Administrator can test a purely simulated practice message without provider delivery");
            Check(!Field<Control>("schedule").Visible && !Field<Control>("prepare").Visible,"Scheduling and drafts are hidden by default");
            typeof(CommunicationCenterPage).GetField("installed",flags)!.SetValue(page,true);
            tabs.Enabled=true;
            Descendants(page).OfType<ModernButton>().Single(button=>button.Text=="More Options").PerformClick();
            Check(Field<Control>("schedule").Visible&&Field<Control>("prepare").Visible,"More Options exposes scheduled and draft workflows without another module navigation layer");
            typeof(CommunicationCenterPage).GetField("installed",flags)!.SetValue(page,false);
            Field<ModernTextBox>("announcement").Text="Fictional school notice";
            Check(page.HasUnsavedChanges,"A substantive unsaved message gets the existing navigation guard");
            var dispatcher=(Task?)typeof(CommunicationCenterPage).GetField("dispatchTask",flags)!.GetValue(page);
            Check(dispatcher is null,"Opening the workspace never starts a dispatcher or live send");
            foreach(var size in new[]{new Size(1000,620),new Size(1800,1000)})
            {
                parent.ClientSize=size;Application.DoEvents();
                Check(tabs.Width>0&&tabs.Height>0,"Communication tabs retain a usable client area");
                Check(tabs.TabPages[0].Controls.OfType<Panel>().Single().AutoScroll,"Compose retains scrolling when its content exceeds the available client area");
                foreach(string field in new[]{"channel","templateChoice","schedule"})
                {
                    var input=Field<Control>(field);
                    Check(input.Visible&&input.Width>=AppScale.Scale(100)&&input.Height>0,"Compose message settings have visible, usable bounds");
                    Check(input.Parent!.Parent is TableLayoutPanel layout&&layout.GetRow(input.Parent)==0,"Compose message settings share the first layout row");
                }
                Field<ModernComboBox>("audience").SelectedIndex=3;
                var clear=Descendants(tabs.TabPages[0]).OfType<ModernButton>().Single(b=>b.Text=="Clear Selection");
                Check(clear.Bottom<=clear.Parent!.ClientSize.Height,"Compose toolbar grows to fit wrapped actions at smaller window sizes");
            }
            foreach(var selected in new[]{AppThemeType.OceanBlue,AppThemeType.ProfessionalSimple})
            {AppTheme.SetTheme(selected);Application.DoEvents();Check(Field<ModernTextBox>("announcement").Text=="Fictional school notice","Theme changes preserve the communication draft");}
            tabs.Enabled=true;
            var tabButtons=Descendants(tabs.Parent!.Parent!).OfType<ModernButton>().Where(b=>b.ThemeRole is "TabActive" or "TabInactive").ToArray();
            Check(tabButtons.Length==3,"Communication uses only three main navigation sections");
            var first=tabButtons.Single(b=>b.Text=="Send Message");
            foreach(var selectedTheme in AppTheme.SelectableThemes)
            {
                AppTheme.SetTheme(selectedTheme);Application.DoEvents();
                for(int target=1;target<3;target++)
                {
                    tabs.SelectedIndex=0;Check(first.Focus(),"Compose tab can receive keyboard focus");
                    tabs.SelectedIndex=target;Application.DoEvents();
                    Check(tabButtons.Single(b=>b.ThemeRole=="TabActive").Focused,"Switching tabs moves retained navigation focus to the selected tab");
                    first.Focus();Application.DoEvents();
                    Check(tabs.SelectedIndex==target&&tabs.SelectedTab!.Visible&&!tabs.TabPages[0].Visible,"Selecting another tab shows only its content");
                    Check(first.Focused&&first.ThemeRole=="TabInactive"&&tabButtons.Count(b=>b.ThemeRole=="TabActive")==1,"Retained keyboard focus never makes Compose the selected tab");
                    first.ApplyTheme();using var image=new Bitmap(first.Width,first.Height);first.DrawToBitmap(image,first.ClientRectangle);
                    Check(image.GetPixel(Math.Min(10,first.Width/2),first.Height/2).ToArgb()==AppTheme.SurfaceSecondary.ToArgb(),"A focused inactive Compose tab keeps its inactive fill across themes");
                }
            }
            page.Dispose();Check(page.IsDisposed&&connections==0,"Communication disposal performs no hidden SQL, dispatch or live network operation");
        }
        finally{AppTheme.SetTheme(theme);CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Item5);}
    }
}
