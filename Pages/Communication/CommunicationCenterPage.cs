using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Services.Communication;

namespace FMSC_SMS.Pages.Communication;

// BD-35/36: opening this optional module neither installs it nor starts live dispatch.
// Use reviewed contacts/consent and templates; queued or simulated messages are not delivered messages.
public sealed partial class CommunicationCenterPage : V2Page,IBusyPage,IUnsavedChanges
{
    private readonly SqlCommunicationStore store;
    private readonly CommunicationService service;
    private readonly Func<string,SqlParameter[],Task<DataTable>> read;
    private readonly WindowsGatewayStore gateways=new();
    private static readonly HttpClient Http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(30)};
    private readonly MessageDispatcher dispatcher;
    private readonly CancellationTokenSource lifetime=new();
    private CancellationTokenSource? dispatchStop;
    private Task? dispatchTask;
    private bool installed;
    private readonly Label operational=new(){Dock=DockStyle.Top,Height=AppScale.Scale(60),AutoEllipsis=true,Text="Checking communication database readiness…",Font=AppTheme.ButtonFont,ForeColor=AppTheme.MutedText};
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
    private readonly ModernButton sendNow=new(){Text="Send Now",ThemeRole="Primary",Enabled=false};
    private readonly Label sendHint=new(){AutoEllipsis=true,ForeColor=AppTheme.Text,Text="Preview the message before sending."};
    private readonly List<Control> composeOptions=new();
    private bool composeOptionsExpanded;
    private int settingsSection;
    private readonly ModernComboBox audience=HumanUi.Combo();
    private DataTable? reviewedTemplateOptions;
    private int recipientPage,historyPage,contactPage,templatePage;
    private Guid requestId=Guid.NewGuid();private DateTime? requestDue;private string previewSignature="",savedSignature="";
    private bool populating;
    public bool IsBusy=>BusyOperationRunning||dispatcher.IsSubmitting;
    public string BusyMessage=>"A communication operation or provider submission is finishing. Wait for its recorded outcome.";
    public bool HasUnsavedChanges=>(announcement.Text.Trim().Length>0||included.Count>0) && Signature()!=savedSignature;
    public string UnsavedChangesMessage=>"This message composition has not been saved as a campaign. Discard it?";
    public CommunicationCenterPage():this(new SqlCommunicationStore(DatabaseHelper.GetConnection,CurrentUser.UserId),true){}
    internal CommunicationCenterPage(SqlCommunicationStore store,bool initialize,Func<string,SqlParameter[],Task<DataTable>>? read=null):base("Communication Center","Compose and track external SMS and WhatsApp messages. Live delivery requires approved gateway setup.")
    {
        this.store=store;service=new(store);dispatcher=new(store,c=>gateways.Provider(c,Http));
        this.read=read??((view,args)=>store.CallAsync("Read",lifetime.Token,[SqlCommunicationStore.P("@View",view),..args]));
        BuildWorkspace();
        Disposed+=(_,_)=>{lifetime.Cancel();dispatchStop?.Cancel();};
        if(initialize)Defer(InitializeAsync);
    }
    private async Task InitializeAsync()
    {
        await ExecuteAsync(async()=>{
            installed=await store.IsInstalledAsync(lifetime.Token);
            tabs.Enabled=installed;
            if(!installed){operational.Text="Communication setup is missing or incompatible. Ask your administrator to review the approved communication setup and recovery guidance. No database changes were made.";SetStatus(operational.Text,PageStatusKind.Warning);return;}
            await RefreshOperational();
            await RefreshTemplates();
            foreach(var item in new[]{(campus,"Campuses","CampusId","CampusName"),(year,"Years","AcademicYearId","YearName"),(cls,"Classes","ClassId","ClassName"),(section,"Sections","SectionId","SectionName")})
                item.Item1.Bind(await ReadAsync(item.Item2),item.Item3,item.Item4,"All");
            await LoadSelectedTab();ShowTabGuidance();
        });
    }
    private async Task ExecuteAsync(Func<Task> action)
    {
        try{await RunBusyAsync("Loading communication data…",action,this);}
        catch(OperationCanceledException) when(lifetime.IsCancellationRequested){}
        catch(Exception ex){if(!IsDisposed)SetStatus(DbMessage.UserMessage(ex),PageStatusKind.Warning);}
    }
    private ModernButton Button(string text,Func<Task> action,string permission="Communication.View",string role="Secondary",
        DataGridView? selectedGrid=null,Func<DataRow,bool>? available=null)
    {
        var b=new ModernButton{Text=text,ThemeRole=role,Height=AppMetrics.ButtonHeight,Margin=new Padding(4)};AppMetrics.FitButtonWidth(b,minWidth:100);
        b.Enabled=CurrentUser.HasPermission(permission);
        if(selectedGrid is not null){void Update()=>b.Enabled=CurrentUser.HasPermission(permission)&&Selected(selectedGrid) is { } row
            && (available?.Invoke(row)??true);selectedGrid.SelectionChanged+=(_,_)=>Update();Update();}
        b.Click+=async(_,_)=>{
            if(!CurrentUser.HasPermission(permission)||!installed){SetStatus("This action needs permission and completed communication setup. Ask your administrator.",PageStatusKind.Warning);return;}
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
        channel.Items.AddRange(["Sms","WhatsApp"]);channel.SelectedIndex=1;
        // Don't offer recipient types the operator cannot inspect.
        if(CurrentUser.HasPermission("Students.View"))
        {
            audience.Items.Add("Campus");
            audience.Items.Add("Class / Section");
            if(CurrentUser.HasPermission("Fees.View"))audience.Items.Add("Outstanding Fees");
            audience.Items.Add("Individual Contact");
        }
        if(CurrentUser.HasPermission("Staff.View"))audience.Items.Add("Staff");
        audience.Items.Add("Manual Contacts");
        audience.SelectedIndex=0;
        recipientFilter.Items.AddRange(["Student / family / manual","Outstanding","Family","Staff","Manual","Pending"]);recipientFilter.SelectedIndex=CurrentUser.HasPermission("Students.View")?0:CurrentUser.HasPermission("Staff.View")?3:4;
        channel.Width=AppScale.Scale(120);recipientFilter.Width=AppScale.Scale(150);templateChoice.Width=AppScale.Scale(320);
        schedule.ShowCheckBox=true;schedule.Checked=false;schedule.Format=DateTimePickerFormat.Custom;schedule.CustomFormat="dd-MMM-yyyy HH:mm";
        announcement.Height=AppScale.Scale(80);announcement.MinimumSize=new Size(0,AppScale.Scale(80));
        var compose=new Panel{Dock=DockStyle.Fill,AutoScroll=true};
        var root=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,RowCount=0,Margin=Padding.Empty};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        void Row(Control c,int h){c.Dock=DockStyle.Fill;root.RowStyles.Add(h==0?new RowStyle(SizeType.AutoSize):new RowStyle(SizeType.Absolute,AppScale.Scale(h)));root.Controls.Add(c,0,root.RowCount++);}
        // Field containers dock to fill; a flow panel cannot give them usable bounds.
        var messageFields=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};
        messageFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,20));
        messageFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));
        messageFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));
        messageFields.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var channelField=HumanUi.Field("Channel",channel);
        var scheduleField=HumanUi.Field("Send after (optional)",schedule);
        composeOptions.AddRange([channelField,scheduleField,prepare]);
        messageFields.Controls.Add(channelField,0,0);
        messageFields.Controls.Add(HumanUi.Field("Message template",templateChoice),1,0);
        messageFields.Controls.Add(scheduleField,2,0);
        var filters=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1};
        foreach(var item in new[]{("Campus",campus),("Academic year",year),("Class",cls),("Section",section)}){filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));filters.Controls.Add(HumanUi.Field(item.Item1,item.Item2),filters.Controls.Count,0);}
        composeOptions.Add(filters.GetControlFromPosition(1,0)!);
        Row(HumanUi.Field("Send to",audience),72);
        int filtersRow=root.RowCount;Row(filters,78);
        search.PlaceholderText="Contact or student name";search.Width=220;
        var findContacts=Button("Find Contacts",async()=>{
            recipientPage=0;
            if(AudienceMode()<4 && CurrentUser.HasPermission("Students.View"))
                await store.CallAsync("SyncAdmissionContacts",lifetime.Token);
            await RefreshRecipients();
        },"Communication.Compose");
        search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;findContacts.PerformClick();}};
        var contactToolbar=Bar([recipientFilter,search,findContacts,
            ..Paging(recipients,()=>recipientPage,value=>recipientPage=value,RefreshRecipients,"Communication.Compose"),
            Button("Clear Selection",()=>{included.Clear();InvalidatePreview();ApplyTicks();return Task.CompletedTask;},"Communication.Compose")]);
        Row(contactToolbar,0);
        int selectionRow=root.RowCount;Row(selection,44);
        int recipientsRow=root.RowCount;Row(recipients,220);
        Row(messageFields,80);
        var noticeField=HumanUi.Field("Notice text",announcement);
        int noticeRow=root.RowCount;
        Row(noticeField,100);
        void UpdateNoticeField()
        {
            bool usesNotice=templateChoice.SelectedItem is DataRowView selected && selected.Row.Table.Columns.Contains("Body")
                && (selected.Row.Field<string>("Body")?.Contains("{{announcement}}",StringComparison.Ordinal)??false);
            noticeField.Visible=usesNotice;
            root.RowStyles[noticeRow].Height=usesNotice?AppScale.Scale(100):0;
        }
        templateChoice.SelectedIndexChanged+=(_,_)=>UpdateNoticeField();
        UpdateNoticeField();
        recipients.ReadOnly=false;
        recipients.CurrentCellDirtyStateChanged+=(_,_)=>{if(recipients.IsCurrentCellDirty)recipients.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        recipients.CellValueChanged+=(_,e)=>{if(populating||e.RowIndex<0||e.ColumnIndex<0||recipients.Columns[e.ColumnIndex].Name!="Include")return;
            var row=recipients.Rows[e.RowIndex];Guid id=(Guid)row.Cells["ContactId"].Value;
            if(Convert.ToBoolean(row.Cells["Include"].Value??false))included.Add(id);else included.Remove(id);InvalidatePreview();};
        prepare.Click+=async(_,_)=>{if(!prepare.Enabled||!CurrentUser.HasPermission("Communication.Compose")||previewSignature!=Signature()||!installed)return;
            await ExecuteAsync(async()=>{
                if(!ModernMessageDialog.Confirm(FindForm(),$"Save a draft for {included.Count} selected contacts? Nothing is sent until Submit is confirmed.","Create Communication Draft"))return;
                requestDue??=schedule.Checked?schedule.Value.ToUniversalTime():DateTime.UtcNow;
                await service.PrepareAsync(requestId,SelectedTemplate(),SelectedChannel(),included,announcement.Text,requestDue.Value,lifetime.Token);
                savedSignature=Signature();SetStatus("Draft saved. Nothing has been sent. "+(CurrentUser.HasPermission("Communication.Send")?"Open History → More Actions to review and submit. Approval may be required.":"Ask an authorized sender to review and submit it from History."));
                prepare.Enabled=false;
                sendNow.Enabled=false;
            });};
        sendNow.Click+=async(_,_)=>{
            if(!installed || !sendNow.Enabled || savedSignature.Length>0 || !CurrentUser.HasPermission("Communication.Send")
                || previewSignature!=Signature() || preview.DataSource is not DataTable reviewed)return;
            await ExecuteAsync(()=>SendReviewedAsync(reviewed));
        };
        // Initialize before capturing this control in the local options layout function.
        ModernButton moreOptions=null!;
        moreOptions=Button("More Options",()=>{ShowComposeOptions(!composeOptionsExpanded);return Task.CompletedTask;},"Communication.Compose");
        Row(Bar(Button("Preview Messages",async()=>{
            if(AudienceMode()<3)await SelectMatchingContactsAsync();
            await PreviewAsync();
        },"Communication.Compose","Primary"),sendNow,prepare,Button("New Message",()=>{
            if(HasUnsavedChanges&&!ModernMessageDialog.ConfirmDiscardIfDirty(FindForm(),()=>HasUnsavedChanges,UnsavedChangesMessage))return Task.CompletedTask;
            ResetComposition();return Task.CompletedTask;
        },"Communication.Compose"),moreOptions),60);
        Row(sendHint,42);
        Row(preview,260);compose.Controls.Add(root);
        compose.Resize+=(_,_)=>root.Width=Math.Max(AppScale.Scale(760),compose.ClientSize.Width-SystemInformation.VerticalScrollBarWidth);
        root.Width=AppScale.Scale(900);
        foreach(Control c in new Control[]{channel,templateChoice,announcement,schedule})
        {if(c is ModernComboBox cb)cb.SelectedIndexChanged+=(_,_)=>InvalidatePreview();else if(c is ModernDatePicker dp)dp.ValueChanged+=(_,_)=>InvalidatePreview();else c.TextChanged+=(_,_)=>InvalidatePreview();}
        void UpdateAudience()
        {
            int mode=AudienceMode();
            recipientFilter.SelectedIndex=mode switch {2=>1,4=>3,5=>4,_=>0};
            bool individual=mode>=3;
            contactToolbar.Visible=individual;
            selection.Visible=individual;
            recipients.Visible=individual;
            root.RowStyles[selectionRow].Height=individual?AppScale.Scale(44):0;
            root.RowStyles[recipientsRow].Height=individual?AppScale.Scale(220):0;
            filters.Visible=mode<4;
            root.RowStyles[filtersRow].Height=mode<4?AppScale.Scale(78):0;
            Control[] scopeFields=Enumerable.Range(0,4).Select(i=>filters.GetControlFromPosition(i,0)!).ToArray();
            bool schoolScope=mode<4;
            bool[] show=[schoolScope,schoolScope&&composeOptionsExpanded,mode is 1 or 2,mode is 1 or 2];
            int count=show.Count(value=>value);
            for(int i=0;i<4;i++)
            {
                scopeFields[i].Visible=show[i];
                filters.ColumnStyles[i].Width=show[i]?100f/count:0f;
            }
        }
        void ShowComposeOptions(bool show)
        {
            if(!show && (schedule.Checked || SelectedChannel()!=MessageChannel.WhatsApp || year.SelectedId!=0))
            {
                SetStatus("Clear the scheduled time and academic-year filter, and choose WhatsApp before hiding More Options. This prevents hidden sending settings.",PageStatusKind.Warning);
                return;
            }
            composeOptionsExpanded=show;
            foreach(var option in composeOptions)option.Visible=show;
            messageFields.ColumnStyles[0].Width=show?20:0;
            messageFields.ColumnStyles[1].Width=show?45:100;
            messageFields.ColumnStyles[2].Width=show?35:0;
            moreOptions.Text=show?"Fewer Options":"More Options";
            UpdateAudience();
        }
        audience.SelectedIndexChanged+=(_,_)=>{
            included.Clear();
            search.Text="";
            if(AudienceMode()>=4 && year.SelectedId!=0)
                year.SelectedIndex=0;
            RecipientFiltersChanged();
            InvalidatePreview();
            UpdateAudience();
            ApplyTemplateChoices();
        };
        ShowComposeOptions(false);
        tabs.TabPages.Add(Tab("Send Message",compose));
        tabs.TabPages.Add(Tab("History",BuildHistory()));
        if(CurrentUser.HasPermission("Communication.Configure"))
            tabs.TabPages.Add(Tab("Settings",BuildSettingsWorkspace()));
        tabs.Enabled=false;TabStrip.Apply(tabs,AppTheme.Primary);
        var content=new Panel{Dock=DockStyle.Fill};
        content.Controls.Add(TabStrip.CreateHost(tabs));
        operational.Visible=false;
        tabs.SelectedIndexChanged+=(_,_)=>operational.Visible=tabs.SelectedTab?.Text=="Settings";
        content.Controls.Add(operational);
        SetContent(content);

        if(!CurrentUser.HasPermission("Communication.Compose"))tabs.SelectedIndex=1;
        ConfigureGuidance();
        foreach(var input in new[]{campus,year,cls,section})input.UserSelectionChanged+=(_,_)=>RecipientFiltersChanged();
        recipientFilter.SelectedIndexChanged+=(_,_)=>RecipientFiltersChanged();search.TextChanged+=(_,_)=>RecipientFiltersChanged();
    }
    private Control BuildSettingsWorkspace()
    {
        // The daily operator never sees the full module's administration workspaces.
        // Administrators can still reach contacts, templates and gateway settings here.
        var pages=new Control[4];
        pages[0]=BuildContacts();
        pages[1]=ListHost(templates,Bar([
            Button("Refresh",RefreshTemplates,"Communication.Configure"),
            Button("New Version / Review",EditTemplate,"Communication.Configure"),
            Button("Revoke Selected",async()=>{
                var row=Selected(templates)??throw new ArgumentException("Select a template first.");
                if(!ModernMessageDialog.Confirm(FindForm(),"Revoke this template? Unsubmitted messages will be suppressed; past messages remain in history.","Revoke Template"))return;
                await store.CallAsync("RevokeTemplate",lifetime.Token,SqlCommunicationStore.P("@Id",row["TemplateId"]));
                await AfterSavedAsync(RefreshTemplates,"Template revoked. Unsubmitted messages using it are blocked; past messages stay in History.");
            },"Communication.Configure",selectedGrid:templates),
            ..Paging(templates,()=>templatePage,value=>templatePage=value,RefreshTemplates,"Communication.Configure")
        ]));
        var gateway=new Panel{Dock=DockStyle.Fill,AutoScroll=true};
        gateway.Controls.Add(Bar(
            Button("Sending Mode / Limits",EditSettings,"Communication.Configure"),
            Button("Secure Gateway Credentials",EditGateway,"Communication.Configure"),
            Button("Try a Sample",TrySampleAsync,"Communication.Configure")));
        pages[2]=gateway;
        pages[3]=ListHost(rules,Bar(
            Button("Refresh",RefreshRules,"Communication.Configure"),
            Button("Disable Selected Rule",DisableSelectedRuleAsync,"Communication.Configure",
                selectedGrid:rules,available:row=>row.Table.Columns.Contains("Enabled")
                    && Convert.ToBoolean(row["Enabled"]))));
        var host=new Panel{Dock=DockStyle.Fill};
        foreach(var page in pages)
        {
            page.Dock=DockStyle.Fill;
            page.Visible=false;
            host.Controls.Add(page);
        }
        pages[0].Visible=true;
        var navigation=Bar(
            Button("Contacts & Consent",async()=>{settingsSection=0;ShowSection();await LoadSettingsSection();},"Communication.Configure"),
            Button("Templates",async()=>{settingsSection=1;ShowSection();await LoadSettingsSection();},"Communication.Configure"),
            Button("Sending Setup",async()=>{settingsSection=2;ShowSection();await LoadSettingsSection();},"Communication.Configure"),
            Button("Existing Automations",async()=>{settingsSection=3;ShowSection();await LoadSettingsSection();},"Communication.Configure"));
        host.Controls.Add(navigation);
        return host;
        void ShowSection()
        {
            for(int i=0;i<pages.Length;i++)pages[i].Visible=i==settingsSection;
        }
    }
    private Task LoadSettingsSection()=>settingsSection switch
    {
        0=>RefreshContacts(),
        1=>RefreshTemplates(),
        2=>RefreshOperational(),
        _=>RefreshRules()
    };
    private int AudienceMode()=>Convert.ToString(audience.SelectedItem) switch
    {
        "Campus"=>0,
        "Class / Section"=>1,
        "Outstanding Fees"=>2,
        "Individual Contact"=>3,
        "Staff"=>4,
        _=>5 // Manual contacts
    };
    private Guid SelectedTemplate()=>templateChoice.SelectedValue is Guid id?id:throw new ArgumentException("Choose a reviewed template.");
    private MessageChannel SelectedChannel()=>Enum.Parse<MessageChannel>(Convert.ToString(channel.SelectedItem)!);
    private string Signature()=>string.Join("|",templateChoice.SelectedValue,channel.SelectedItem,announcement.Text,string.Join(',',included.Order()),schedule.Checked,schedule.Checked?schedule.Value.ToUniversalTime().ToString("O"):"");
    private void InvalidatePreview(){previewSignature="";prepare.Enabled=false;sendNow.Enabled=false;sendHint.Text="Preview the message before sending.";preview.DataSource=null;UiHelper.SetGridState(preview,UiHelper.GridDisplayState.Waiting,"Preview required. Select contacts and a reviewed template, then choose Preview Messages.");selection.Text=$"{included.Count} contacts selected across all result pages. Preview required before sending. "+TemplateHint();}
    private void ApplyTicks()
    {populating=true;try{foreach(DataGridViewRow r in recipients.Rows)if(!r.IsNewRow)r.Cells["Include"].Value=included.Contains((Guid)r.Cells["ContactId"].Value);}finally{populating=false;}}
    private async Task SelectMatchingContactsAsync()
    {
        if(!CurrentUser.HasPermission("Communication.Compose"))
            throw new ArgumentException("This action requires permission to compose messages.");
        int campusId=campus.SelectedId;
        int yearId=composeOptionsExpanded?year.SelectedId:0;
        int classId=AudienceMode() is 1 or 2?cls.SelectedId:0;
        int sectionId=AudienceMode() is 1 or 2?section.SelectedId:0;
        if(campusId==0)
            throw new ArgumentException("Choose a campus before previewing a group message.");
        if(AudienceMode()==1 && classId==0)
            throw new ArgumentException("Choose a class before previewing a class/section message.");
        if(recipientFilter.SelectedIndex is not (0 or 1))
            throw new ArgumentException("Choose the student or outstanding-balance filter for a school group.");
        // Creating recipient contacts from consent happens only when the user
        // explicitly requests a group preview, never on page open or admission save.
        await store.CallAsync("SyncAdmissionContacts",lifetime.Token);
        string term=search.Text;
        string filter=recipientFilter.SelectedIndex==0?"":"Outstanding";
        SqlParameter[] Scope()=>[SqlCommunicationStore.P("@Campus",campusId),SqlCommunicationStore.P("@Year",yearId),
            SqlCommunicationStore.P("@Class",classId),SqlCommunicationStore.P("@Section",sectionId)];
        var settings=await ReadAsync("Operational");
        int limit=Convert.ToInt32(settings.Rows[0]["MaxBatch"]);
        var matching=new HashSet<Guid>();
        for(int page=0;page<=limit/100;page++)
        {
            var rows=await ReadAsync("Recipients",page,term,filter,Scope());
            foreach(DataRow row in rows.Rows)matching.Add(row.Field<Guid>("ContactId"));
            if(matching.Count>limit)
                throw new ArgumentException($"More than {limit} contacts match. Narrow the filters to stay within the configured send limit.");
            if(rows.Rows.Count<100)break;
            if(page==limit/100)throw new ArgumentException("Narrow the filters and search again before sending.");
        }
        if(matching.Count==0)throw new ArgumentException(EmptyRecipients());
        included.Clear();
        included.UnionWith(matching);
        InvalidatePreview();
        ApplyTicks();
        SetStatus($"{included.Count} matching contacts selected. Preview Messages to check eligibility.");
    }
    private Task RefreshRecipients()
    {
        if(!CurrentUser.HasPermission("Communication.Compose"))return Task.CompletedTask;
        return LoadGridAsync(recipients,()=>ReadAsync("Recipients",recipientPage,search.Text,recipientFilter.SelectedIndex==0?"":Convert.ToString(recipientFilter.SelectedItem)!,
            SqlCommunicationStore.P("@Campus",AudienceMode()>=4?0:campus.SelectedId),
            SqlCommunicationStore.P("@Year",AudienceMode()<4&&composeOptionsExpanded?year.SelectedId:0),
            SqlCommunicationStore.P("@Class",AudienceMode() is 1 or 2?cls.SelectedId:0),
            SqlCommunicationStore.P("@Section",AudienceMode() is 1 or 2?section.SelectedId:0)),rows=>{
            populating=true;
            try{rows.Columns.Add("Include",typeof(bool));recipients.DataSource=rows;foreach(DataGridViewColumn c in recipients.Columns)c.ReadOnly=c.Name!="Include";
                recipients.Columns["Include"].DisplayIndex=0;ApplyMessageColumns();ApplyTicks();}
            finally{populating=false;}
            selection.Text=$"Page {recipientPage+1} · {rows.Rows.Count} contacts shown · {included.Count} selected across all pages. Preview checks consent. "+TemplateHint();
        },EmptyRecipients());
    }
    private async Task PreviewAsync()
    {
        if(savedSignature.Length>0)throw new ArgumentException("This campaign snapshot is already saved. Use New Message for another campaign; cancel the old draft in Outbox if it is no longer needed.");
        if(included.Count==0 && recipients.Enabled && recipients.DataSource is DataTable matches && matches.Rows.Count==1 && recipientPage==0)
        {
            included.Add(matches.Rows[0].Field<Guid>("ContactId"));
            ApplyTicks();
            selection.Text="The only matching contact is selected.";
        }
        if(included.Count==0)throw new ArgumentException("Select contacts first. Add verified, consented contacts in Settings → Contacts & Consent if none are listed.");
        if(templateChoice.SelectedItem is not DataRowView chosen || !TemplateMatchesAudience(chosen.Row))
            throw new ArgumentException("Choose a reviewed template that matches the recipients before previewing.");
        string signature=Signature();prepare.Enabled=false;sendNow.Enabled=false;previewSignature="";
        sendHint.Text="Checking who can receive this message…";
        UiHelper.SetGridState(preview,UiHelper.GridDisplayState.Loading,"Checking recipient consent, eligibility and costs…");
        DataTable rows;
        try{rows=await store.PreviewAsync(SelectedTemplate(),SelectedChannel(),included,announcement.Text,token:lifetime.Token);}
        catch{if(!IsDisposed){sendHint.Text="Preview could not be checked. Try Preview Messages again.";UiHelper.SetGridState(preview,UiHelper.GridDisplayState.Failed,"Preview could not be checked. Try Preview Messages again; no draft was created.");}throw;}
        if(IsDisposed||Signature()!=signature)return;UiHelper.ClearGridState(preview);preview.DataSource=rows;
        ApplyMessageColumns();
        preview.DefaultCellStyle.WrapMode=DataGridViewTriState.True;preview.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.DisplayedCells;
        bool eligible=rows.Rows.Count==included.Count && rows.AsEnumerable().All(r=>Convert.ToString(r["Exclusion"])=="");
        int ready=rows.AsEnumerable().Count(r=>Convert.ToString(r["Exclusion"])=="");
        previewSignature=ready>0?signature:"";
        prepare.Enabled=eligible&&CurrentUser.HasPermission("Communication.Compose");
        int unknown=rows.AsEnumerable().Count(r=>r.IsNull("EstimatedCost"));

        // A valid recipient preview is not proof that the sending environment is
        // ready. Expose mode/pause/unknown pricing before the operator clicks Send.
        var operationalSettings=await ReadAsync("Operational");
        if(IsDisposed || Signature()!=signature)return;
        var sending=operationalSettings.Rows.Count>0?operationalSettings.Rows[0]:null;
        string mode=sending is not null && sending.Table.Columns.Contains("Mode")
            ?Convert.ToString(sending["Mode"])??"Disabled":"Disabled";
        bool paused=sending is not null && sending.Table.Columns.Contains("Paused") && Convert.ToBoolean(sending["Paused"]);
        string? sendBlock=ready==0
            ? rows.Rows.Count==1?Convert.ToString(rows.Rows[0]["Exclusion"]):"No eligible contacts. Check the Exclusion column."
            : schedule.Checked?"Clear Send after to send immediately."
            : !CurrentUser.HasPermission("Communication.Send")?"Ask an administrator for permission to send messages."
            : ready>1 && !CurrentUser.HasPermission("Communication.Bulk")?"Select one contact or ask for bulk sending permission."
            : mode=="Disabled"?"Sending is disabled in Settings."
            : paused?"Sending is paused in Settings."
            : mode=="Live" && unknown>0?"Live sending requires provider pricing for every eligible recipient."
            : null;
        sendNow.Enabled=sendBlock is null;
        sendHint.Text=sendBlock is null
            ? mode=="Simulation"
                ?$"{ready} ready for SIMULATION only. No real message will be sent."
                :$"{ready} ready to send. Review the preview, then choose Send Now."
            :$"Send Now unavailable. {sendBlock}";
        string feedback=mode=="Simulation"
            ?"Simulation only: no phones will be contacted."
            :unknown>0?"Cost estimate unavailable for some recipients."
            :"Estimated provider cost: "+rows.AsEnumerable().Where(r=>!r.IsNull("EstimatedCost")).Sum(r=>Convert.ToDecimal(r["EstimatedCost"])).ToString("0.0000")+" "+(rows.Rows.Count>0?rows.Rows[0]["Currency"]:"");
        SetStatus(eligible
            ?$"Preview ready: {rows.Rows.Count} recipients. {feedback}"
            :$"Preview: {ready} eligible, {included.Count-ready} skipped. Review the exclusion column. {feedback}");
    }
    private Task RefreshTemplates()=>LoadGridAsync(templates,()=>ReadAsync("Templates",templatePage),BindTemplateChoices);
    private void BindTemplateChoices(DataTable rows)
    {
        templates.DataSource=rows;
        Guid? selected=templateChoice.SelectedValue is Guid id?id:null;
        var old=(templateChoice.DataSource as DataTable)?.AsEnumerable()
            .FirstOrDefault(r=>selected.HasValue&&(Guid)r["TemplateId"]==selected.Value);
        var approved=rows.Clone();
        foreach(DataRow row in rows.Rows)
            if(Convert.ToBoolean(row["Approved"]))approved.ImportRow(row);

        // Keep one previously selected reviewed template across paged browsing,
        // but never preserve a revoked version returned by the current page.
        if(old is not null && !rows.AsEnumerable().Any(r=>(Guid)r["TemplateId"]==selected))
        {
            var kept=approved.NewRow();
            foreach(DataColumn column in approved.Columns)
                kept[column.ColumnName]=old.Table.Columns.Contains(column.ColumnName)?old[column.ColumnName]:DBNull.Value;
            approved.Rows.Add(kept);
        }
        reviewedTemplateOptions=approved;
        ApplyTemplateChoices(selected);
    }
    private bool TemplateMatchesAudience(DataRow row)
    {
        // Older UI fixture tables contain only identity/version columns.
        if(!row.Table.Columns.Contains("Scope"))return true;
        string scope=Convert.ToString(row["Scope"])??"";
        return AudienceMode() switch
        {
            0 or 1 => scope=="Student", // School-group recipients are student-linked contacts.
            2 => scope=="Student" && row.Table.Columns.Contains("Code") &&
                 string.Equals(Convert.ToString(row["Code"]),"outstanding",StringComparison.OrdinalIgnoreCase),
            4 => scope=="Staff",
            5 => scope=="Manual",
            _ => true // Individual contacts can be student, family or manually registered.
        };
    }
    private void ApplyTemplateChoices(Guid? preferred=null)
    {
        if(reviewedTemplateOptions is null)return;
        Guid? selected=preferred??(templateChoice.SelectedValue is Guid id?id:null);
        var visible=reviewedTemplateOptions.Clone();
        foreach(DataRow row in reviewedTemplateOptions.Rows)
            if(TemplateMatchesAudience(row))visible.ImportRow(row);
        visible.Columns.Add("Display",typeof(string));
        foreach(DataRow row in visible.Rows)
            row["Display"]=$"{row["Code"]} · {row["Language"]} · v{row["Version"]}";
        templateChoice.DataSource=null;
        templateChoice.DisplayMember="Display";
        templateChoice.ValueMember="TemplateId";
        templateChoice.DataSource=visible;
        if(selected.HasValue && visible.AsEnumerable().Any(r=>(Guid)r["TemplateId"]==selected.Value))
            templateChoice.SelectedValue=selected.Value;
        else
            templateChoice.SelectedIndex=!selected.HasValue&&visible.Rows.Count==1?0:-1;
        if(visible.Rows.Count==0)
            SetStatus(AudienceMode()==2
                ?"No reviewed outstanding-reminder template is available. An administrator must review one in Settings → Templates."
                :"No reviewed template matches these recipients. Ask an administrator to review an appropriate template in Settings → Templates.",
                PageStatusKind.Warning);
    }
    private string TemplateHint()=>templateChoice.Items.Count==0
        ?"No reviewed template is available for the selected recipients."
        :"Choose a reviewed template before previewing.";
    private async Task RefreshOperational(){var data=await ReadAsync("Operational");if(!IsDisposed&&data.Rows.Count>0){var r=data.Rows[0];sendingSummary=$"Mode: {r["Mode"]} · master pause: {r["Paused"]}. ";operational.Text=sendingSummary+$"Dispatcher {(dispatchTask is {IsCompleted:false}?"running":"stopped")}. Scheduled work requires a running dispatcher; accepted does not mean delivered.";}}
    private Task RefreshHistory()=>LoadGridAsync(history,()=>ReadAsync(CurrentUser.HasPermission("Communication.History")?"History":"Outbox",historyPage,historySearch.Text,historyState.SelectedItem is StatusFilter selectedStatus?selectedStatus.State:""),rows=>{
        if(rows.Columns.Contains("State"))
        {
            rows.Columns.Add("DisplayStatus",typeof(string));
            foreach(DataRow row in rows.Rows)
            {
                string state=Convert.ToString(row["State"])??"";
                row["DisplayStatus"]=FriendlyDeliveryState(state);
            }
        }
        history.DataSource=rows;ApplyMessageColumns();
        if(rows.Rows.Count>0)SetStatus($"Page {historyPage+1} · {rows.Rows.Count} messages shown. Select a message to review its campaign and available actions.");
    });
    private void ApplyMessageColumns()
    {
        void SetColumns(DataGridView grid,string[] everyday)
        {
            foreach(DataGridViewColumn column in grid.Columns)
                column.Visible=column.Name is not ("ContactId" or "AdmissionId" or "EnrollmentId" or "FamilyId"
                    or "Parameters" or "MessageId" or "CampaignId" or "CreatedBy" or "ApprovedBy" or "ProviderReference")
                    && everyday.Contains(column.Name);
        }
        SetColumns(recipients,["Include","Student","ContactName","Class","Outstanding"]);
        SetColumns(preview,["ContactName","Destination","Body","Exclusion","EstimatedCost","Currency"]);
        SetColumns(history,["Student","Recipient","Template","DisplayStatus","Simulation","UpdatedUtc"]);
        if(history.Columns.Contains("UpdatedUtc"))history.Columns["UpdatedUtc"].HeaderText="Last updated (UTC)";
        if(history.Columns.Contains("DisplayStatus"))history.Columns["DisplayStatus"].HeaderText="Status";
    }
    private Task RefreshContacts()=>LoadGridAsync(contacts,()=>ReadAsync("Contacts",contactPage));
    private Task RefreshRules()=>CurrentUser.HasPermission("Communication.Configure")?LoadGridAsync(rules,()=>ReadAsync("Rules")):Task.CompletedTask;
    private async Task EvaluateRules(){await store.CallAsync("Evaluate",lifetime.Token);SetStatus("Events checked. Review campaigns in Outbox. Sending may need approval and a running dispatcher; delivery is not confirmed.");}
}
