using System.Data;
using FMSC_SMS.Core;
using FMSC_SMS.Services.Communication;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Pages.Communication;

public sealed partial class CommunicationCenterPage
{
    private readonly Dictionary<DataGridView,(ModernButton Previous,ModernButton Next,Func<int> Page,string Permission)> paging=new();
    private string dispatcherError="";
    private string sendingSummary="";
    private Task<DataTable> ReadAsync(string view,int page=0,string search="",string filter="",params SqlParameter[] scope)=>
        read(view,[SqlCommunicationStore.P("@Page",page),SqlCommunicationStore.P("@Search",search),SqlCommunicationStore.P("@Filter",filter),..scope]);

    private string EmptyRecipients()=>CurrentUser.HasPermission("Communication.Configure")
        ? "No saved contacts match these filters. Clear or widen the filters, or open Contacts / Consent → Add From School Records to register a guardian or staff contact. Student phone numbers are not added as consented contacts automatically."
        : "No saved contacts match these filters. Clear or widen the filters, or ask an authorized administrator to add and verify the contact and record consent.";
    private void ConfigureGuidance()
    {
        foreach(var item in new[]{(recipients,"Find contacts to choose recipients. "+EmptyRecipients()),(preview,"Choose a reviewed template and contacts, then select Preview Messages. No message is sent by previewing."),
            (history,"No messages match this view. Create a draft in Compose / Bulk, or clear the search and state filter. Scheduled work appears after a campaign is submitted."),
            (contacts,"No communication contacts are recorded on this page. Use Add From School Records or Add Manual Contact, then verify ownership and record consent."),
            (templates,"No templates are available on this page. Return to the previous page or use New Version / Review."),
            (rules,"No automation rules are configured. Review a student-context template first, then use New / Edit Rule. Rules remain disabled until explicitly enabled.")})
        {UiHelper.SetGridEmptyMessage(item.Item1,item.Item2);UiHelper.SetGridPlaceholderMessage(item.Item1,item.Item2);}
        tabs.Selecting+=(_,e)=>{if(IsBusy)e.Cancel=true;};
        tabs.SelectedIndexChanged+=async(_,_)=>{
            ShowTabGuidance();
            if(installed&&!IsBusy)await ExecuteAsync(LoadSelectedTab);
        };
        void HistoryFiltersChanged()
        {historyPage=0;history.DataSource=null;UiHelper.SetGridState(history,UiHelper.GridDisplayState.Waiting,"Search or state filter changed. Select Search to load matching messages.");UpdatePaging(history);}
        historySearch.TextChanged+=(_,_)=>HistoryFiltersChanged();historyState.SelectedIndexChanged+=(_,_)=>HistoryFiltersChanged();
        ShowTabGuidance();
    }
    private void ShowTabGuidance()
    {
        SetPageHeaderText("Communication Center",tabs.SelectedTab?.Text??"");
        string text=tabs.SelectedIndex switch {
            0=>CurrentUser.HasPermission("Communication.Compose")?"1. Choose channel and reviewed template. 2. Find and select saved contacts. 3. Preview, then Create Draft. Open Outbox to submit; creating a draft sends nothing.":"Your account can view communication records. Open Outbox / Schedule / History; composing requires Communication.Compose.",
            1=>"Select a message to review its campaign. Submit a draft, approve when required, then run the dispatcher for queued work. Accepted by provider is not proof of delivery.",
            2=>CurrentUser.HasPermission("Communication.Configure")?"Add a contact from school records or manually. Verify who controls the number and record channel/purpose consent. Saving a contact does not send a message.":"Contact and consent administration requires Communication.Configure. Ask an authorized administrator to add or update a contact.",
            3=>"Select a template to review its text, language and recipient context. New Version / Review saves an immutable version; approve it before composing. WhatsApp also needs an approved provider mapping.",
            4=>CurrentUser.HasPermission("Communication.Configure")?"Configure future-event rules only after contacts, consent and templates are ready. Evaluation prepares eligible campaigns; approval and dispatcher controls still apply.":"Automation configuration requires Communication.Configure. Ask an authorized administrator to review the rules.",
            _=>CurrentUser.HasPermission("Communication.Configure")?"Start with Simulation to rehearse without external delivery. Configure budgets, sender credentials and approved templates before explicitly enabling Live. Credentials are protected for this Windows user.":"Gateway configuration requires Communication.Configure. Ask an authorized administrator; no credentials are displayed here."
        };
        SetHeaderGuidance(text);
    }
    private async Task LoadSelectedTab()
    {
        switch(tabs.SelectedIndex)
        {
            case 0: await RefreshRecipients();break;
            case 1: await RefreshHistory();break;
            case 2: if(CurrentUser.HasPermission("Communication.Configure"))await RefreshContacts();break;
            case 3: await RefreshTemplates();break;
            case 4: if(CurrentUser.HasPermission("Communication.Configure"))await RefreshRules();break;
        }
    }
    private Control[] Paging(DataGridView grid,Func<int> page,Action<int> setPage,Func<Task> refresh,string permission="Communication.View")
    {
        var previous=Button("Previous",async()=>{setPage(Math.Max(0,page()-1));await refresh();},permission);
        var next=Button("Next",async()=>{setPage(Math.Min(100000,page()+1));await refresh();},permission);
        paging.Add(grid,(previous,next,page,permission));UpdatePaging(grid);return [previous,next];
    }
    private void UpdatePaging(DataGridView grid)
    {
        if(!paging.TryGetValue(grid,out var controls))return;
        bool allowed=CurrentUser.HasPermission(controls.Permission);
        controls.Previous.Enabled=allowed && controls.Page()>0;
        controls.Next.Enabled=allowed && controls.Page()<100000 && grid.DataSource is DataTable rows && rows.Rows.Count==100;
    }
    private async Task LoadGridAsync(DataGridView grid,Func<Task<DataTable>> load,Action<DataTable>? bind=null,string? empty=null)
    {
        UiHelper.SetGridState(grid,UiHelper.GridDisplayState.Loading,"Loading records… Please wait.");grid.Enabled=false;
        try
        {
            var rows=await load();if(IsDisposed||grid.IsDisposed)return;
            if(bind is null)grid.DataSource=rows;else bind(rows);
            UiHelper.ClearGridState(grid);grid.Enabled=true;
            if(empty is not null){UiHelper.SetGridEmptyMessage(grid,empty);UiHelper.SetGridPlaceholderMessage(grid,empty);}
            UpdatePaging(grid);
            if(rows.Rows.Count==0)SetStatus(empty??Convert.ToString(grid.Tag)??"No records found. Try a different filter.");
        }
        catch
        {
            if(!IsDisposed&&!grid.IsDisposed){grid.DataSource=null;UiHelper.SetGridState(grid,UiHelper.GridDisplayState.Failed,"Records could not be loaded. Use Refresh or Search to try again.");UpdatePaging(grid);}
            throw;
        }
    }
    private void RecipientFiltersChanged()
    {
        recipientPage=0;recipients.DataSource=null;recipients.Enabled=false;InvalidatePreview();UpdatePaging(recipients);
        UiHelper.SetGridState(recipients,UiHelper.GridDisplayState.Waiting,"Filters changed. Select Find Contacts to load matching records. Earlier selections are retained; use Clear Selection to start again.");
    }
    private void ResetComposition()
    {
        included.Clear();announcement.Text="";schedule.Checked=false;requestId=Guid.NewGuid();requestDue=null;savedSignature="";
        preview.DataSource=null;InvalidatePreview();ApplyTicks();
        UiHelper.SetGridState(preview,UiHelper.GridDisplayState.Waiting,"Choose a reviewed template and contacts, then select Preview Messages.");
        SetStatus("New message ready. Select contacts and review a fresh preview before creating a draft.");
    }
    private async Task AfterSavedAsync(Func<Task> refresh,string message)
    {
        try{await refresh();if(!IsDisposed)SetStatus(message);}
        catch(OperationCanceledException) when(lifetime.IsCancellationRequested){}
        catch(Exception){if(!IsDisposed)SetStatus(message+" The saved action completed, but the list could not be refreshed. Use Refresh; do not repeat the saved action.",PageStatusKind.Warning);}
    }
}
