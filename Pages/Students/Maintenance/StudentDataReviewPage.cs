using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Admissions;
namespace FMSC_SMS.Pages.Students.Maintenance;

public sealed class StudentDataReviewPage : UserControl
{
    public StudentDataReviewPage(Action<Func<Control>,string,string> navigate)
    {
        Dock=DockStyle.Fill;
        var tabs=new TabControl{Dock=DockStyle.Fill,Font=AppTheme.NormalFont};
        void Add(string title,Func<Control> factory){var tab=new TabPage(title){BackColor=AppTheme.Surface,Tag=factory};tabs.TabPages.Add(tab);}
        var legacyTab=new TabPage("Legacy Verification"){BackColor=AppTheme.Surface};
        legacyTab.Tag=(Func<Control>)(()=>{
            var page=new AdmissionReviewListPage(navigate,false);
            page.LegacySetupCompleted+=()=>{tabs.TabPages.Remove(legacyTab);legacyTab.Dispose();};
            return page;
        });
        tabs.TabPages.Add(legacyTab);
        Add("Record Issues / History",()=>new StudentRecordChecksPage(navigate));
        Add("Possible Duplicates",()=>new DuplicateStudentAuditPage(navigate));
        TabStrip.Apply(tabs,AppTheme.ModuleAccent("Students"));Controls.Add(TabStrip.CreateHost(tabs));
        void LoadTab(){var selected=tabs.SelectedTab;if(selected is not null&&selected.Controls.Count==0&&selected.Tag is Func<Control> create){var page=create();page.Dock=DockStyle.Fill;selected.Controls.Add(page);}}
        tabs.SelectedIndexChanged+=(_,_)=>LoadTab();Load+=(_,_)=>LoadTab();
    }
}

