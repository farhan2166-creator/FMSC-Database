using System.Text.Json;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Collection;
namespace FMSC_SMS.Pages.Students.Certificates;

internal sealed class LeaveFeeReviewBar : TableLayoutPanel
{
    private readonly Func<int> admission;
    private readonly Func<int> enrollment;
    private readonly Func<DateTime> date;
    private readonly Func<bool> certificateExists;
    private readonly Label summary = new() {AutoSize=true,Margin=new Padding(0,10,12,0),Font=AppTheme.BodyBoldFont};
    private readonly ModernButton collect = new() {Text="Collect Fees",Width=130};
    private readonly ModernButton review = new() {Text="Review Fees",Width=130};
    private DataTable later = new();
    private DataTable displayCharges = new();
    private decimal balance;
    private bool reviewed;
    private bool loaded;
    private string snapshot = "[]";
    private DateTime snapshotDate;
    private int snapshotAdmission;
    private int snapshotEnrollment;
    private readonly HashSet<long> reverseIds = new();
    internal LeaveFeeReviewBar(Func<int> admission,Func<int> enrollment,Func<DateTime> date,Func<bool> certificateExists)
    {
        this.admission=admission; this.enrollment=enrollment; this.date=date;
        this.certificateExists=certificateExists;
        Dock=DockStyle.Fill; Margin=new Padding(8,0,0,0); ColumnCount=3; RowCount=1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,AppScale.Scale(130)));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,AppScale.Scale(130)));
        RowStyles.Add(new RowStyle(SizeType.Percent,100));
        summary.Margin=Padding.Empty; summary.Dock=DockStyle.Fill;
        summary.TextAlign=ContentAlignment.MiddleLeft;
        summary.AutoSize=false; summary.AutoEllipsis=true;
        Controls.Add(summary,0,0);
        collect.Dock=review.Dock=DockStyle.None;
        collect.Anchor=review.Anchor=AnchorStyles.Left;
        collect.Width=review.Width=AppScale.Scale(124);
        collect.Height=review.Height=AppMetrics.ButtonHeight;
        Controls.Add(collect,1,0); Controls.Add(review,2,0);
        collect.Click+=(_,_)=> OpenCollection();
        review.Click+=(_,_)=> {try {RefreshFees(); ReviewLaterCharges();} catch(Exception ex) {ShowError(ex);}};
        ClearFees();
    }
    internal void ClearFees()
    {
        loaded=false; reviewed=false; reverseIds.Clear(); later=new(); displayCharges=new(); snapshot="[]";
        summary.Text="Select a student to view fees."; summary.ForeColor=AppTheme.MutedText;
        collect.Enabled=false; review.Enabled=false;
        Visible=false;
    }
    internal void RefreshFees()
    {
        if(admission()<=0) {ClearFees(); return;}
        try {
            var data=DatabaseHelper.QuerySql("""
                EXEC dbo.RequirePermission @UserId,N'Students.Leave';
                EXEC dbo.RequirePermission @UserId,N'Students.View';
                SELECT B.BillingChargeId,B.EnrollmentId,F.FeeTypeName,B.BillingMonth,B.ChargeDate,B.NetAmount,B.Status,
                    (SELECT E.Status FROM dbo.Enrollments E WHERE E.EnrollmentId=B.EnrollmentId) EnrollmentStatus,
                    CONVERT(bit,CASE WHEN EXISTS(
                        SELECT 1 FROM dbo.FeePaymentAllocations A JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
                        WHERE A.BillingChargeId=B.BillingChargeId AND P.Status=N'Completed' AND A.Amount>0)
                        THEN 1 ELSE 0 END) HasPaymentHistory,
                    CONVERT(bit,CASE WHEN B.EnrollmentId=@EnrollmentId AND
                        (B.BillingMonth>DATEFROMPARTS(YEAR(@LeaveDate),MONTH(@LeaveDate),1)
                        OR (B.BillingMonth IS NULL AND B.ChargeDate>@LeaveDate)) THEN 1 ELSE 0 END) IsLater,
                    dbo.GetFeeChargeBalance(B.BillingChargeId) Balance,
                    CONVERT(bit,CASE WHEN B.EnrollmentId=@EnrollmentId AND EXISTS(SELECT 1 FROM dbo.Enrollments E WHERE E.EnrollmentId=B.EnrollmentId AND E.Status=N'Active') AND B.Status=N'Open' AND NOT EXISTS(
                        SELECT 1 FROM dbo.FeePaymentAllocations A JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId
                        WHERE A.BillingChargeId=B.BillingChargeId AND P.Status=N'Completed' AND A.Amount>0)
                        AND dbo.GetFeeChargeBalance(B.BillingChargeId)=B.NetAmount THEN 1 ELSE 0 END) CanReverse,
                    (SELECT ISNULL(SUM(dbo.GetFeeChargeBalance(X.BillingChargeId)),0)
                        FROM dbo.BillingCharges X WHERE X.AdmissionId=@AdmissionId AND X.Status<>N'Cancelled') TotalBalance
                FROM dbo.BillingCharges B JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
                WHERE B.AdmissionId=@AdmissionId AND B.Status<>N'Cancelled'
                    AND (dbo.GetFeeChargeBalance(B.BillingChargeId)>0 OR (B.EnrollmentId=@EnrollmentId AND
                        (B.BillingMonth>DATEFROMPARTS(YEAR(@LeaveDate),MONTH(@LeaveDate),1)
                        OR (B.BillingMonth IS NULL AND B.ChargeDate>@LeaveDate))))
                ORDER BY B.BillingMonth,F.DisplayOrder,B.BillingChargeId;
                """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@AdmissionId",admission()),
                DatabaseHelper.Int("@EnrollmentId",enrollment()),DatabaseHelper.Date("@LeaveDate",date().Date));
            decimal currentBalance=data.Rows.Count>0 ? Convert.ToDecimal(data.Rows[0]["TotalBalance"])
                : Convert.ToDecimal(DatabaseHelper.ScalarUser("GetStudentProfileOutstandingBalance",DatabaseHelper.Int("@AdmissionId",admission()))??0m);
            var laterRows=data.AsEnumerable().Where(row=>Convert.ToBoolean(row["IsLater"])).ToArray();
            string json=JsonSerializer.Serialize(data.AsEnumerable().Where(row=>Convert.ToInt32(row["EnrollmentId"])==enrollment()).Select(row=>new {
                BillingChargeId=Convert.ToInt64(row["BillingChargeId"]),NetAmount=Convert.ToDecimal(row["NetAmount"]),
                Status=Convert.ToString(row["Status"]),Balance=Convert.ToDecimal(row["Balance"])
            }));
            if(snapshot!=json || balance!=currentBalance || snapshotDate!=date().Date || snapshotAdmission!=admission() || snapshotEnrollment!=enrollment()) {
                reverseIds.Clear(); reviewed=false;
            }
            displayCharges=data; later=data.Clone(); foreach(var row in laterRows) later.ImportRow(row);
            snapshot=json; balance=currentBalance; snapshotDate=date().Date; snapshotAdmission=admission(); snapshotEnrollment=enrollment(); loaded=true;
            UpdateSummary();
        } catch(Exception ex) {
            loaded=false; reviewed=false; reverseIds.Clear(); collect.Enabled=false; review.Enabled=false;
            summary.Text="Fee balance unavailable — refresh before confirming."; summary.ForeColor=AppTheme.Danger;
            Visible=admission()>0;
            AppExceptionHandler.LogNonFatal(ex,"Leave certificate fee summary");
        }
    }
    private void UpdateSummary()
    {
        summary.Text=$"Outstanding: Rs. {balance:N0}";
        Visible=balance>0;
        summary.ForeColor=balance>0 ? AppTheme.Danger : AppTheme.Text;
        collect.Enabled=admission()>0 && CurrentUser.HasPermission("Fees.Collect");
        UpdateCertificateState();
    }
    internal void UpdateCertificateState() => review.Enabled=loaded && balance>0 && !certificateExists();
    internal bool Prepare()
    {
        RefreshFees();
        if(!loaded) {ModernMessageDialog.Show(FindForm(),"The fee balance could not be read. Refresh before confirming leave.","Fee Review",MessageBoxButtons.OK,MessageBoxIcon.Warning); return false;}
        return (balance==0 && later.Rows.Count==0) || reviewed || ReviewLaterCharges();
    }
    private bool ReviewLaterCharges()
    {
        if(!loaded || certificateExists()) return false;
        using var dialog=CreateReviewDialog();
        DialogResult result=dialog.ShowDialog(FindForm());
        if(result==DialogResult.Retry) {OpenCollection(); return false;}
        return result==DialogResult.OK;
    }
    private string ReversalReason(DataRow row)
    {
        if(Convert.ToInt32(row["EnrollmentId"])!=enrollment()) return "Previous placement — keep its charges";
        if(Convert.ToString(row["EnrollmentStatus"])!="Active") return "Placement is already closed";
        if(Convert.ToDecimal(row["NetAmount"])<=0) return "Zero amount — nothing to reverse";
        if(Convert.ToBoolean(row["HasPaymentHistory"])) return "Payment history exists";
        if(!CurrentUser.HasPermission("Fees.Collect")) return "Fee collection permission required";
        return Convert.ToBoolean(row["CanReverse"]) ? "Eligible — wholly unpaid, no payment history" : "Use payment review for this charge";
    }
    internal ApplicationWindowForm CreateReviewDialog()
    {
        var dialog=new ApplicationWindowForm {Text="Review Fees Before Leaving",StartPosition=FormStartPosition.CenterParent,
            Size=new Size(AppScale.Scale(1000),AppScale.Scale(620)),MinimumSize=new Size(780,520)};
        var keep=new RadioButton {Name="KeepCharges",Text="Keep outstanding dues and continue — the balance remains payable",AutoSize=true};
        var reverse=new RadioButton {Name="ReverseCharges",Text="Reverse selected eligible charges — reduce the outstanding balance",AutoSize=true,
            Enabled=CurrentUser.HasPermission("Fees.Collect") && displayCharges.AsEnumerable().Any(row=>Convert.ToBoolean(row["CanReverse"]) && Convert.ToDecimal(row["NetAmount"])>0)};
        if(!reverse.Enabled) reverse.Text=CurrentUser.HasPermission("Fees.Collect")
            ? "Reverse selected charges (unavailable — see each charge's reason below)" : "Reverse selected charges (fee collection permission required)";
        var payments=new RadioButton {Name="ReviewPayments",Text="Open Fee Collection before deciding — no certificate is created yet",AutoSize=true,Enabled=CurrentUser.HasPermission("Fees.Collect")};
        var choices=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false};
        choices.Controls.Add(keep); choices.Controls.Add(reverse); choices.Controls.Add(payments);
        int extraCount=displayCharges.AsEnumerable().Count(row=>Convert.ToDecimal(row["Balance"])<=0);
        var showHistory=new CheckBox {Name="ShowHistory",Text=extraCount>0
            ? $"Show {extraCount} settled / zero-amount charge(s) after leaving (view only)"
            : "No settled / zero-amount charges after leaving to show",AutoSize=true,Enabled=extraCount>0};
        FinanceHelp.Attach(dialog,keep,"Continue with the existing fees still due. Creating the certificate does not waive the outstanding balance.","Keep Rs. 1,000 dues: that Rs. 1,000 remains payable after leaving.");
        FinanceHelp.Attach(dialog,reverse,FinanceHelp.Topic.CancelCharge);
        FinanceHelp.Attach(dialog,payments,"Open Fee Collection, then return and review the balance before deciding about the certificate.","If Rs. 500 is due and you collect Rs. 300, return here to review the remaining Rs. 200.");
        FinanceHelp.Attach(dialog,showHistory,"Show additional settled or zero-amount charges after the leaving month. Viewing them does not refund or reverse anything.","Leaving on 20 October with November tuition already paid: tick this to see the settled November charge for reference.");
        var historyRow=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true}; historyRow.Controls.Add(showHistory);
        var grid=HumanUi.Grid(false); grid.Name="LeaveCharges"; grid.AutoGenerateColumns=false;
        grid.Columns.Add(new DataGridViewCheckBoxColumn {Name="Reverse",HeaderText="Reverse?",FillWeight=65});
        foreach(var column in new[]{("Fee","Fee",150f),("Month","Period",90f),("Timing","When",120f),("Amount","Charge",85f),("Balance","Due",85f),("Status","Status",100f)})
            grid.Columns.Add(new DataGridViewTextBoxColumn {Name=column.Item1,HeaderText=column.Item2,FillWeight=column.Item3,ReadOnly=true});
        grid.Columns.Add(new DataGridViewTextBoxColumn {Name="ReversalReason",HeaderText="Reversal eligibility",FillWeight=240,ReadOnly=true});
        grid.ReadOnly=true; grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
        foreach(var column in new[]{("Reverse",55),("Month",95),("Timing",110),("Amount",70),("Balance",70),("Status",100)})
        {
            grid.Columns[column.Item1].AutoSizeMode=DataGridViewAutoSizeColumnMode.None;
            grid.Columns[column.Item1].Width=AppScale.Scale(column.Item2);
        }
        grid.Columns["Fee"].MinimumWidth=AppScale.Scale(110);
        grid.Columns["ReversalReason"].MinimumWidth=AppScale.Scale(220);
        grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;
        grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;
        void PopulateGrid() {
        var selected=grid.Rows.Cast<DataGridViewRow>()
            .Where(row=>row.Tag is ValueTuple<long,bool> tag && tag.Item2 && Convert.ToBoolean(row.Cells["Reverse"].Value))
            .Select(row=>((ValueTuple<long,bool>)row.Tag!).Item1).ToHashSet();
        grid.Rows.Clear();
        foreach(DataRow row in displayCharges.Rows) {
            if(!showHistory.Checked && Convert.ToDecimal(row["Balance"])<=0) continue;
            long id=Convert.ToInt64(row["BillingChargeId"]);
            var period=Convert.ToDateTime(row[row["BillingMonth"]==DBNull.Value?"ChargeDate":"BillingMonth"]);
            bool can=Convert.ToBoolean(row["CanReverse"]) && Convert.ToDecimal(row["NetAmount"])>0 && CurrentUser.HasPermission("Fees.Collect");
            int index=grid.Rows.Add(reverse.Checked && can && selected.Contains(id),row["FeeTypeName"],period.ToString("MMM yyyy"),
                Convert.ToBoolean(row["IsLater"])?"After leaving":period<new DateTime(date().Year,date().Month,1)?"Earlier":"Current",
                Convert.ToDecimal(row["NetAmount"]).ToString("N0"),Convert.ToDecimal(row["Balance"]).ToString("N0"),
                Convert.ToString(row["Status"])=="PartiallyPaid"?"Partially paid":Convert.ToDecimal(row["NetAmount"])==0?"No fee":Convert.ToString(row["Status"])=="Open"?"Unpaid":row["Status"],ReversalReason(row));
            grid.Rows[index].Tag=(id,can);
            string explanation=ReversalReason(row);
            if(Convert.ToBoolean(row["HasPaymentHistory"])) explanation+=". Earlier payment allocations remain after reductions or refunds; this leave review cannot reverse the charge.";
            grid.Rows[index].Cells["ReversalReason"].ToolTipText=explanation;
            if(!can) grid.Rows[index].Cells["Reverse"]=new DataGridViewTextBoxCell {Value="—",ToolTipText=explanation};
        }
        grid.ClearSelection();
        }
        PopulateGrid();
        var note=new Label {Dock=DockStyle.Top,Height=AppScale.Scale(70),Text=$"Leave date: {date():dd MMM yyyy}  |  Total outstanding: Rs. {balance:N0}\nEarlier/current: Rs. {balance-LaterBalance:N0}  +  After leaving: Rs. {LaterBalance:N0}\nChoose an action. No fee decision is selected, and nothing is saved yet.",Font=AppTheme.NormalFont};
        var totals=new Label {Name="DecisionSummary",Dock=DockStyle.Bottom,Height=AppScale.Scale(58),Font=AppTheme.BodyBoldFont};
        var use=new ModernButton {Name="UseDecision",Text="Continue",Width=180,Enabled=false};
        void UpdateTotals() {
            decimal amount=grid.Rows.Cast<DataGridViewRow>().Where(row=>row.Tag is ValueTuple<long,bool> tag && tag.Item2 && Convert.ToBoolean(row.Cells["Reverse"].Value))
                .Sum(row=>Convert.ToDecimal(displayCharges.AsEnumerable().First(item=>Convert.ToInt64(item["BillingChargeId"])==((ValueTuple<long,bool>)row.Tag!).Item1)["NetAmount"]));
            totals.Text=keep.Checked ? $"Rs. {balance:N0} remains payable. No dues are waived.\nThis decision is saved only when you confirm creation of the leave certificate."
                : payments.Checked ? "Open Fee Collection, then return and review the updated fees.\nThe leave certificate has not been created."
                : reverse.Checked ? $"Selected charges to reverse: Rs. {amount:N0}  |  Outstanding afterwards: Rs. {balance-amount:N0}\nReversals are saved only when you confirm creation of the leave certificate."
                : "Choose how to handle the fees before continuing.";
            use.Enabled=keep.Checked || payments.Checked || (reverse.Checked && amount>0);
        }
        grid.CellClick+=(_,e)=>{if(reverse.Checked && e.RowIndex>=0 && grid.Rows[e.RowIndex].Tag is ValueTuple<long,bool> tag && tag.Item2) {
            var cell=grid.Rows[e.RowIndex].Cells["Reverse"]; cell.Value=!Convert.ToBoolean(cell.Value); UpdateTotals();
        }};
        foreach(var choice in new[]{keep,reverse,payments}) choice.CheckedChanged+=(_,_)=>{
            if(choice.Checked) {
                foreach(DataGridViewRow row in grid.Rows) if(row.Tag is ValueTuple<long,bool> tag && tag.Item2) row.Cells["Reverse"].Value=false;
                use.Text=payments.Checked?"Open Fee Collection":"Use This Decision"; UpdateTotals();
            }
        };
        showHistory.CheckedChanged+=(_,_)=>{PopulateGrid(); UpdateTotals();};
        var cancel=new ModernButton {Text=UiText.Actions.Cancel,DialogResult=DialogResult.Cancel,Width=110};
        use.Click+=(_,_)=> {
            if(!use.Enabled) return;
            if(payments.Checked) {dialog.DialogResult=DialogResult.Retry; return;}
            reverseIds.Clear();
            foreach(DataGridViewRow row in grid.Rows) if(reverse.Checked && row.Tag is ValueTuple<long,bool> tag && tag.Item2 && Convert.ToBoolean(row.Cells["Reverse"].Value)) reverseIds.Add(tag.Item1);
            reviewed=true; dialog.DialogResult=DialogResult.OK;
        };
        var buttons=HumanUi.ButtonRow(use,cancel); buttons.Dock=DockStyle.Bottom;
        var host=new Panel {Dock=DockStyle.Fill,Padding=new Padding(16)};
        host.Controls.Add(grid); host.Controls.Add(totals); host.Controls.Add(buttons); host.Controls.Add(historyRow); host.Controls.Add(choices); host.Controls.Add(note);
        dialog.Controls.Add(host); UpdateTotals();
        dialog.Shown+=(_,_)=>{grid.Focus();keep.Checked=false;reverse.Checked=false;payments.Checked=false;UpdateTotals();};
        return dialog;
    }
    internal string ConfirmationText {
        get {
            decimal reversed=displayCharges.AsEnumerable().Where(row=>reverseIds.Contains(Convert.ToInt64(row["BillingChargeId"]))).Sum(row=>Convert.ToDecimal(row["NetAmount"]));
            return $"Leave date: {date():dd MMM yyyy}\nTotal outstanding: Rs. {balance:N0}\nEarlier/current dues: Rs. {balance-LaterBalance:N0}; later dues: Rs. {LaterBalance:N0}.\n"+
                (reverseIds.Count>0 ? $"Reverse {reverseIds.Count} selected uncollected charge(s): Rs. {reversed:N0}.\n" : "All existing charges and payments stay unchanged.\n")+
                $"Remaining balance: Rs. {balance-reversed:N0}, still collectible.\nClosing preserves the remaining charges. Further posting and ordinary reversal are disabled.";
        }
    }
    private decimal LaterBalance => later.AsEnumerable().Sum(row=>Convert.ToDecimal(row["Balance"]));
    private void OpenCollection()
    {
        if(admission()<=0) return;
        try {
            var screenArea=Screen.FromControl(this).WorkingArea;
            using var window=new ApplicationWindowForm {Text="Fee Collection",StartPosition=FormStartPosition.CenterParent,
                Size=new Size(Math.Min(AppScale.Scale(1540),screenArea.Width-AppScale.Scale(24)),
                    Math.Min(AppScale.Scale(940),screenArea.Height-AppScale.Scale(24))),
                MinimumSize=new Size(Math.Min(900,screenArea.Width-AppScale.Scale(24)),
                    Math.Min(620,screenArea.Height-AppScale.Scale(24)))};
            window.Controls.Add(new FeeCollectionPage(admission()) {Dock=DockStyle.Fill});
            window.ShowDialog(FindForm()); reviewed=false; reverseIds.Clear(); RefreshFees();
        } catch(Exception ex) {ShowError(ex);}
    }
    internal Microsoft.Data.SqlClient.SqlParameter[] Parameters() => new[]{
        DatabaseHelper.Int("@EnrollmentId",enrollment()),DatabaseHelper.P("@ExpectedBalance",balance),
        DatabaseHelper.P("@ReviewedCharges",snapshot),DatabaseHelper.P("@ReverseChargeIds",JsonSerializer.Serialize(reverseIds.OrderBy(id=>id).ToArray()))
    };
    private void ShowError(Exception ex)=>ModernMessageDialog.Show(FindForm(),DbMessage.UserMessage(ex,"Could not review fees."),"Leave Fees",MessageBoxButtons.OK,MessageBoxIcon.Error);
}
