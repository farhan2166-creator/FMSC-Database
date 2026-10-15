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
            var tabs=Field<TabControl>("tabs");Check(tabs.TabPages.Count==6&&!tabs.Enabled,"All communication workspaces remain disabled until the schema contract is verified");
            Field<ModernTextBox>("announcement").Text="Fictional school notice";
            Check(page.HasUnsavedChanges,"A substantive unsaved message gets the existing navigation guard");
            var dispatcher=(Task?)typeof(CommunicationCenterPage).GetField("dispatchTask",flags)!.GetValue(page);
            Check(dispatcher is null,"Opening the workspace never starts a dispatcher or live send");
            foreach(var size in new[]{new Size(1000,620),new Size(1800,1000)})
            {
                parent.ClientSize=size;Application.DoEvents();
                Check(tabs.Width>0&&tabs.Height>0,"Communication tabs retain a usable client area");
                Check(tabs.TabPages[0].Controls.OfType<Panel>().Single().AutoScroll,"Compose retains scrolling when its content exceeds the available client area");
            }
            foreach(var selected in new[]{AppThemeType.OceanBlue,AppThemeType.ProfessionalSimple})
            {AppTheme.SetTheme(selected);Application.DoEvents();Check(Field<ModernTextBox>("announcement").Text=="Fictional school notice","Theme changes preserve the communication draft");}
            page.Dispose();Check(page.IsDisposed&&connections==0,"Communication disposal performs no hidden SQL, dispatch or live network operation");
        }
        finally{AppTheme.SetTheme(theme);CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Item5);}
    }
}