public sealed class AdmissionReviewListPage : V2Page
{
    private readonly Action<Func<Control>,string,string> navigate;
    private readonly bool pending;
    private readonly Func<int,Task>? openRegistration;
    private bool refreshing;
    private readonly ModernButton? goLive;
    private readonly ModernButton? reviewRegistration;
    private readonly ModernButton open;
    internal event Action? LegacySetupCompleted;
    internal event Action<int>? RegistrationReviewed;
    private DataTable records=new();
    private DataView? recordView;
    private int verifiedCount,activeCount;
    private readonly System.Windows.Forms.Timer searchDebounce=new(){Interval=200};
    private readonly ModernTextBox search=HumanUi.TextBox(false,200);
    private readonly CheckBox remaining=new(){Text="Needs review only",AutoSize=true},active=new(){Text="Active students only",AutoSize=true,Checked=true};
    public AdmissionReviewListPage(Action<Func<Control>,string,string> navigate,bool pending,Func<int,Task>? openRegistration=null)
        : base(pending?"Pending Registrations":"Legacy Verification",pending?"Find an unfinished registration and open it to edit details, confirm admission or cancel.":"Correct records in Admission and mark them verified. Finish legacy setup once all active records are ready.")
    {
        this.navigate=navigate;this.pending=pending;this.openRegistration=openRegistration;
        AddBusyAction("Refresh",RefreshAsync,"Secondary");
        open=AddAction(pending?"Open Registration":"Open Admission",async(_,_)=>await OpenSelectedAsync(),"Primary");if(!pending)GateActionAny(open,"Students.Admit","Students.Administer");
        if(pending){reviewRegistration=AddAction("Review / Confirm",async(_,_)=>await ReviewRegistrationAsync(),"Secondary");GateAction(reviewRegistration,"Students.Admit");}
        Grid.SelectionChanged+=(_,_)=>UpdateSelectionActions();
        Grid.DataBindingComplete+=(_,_)=>ClearSelection();
        Grid.MouseDown+=(_,e)=>{if(Grid.HitTest(e.X,e.Y).RowIndex<0)ClearSelection();};
        Grid.Leave+=(_,_)=>{if(IsHandleCreated)BeginInvoke(new Action(()=>{if(!IsDisposed&&!Grid.ContainsFocus&&!Actions.ContainsFocus)ClearSelection();}));};
        search.Enter+=(_,_)=>ClearSelection();
        if(!pending){goLive=AddBusyAction("Finish Setup / Go Live",async()=>{using var dialog=new LegacyGoLiveDialog();dialog.ShowDialog(FindForm());if(dialog.Changed)await RefreshAsync();},"Success");GateAction(goLive,"Students.Administer");goLive.Visible=false;}
        var card=HumanUi.Card(pending?"Registrations Awaiting Completion":"Legacy Records",pending?"Payments and family links remain attached when admission is completed.":"Changes to student details, placement or fees clear the verification mark.");card.Content.Controls.Add(Grid);
        var filters=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65));filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));
        search.PlaceholderText="Admission / registration reference, student or parent name";filters.Controls.Add(HumanUi.Field("Search records",search),0,0);
        var options=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.LeftToRight,WrapContents=false,Padding=new Padding(0,AppMetrics.FieldRowHeight/3,0,0)};
        if(!pending){options.Controls.Add(active);options.Controls.Add(remaining);filters.Controls.Add(options,1,0);}
        else filters.SetColumnSpan(filters.GetControlFromPosition(0,0)!,2);
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.Controls.Add(filters,0,0);root.Controls.Add(card,0,1);SetContent(root);
        ClearOnOutsideClick(this);UpdateSelectionActions();
        Grid.CellToolTipTextNeeded+=(_,e)=>{if(e.RowIndex>=0&&e.ColumnIndex>=0&&Grid.Columns[e.ColumnIndex].Name=="Family"&&Grid.Rows[e.RowIndex].DataBoundItem is DataRowView row)e.ToolTipText=Convert.ToString(row["LinkedChildren"]);};
        search.TextChanged+=(_,_)=>{searchDebounce.Stop();searchDebounce.Start();};
        searchDebounce.Tick+=(_,_)=>ApplyFilter();
        remaining.CheckedChanged+=(_,_)=>ApplyFilter();active.CheckedChanged+=(_,_)=>ApplyFilter();
        Load+=async(_,_)=>await RefreshAsync();Grid.CellDoubleClick+=async(_,e)=>{if(e.RowIndex>=0)await OpenSelectedAsync();};
    }
    private async Task OpenSelectedAsync(){if(refreshing)return;ApplyFilter();if(pending&&!QuickRegistrationAccess.CanUse(CurrentUser.HasPermission)){Warn("Your role needs admission and student-view access to open this registration.");return;}if(SelectedRecord is not DataRowView row)return;int id=Convert.ToInt32(row["AdmissionId"]);if(pending){if(openRegistration is not null)await openRegistration(id);else navigate(()=>new QuickRegistrationPage(navigate,id),"Students","Quick Registration");}else navigate(()=>new StudentAdmissionPage(navigate,id),"Students",PageTitles.Admissions);}
    private async Task ReviewRegistrationAsync(){
        if(refreshing)return;ApplyFilter();
        if(!QuickRegistrationAccess.CanUse(CurrentUser.HasPermission)){Warn("Your role needs admission and student-view access to review this registration.");return;}
        if(SelectedRecord is not DataRowView row)return;
        int id=Convert.ToInt32(row["AdmissionId"]);
        using var dialog=new ApplicationWindowForm{Text="Quick Registration — Review / Confirm",Size=new Size(AppScale.Scale(1280),AppScale.Scale(900)),MinimumSize=new Size(AppScale.Scale(1000),AppScale.Scale(700)),StartPosition=FormStartPosition.CenterParent};
        using var page=new StudentAdmissionPage(null,id,registrationReview:true){Dock=DockStyle.Fill};
        page.RegistrationFinished+=()=>{dialog.DialogResult=DialogResult.OK;dialog.Close();};dialog.Controls.Add(page);dialog.ShowDialog(FindForm());await RefreshAsync();if(dialog.DialogResult==DialogResult.OK)RegistrationReviewed?.Invoke(id);
    }
    private DataRowView? SelectedRecord=>Grid.SelectedRows.Count==1?Grid.SelectedRows[0].DataBoundItem as DataRowView:null;
    private void UpdateSelectionActions()
    {
        bool selected=!refreshing&&SelectedRecord is not null;
        open.Enabled=selected&&(pending?QuickRegistrationAccess.CanUse(CurrentUser.HasPermission):CurrentUser.HasPermission("Students.Admit")||CurrentUser.HasPermission("Students.Administer"));
        if(reviewRegistration is not null)reviewRegistration.Enabled=selected&&QuickRegistrationAccess.CanUse(CurrentUser.HasPermission);
    }
    private void ClearSelection(){Grid.ClearSelection();Grid.CurrentCell=null;UpdateSelectionActions();}
    private void ClearOnOutsideClick(Control parent)
    {
        // Action clicks deliberately use the visibly selected row. All other
        // page clicks leave the grid with no implied confirmation target.
        foreach(Control child in parent.Controls)
        {
            if(child==Grid||child==Actions)continue;
            child.MouseDown+=(_,_)=>ClearSelection();ClearOnOutsideClick(child);
        }
    }
    internal Task RefreshRecordsAsync()=>RefreshAsync();
    private void UpdateLegacySetupState(bool completed)
    {
        if(goLive is not null)goLive.Visible=!completed&&CurrentUser.HasPermission("Students.Administer");
        if(completed){SetStatus("Legacy setup is complete.");LegacySetupCompleted?.Invoke();}
    }
    private async Task RefreshAsync()
    {
        if(refreshing||IsDisposed)return;refreshing=true;ClearSelection();
        try{
            if(!pending){
                var setup=await Task.Run(()=>DatabaseHelper.QuerySql("""
                    EXEC dbo.RequirePermission @UserId,N'Students.View';
                    SELECT CompletedAt FROM dbo.LegacyAdmissionSetup WHERE SetupId=1;
                    """,DatabaseHelper.Int("@UserId",CurrentUser.UserId)));
                if(IsDisposed)return;
                bool completed=setup.Rows.Count>0&&setup.Rows[0]["CompletedAt"]!=DBNull.Value;
                UpdateLegacySetupState(completed);
                if(completed)return;
            }
            var data=await Task.Run(()=>DatabaseHelper.QuerySql(AdmissionReviewSql.Query,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Bit("@Pending",pending)));
            if(IsDisposed)return;
            recordView?.Dispose();records=data;records.CaseSensitive=false;records.Locale=System.Globalization.CultureInfo.InvariantCulture;
            recordView=new DataView(records);Grid.DataSource=recordView;
            verifiedCount=records.AsEnumerable().Count(r=>Convert.ToString(r["Status"])=="Active"&&Convert.ToString(r["Verification"])=="Verified");
            activeCount=records.AsEnumerable().Count(r=>Convert.ToString(r["Status"])=="Active");Grid.Columns["AdmissionId"].Visible=false;Grid.Columns["Verification"].Visible=!pending;
            Grid.Columns["Reference"].HeaderText="Admission / Registration";Grid.Columns["StudentName"].HeaderText="Student";Grid.Columns["FatherName"].HeaderText="Parent";Grid.Columns["ContactNumber"].HeaderText="Contact";
            if(pending){Grid.Columns["Reference"].HeaderText="Temp Reference";Grid.Columns["Status"].Visible=false;Grid.Columns["BillingStart"].Visible=false;}
            Grid.Columns["YearName"].HeaderText="Academic Year";
            Grid.Columns["ClassName"].Visible=Grid.Columns["SectionName"].Visible=false;
            Grid.Columns["Placement"].HeaderText="Campus / Class / Section";
            Grid.Columns["Family"].HeaderText="Family Group";Grid.Columns["Family"].Visible=pending;
            Grid.Columns["LinkedChildren"].Visible=false;
            // DatabaseHelper enriches AdmissionId with a display number, using
            // QR references for pending students. Never label that as admission.
            if(Grid.Columns.Contains("AdmissionNumber"))Grid.Columns["AdmissionNumber"].Visible=false;
            foreach(string key in new[]{"PlacementStart","BillingStart"}){Grid.Columns[key].HeaderText=key=="PlacementStart"?"Placement Start":"Billing Start";Grid.Columns[key].DefaultCellStyle.Format="dd MMM yyyy";}
            if(pending)Grid.Columns["PlacementStart"].HeaderText="Joining Date";
            UiHelper.FitColumnsToWidth(Grid);ApplyFilter();
        }catch(Exception ex){if(!IsDisposed)Fail(ex);}finally{refreshing=false;if(!IsDisposed){ClearSelection();UpdateSelectionActions();}}
    }
    private void ApplyFilter()
    {
        searchDebounce.Stop();
        if(IsDisposed||recordView is null)return;
        string filter=AdmissionReviewFilter.Create(pending,active.Checked,remaining.Checked,search.Text);
        if(recordView.RowFilter!=filter){recordView.RowFilter=filter;ClearSelection();}
        SetStatus(pending?$"{recordView.Count} pending registration(s).":$"{verifiedCount}/{activeCount} active students verified · {recordView.Count} shown.");
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){searchDebounce.Stop();searchDebounce.Dispose();recordView?.Dispose();}
        base.Dispose(disposing);
    }
}
