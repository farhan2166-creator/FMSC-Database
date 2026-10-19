using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Collection;
using FMSC_SMS.Pages.Finance.StudentAccounts;
using FMSC_SMS.Pages.Students.Enrollment;

namespace FMSC_SMS.Pages.Students.Admissions;

/// <summary>A family visit retains separate child editors and uses existing protected writes.</summary>
public sealed partial class FamilyAdmissionWorkspace : V2Page,IUnsavedChanges,IBusyPage
{
    private sealed class Child
    {
        internal int Id;
        internal DataRow? Record;
        internal string Name="New child",Outcome="Not processed";
        internal bool Included=true,Initialized,ReturnCompleted,MembershipVerified=true;
        internal Control? Editor;
        internal Control? Viewport;
        internal QuickRegistrationFormPage? Registration=>Editor as QuickRegistrationFormPage;
        internal ConfirmReturnAdmissionPage? Return=>Editor as ConfirmReturnAdmissionPage;
        internal StudentFeeAssignmentsPanel? Assignments;
        internal string Status=>Record is null?"New admission":FamilyBillingData.Readiness(Record);
        internal bool AwaitsReturn=>Record is not null && FamilyBillingData.AwaitsReturn(Record) && !ReturnCompleted;
        internal bool Busy=>Registration?.IsBusy==true || (Editor as IBusyPage)?.IsBusy==true || Assignments?.IsBusy==true;
        internal bool Dirty=>(Editor as IUnsavedChanges)?.HasUnsavedChanges==true;
        internal string DisplayName=>Registration?.WorkspaceName is {Length:>0} value?value:Name;
        public override string ToString()=> $"{(Id>0?FieldText(Record,"Reference"):"NEW")} · {DisplayName}\n{Status} · {Outcome}";
    }
    private readonly Action<Func<Control>,string,string> navigate;
    private readonly Func<int,Task<DataTable>> readFamily;
    private readonly bool initialize;
    private readonly StudentPicker find=new();
    private readonly ModernTextBox familyParent=HumanUi.TextBox(false,500),familyPhone=HumanUi.TextBox(false,30),familyAddress=HumanUi.TextBox(false,500);
    private readonly FamilyStudentList children=new(){Dock=DockStyle.Fill,CheckOnClick=true,IntegralHeight=false,BorderStyle=BorderStyle.None,BackColor=AppTheme.Surface};
    private readonly Panel editorHost=new(){Dock=DockStyle.Fill,AutoScroll=true};
    private readonly Label familyContext=new(){AutoSize=true,Dock=DockStyle.Fill,Font=AppTheme.BodyBoldFont,Padding=new Padding(6),Text="Find a student to load their family, or start a new family."};
    private readonly Label summary=new(){Dock=DockStyle.Fill,AutoSize=true,Padding=new Padding(8),Font=AppTheme.NormalFont};
    private readonly List<Child> members=new();
    private readonly ModernButton addChild,process,print,refresh,newFamily,removeDraft,reviewFees,reviewAdmission,pendingRegistrations;
    private int anchor;
    private int familyVersion;
    private string father="",phone="",address="";
    private bool busy,processing,selecting,syncingFamily;
    private Child? current;
    public bool IsBusy=>busy||processing||members.Any(c=>c.Busy);
    public string BusyMessage=>"Wait for this family's current operation to finish. Each child keeps their own saved outcome.";
    public bool HasUnsavedChanges=>members.Any(c=>c.Dirty);
    public string UnsavedChangesMessage=>"Leave this family workspace and discard its unsaved child drafts? Saved admissions, fee changes and returns remain recorded.";
    public FamilyAdmissionWorkspace(Action<Func<Control>,string,string> navigate,int preselectAdmissionId=0):this(navigate,FamilyAdmissionData.ReadAsync,true)
    {
        if(preselectAdmissionId>0)Load+=async(_,_)=>await LoadFamilyAsync(preselectAdmissionId);
    }
    internal FamilyAdmissionWorkspace(Action<Func<Control>,string,string> navigate,Func<int,Task<DataTable>> readFamily,bool initialize)
        :base(PageTitles.FamilyAdmission,"Prepare siblings together, review each child's fees, then print their bill.")
    {
        this.navigate=navigate;this.readFamily=readFamily;this.initialize=initialize;
        newFamily=AddAction("New Family",async(_,_)=>await StartNewFamilyAsync(),"Secondary");
        addChild=AddAction("Add New Child",async(_,_)=>await AddNewChildAsync(),"Secondary");
        process=AddAction("Process Selected",async(_,_)=>await ProcessSelectedAsync(),"Primary");
        print=AddAction("Print Selected Bill",async(_,_)=>await PrintSelectedAsync(),"Secondary");
        refresh=AddAction("Refresh Family",async(_,_)=>await ReloadFamilyAsync(),"Secondary");
        pendingRegistrations=AddAction("Pending Registrations",async(_,_)=>await OpenPendingRegistrationsAsync(),"Secondary");
        removeDraft=AddAction("Remove Draft",(_,_)=>RemoveDraft(),"Secondary");
        reviewFees=AddAction("Review Fee Terms",async(_,_)=>await ReviewFeesAsync(),"Secondary");
        reviewAdmission=AddAction("Review Admission",async(_,_)=>await ReviewAdmissionAsync(),"Secondary");
        UseWrappingToolbar();
        find.PlaceholderText="Admission number or student name — load their family";
        find.SearchFailed+=ex=>Fail(ex);
        find.UserSelectionChanged+=async(_,_)=>{if(find.SelectedId>0)await LoadFamilyAsync(find.SelectedId);};
        var familyCard=HumanUi.Card("Family","Load one student to include their siblings. New children join the same family.");
        var familyFields=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=2};
        foreach(float width in new[]{27f,23f,23f,27f})familyFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,width));
        familyFields.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        familyFields.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        var searchField=HumanUi.Field("Find student / family",find);familyFields.Controls.Add(searchField,0,0);familyFields.SetColumnSpan(searchField,2);
        familyFields.Controls.Add(familyContext,2,0);familyFields.SetColumnSpan(familyContext,2);
        familyFields.Controls.Add(HumanUi.Field("Parent name",familyParent,true),0,1);
        familyFields.Controls.Add(HumanUi.Field("Phone (optional)",familyPhone),1,1);
        var homeField=HumanUi.Field("Address",familyAddress);familyFields.Controls.Add(homeField,2,1);familyFields.SetColumnSpan(homeField,2);
        foreach(var input in new[]{familyParent,familyPhone,familyAddress})input.TextChanged+=(_,_)=>{
            if(syncingFamily || anchor>0 || IsBusy)return;
            father=familyParent.Text.Trim();phone=familyPhone.Text.Trim();address=familyAddress.Text.Trim();
            foreach(var child in members.Where(c=>c.Id==0))child.Registration?.SetWorkspaceFamily(0,father,phone,address);
            UpdateWorkspace();
        };
        familyCard.Content.Controls.Add(familyFields);
        var childCard=HumanUi.Card("Children","Check to include. Select to review.");
        var childBody=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};
        childBody.RowStyles.Add(new RowStyle(SizeType.Percent,100));childBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));childBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var childActions=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true};
        RelocateFromActions(removeDraft,reviewFees,reviewAdmission);
        childActions.Controls.Add(reviewFees);childActions.Controls.Add(reviewAdmission);childActions.Controls.Add(removeDraft);
        childBody.Controls.Add(children,0,0);childBody.Controls.Add(summary,0,1);childBody.Controls.Add(childActions,0,2);childCard.Content.Controls.Add(childBody);
        var editorCard=HumanUi.Card("Child Details & Fees","Each child's draft is retained when you switch.");
        editorCard.Header.Visible=false;
        editorCard.Content.Controls.Add(editorHost);
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,27));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,73));
        body.Controls.Add(childCard,0,0);body.Controls.Add(editorCard,1,0);
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldCardHeight(2)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.Controls.Add(familyCard,0,0);root.Controls.Add(body,0,1);SetContent(root);
        children.SelectedIndexChanged+=async(_,_)=>{
            if(!selecting && !IsBusy && children.SelectedIndex>=0)await ShowChildAsync(members[children.SelectedIndex]);
        };
        children.ItemCheck+=(_,e)=>{
            if(selecting)return;
            if(IsBusy){e.NewValue=e.CurrentValue;return;}
            members[e.Index].Included=e.NewValue==CheckState.Checked;UpdateWorkspace();
        };
        Disposed+=(_,_)=>{foreach(var child in members)(child.Viewport??child.Editor)?.Dispose();};
        ShowEmptyEditor();UpdateWorkspace();
        if(!FamilyAdmissionData.CanUse(CurrentUser.HasPermission)){
            Enabled=false;SetStatus("Student-view access and admission or student-administration access are required.",PageStatusKind.Warning);
        }
    }
    private static string FieldText(DataRow? row,string column)=>row is not null && row.Table.Columns.Contains(column) && !row.IsNull(column)?Convert.ToString(row[column])??"":"";
    private void ShowEmptyEditor()
    {
        editorHost.Controls.Clear();
        editorHost.Controls.Add(new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,ForeColor=AppTheme.MutedText,
            Text="Find an existing student or start a new family.\nEach child's details and fees will appear here."});
    }
    private void AddMember(Child child)
    {
        members.Add(child);selecting=true;
        try{children.Items.Add(child,child.Included);}finally{selecting=false;}
    }
    private void UpdateWorkspace()
    {
        foreach(var child in members)
            if(child.Registration is { } registration && registration.SavedRegistrationId>0)
            {
                child.Id=registration.SavedRegistrationId;
                if(anchor==0 && !registration.HasUnresolvedSave && !registration.WorkspaceNeedsRefresh)
                {anchor=child.Id;father=registration.WorkspaceParent;phone=registration.WorkspaceContact;address=registration.WorkspaceAddress;SyncFamilyFields();}
            }
        children.Refresh();
        int selected=members.Count(c=>c.Included);
        decimal outstanding=members.Where(c=>c.Included && c.Record?.Table.Columns.Contains("Outstanding")==true && !c.Record.IsNull("Outstanding"))
            .Sum(c=>Convert.ToDecimal(c.Record!["Outstanding"]));
        var drafts=members.Where(c=>c.Included && c.Id==0).Select(c=>c.Registration).OfType<QuickRegistrationFormPage>().ToArray();
        summary.Text=$"{selected} of {members.Count} children included\n"+
            (CurrentUser.HasPermission("Fees.View")?$"Posted dues: Rs. {outstanding:N0}\n":"")+
            $"New monthly plan: Rs. {drafts.Sum(c=>c.WorkspaceMonthly):N0}\nNew yearly / once: Rs. {drafts.Sum(c=>c.WorkspaceOther):N0}\n"+
            $"Return opening plans: Rs. {members.Where(c=>c.Included).Sum(c=>c.Return?.WorkspaceOpeningPlan??0):N0}";
        int unreviewedReturns=members.Count(c=>c.Included && c.AwaitsReturn && c.Return is null);
        if(unreviewedReturns>0)summary.Text+=$"\n{unreviewedReturns} return(s) need fee review";
        bool idle=!IsBusy;
        editorHost.Enabled=!busy && !processing;
        find.Enabled=children.Enabled=idle;
        newFamily.Enabled=idle && CurrentUser.HasPermission("Students.Admit");
        familyParent.ReadOnly=familyPhone.ReadOnly=familyAddress.ReadOnly=anchor>0 || !idle || members.Count==0;
        bool unresolved=members.Any(c=>c.Registration?.HasUnresolvedSave==true);
        addChild.Enabled=idle && !unresolved && CurrentUser.HasPermission("Students.Admit") &&
            (members.Count==0 || CurrentUser.HasPermission("Students.Administer"));
        process.Enabled=idle && !unresolved && selected>0;
        print.Enabled=idle && selected>0 && !unresolved && members.Where(c=>c.Included).All(CanBill) && CurrentUser.HasPermission("Fees.View");
        refresh.Enabled=idle && anchor>0;
        pendingRegistrations.Enabled=idle && !unresolved && QuickRegistrationAccess.CanUse(CurrentUser.HasPermission);
        removeDraft.Enabled=idle && current is {Id:0} && current.Registration?.HasUnresolvedSave!=true;
        removeDraft.Visible=current is {Id:0};reviewFees.Visible=reviewAdmission.Visible=current is {Id:>0};
        reviewFees.Enabled=idle && current is {Id:>0,MembershipVerified:true} && !current.Dirty &&
            int.TryParse(FieldText(current.Record,"EnrollmentId"),out int enrollment) && enrollment>0 && CurrentUser.HasPermission("Fees.View");
        reviewAdmission.Enabled=idle && current is {Id:>0,MembershipVerified:true} && !current.Dirty && CurrentUser.HasPermission("Students.Admit");
        familyContext.Text=anchor>0?$"{father}\nFamily loaded from {AdmissionNumbers.ForId(anchor)} · {members.Count} children":
            members.Count>0?"New family\nEnter shared parent details here, then review each child.":"Find a student to load their family, or start a new family.";
    }
    private void SyncFamilyFields()
    {
        syncingFamily=true;
        try{familyParent.Text=father;familyPhone.Text=phone;familyAddress.Text=address;}
        finally{syncingFamily=false;}
    }
    private static bool CanBill(Child child)=>child.Id>0 && child.MembershipVerified && !child.Dirty && !child.AwaitsReturn &&
        child.Registration?.HasUnresolvedSave!=true && child.Registration?.WorkspaceNeedsRefresh!=true &&
        (child.Record is null || FieldText(child.Record,"Status")=="Pending" || child.ReturnCompleted || FamilyBillingData.Readiness(child.Record)=="Ready for Billing");
}
