using System.Data;
using System.Text.Json;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Services.Communication;

namespace FMSC_SMS.Pages.Communication;

public sealed partial class CommunicationCenterPage : V2Page,IBusyPage,IUnsavedChanges
{
    private readonly SqlCommunicationStore store;
    private readonly CommunicationService service;
    private readonly WindowsGatewayStore gateways=new();
    private static readonly HttpClient Http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(30)};
    private readonly MessageDispatcher dispatcher;
    private readonly CancellationTokenSource lifetime=new();
    private CancellationTokenSource? dispatchStop;
    private Task? dispatchTask;
    private bool installed;
    private readonly Label operational=new(){Dock=DockStyle.Top,Height=AppScale.Scale(46),Text="Checking communication database readiness…",Font=AppTheme.ButtonFont,ForeColor=AppTheme.MutedText};
    private readonly TabControl tabs=new(){Dock=DockStyle.Fill};
    private readonly HashSet<Guid> included=new();
    private readonly DataGridView recipients=HumanUi.Grid(),preview=HumanUi.Grid(),history=HumanUi.Grid(),contacts=HumanUi.Grid(),templates=HumanUi.Grid(),rules=HumanUi.Grid();
    private readonly ModernTextBox search=HumanUi.TextBox(false,100),announcement=HumanUi.TextBox(true,1000);
    private readonly LookupBox campus=new(),year=new(),cls=new(),section=new();
    private readonly ModernComboBox channel=HumanUi.Combo(),recipientFilter=HumanUi.Combo(),templateChoice=HumanUi.Combo();
    private readonly ModernTextBox historySearch=HumanUi.TextBox(false,100);
    private readonly ModernComboBox historyState=HumanUi.Combo();
    private readonly ModernDatePicker schedule=HumanUi.Date();
    private readonly Label selection=new(){Dock=DockStyle.Top,Height=30,Font=AppTheme.NormalFont,ForeColor=AppTheme.Text};
    private readonly ModernButton prepare=new(){Text="Create Draft",ThemeRole="Primary",Enabled=false};
    private int recipientPage,historyPage,contactPage,templatePage;
    private Guid requestId=Guid.NewGuid();private DateTime? requestDue;private string previewSignature="",savedSignature="";
    private bool populating;
    public bool IsBusy=>BusyOperationRunning||dispatcher.IsSubmitting;
    public string BusyMessage=>"A communication operation or provider submission is finishing. Wait for its recorded outcome.";
    public bool HasUnsavedChanges=>(announcement.Text.Trim().Length>0||included.Count>0) && Signature()!=savedSignature;
    public string UnsavedChangesMessage=>"This message composition has not been saved as a campaign. Discard it?";
    public CommunicationCenterPage():this(new SqlCommunicationStore(DatabaseHelper.GetConnection,CurrentUser.UserId),true){}
    internal CommunicationCenterPage(SqlCommunicationStore store,bool initialize):base("Communication Center","Compose and track external SMS and WhatsApp messages. Live delivery requires approved gateway setup.")
    {
        this.store=store;service=new(store);dispatcher=new(store,c=>gateways.Provider(c,Http));
        BuildWorkspace();
        Disposed+=(_,_)=>{lifetime.Cancel();dispatchStop?.Cancel();};
        if(initialize)Defer(InitializeAsync);
    }
    private async Task InitializeAsync()
    {
        await ExecuteAsync(async()=>{
            installed=await store.IsInstalledAsync(lifetime.Token);
            tabs.Enabled=installed;
            if(!installed){operational.Text="Communication is not installed or its schema differs from the reviewed add-on. See the communication deployment guide; no database changes were made.";return;}
            await RefreshOperational();
            await RefreshTemplates();await RefreshRecipients();
            foreach(var item in new[]{(campus,"Campuses","CampusId","CampusName"),(year,"Years","AcademicYearId","YearName"),(cls,"Classes","ClassId","ClassName"),(section,"Sections","SectionId","SectionName")})
                item.Item1.Bind(await store.ReadAsync(item.Item2,token:lifetime.Token),item.Item3,item.Item4);
        });
    }
    private async Task ExecuteAsync(Func<Task> action)
    {
        try{await RunBusyAsync("Loading communication data…",action,this);}
        catch(OperationCanceledException) when(lifetime.IsCancellationRequested){}
        catch(Exception ex){if(!IsDisposed)SetStatus(DbMessage.UserMessage(ex),PageStatusKind.Warning);}
    }
    private ModernButton Button(string text,Func<Task> action,string permission="Communication.View",string role="Secondary")
    {
        var b=new ModernButton{Text=text,ThemeRole=role,Height=AppMetrics.ButtonHeight,Margin=new Padding(4)};AppMetrics.FitButtonWidth(b,minWidth:100);
        b.Enabled=CurrentUser.HasPermission(permission);
        b.Click+=async(_,_)=>{
            if(!CurrentUser.HasPermission(permission)||!installed){SetStatus("This action is unavailable for your account or the communication schema is not ready.",PageStatusKind.Warning);return;}
            await ExecuteAsync(action);
        };return b;
    }
    private static FlowLayoutPanel Bar(params Control[] controls)
    {var p=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Margin=Padding.Empty,Padding=new Padding(0,4,0,4)};p.Controls.AddRange(controls);return p;}
    private static TabPage Tab(string title,Control content)
    {var tab=new TabPage(title){BackColor=AppTheme.PageBackground,Padding=new Padding(AppMetrics.SpacingSm)};content.Dock=DockStyle.Fill;tab.Controls.Add(content);return tab;}
    private static Panel ListHost(DataGridView grid,Control toolbar)
    {var p=new Panel{Dock=DockStyle.Fill};p.Controls.Add(grid);p.Controls.Add(toolbar);return p;}
    private void BuildWorkspace()
    {
        channel.Items.AddRange(["Sms","WhatsApp"]);channel.SelectedIndex=0;
        recipientFilter.Items.AddRange(["Student / family / manual","Outstanding","Family","Staff","Manual","Pending"]);recipientFilter.SelectedIndex=0;
        channel.Width=AppScale.Scale(120);recipientFilter.Width=AppScale.Scale(150);templateChoice.Width=AppScale.Scale(320);
        schedule.ShowCheckBox=true;schedule.Checked=false;schedule.Format=DateTimePickerFormat.Custom;schedule.CustomFormat="dd-MMM-yyyy HH:mm";
        announcement.Height=AppScale.Scale(80);announcement.MinimumSize=new Size(0,AppScale.Scale(80));
        var compose=new Panel{Dock=DockStyle.Fill,AutoScroll=true};
        var root=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,RowCount=0,Margin=Padding.Empty};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        void Row(Control c,int h){c.Dock=DockStyle.Fill;root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppScale.Scale(h)));root.Controls.Add(c,0,root.RowCount++);}
        Row(Bar(HumanUi.Field("Channel",channel),HumanUi.Field("Reviewed template",templateChoice),HumanUi.Field("Send after (local time, optional)",schedule)),100);
        Row(HumanUi.Field("Announcement text (only the approved announcement field can be customized)",announcement),120);
        var filters=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1};
        foreach(var item in new[]{("Campus",campus),("Academic year",year),("Class",cls),("Section",section)}){filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));filters.Controls.Add(HumanUi.Field(item.Item1,item.Item2),filters.Controls.Count,0);}
        Row(filters,90);
        search.PlaceholderText="Contact or student name";search.Width=220;
        Row(Bar(recipientFilter,search,Button("Find Contacts",async()=>{recipientPage=0;await RefreshRecipients();},"Communication.Compose"),
            Button("Previous",async()=>{recipientPage=Math.Max(0,recipientPage-1);await RefreshRecipients();},"Communication.Compose"),
            Button("Next",async()=>{recipientPage++;await RefreshRecipients();},"Communication.Compose"),
            Button("Clear Selection",()=>{included.Clear();InvalidatePreview();ApplyTicks();return Task.CompletedTask;},"Communication.Compose")),100);
        Row(selection,30);Row(recipients,220);
        recipients.ReadOnly=false;
        recipients.CurrentCellDirtyStateChanged+=(_,_)=>{if(recipients.IsCurrentCellDirty)recipients.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        recipients.CellValueChanged+=(_,e)=>{if(populating||e.RowIndex<0||e.ColumnIndex<0||recipients.Columns[e.ColumnIndex].Name!="Include")return;
            var row=recipients.Rows[e.RowIndex];Guid id=(Guid)row.Cells["ContactId"].Value;
            if(Convert.ToBoolean(row.Cells["Include"].Value??false))included.Add(id);else included.Remove(id);InvalidatePreview();};
        prepare.Click+=async(_,_)=>{if(!CurrentUser.HasPermission("Communication.Compose")||previewSignature!=Signature()||!installed)return;
            await ExecuteAsync(async()=>{
                if(!ModernMessageDialog.Confirm(FindForm(),$"Create a durable draft for {included.Count} selected contacts? No message is sent until Submit is confirmed.","Create Communication Draft"))return;
                requestDue??=schedule.Checked?schedule.Value.ToUniversalTime():DateTime.UtcNow;
                await service.PrepareAsync(requestId,SelectedTemplate(),SelectedChannel(),included,announcement.Text,requestDue.Value,lifetime.Token);
                savedSignature=Signature();SetStatus("Draft saved: "+requestId+". Review it in Outbox, then Submit. Approval may be required.");
                prepare.Enabled=false;
            });};
        Row(Bar(Button("Preview Messages",PreviewAsync,"Communication.Compose","Primary"),prepare,Button("New Message",()=>{
            if(HasUnsavedChanges&&!ModernMessageDialog.ConfirmDiscardIfDirty(FindForm(),()=>HasUnsavedChanges,UnsavedChangesMessage))return Task.CompletedTask;
            included.Clear();announcement.Text="";requestId=Guid.NewGuid();requestDue=null;savedSignature="";preview.DataSource=null;InvalidatePreview();ApplyTicks();return Task.CompletedTask;
        },"Communication.Compose")),60);
        Row(preview,260);compose.Controls.Add(root);
        compose.Resize+=(_,_)=>root.Width=Math.Max(AppScale.Scale(760),compose.ClientSize.Width-SystemInformation.VerticalScrollBarWidth);
        root.Width=AppScale.Scale(900);
        foreach(Control c in new Control[]{channel,templateChoice,announcement,schedule})
        {if(c is ModernComboBox cb)cb.SelectedIndexChanged+=(_,_)=>InvalidatePreview();else if(c is ModernDatePicker dp)dp.ValueChanged+=(_,_)=>InvalidatePreview();else c.TextChanged+=(_,_)=>InvalidatePreview();}
        tabs.TabPages.Add(Tab("Compose / Bulk",compose));
        tabs.TabPages.Add(Tab("Outbox / Schedule / History",BuildHistory()));
        tabs.TabPages.Add(Tab("Contacts / Consent",BuildContacts()));
        tabs.TabPages.Add(Tab("Templates",ListHost(templates,Bar(Button("Refresh",RefreshTemplates),Button("New Version / Review",EditTemplate,"Communication.Configure"),Button("Revoke Selected",async()=>{var row=Selected(templates)??throw new ArgumentException("Select a template first.");if(!ModernMessageDialog.Confirm(FindForm(),"Revoke this template? Unsubmitted messages will be suppressed; past messages remain in history.","Revoke Template"))return;await store.CallAsync("RevokeTemplate",lifetime.Token,SqlCommunicationStore.P("@Id",row["TemplateId"]));await RefreshTemplates();},"Communication.Configure"),Button("Previous",async()=>{templatePage=Math.Max(0,templatePage-1);await RefreshTemplates();}),Button("Next",async()=>{templatePage++;await RefreshTemplates();})))));
        tabs.TabPages.Add(Tab("Automation Rules",ListHost(rules,Bar(Button("Refresh",RefreshRules,"Communication.Configure"),Button("New / Edit Rule",EditRule,"Communication.Configure"),Button("Evaluate Committed Events",EvaluateRules,"Communication.Configure")))));
        var gateway=new Panel{Dock=DockStyle.Fill,AutoScroll=true};gateway.Controls.Add(Bar(Button("Sending Mode / Limits",EditSettings,"Communication.Configure"),Button("Secure Gateway Credentials",EditGateway,"Communication.Configure")));
        tabs.TabPages.Add(Tab("Gateway Settings",gateway));
        tabs.Enabled=false;TabStrip.Apply(tabs,AppTheme.Primary);
        var content=new Panel{Dock=DockStyle.Fill};content.Controls.Add(TabStrip.CreateHost(tabs));content.Controls.Add(operational);SetContent(content);
        tabs.SelectedIndexChanged+=async(_,_)=>{if(!installed||IsBusy)return;if(tabs.SelectedIndex==1)await ExecuteAsync(RefreshHistory);if(tabs.SelectedIndex==2&&CurrentUser.HasPermission("Communication.Configure"))await ExecuteAsync(RefreshContacts);if(tabs.SelectedIndex==4)await ExecuteAsync(RefreshRules);};
    }
    private Guid SelectedTemplate()=>templateChoice.SelectedValue is Guid id?id:throw new ArgumentException("Choose a reviewed template.");
    private MessageChannel SelectedChannel()=>Enum.Parse<MessageChannel>(Convert.ToString(channel.SelectedItem)!);
    private string Signature()=>string.Join("|",templateChoice.SelectedValue,channel.SelectedItem,announcement.Text,string.Join(',',included.Order()),schedule.Checked,schedule.Checked?schedule.Value.ToUniversalTime().ToString("O"):"");
    private void InvalidatePreview(){previewSignature="";prepare.Enabled=false;selection.Text=$"{included.Count} contacts selected across all result pages. Preview required before saving.";}
    private void ApplyTicks()
    {populating=true;try{foreach(DataGridViewRow r in recipients.Rows)if(!r.IsNewRow)r.Cells["Include"].Value=included.Contains((Guid)r.Cells["ContactId"].Value);}finally{populating=false;}}
    private async Task RefreshRecipients()
    {
        if(!CurrentUser.HasPermission("Communication.Compose"))return;
        var rows=await store.CallAsync("Read",lifetime.Token,SqlCommunicationStore.P("@View","Recipients"),SqlCommunicationStore.P("@Page",recipientPage),SqlCommunicationStore.P("@Search",search.Text),
            SqlCommunicationStore.P("@Filter",recipientFilter.SelectedIndex==0?"":recipientFilter.SelectedItem),SqlCommunicationStore.P("@Campus",campus.SelectedId),SqlCommunicationStore.P("@Year",year.SelectedId),SqlCommunicationStore.P("@Class",cls.SelectedId),SqlCommunicationStore.P("@Section",section.SelectedId));
        if(IsDisposed)return;populating=true;
        try{rows.Columns.Add("Include",typeof(bool));recipients.DataSource=rows;foreach(DataGridViewColumn c in recipients.Columns)c.ReadOnly=c.Name!="Include";
            recipients.Columns["Include"].DisplayIndex=0;recipients.Columns["ContactId"].Visible=false;ApplyTicks();}
        finally{populating=false;}
        selection.Text=$"Page {recipientPage+1} · {rows.Rows.Count} contacts shown · {included.Count} selected in total. Missing consent is explained in Preview.";
    }
    private async Task PreviewAsync()
    {
        if(savedSignature.Length>0)throw new ArgumentException("This campaign snapshot is already saved. Use New Message for another campaign; cancel the old draft in Outbox if it is no longer needed.");
        if(included.Count==0)throw new ArgumentException("Select contacts first. Add verified, consented contacts in Contacts / Consent if none are listed.");
        string signature=Signature();var rows=await store.PreviewAsync(SelectedTemplate(),SelectedChannel(),included,announcement.Text,token:lifetime.Token);
        if(IsDisposed||Signature()!=signature)return;preview.DataSource=rows;
        foreach(string name in new[]{"ContactId","AdmissionId","FamilyId","Parameters"})if(preview.Columns.Contains(name))preview.Columns[name].Visible=false;
        preview.DefaultCellStyle.WrapMode=DataGridViewTriState.True;preview.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.DisplayedCells;
        bool eligible=rows.Rows.Count==included.Count && rows.AsEnumerable().All(r=>Convert.ToString(r["Exclusion"])=="");
        previewSignature=eligible?signature:"";prepare.Enabled=eligible&&CurrentUser.HasPermission("Communication.Compose");
        int unknown=rows.AsEnumerable().Count(r=>r.IsNull("EstimatedCost"));
        SetStatus(eligible?$"Preview ready: {rows.Rows.Count} recipients. {(unknown>0?"Cost estimate unavailable for some recipients; live dispatch will be blocked.":"Estimated provider cost: "+rows.AsEnumerable().Sum(r=>Convert.ToDecimal(r["EstimatedCost"])).ToString("0.0000")+" "+rows.Rows[0]["Currency"])}":"Preview contains exclusions. Correct contacts, consent or template before saving.");
    }
    private async Task RefreshTemplates()
    {
        var rows=await store.ReadAsync("Templates",templatePage,token:lifetime.Token);if(IsDisposed)return;templates.DataSource=rows;
        var approved=rows.Clone();foreach(DataRow row in rows.Rows)if(Convert.ToBoolean(row["Approved"]))approved.ImportRow(row);
        approved.Columns.Add("Display",typeof(string));foreach(DataRow row in approved.Rows)row["Display"]=$"{row["Code"]} · {row["Language"]} · v{row["Version"]}";
        templateChoice.DataSource=approved;templateChoice.DisplayMember="Display";templateChoice.ValueMember="TemplateId";
        if(approved.Rows.Count==0)SetStatus("No reviewed template is available on this page. An authorized administrator must review a template and create an approved version.",PageStatusKind.Warning);
    }
    private async Task RefreshOperational(){var data=await store.ReadAsync("Operational",token:lifetime.Token);if(!IsDisposed&&data.Rows.Count>0){var r=data.Rows[0];operational.Text=$"Mode: {r["Mode"]} · master pause: {r["Paused"]}. Dispatcher {(dispatchTask is {IsCompleted:false}?"running":"stopped")}. Scheduled work requires a running dispatcher; accepted does not mean delivered.";}}
    private async Task RefreshHistory(){var rows=await store.ReadAsync(CurrentUser.HasPermission("Communication.History")?"History":"Outbox",historyPage,historySearch.Text,historyState.SelectedIndex<=0?"":Convert.ToString(historyState.SelectedItem)!,token:lifetime.Token);if(!IsDisposed){history.DataSource=rows;foreach(string name in new[]{"MessageId","CreatedBy","ApprovedBy","ProviderReference"})if(history.Columns.Contains(name))history.Columns[name].Visible=false;}}
    private async Task RefreshContacts(){var rows=await store.ReadAsync("Contacts",contactPage,token:lifetime.Token);if(!IsDisposed)contacts.DataSource=rows;}
    private async Task RefreshRules(){if(!CurrentUser.HasPermission("Communication.Configure"))return;var rows=await store.ReadAsync("Rules",token:lifetime.Token);if(!IsDisposed)rules.DataSource=rows;}
    private Task EvaluateRules()=>store.CallAsync("Evaluate",lifetime.Token);
}
