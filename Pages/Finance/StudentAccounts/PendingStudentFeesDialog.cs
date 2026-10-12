using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Finance.StudentAccounts;

internal sealed class PendingStudentFeesDialog : ApplicationWindowForm
{
    private sealed record MonthChoice(DateTime Month) { public override string ToString() => Month.ToString("MMMM yyyy"); }
    private readonly int enrollmentId;
    private readonly ModernComboBox month = HumanUi.Combo();
    private readonly DataGridView grid = HumanUi.Grid(false);
    private readonly Label status = new() { Dock=DockStyle.Fill, TextAlign=ContentAlignment.MiddleLeft, AutoEllipsis=true };
    private readonly ModernButton post = new() { Text="Post Selected Fees", ThemeRole="Success", AutoSize=true, Enabled=false };
    private readonly ModernButton refresh = new() { Text=UiText.Actions.Refresh, AutoSize=true };
    private DataTable preview = new();
    private bool busy;
    private string resultNote = "";
    private bool historical;
    private string yearName = "";
    private readonly ModernTextBox historicalReason = HumanUi.TextBox(false, 400);
    private readonly Label scopeNotice = new() { Dock=DockStyle.Fill };
    private readonly RowStyle reasonRow = new(SizeType.Absolute,0);
    public bool ReviewCompleted { get; private set; }
    public bool PostingMade { get; private set; }
    public PendingStudentFeesDialog(int enrollmentId)
    {
        this.enrollmentId=enrollmentId;
        Text="Review Pending Fees"; StartPosition=FormStartPosition.CenterParent;
        var workArea=Screen.FromControl(this).WorkingArea;
        ClientSize=new Size(Math.Min(AppScale.Scale(1060),workArea.Width-AppScale.Scale(48)),
            Math.Min(AppScale.Scale(500),workArea.Height-AppScale.Scale(80)));
        MinimizeBox=false; ShowInTaskbar=false;
        grid.Dock=DockStyle.Fill; grid.AutoGenerateColumns=false; grid.ReadOnly=true;
        grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect=false;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name="Post", HeaderText="Post?", Width=65 });
        foreach(var field in new[]{("FeeTypeName","Fee"),("GrossAmount","Amount"),("DiscountAmount","Discount"),("NetAmount","Net payable"),("ReviewNote","Note")})
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name=field.Item1, HeaderText=field.Item2, DataPropertyName=field.Item1, ReadOnly=true });
        grid.Columns["Post"].FillWeight=45;
        grid.Columns["FeeTypeName"].FillWeight=140;
        foreach(var name in new[]{"GrossAmount","DiscountAmount","NetAmount"}) grid.Columns[name].FillWeight=90;
        grid.Columns["ReviewNote"].FillWeight=260;
        grid.Columns["ReviewNote"].DefaultCellStyle.WrapMode=DataGridViewTriState.True;
        grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;
        grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;
        grid.CellClick+=(_,e)=>ToggleRow(e.RowIndex);
        grid.KeyDown+=(_,e)=> {
            if(e.KeyCode!=Keys.Space || grid.CurrentRow is null) return;
            ToggleRow(grid.CurrentRow.Index); e.Handled=true; e.SuppressKeyPress=true;
        };
        grid.CellValueChanged+=(_,_)=>UpdateTotal();
        var close=new ModernButton { Text=UiText.Actions.Close, AutoSize=true, DialogResult=DialogResult.OK };
        var root=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=6, Padding=new Padding(AppScale.Scale(16)) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppScale.Scale(60)));
        root.RowStyles.Add(reasonRow);
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppScale.Scale(30)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.ButtonRowHeight));
        root.Controls.Add(HumanUi.Field("Billing month",month),0,0);
        root.Controls.Add(scopeNotice,0,1);
        root.Controls.Add(HumanUi.Field("Reason for historical posting",historicalReason,true),0,2);
        root.Controls.Add(grid,0,3); root.Controls.Add(status,0,4);
        var actions=HumanUi.ButtonRow(refresh,post,close); actions.Dock=DockStyle.Fill; root.Controls.Add(actions,0,5);
        Controls.Add(root);
        month.SelectedIndexChanged+=async(_,_)=> { if(!busy) await LoadMonthAsync(); };
        refresh.Click+=async(_,_)=>await LoadMonthsAsync();
        historicalReason.TextChanged+=(_,_)=>UpdateTotal();
        post.Click+=async(_,_)=>await PostAsync();
        Shown+=async(_,_)=>await LoadMonthsAsync();
        FormClosing+=(_,e)=> {
            if(busy) e.Cancel=true;
        };
    }
    private void ToggleRow(int index)
    {
        if(busy || !ReviewCompleted || historical && !CurrentUser.IsAdministrator || index<0 || index>=grid.Rows.Count) return;
        var cell=grid.Rows[index].Cells["Post"];
        cell.Value=cell.Value is not true;
        UpdateTotal();
    }
    private void SetBusy(bool value) { busy=value; month.Enabled=!value; refresh.Enabled=!value; grid.Enabled=!value; post.Enabled=!value; }
    private async Task LoadMonthsAsync()
    {
        if(busy) return;
        DateTime? previous=month.SelectedItem is MonthChoice chosen ? chosen.Month : null;
        SetBusy(true);
        bool loaded=false;
        try {
            var scope=await Task.Run(()=>DatabaseHelper.QuerySql("""
                EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
                SELECT A.YearName,A.IsCurrent FROM dbo.Enrollments E
                JOIN dbo.AcademicYears A ON A.AcademicYearId=E.AcademicYearId WHERE E.EnrollmentId=@EnrollmentId;
                """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@EnrollmentId",enrollmentId)));
            if(scope.Rows.Count!=1) throw new InvalidOperationException("The placement no longer exists.");
            historical=!Convert.ToBoolean(scope.Rows[0]["IsCurrent"]);
            yearName=Convert.ToString(scope.Rows[0]["YearName"]) ?? "";
            reasonRow.Height=historical && CurrentUser.IsAdministrator ? AppMetrics.FieldRowHeight : 0;
            historicalReason.Enabled=historical && CurrentUser.IsAdministrator;
            scopeNotice.Text=$"Academic year: {yearName}. Monthly fees only.\r\n"+
                (historical ? CurrentUser.IsAdministrator
                    ? "Historical posting: review the month and amount, then enter a reason. Admission charges stay unchanged."
                    : "Historical fees are view-only. An administrator must review and post them."
                    : "Choose a month; click a row to select it. Reposting a reversed fee creates a new charge.");
            var pending=await FeeAssignmentPosting.PendingThroughAsync(enrollmentId,DateTime.MaxValue.Date);
            month.Items.Clear();
            foreach(var date in pending.AsEnumerable().Select(row=>Convert.ToDateTime(row["BillingMonth"])).Distinct().OrderBy(date=>date)) month.Items.Add(new MonthChoice(date));
            if(month.Items.Count>0) {
                var options=month.Items.Cast<MonthChoice>().ToArray();
                month.SelectedItem=options.FirstOrDefault(option=>option.Month==previous) ??
                    options.FirstOrDefault(option=>previous.HasValue && option.Month>previous.Value) ?? options[0];
            }
            loaded=true;
        } catch(Exception ex) { ReviewCompleted=false; ModernMessageDialog.ShowError(this,ex.Message); }
        finally { SetBusy(false); UpdateTotal(); }
        if(loaded) await LoadMonthAsync();
    }
    private async Task LoadMonthAsync()
    {
        if(busy) return;
        if(month.SelectedItem is not MonthChoice selected) {
            preview=new(); grid.DataSource=null;
            status.Text=resultNote+"No monthly fees await posting.";
            ReviewCompleted=true; post.Enabled=false; month.Enabled=false; return;
        }
        ReviewCompleted=false;
        SetBusy(true);
        try {
            preview=await FeeAssignmentPosting.PreviewAsync(enrollmentId,selected.Month);
            var reversed=await Task.Run(()=>DatabaseHelper.QuerySql("""
                EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
                SELECT DISTINCT FeeTypeId FROM dbo.BillingCharges WHERE EnrollmentId=@EnrollmentId
                    AND BillingMonth=@Month AND ChargeKind=N'Monthly' AND Status=N'Cancelled';
                """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@EnrollmentId",enrollmentId),DatabaseHelper.Date("@Month",selected.Month)));
            preview.Columns.Add("ReviewNote",typeof(string));
            foreach(DataRow row in preview.Rows) row["ReviewNote"]=reversed.AsEnumerable().Any(r=>Convert.ToInt32(r[0])==Convert.ToInt32(row["FeeTypeId"])) ? "Previously reversed — repost only if intended" : "Not posted";
            grid.DataSource=preview;
            foreach(DataGridViewRow row in grid.Rows) row.Cells["Post"].Value=false;
            ReviewCompleted=true;
        } catch(Exception ex) { preview=new(); grid.DataSource=null; ModernMessageDialog.ShowError(this,ex.Message); }
        finally { SetBusy(false); UpdateTotal(); }
        if(ReviewCompleted && preview.Rows.Count==0) await LoadMonthsAsync();
    }
    private DataTable Selected()
    {
        grid.EndEdit(); var selected=preview.Clone();
        foreach(DataGridViewRow row in grid.Rows)
            if(row.Cells["Post"].Value is true && row.DataBoundItem is DataRowView item) selected.ImportRow(item.Row);
        return selected;
    }
    private void UpdateTotal()
    {
        if(!ReviewCompleted) { status.Text="Review not loaded. Refresh before posting."; post.Enabled=false; return; }
        var selected=Selected();
        status.Text=resultNote+$"{preview.Rows.Count} pending; {selected.Rows.Count} selected — Rs. {selected.AsEnumerable().Sum(row=>Convert.ToDecimal(row["NetAmount"])):N0}";
        post.Enabled=!busy && selected.Rows.Count>0 && (!historical ||
            CurrentUser.IsAdministrator && !string.IsNullOrWhiteSpace(historicalReason.Text));
    }
    private async Task PostAsync()
    {
        if(busy) return;
        var selected=Selected(); if(selected.Rows.Count==0) return;
        if(historical && (!CurrentUser.IsAdministrator || string.IsNullOrWhiteSpace(historicalReason.Text))) return;
        string reason=historicalReason.Text.Trim();
        DateTime date=Convert.ToDateTime(selected.Rows[0]["BillingMonth"]);
        decimal total=selected.AsEnumerable().Sum(row=>Convert.ToDecimal(row["NetAmount"]));
        string details=string.Join("\r\n",selected.AsEnumerable().Select(row =>
            $"{row["FeeTypeName"]}: Rs. {Convert.ToDecimal(row["NetAmount"]):N0}" +
            (Convert.ToString(row["ReviewNote"])!.StartsWith("Previously reversed") ? " (reposting a reversed fee)" : "")));
        if(!ModernMessageDialog.Confirm(this,$"Academic year: {yearName}\r\nPost {selected.Rows.Count} selected fee(s) for {date:MMMM yyyy}?\r\n\r\n{details}\r\n\r\nTotal: Rs. {total:N0}"+
            (historical ? $"\r\nHistorical posting reason: {reason}" : ""),"Post Selected Fees",defaultYes:false)) return;
        SetBusy(true);
        try {
            int created=await FeeAssignmentPosting.PostReviewedAsync(selected,reason);
            PostingMade=true;
            resultNote=$"Posted {created} fee(s), {date:MMM yyyy}, Rs. {total:N0}.  ";
        }
        catch(Exception ex) { ModernMessageDialog.ShowError(this,ex.Message,"Fees Not Posted"); }
        finally { SetBusy(false); }
        await LoadMonthsAsync();
    }
}
