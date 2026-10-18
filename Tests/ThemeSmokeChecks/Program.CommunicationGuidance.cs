using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Communication;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Services.Communication;
using Microsoft.Data.SqlClient;

internal static partial class Program
{
    private static void CheckCommunicationGuidance()
    {
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,CurrentUser.GetPermissions().ToArray());
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        try
        {
            CurrentUser.SignIn(919,"Guidance fixture","Administrator",false,Array.Empty<string>());
            int connections=0,reads=0;bool fail=false;DataTable? templateReply=null;TaskCompletionSource<DataTable>? pending=null;
            DataTable Rows(string view,SqlParameter[] args)
            {
                reads++;if(fail)throw new IOException("Synthetic read refusal");
                if(view=="Templates"&&templateReply is not null)return templateReply.Copy();
                var rows=new DataTable();
                if(view=="Templates")
                {rows.Columns.Add("TemplateId",typeof(Guid));rows.Columns.Add("Approved",typeof(bool));rows.Columns.Add("Code",typeof(string));rows.Columns.Add("Language",typeof(string));rows.Columns.Add("Version",typeof(int));}
                else if(view=="Recipients"||view=="Contacts")rows.Columns.Add("ContactId",typeof(Guid));
                else if(view is "History" or "Outbox"){rows.Columns.Add("MessageId",typeof(Guid));rows.Columns.Add("CampaignId",typeof(Guid));}
                else rows.Columns.Add("RuleId",typeof(Guid));
                return rows;
            }
            var store=new SqlCommunicationStore(()=>{connections++;throw new Exception("UI fixture must not connect to SQL.");},919);
            using var page=new CommunicationCenterPage(store,false,(view,args)=>pending is not null&&view is "History" or "Outbox"?pending.Task:Task.FromResult(Rows(view,args)));
            T Field<T>(string name)=>(T)typeof(CommunicationCenterPage).GetField(name,flags)!.GetValue(page)!;
            object? Invoke(string name,params object[] args)=>typeof(CommunicationCenterPage).GetMethod(name,flags)!.Invoke(page,args);
            void Await(string name){var task=(Task)Invoke(name)!;PumpUntil(()=>task.IsCompleted,"Communication "+name+" settles");task.GetAwaiter().GetResult();}
            string Status()=>((Label)typeof(V2Page).GetField("StatusLabel",flags)!.GetValue(page)!).Text;
            using var parent=new Form{ClientSize=new Size(1100,720)};parent.Controls.Add(page);parent.Show();Application.DoEvents();
            var tabs=Field<TabControl>("tabs");typeof(CommunicationCenterPage).GetField("installed",flags)!.SetValue(page,true);tabs.Enabled=true;
            for(int index=1;index<6;index++)
            {
                tabs.SelectedIndex=index;PumpUntil(()=>!page.IsBusy,"Tab data load settles");
                Check(tabs.SelectedIndex==index,"Busy refresh preserves the operator's selected tab");
                Check(!string.IsNullOrWhiteSpace((string?)typeof(V2Page).GetField("_headerGuidance",flags)!.GetValue(page)),"Every communication tab provides contextual next-step guidance");
            }
            var history=Field<DataGridView>("history");
            tabs.SelectedIndex=0;PumpUntil(()=>!page.IsBusy,"Compose opens before delayed refresh");
            Field<ModernTextBox>("announcement").Text="Preserve this fictional draft";
            pending=new TaskCompletionSource<DataTable>();tabs.SelectedIndex=1;
            Check(page.IsBusy,"Outbox exposes the real shared busy contract while a read is pending");
            tabs.SelectedIndex=2;Check(tabs.SelectedIndex==1,"A pending refresh vetoes tab changes consistently");
            pending.SetResult(Rows("History",[]));pending=null;PumpUntil(()=>!page.IsBusy,"Delayed communication refresh finishes");
            Check(tabs.SelectedIndex==1&&Field<ModernTextBox>("announcement").Text=="Preserve this fictional draft","Delayed refresh preserves selected tab and unsaved composition");
            Check(Descendants(tabs.Parent!.Parent!).OfType<ModernButton>().Count(b=>b.ThemeRole=="TabActive")==1,"Busy disable/restore leaves exactly one tab selected");
            tabs.SelectedIndex=1;PumpUntil(()=>!page.IsBusy,"Outbox opens");
            Check(Status().Contains("No messages")&&Convert.ToString(history.Tag)!.Contains("Create a draft"),"An empty outbox explains how to begin and how to widen filters");
            Check(!Descendants(tabs.TabPages[1]).OfType<ModernButton>().Single(b=>b.Text=="Submit").Enabled,"No campaign-selection action is enabled on an empty outbox");
            Field<ModernTextBox>("historySearch").Text="Fictional search";
            Check(history.DataSource is null,"Changed history filters clear stale campaign selection before another action");
            fail=true;
            var failedRead=(Task)Invoke("ExecuteAsync",(Func<Task>)(()=>(Task)Invoke("RefreshHistory")!))!;
            PumpUntil(()=>failedRead.IsCompleted,"Failed communication read settles");
            Check(failedRead.IsCompletedSuccessfully&&history.DataSource is null&&!history.Enabled&&!string.IsNullOrWhiteSpace(Status()),"A failed read clears stale rows, disables row actions and reaches the inline error boundary");
            var saved=(Task)Invoke("AfterSavedAsync",(Func<Task>)(()=>Task.FromException(new IOException("Synthetic post-save refresh refusal"))),"Contact and consent saved.")!;
            PumpUntil(()=>saved.IsCompleted,"Saved-but-refresh-failed feedback settles");
            Check(Status().Contains("Contact and consent saved")&&Status().Contains("do not repeat"),"A post-save read failure never disguises a completed save as a failed write");
            fail=false;Await("RefreshHistory");Check(history.Enabled,"Refreshing after a read failure restores the grid");
            tabs.SelectedIndex=0;PumpUntil(()=>!page.IsBusy,"Compose opens");
            var recipients=Field<DataGridView>("recipients");
            Check(Convert.ToString(recipients.Tag)!.Contains("Add From School Records")&&Status().Contains("No saved contacts"),"Empty recipient results explain filters and consented-contact setup without inventing an active-student restriction");
            var included=Field<HashSet<Guid>>("included");included.Add(Guid.NewGuid());
            Field<ModernTextBox>("search").Text="Changed student filter";
            Check(!recipients.Enabled&&included.Count==1&&!Field<ModernButton>("prepare").Enabled,"Changing recipient filters blocks stale rows and preview while preserving the explicit cross-page selection");
            Field<ModernTextBox>("announcement").Text="Keep the fictional draft";Field<ModernDatePicker>("schedule").Checked=true;
            Invoke("ResetComposition");
            Check(included.Count==0&&!Field<ModernDatePicker>("schedule").Checked&&Field<ModernTextBox>("announcement").Text==""&&!page.HasUnsavedChanges,"New Message clears old recipients, text and schedule without carrying a previous campaign's send date");
            var templateTable=Rows("Templates",[]);
            for(int i=0;i<100;i++)templateTable.Rows.Add(Guid.NewGuid(),true,"fixture_"+i,"en",1);
            templateReply=templateTable;Await("RefreshTemplates");
            var templateButtons=Descendants(tabs.TabPages[3]).OfType<ModernButton>().ToArray();
            Check(templateButtons.Single(b=>b.Text=="Next").Enabled&&!templateButtons.Single(b=>b.Text=="Previous").Enabled,"Paging enables Next only for a full page and disables Previous at the start");
            Guid chosen=(Guid)templateTable.Rows[0]["TemplateId"];Field<ModernComboBox>("templateChoice").SelectedValue=chosen;
            templateReply=templateTable.Clone();templateReply.Rows.Add(Guid.NewGuid(),true,"another_page","en",1);
            typeof(CommunicationCenterPage).GetField("templatePage",flags)!.SetValue(page,1);Await("RefreshTemplates");
            Check(Equals(Field<ModernComboBox>("templateChoice").SelectedValue,chosen),"Browsing another template page preserves the composition's chosen template");
            Check(!templateButtons.Single(b=>b.Text=="Next").Enabled&&templateButtons.Single(b=>b.Text=="Previous").Enabled,"A short or empty result page stops Next while allowing a return");
            templateReply=templateTable.Clone();templateReply.Rows.Add(chosen,false,"revoked_fixture","en",1);Await("RefreshTemplates");
            Check(Field<ModernComboBox>("templateChoice").SelectedIndex==-1,"A known revoked template is removed rather than silently replaced by a different template");
            Check(connections==0&&reads>0,"Communication workflow fixtures exercise injected reads only, without SQL, writes or external delivery");
            CurrentUser.SignIn(920,"View-only fixture","Operator",false,["Communication.View"]);
            using var restricted=new CommunicationCenterPage(store,false);
            var restrictedTabs=(TabControl)typeof(CommunicationCenterPage).GetField("tabs",flags)!.GetValue(restricted)!;
            Check(restrictedTabs.SelectedIndex==1,"A view-only operator starts in the outbox rather than an unavailable compose workflow");
            restrictedTabs.SelectedIndex=2;
            typeof(CommunicationCenterPage).GetMethod("ShowTabGuidance",flags)!.Invoke(restricted,null);
            Check(((string)typeof(V2Page).GetField("_headerGuidance",flags)!.GetValue(restricted)!).Contains("requires Communication.Configure"),"Restricted operators get permission-aware guidance rather than unexplained empty administration tabs");
        }
        finally{CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Item5);}
    }
}
