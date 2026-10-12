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
    internal event Action? LegacySetupCompleted;
    internal event Action<int>? RegistrationReviewed;
    private DataTable records=new();
    private readonly ModernTextBox search=HumanUi.TextBox(false,200);
    private readonly CheckBox remaining=new(){Text="Needs review only",AutoSize=true},active=new(){Text="Active students only",AutoSize=true,Checked=true};
    public AdmissionReviewListPage(Action<Func<Control>,string,string> navigate,bool pending,Func<int,Task>? openRegistration=null)
        : base(pending?"Pending Registrations":"Legacy Verification",pending?"Find an unfinished registration and open it to edit details, confirm admission or cancel.":"Correct records in Admission and mark them verified. Finish legacy setup once all active records are ready.")
    {
        this.navigate=navigate;this.pending=pending;this.openRegistration=openRegistration;
        AddBusyAction("Refresh",RefreshAsync,"Secondary");
        var open=AddBusyAction(pending?"Open Registration":"Open Admission",OpenSelectedAsync,"Primary");GateActionAny(open,"Students.Admit","Students.Administer");
        if(pending){reviewRegistration=AddBusyAction("Review / Confirm",ReviewRegistrationAsync,"Secondary");GateAction(reviewRegistration,"Students.Admit");reviewRegistration.Enabled=false;Grid.SelectionChanged+=(_,_)=>reviewRegistration.Enabled=CurrentUser.HasPermission("Students.Admit")&&Grid.CurrentRow?.DataBoundItem is DataRowView;}
        if(!pending){goLive=AddBusyAction("Finish Setup / Go Live",async()=>{using var dialog=new LegacyGoLiveDialog();dialog.ShowDialog(FindForm());if(dialog.Changed)await RefreshAsync();},"Success");GateAction(goLive,"Students.Administer");goLive.Visible=false;}
        var card=HumanUi.Card(pending?"Registrations Awaiting Completion":"Legacy Records",pending?"Payments and family links remain attached when admission is completed.":"Changes to student details, placement or fees clear the verification mark.");card.Content.Controls.Add(Grid);
        var filters=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65));filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));
        search.PlaceholderText="Admission / registration reference, student or parent name";filters.Controls.Add(HumanUi.Field("Search records",search),0,0);
        var options=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.LeftToRight,WrapContents=false,Padding=new Padding(0,AppMetrics.FieldRowHeight/3,0,0)};
        if(!pending){options.Controls.Add(active);options.Controls.Add(remaining);filters.Controls.Add(options,1,0);}
        else filters.SetColumnSpan(filters.GetControlFromPosition(0,0)!,2);
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.Controls.Add(filters,0,0);root.Controls.Add(card,0,1);SetContent(root);
        search.TextChanged+=(_,_)=>ApplyFilter();remaining.CheckedChanged+=(_,_)=>ApplyFilter();active.CheckedChanged+=(_,_)=>ApplyFilter();
        Load+=async(_,_)=>await RefreshAsync();Grid.CellDoubleClick+=async(_,e)=>{if(e.RowIndex>=0)await OpenSelectedAsync();};
    }
    private async Task OpenSelectedAsync(){if(Grid.CurrentRow?.DataBoundItem is not DataRowView row)return;int id=Convert.ToInt32(row["AdmissionId"]);if(pending){if(openRegistration is not null)await openRegistration(id);else navigate(()=>new QuickRegistrationPage(navigate,id),"Students","Quick Registration");}else navigate(()=>new StudentAdmissionPage(navigate,id),"Students",PageTitles.Admissions);}
    private async Task ReviewRegistrationAsync(){
        if(Grid.CurrentRow?.DataBoundItem is not DataRowView row)return;
        int id=Convert.ToInt32(row["AdmissionId"]);
        using var dialog=new ApplicationWindowForm{Text="Quick Registration — Review / Confirm",Size=new Size(AppScale.Scale(1280),AppScale.Scale(900)),MinimumSize=new Size(AppScale.Scale(1000),AppScale.Scale(700)),StartPosition=FormStartPosition.CenterParent};
        using var page=new StudentAdmissionPage(null,id,registrationReview:true){Dock=DockStyle.Fill};
        page.RegistrationFinished+=()=>{dialog.DialogResult=DialogResult.OK;dialog.Close();};dialog.Controls.Add(page);dialog.ShowDialog(FindForm());await RefreshAsync();RegistrationReviewed?.Invoke(id);
    }
    internal Task RefreshRecordsAsync()=>RefreshAsync();
    private void UpdateLegacySetupState(bool completed)
    {
        if(goLive is not null)goLive.Visible=!completed&&CurrentUser.HasPermission("Students.Administer");
        if(completed){SetStatus("Legacy setup is complete.");LegacySetupCompleted?.Invoke();}
    }
    private async Task RefreshAsync()
    {
        if(refreshing||IsDisposed)return;refreshing=true;
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
            var data=await Task.Run(()=>DatabaseHelper.QuerySql("""
                EXEC dbo.RequirePermission @UserId,N'Students.View';
                SELECT S.AdmissionId,COALESCE(S.AdmissionNumber,S.QuickReference) Reference,S.StudentName,S.FatherName,S.ContactNumber,S.Status,
                 CASE WHEN S.LegacyVerifiedAt IS NULL THEN N'Needs Review' ELSE N'Verified' END Verification,
                 Y.YearName,C.ClassName,SEC.SectionName,E.EnrollmentDate PlacementStart,E.BillingStartDate BillingStart,
                 COALESCE((SELECT SUM(dbo.GetFeeChargeBalance(B.BillingChargeId)) FROM dbo.BillingCharges B WHERE B.AdmissionId=S.AdmissionId AND B.Status<>N'Cancelled'),0) Outstanding
                FROM dbo.Students S OUTER APPLY(SELECT TOP(1) * FROM dbo.Enrollments H WHERE H.AdmissionId=S.AdmissionId ORDER BY H.EnrollmentId DESC) E
                LEFT JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId LEFT JOIN dbo.CampusClassSections CS ON CS.ClassSectionId=E.ClassSectionId
                LEFT JOIN dbo.Classes C ON C.ClassId=CS.ClassId LEFT JOIN dbo.Sections SEC ON SEC.SectionId=CS.SectionId
                WHERE (@Pending=1 AND S.Status=N'Pending') OR (@Pending=0 AND S.LegacySetupPending=1) ORDER BY S.LegacyVerifiedAt,S.AdmissionId;
                """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Bit("@Pending",pending)));
            if(IsDisposed)return;records=data;Grid.DataSource=data;Grid.Columns["AdmissionId"].Visible=false;Grid.Columns["Verification"].Visible=!pending;
            Grid.Columns["Reference"].HeaderText="Admission / Registration";Grid.Columns["StudentName"].HeaderText="Student";Grid.Columns["FatherName"].HeaderText="Parent";Grid.Columns["ContactNumber"].HeaderText="Contact";
            if(pending){Grid.Columns["Reference"].HeaderText="Temp Reference";Grid.Columns["Status"].Visible=false;Grid.Columns["BillingStart"].Visible=false;}
            Grid.Columns["YearName"].HeaderText="Academic Year";Grid.Columns["ClassName"].HeaderText="Class";Grid.Columns["SectionName"].HeaderText="Section";
            foreach(string key in new[]{"PlacementStart","BillingStart"}){Grid.Columns[key].HeaderText=key=="PlacementStart"?"Placement Start":"Billing Start";Grid.Columns[key].DefaultCellStyle.Format="dd MMM yyyy";}
            if(pending)Grid.Columns["PlacementStart"].HeaderText="Joining Date";
            UiHelper.FitColumnsToWidth(Grid);ApplyFilter();
        }catch(Exception ex){if(!IsDisposed)Fail(ex);}finally{refreshing=false;}
    }
    private void ApplyFilter()
    {
        if(!records.Columns.Contains("StudentName"))return;
        string term=search.Text.Trim();var filtered=records.Clone();
        foreach(DataRow row in records.Rows){if(!pending&&active.Checked&&Convert.ToString(row["Status"])!="Active")continue;if(!pending&&remaining.Checked&&Convert.ToString(row["Verification"])=="Verified")continue;
            if(term.Length>0&&!new[]{"Reference","StudentName","FatherName","ContactNumber"}.Any(key=>(Convert.ToString(row[key])??"").Contains(term,StringComparison.OrdinalIgnoreCase)))continue;filtered.ImportRow(row);}
        Grid.DataSource=filtered;
        int verified=records.AsEnumerable().Count(r=>Convert.ToString(r["Status"])=="Active"&&Convert.ToString(r["Verification"])=="Verified");
        int activeCount=records.AsEnumerable().Count(r=>Convert.ToString(r["Status"])=="Active");
        SetStatus(pending?$"{filtered.Rows.Count} pending registration(s).":$"{verified}/{activeCount} active students verified · {filtered.Rows.Count} shown.");
    }
}
