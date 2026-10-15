using System.Data;
using System.Text.Json;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Services.Communication;

namespace FMSC_SMS.Pages.Communication;

public sealed partial class CommunicationCenterPage
{
    private static DataRow? Selected(DataGridView grid)=>(grid.CurrentRow?.DataBoundItem as DataRowView)?.Row;
    private Control BuildHistory()
    {
        async Task Transition(string action)
        {
            var row=Selected(history)??throw new ArgumentException("Select a campaign in the outbox first.");
            Guid id=(Guid)row["CampaignId"];
            var summary=await store.CallAsync("CampaignSummary",lifetime.Token,SqlCommunicationStore.P("@Id",id));
            if(summary.Rows.Count==0)throw new ArgumentException("The campaign is no longer available.");
            var s=summary.Rows[0];string context=$"Campaign {id} · {s["Channel"]} · {s["Recipients"]} recipients · template {s["Code"]} {s["Language"]} v{s["Version"]}. "
                +(Convert.ToInt32(s["Simulation"])==1?"SIMULATION — no external delivery. ":"LIVE provider submission may incur charges. ")
                +(s.IsNull("EstimatedCost")?"Estimated cost unavailable. ":$"Estimated {s["EstimatedCost"]} {s["Currency"]}. ");
            if(!ModernMessageDialog.Confirm(FindForm(),context+(action=="Submit"?"Submit this entire campaign? Eligible messages may incur provider charges when a dispatcher runs. Large or automated campaigns require approval.":action+" this campaign? Only unsent work can be paused or cancelled."),"Communication Campaign"))return;
            await store.TransitionAsync(id,action,lifetime.Token);await RefreshHistory();
        }
        historySearch.PlaceholderText="Campaign ID or error code";historySearch.Width=220;
        historyState.Items.Add("All states");historyState.Items.AddRange(Enum.GetNames<DeliveryState>());historyState.SelectedIndex=0;
        return ListHost(history,Bar(historySearch,historyState,Button("Search",async()=>{historyPage=0;await RefreshHistory();}),
            Button("Refresh",RefreshHistory),Button("Previous",async()=>{historyPage=Math.Max(0,historyPage-1);await RefreshHistory();}),Button("Next",async()=>{historyPage++;await RefreshHistory();}),
            Button("Submit",()=>Transition("Submit"),"Communication.Send","Primary"),Button("Approve",()=>Transition("Approve"),"Communication.Approve"),
            Button("Pause",()=>Transition("Pause"),"Communication.Compose"),Button("Resume",()=>Transition("Resume"),"Communication.Send"),Button("Cancel Campaign",()=>Transition("Cancel"),"Communication.Compose"),
            Button("Details",async()=>{
                var row=Selected(history)??throw new ArgumentException("Select a message first.");
                var detail=await store.CallAsync("Details",lifetime.Token,SqlCommunicationStore.P("@Id",row["MessageId"]));
                if(IsDisposed||detail.Rows.Count==0)return;
                using var dialog=new ApplicationWindowForm{Text="Communication Message Details",ClientSize=new Size(AppScale.Scale(850),AppScale.Scale(600)),ShowInTaskbar=false,StartPosition=FormStartPosition.CenterParent};
                var grid=HumanUi.Grid();grid.DataSource=detail;dialog.Controls.Add(grid);dialog.ShowDialog(FindForm());
            }),
            Button("Safe Retry",async()=>{
                var row=Selected(history)??throw new ArgumentException("Select a rejected message first.");
                await store.CallAsync("Retry",lifetime.Token,SqlCommunicationStore.P("@Id",row["MessageId"]));await RefreshHistory();
            },"Communication.Send"),
            Button("Check Provider Status",async()=>{
                var row=Selected(history)??throw new ArgumentException("Select an accepted provider message first.");
                if(row.IsNull("ProviderReference"))throw new ArgumentException("No provider reference is available. Do not resend an uncertain submission; reconcile with the provider.");
                if(Convert.ToString(row["Provider"])!="Twilio")throw new ArgumentException("This provider needs a signed webhook or provider-console review. Simulation is never proof of delivery.");
                var status=await gateways.Provider(MessageChannel.Sms,Http).QueryAsync(Convert.ToString(row["ProviderReference"])!,lifetime.Token);
                await store.CallAsync("StatusUpdate",lifetime.Token,SqlCommunicationStore.P("@Id",row["MessageId"]),SqlCommunicationStore.P("@Provider","Twilio"),SqlCommunicationStore.P("@Reference",status.Reference),SqlCommunicationStore.P("@State",status.State.ToString()));await RefreshHistory();
            },"Communication.Configure"),
            Button("Start Dispatcher",StartDispatcher,"Communication.Send"),Button("Stop Dispatcher",()=>{dispatchStop?.Cancel();operational.Text="Stopping dispatcher. In-flight submissions are recorded before stopping.";return Task.CompletedTask;},"Communication.Send")));
    }
    private Task StartDispatcher()
    {
        if(dispatchTask is {IsCompleted:false})return Task.CompletedTask;
        dispatchStop?.Dispose();dispatchStop=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        dispatchTask=RunDispatcher(dispatchStop.Token);return Task.CompletedTask;
    }
    private async Task RunDispatcher(CancellationToken token)
    {
        try
        {
            await dispatcher.RunAsync(token,text=>{
                if(IsDisposed||!IsHandleCreated)return;
                try{BeginInvoke((Action)(()=>{if(!IsDisposed)operational.Text=text+". Simulation is labelled separately in History; accepted does not mean delivered.";}));}
                catch(InvalidOperationException){}
            },CurrentUser.HasPermission("Communication.Configure")&&CurrentUser.HasPermission("Communication.Approve"));
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception){if(!IsDisposed)operational.Text="Dispatcher stopped after an error. Review outbox outcomes before retrying.";}
        finally{if(!IsDisposed)operational.Text="Dispatcher stopped. Queued work remains stored; it will not run while the application is closed.";}
    }
    private Control BuildContacts()=>ListHost(contacts,Bar(Button("Refresh",RefreshContacts),Button("Previous",async()=>{contactPage=Math.Max(0,contactPage-1);await RefreshContacts();}),Button("Next",async()=>{contactPage++;await RefreshContacts();}),
        Button("Add From School Records",async()=>{var candidate=await PickCandidate();if(candidate is not null)await EditContact(null,candidate);},"Communication.Configure"),
        Button("Add Manual Contact",()=>EditContact(null,null),"Communication.Configure"),Button("Edit Contact / Consent",()=>EditContact(Selected(contacts)??throw new ArgumentException("Select a contact first."),null),"Communication.Configure")));
    private Task<DataRow?> PickCandidate()
    {
        using var dialog=new ApplicationWindowForm{Text="Choose Student, Family or Staff Contact",ClientSize=new Size(AppScale.Scale(1050),AppScale.Scale(650)),ShowInTaskbar=false,StartPosition=FormStartPosition.CenterParent};
        var grid=HumanUi.Grid();var term=HumanUi.TextBox(false,100);term.Width=250;term.PlaceholderText="Person or family name";int page=0;bool loading=false;DataRow? picked=null;
        async Task Load()
        {
            if(loading)return;loading=true;
            try{var rows=await store.ReadAsync("Candidates",page,term.Text,token:lifetime.Token);if(!dialog.IsDisposed)grid.DataSource=rows;}
            catch(Exception ex){if(!dialog.IsDisposed)ModernMessageDialog.ShowWarning(dialog,DbMessage.UserMessage(ex));}
            finally{loading=false;}
        }
        var use=new ModernButton{Text="Use Selected Record"};use.Click+=(_,_)=>{picked=Selected(grid);if(picked is null)return;dialog.DialogResult=DialogResult.OK;dialog.Close();};
        ModernButton Local(string text,Action change){var b=new ModernButton{Text=text};b.Click+=async(_,_)=>{change();await Load();};return b;}
        dialog.Controls.Add(grid);dialog.Controls.Add(Bar(term,Local("Search",()=>page=0),Local("Previous",()=>page=Math.Max(0,page-1)),Local("Next",()=>page++),use));
        dialog.Shown+=async(_,_)=>await Load();dialog.ShowDialog(FindForm());return Task.FromResult(picked);
    }
    private async Task EditContact(DataRow? original,DataRow? candidate)
    {
        Guid id=original is null?Guid.NewGuid():(Guid)original["ContactId"];
        string kind=original is not null?CellText(original,"OwnerKind"):candidate is not null?CellText(candidate,"OwnerKind"):"Manual";
        int? Owner(string name,string target)=>original is not null?(original.IsNull(name)?null:Convert.ToInt32(original[name])):candidate is not null&&kind==target?Convert.ToInt32(candidate["OwnerId"]):null;
        using var editor=new CommunicationEditor("Contact and Recorded Consent — "+kind,async e=>{
            await store.CallAsync("SaveContact",lifetime.Token,SqlCommunicationStore.P("@Id",id),SqlCommunicationStore.P("@Kind",kind),SqlCommunicationStore.P("@AdmissionId",Owner("AdmissionId","Student")),SqlCommunicationStore.P("@FamilyId",Owner("FamilyId","Family")),SqlCommunicationStore.P("@StaffId",Owner("StaffId","Staff")),
                SqlCommunicationStore.P("@Name",e.Value("name")),SqlCommunicationStore.P("@Phone",CommunicationPhone.Normalize(e.Value("phone"),e.Value("country"))),SqlCommunicationStore.P("@Language",e.Value("language")),SqlCommunicationStore.P("@Channel",e.Value("channel")),
                SqlCommunicationStore.P("@Verified",e.Checked("verified")),SqlCommunicationStore.P("@Sms",e.Checked("sms")),SqlCommunicationStore.P("@WhatsApp",e.Checked("whatsapp")),SqlCommunicationStore.P("@Financial",e.Checked("financial")),SqlCommunicationStore.P("@Announcements",e.Checked("announcements")),SqlCommunicationStore.P("@OptOut",e.Checked("optout")),SqlCommunicationStore.P("@Source",e.Value("source")),SqlCommunicationStore.P("@ExpectedRevision",original?["Revision"]));
        });
        editor.TextField("name","Actual parent / guardian / staff contact name",original is not null?CellText(original,"ContactName"):candidate is not null?CellText(candidate,"Guardian"):"");
        editor.TextField("phone","Mobile number (candidate number is not proof of ownership)",original is not null?CellText(original,"Destination"):candidate is not null?CellText(candidate,"ContactNumber"):"");editor.TextField("country","Default country code; use a full international number elsewhere","+92");
        editor.Choice("language","Preferred language",["en","ur","en_US"],original is not null?CellText(original,"Language"):"en");editor.Choice("channel","Preferred channel",["Sms","WhatsApp"],original is not null?CellText(original,"PreferredChannel"):"Sms");
        bool Flag(string name)=>original is not null&&Convert.ToBoolean(original[name]);
        editor.Flag("verified","I verified that this contact controls this number",Flag("Verified"));editor.Flag("sms","Recorded SMS consent",Flag("SmsConsent"));editor.Flag("whatsapp","Recorded WhatsApp consent",Flag("WhatsAppConsent"));editor.Flag("financial","Financial-message consent (student contacts only)",Flag("FinancialConsent"));editor.Flag("announcements","School-announcement consent",Flag("AnnouncementConsent"));editor.Flag("optout","Opted out — suppress sending",Flag("OptedOut"));editor.TextField("source","Consent source / reference / date",original is not null?CellText(original,"ConsentSource"):"");
        editor.OnChange("phone",()=>{foreach(string flag in new[]{"verified","sms","whatsapp","financial","announcements"})editor.ClearFlag(flag);});
        if(editor.ShowDialog(FindForm())==DialogResult.OK)await RefreshContacts();
    }
    private async Task EditTemplate()
    {
        var original=Selected(templates);Guid id=Guid.NewGuid();int version=original is null?1:Convert.ToInt32(original["Version"])+1;
        using var editor=new CommunicationEditor("Review Template — New Immutable Version",async e=>{
            MessageTemplateService.Validate(e.Value("body"));
            string[] fields=e.Value("order").Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
            if(fields.Any(f=>!MessageTemplateService.Fields.Contains(f)))throw new ArgumentException("Use documented placeholder names in the WhatsApp parameter order.");
            await store.CallAsync("SaveTemplate",lifetime.Token,SqlCommunicationStore.P("@Id",id),SqlCommunicationStore.P("@Code",e.Value("code")),SqlCommunicationStore.P("@Version",version),SqlCommunicationStore.P("@Language",e.Value("language")),SqlCommunicationStore.P("@Category",e.Value("category")),SqlCommunicationStore.P("@Scope",e.Value("scope")),SqlCommunicationStore.P("@Body",e.Value("body")),SqlCommunicationStore.P("@ProviderTemplate",e.Value("provider")),SqlCommunicationStore.P("@Order",JsonSerializer.Serialize(fields)),SqlCommunicationStore.P("@Approved",e.Checked("approved")));
        });
        editor.TextField("code","Template code",original is null?"school_notice":CellText(original,"Code"));editor.Choice("language","Language",["en","ur","en_US"],original is null?"en":CellText(original,"Language"));editor.Choice("category","Message purpose",["Utility","Financial","Announcement"],original is null?"Announcement":CellText(original,"Category"));editor.Choice("scope","Recipient context",["Student","Family","Staff","Manual"],original is null?"Student":CellText(original,"Scope"));
        editor.TextField("body","Text — school, student, class, year, amount, receipt, dueDate, announcement in {{braces}}",original is null?"{{school}}: {{announcement}}":CellText(original,"Body"),true);
        editor.TextField("provider","Meta-approved template name (blank means SMS only)",original is null?"":CellText(original,"ProviderTemplate"));
        editor.TextField("order","WhatsApp body parameter order, comma-separated",original is null?"school,announcement":string.Join(',',JsonSerializer.Deserialize<string[]>(CellText(original,"ParameterOrder"))??[]));
        editor.Flag("approved","I reviewed the text, recipient scope and any provider template mapping");
        if(editor.ShowDialog(FindForm())==DialogResult.OK)await RefreshTemplates();
    }
    private async Task EditRule()
    {
        var original=Selected(rules);Guid id=original is null?Guid.NewGuid():(Guid)original["RuleId"];
        var available=await store.ReadAsync("Templates",token:lifetime.Token);var options=available.AsEnumerable().Where(r=>Convert.ToBoolean(r["Approved"])&&CellText(r,"Scope")=="Student").ToArray();
        if(options.Length==0)throw new ArgumentException("Review and approve a student-context template first.");
        string Label(DataRow r)=>$"{r["Code"]} · {r["Language"]} · v{r["Version"]}";
        using var editor=new CommunicationEditor("Automation Rule — Committed Events Only",async e=>{
            var t=options.First(r=>Label(r)==e.Value("template"));
            await store.CallAsync("SaveRule",lifetime.Token,SqlCommunicationStore.P("@Id",id),SqlCommunicationStore.P("@Name",e.Value("name")),SqlCommunicationStore.P("@Event",e.Value("event")),SqlCommunicationStore.P("@TemplateId",t["TemplateId"]),SqlCommunicationStore.P("@Channel",e.Value("channel")),SqlCommunicationStore.P("@Enabled",e.Checked("enabled")),SqlCommunicationStore.P("@Approval",e.Checked("approval")),SqlCommunicationStore.P("@Delay",e.Integer("delay")),SqlCommunicationStore.P("@Limit",e.Integer("limit")));
        });
        editor.TextField("name","Rule name",original is null?"School communication rule":CellText(original,"Name"));editor.Choice("event","Verified event",["PaymentCompleted","AdmissionCompleted","PromotionCompleted","ReturnConfirmed","MonthlyOutstanding"],original is null?"PaymentCompleted":CellText(original,"EventType"));
        editor.Choice("template","Reviewed template",options.Select(Label),original is null?Label(options[0]):options.Where(r=>(Guid)r["TemplateId"]==(Guid)original["TemplateId"]).Select(Label).FirstOrDefault()??Label(options[0]));editor.Choice("channel","Channel",["Sms","WhatsApp"],original is null?"Sms":CellText(original,"Channel"));
        editor.TextField("delay","Delay in minutes",original is null?"0":CellText(original,"DelayMinutes"));editor.TextField("limit","Maximum new messages per evaluation (1–100)",original is null?"25":CellText(original,"BatchLimit"));
        editor.Flag("approval","Require campaign approval",original is null||Convert.ToBoolean(original["RequiresApproval"]));editor.Flag("enabled","Explicitly enable this rule for future events",original is not null&&Convert.ToBoolean(original["Enabled"]));
        if(editor.ShowDialog(FindForm())==DialogResult.OK)await RefreshRules();
    }
    private async Task EditSettings()
    {
        var data=await store.ReadAsync("Settings",token:lifetime.Token);var row=data.Rows[0];
        using var editor=new CommunicationEditor("Sending Mode, Budgets and Quiet Hours",async e=>{
            if(e.Value("mode")=="Live"&&!ModernMessageDialog.Confirm(FindForm(),"Enable real external delivery with provider charges? Confirm sender registration, consent and provider-approved templates before continuing.","Enable Live Communication"))throw new ArgumentException("Live delivery was not enabled.");
            await store.CallAsync("SaveSettings",lifetime.Token,SqlCommunicationStore.P("@Mode",e.Value("mode")),SqlCommunicationStore.P("@Paused",e.Checked("paused")),SqlCommunicationStore.P("@MaxBatch",e.Integer("batch")),SqlCommunicationStore.P("@ApprovalThreshold",e.Integer("approval")),SqlCommunicationStore.P("@HourLimit",e.Integer("hour")),SqlCommunicationStore.P("@DayLimit",e.Integer("day")),SqlCommunicationStore.P("@DailyBudget",e.Cost("daily")),SqlCommunicationStore.P("@MonthlyBudget",e.Cost("monthly")),SqlCommunicationStore.P("@SmsCost",e.Cost("sms")),SqlCommunicationStore.P("@WhatsAppCost",e.Cost("wa")),SqlCommunicationStore.P("@Currency",e.Value("currency")),SqlCommunicationStore.P("@QuietStart",e.Integer("start")),SqlCommunicationStore.P("@QuietEnd",e.Integer("end")),SqlCommunicationStore.P("@Offset",e.Integer("offset")),SqlCommunicationStore.P("@RegistrationConfirmed",e.Checked("registered")));
        });
        editor.Choice("mode","Mode — Simulation never contacts an external provider",["Disabled","Simulation","Live"],CellText(row,"Mode"));editor.Flag("paused","Master pause switch",Convert.ToBoolean(row["Paused"]));
        foreach(var item in new[]{("batch","Maximum recipients per campaign","MaxBatch"),("approval","Approval threshold (recipient count)","ApprovalThreshold"),("hour","Maximum attempts per rolling hour","HourLimit"),("day","Maximum attempts per UTC day","DayLimit"),("daily","Daily estimated budget","DailyBudget"),("monthly","Monthly estimated budget","MonthlyBudget"),("sms","Estimated cost per SMS segment (blank blocks live SMS)","SmsUnitCost"),("wa","Estimated cost per WhatsApp message (blank blocks live WhatsApp)","WhatsAppUnitCost"),("currency","Budget currency; all estimates use this unit","Currency"),("start","Quiet hours start (0–23)","QuietStart"),("end","Quiet hours end (same value disables quiet hours)","QuietEnd"),("offset","Quiet-hour UTC offset in minutes (+300 for Pakistan)","UtcOffsetMinutes")})
            editor.TextField(item.Item1,item.Item2,CellText(row,item.Item3));
        editor.Flag("registered","Provider/sender registration and destination-country eligibility verified",Convert.ToBoolean(row["RegistrationConfirmed"]));
        if(editor.ShowDialog(FindForm())==DialogResult.OK){InvalidatePreview();await RefreshOperational();}
    }
    private async Task EditGateway()
    {
        // SQL permission is rechecked before writing OS-protected credentials.
        await store.ReadAsync("Settings",token:lifetime.Token);
        using var editor=new CommunicationEditor("Secure Gateway Credentials — This Windows User",e=>{
            var c=Enum.Parse<MessageChannel>(e.Value("channel"));var credentials=new GatewayCredentials(e.Value("account"),e.Value("secret"),e.Value("sender"),e.Value("version"));
            _=c==MessageChannel.Sms?(IMessageProvider)new TwilioSmsProvider(Http,credentials.AccountOrPhoneId,credentials.Secret,credentials.Sender):new WhatsAppCloudProvider(Http,credentials.AccountOrPhoneId,credentials.Secret,credentials.GraphVersion);
            gateways.Save(c,credentials);return Task.CompletedTask;
        });
        editor.Choice("channel","Channel",["Sms","WhatsApp"]);editor.TextField("account","Twilio Account SID or Meta business phone-number ID");editor.TextField("secret","New access token / authentication secret (never displayed back)",password:true);editor.TextField("sender","Twilio-authorized sender ID (SMS only)");editor.TextField("version","Supported Graph API version (WhatsApp only)","");
        editor.ShowDialog(FindForm());
    }
    private static string CellText(DataRow row,string name)=>row.IsNull(name)?"":Convert.ToString(row[name],System.Globalization.CultureInfo.InvariantCulture)??"";
}
