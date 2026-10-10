using System.Globalization;
using System.Text.Json;
using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Students.Maintenance;

internal sealed class LifecycleDateRepairDialog : ApplicationWindowForm
{
    private readonly DataTable dates;
    private readonly DataGridView grid=HumanUi.Grid(false);
    private readonly ModernTextBox reason=HumanUi.TextBox(false,500);
    private readonly CheckBox legacy=new() {Text="Allow legacy overlaps or dates outside admission / academic-year boundaries",AutoSize=true};
    private readonly CheckBox finances=new() {Text="Keep existing bills and payments at their original dates and amounts",AutoSize=true};
    private readonly Label impact=new() {Dock=DockStyle.Fill,AutoSize=true};
    private readonly ModernButton save=new() {Text="Apply Date Repair",Enabled=false};
    private readonly ModernButton cancel=new() {Text="Cancel",DialogResult=DialogResult.Cancel};
    private readonly ModernDatePicker startDate=HumanUi.Date();
    private readonly ModernDatePicker leaveDate=HumanUi.Date();
    private readonly ModernDatePicker billingDate=HumanUi.Date();
    private readonly ModernDatePicker withdrawalDate=HumanUi.Date();
    private readonly CheckBox updateWithdrawal=new() {Text="Correct the student's withdrawal date",AutoSize=true};
    private readonly Label selectedRecord=new() {AutoSize=true};
    private readonly int admissionId;
    private readonly string snapshot;
    private bool saving;
    internal LifecycleDateRepairDialog(int admissionId,DataSet data,int selectedEnrollmentId=0,
        DateTime? proposedStart=null,DateTime? proposedLeave=null,bool clearLeave=false,string initialReason="",bool focused=false)
    {
        this.admissionId=admissionId;
        var student=data.Tables[0].Rows[0];snapshot=Convert.ToString(student["Snapshot"])!;
        dates=data.Tables[1].Copy();
        Text="Repair Lifecycle Dates";StartPosition=FormStartPosition.CenterParent;
        var area=Screen.PrimaryScreen!.WorkingArea;
        Size=new Size(Math.Min(AppScale.Scale(1250),area.Width-24),Math.Min(AppScale.Scale(790),area.Height-24));
        MinimumSize=new Size(Math.Min(950,area.Width-24),Math.Min(680,area.Height-24));
        var note=new Label {Dock=DockStyle.Fill,AutoSize=true,
            Text=$"{student["AdmissionNumber"]} - {student["StudentName"]}  |  {student["Status"]}  |  Admission: {Convert.ToDateTime(student["AdmissionDate"]):dd MMM yyyy}\nSelect any old or current enrollment and edit its dates below. Choose certificate and withdrawal corrections separately; nothing follows automatically."};
        grid.AutoGenerateColumns=false;grid.ReadOnly=false;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;
        grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;
        grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;
        foreach(var col in new[]{("EnrollmentId","Record",55f),("YearName","Year",80f),("Status","Status",70f),("Placement","Placement",170f),
            ("EnrollmentDate","Start date",100f),("LeavingDate","Leave date",100f),("BillingStartDate","Billing start",100f),("Charges","Bills",50f),("Outstanding","Due",70f)}) {
            bool editable=col.Item1 is "EnrollmentDate" or "LeavingDate" or "BillingStartDate";
            var column=new DataGridViewTextBoxColumn {Name=col.Item1=="EnrollmentId"?"Record":col.Item1,DataPropertyName=col.Item1,HeaderText=col.Item2,ReadOnly=!editable,FillWeight=col.Item3};
            if(editable) {column.DefaultCellStyle.Format="dd MMM yyyy";column.DefaultCellStyle.NullValue="";column.DefaultCellStyle.BackColor=AppTheme.SurfaceSecondary;}
            if(col.Item1=="Outstanding")column.DefaultCellStyle.Format="N0";
            grid.Columns.Add(column);
        }
        grid.DataSource=dates;
        // Select a record above, then edit its dates in the named fields below.
        grid.ReadOnly=true;
        var selected=dates.AsEnumerable().FirstOrDefault(r=>Convert.ToInt32(r["EnrollmentId"])==selectedEnrollmentId);
        if(selected!=null) {
            if(proposedStart.HasValue)selected["EnrollmentDate"]=proposedStart.Value.Date;
            if(proposedLeave.HasValue)selected["LeavingDate"]=proposedLeave.Value.Date;
            else if(clearLeave)selected["LeavingDate"]=DBNull.Value;
        }
        grid.CellParsing+=(_,e)=>{
            if(e.RowIndex<0 || grid.Columns[e.ColumnIndex].ReadOnly)return;
            string value=Convert.ToString(e.Value)?.Trim()??"";
            if(value.Length==0 && grid.Columns[e.ColumnIndex].Name!="EnrollmentDate") {e.Value=DBNull.Value;e.ParsingApplied=true;}
            else if(DateTime.TryParse(value,CultureInfo.CurrentCulture,DateTimeStyles.None,out var parsed)) {e.Value=parsed.Date;e.ParsingApplied=true;}
        };
        grid.DataError+=(_,e)=>{e.ThrowException=false;impact.Text="Enter a valid date, for example 07 Oct 2026. Start date cannot be empty.";save.Enabled=false;};
        var certificates=HumanUi.Grid(true);certificates.AutoGenerateColumns=false;certificates.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;
        certificates.Columns.Add(new DataGridViewCheckBoxColumn {Name="Apply",DataPropertyName="Apply",HeaderText="Update?",FillWeight=50});
        foreach(var col in new[]{("EnrollmentId","Record"),("CertificateNumber","Certificate"),("LeaveDate","Current leave date"),("ProposedLeaveDate","Repaired leave date"),("RecordedAt","Recorded at"),("FirstIssuedAt","First issued at")}) {
            var column=new DataGridViewTextBoxColumn {Name=col.Item1=="EnrollmentId"?"Record":col.Item1,DataPropertyName=col.Item1,HeaderText=col.Item2,ReadOnly=col.Item1!="ProposedLeaveDate"};
            if(col.Item1.Contains("LeaveDate"))column.DefaultCellStyle.Format="dd MMM yyyy";
            if(col.Item1 is "RecordedAt" or "FirstIssuedAt")column.DefaultCellStyle.Format="dd MMM yyyy HH:mm";
            certificates.Columns.Add(column);
        }
        var certificatePreview=data.Tables[2].Copy();certificatePreview.Columns.Add("ProposedLeaveDate",typeof(DateTime));
        certificatePreview.Columns.Add("Apply",typeof(bool));
        foreach(DataRow certificate in certificatePreview.Rows) {certificate["Apply"]=false;certificate["ProposedLeaveDate"]=certificate["LeaveDate"];}
        certificates.DataSource=certificatePreview;
        certificates.CurrentCellDirtyStateChanged+=(_,_)=>{if(certificates.IsCurrentCellDirty)certificates.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        certificates.DataError+=(_,e)=>{e.ThrowException=false;impact.Text="Enter a valid certificate leave date";save.Enabled=false;};
        withdrawalDate.ShowCheckBox=true;withdrawalDate.Checked=student["WithdrawalDate"]!=DBNull.Value;
        if(withdrawalDate.Checked)withdrawalDate.Value=Convert.ToDateTime(student["WithdrawalDate"]);
        withdrawalDate.Enabled=false;
        var financeRequired=dates.AsEnumerable().Any(r=>Convert.ToInt32(r["Charges"])>0);
        finances.Visible=financeRequired;
        var flags=new FlowLayoutPanel {Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false};
        flags.Controls.Add(finances);flags.Controls.Add(legacy);
        void UpdateImpact() {
            int edits=dates.AsEnumerable().Count(r=>{
                var old=data.Tables[1].AsEnumerable().First(o=>Convert.ToInt32(o["EnrollmentId"])==Convert.ToInt32(r["EnrollmentId"]));
                return new[]{"EnrollmentDate","LeavingDate","BillingStartDate"}.Any(c=>!Equals(old[c],r[c]));
            });
            int certEdits=0;
            certEdits=certificatePreview.AsEnumerable().Count(r=>Convert.ToBoolean(r["Apply"]) && !Equals(r["LeaveDate"],r["ProposedLeaveDate"]));
            var invalid=dates.AsEnumerable().FirstOrDefault(r=>r["LeavingDate"]!=DBNull.Value && Convert.ToDateTime(r["LeavingDate"])<Convert.ToDateTime(r["EnrollmentDate"]));
            bool withdrawalEdited=updateWithdrawal.Checked && !Equals(student["WithdrawalDate"],withdrawalDate.Checked?(object)withdrawalDate.Value.Date:DBNull.Value);
            impact.Text=invalid!=null?$"Enrollment {invalid["EnrollmentId"]}: leave date is before its start date. Correct both dates if necessary":
                $"Changes: {edits} enrollment(s), {certEdits} certificate(s), withdrawal {(withdrawalEdited?"changed":"unchanged")}\nUnselected certificate dates, bills, payments and original history remain as recorded";
            int mismatches=certificatePreview.AsEnumerable().Count(c=>{
                var record=dates.AsEnumerable().FirstOrDefault(r=>c["EnrollmentId"]!=DBNull.Value && Equals(r["EnrollmentId"],c["EnrollmentId"]));
                return record!=null && !Equals(record["LeavingDate"],Convert.ToBoolean(c["Apply"])?c["ProposedLeaveDate"]:c["LeaveDate"]);
            });
            if(invalid==null && mismatches>0)impact.Text+=$"\n{mismatches} certificate date(s) will still differ from the enrollment — select their corrections if needed";
            if(focused && invalid==null)
                impact.Text=$"Billing starts: {(SelectedRow()?["BillingStartDate"] is DateTime billing?billing.ToString("dd MMM yyyy"):"not set")}\nExisting charges keep their dates and amounts."+
                    (mismatches>0?"\nA linked certificate has a different leaving date; use Review Dates to correct it.":"");
            save.Enabled=!saving && invalid==null && (edits>0 || certEdits>0 || withdrawalEdited) && !string.IsNullOrWhiteSpace(reason.Text) && (!financeRequired || finances.Checked);
        }
        updateWithdrawal.CheckedChanged+=(_,_)=>{withdrawalDate.Enabled=updateWithdrawal.Checked;UpdateImpact();};
        withdrawalDate.ValueChanged+=(_,_)=>UpdateImpact();
        certificates.CellValueChanged+=(_,_)=>UpdateImpact();
        reason.Text=initialReason;reason.TextChanged+=(_,_)=>UpdateImpact();finances.CheckedChanged+=(_,_)=>UpdateImpact();
        save.Click+=async(_,_)=>{
            if(saving || !grid.EndEdit() || !certificates.EndEdit())return;
            BindingContext?[dates]?.EndCurrentEdit();
            BindingContext?[certificatePreview]?.EndCurrentEdit();
            var changes=JsonSerializer.Serialize(dates.AsEnumerable().Select(r=>new {
                EnrollmentId=Convert.ToInt32(r["EnrollmentId"]),EnrollmentDate=Convert.ToDateTime(r["EnrollmentDate"]).Date,
                LeavingDate=r["LeavingDate"]==DBNull.Value?(DateTime?)null:Convert.ToDateTime(r["LeavingDate"]).Date,
                BillingStartDate=r["BillingStartDate"]==DBNull.Value?(DateTime?)null:Convert.ToDateTime(r["BillingStartDate"]).Date}));
            saving=true;save.Enabled=false;cancel.Enabled=false;grid.Enabled=false;reason.Enabled=false;flags.Enabled=false;
            certificates.Enabled=false;startDate.Enabled=false;leaveDate.Enabled=false;billingDate.Enabled=false;updateWithdrawal.Enabled=false;withdrawalDate.Enabled=false;
            try {
                await DatabaseHelper.QueryUserAsync("RepairLifecycleDates",DatabaseHelper.Int("@AdmissionId",this.admissionId),
                    DatabaseHelper.P("@ExpectedSnapshot",snapshot),DatabaseHelper.P("@Changes",changes),DatabaseHelper.P("@Reason",reason.Text.Trim()),
                    DatabaseHelper.Bit("@AllowLegacyExceptions",legacy.Checked),DatabaseHelper.Bit("@KeepFinancialHistory",finances.Checked),
                    DatabaseHelper.P("@CertificateChanges",JsonSerializer.Serialize(certificatePreview.AsEnumerable().Where(r=>Convert.ToBoolean(r["Apply"])).Select(r=>new {
                        LeaveRecordId=Convert.ToInt32(r["LeaveRecordId"]),LeaveDate=Convert.ToDateTime(r["ProposedLeaveDate"]).Date}))),
                    DatabaseHelper.Bit("@UpdateWithdrawal",updateWithdrawal.Checked),
                    DatabaseHelper.P("@NewWithdrawalDate",withdrawalDate.Checked?(object)withdrawalDate.Value.Date:DBNull.Value));
                DialogResult=DialogResult.OK;
            } catch(Exception ex) {impact.Text=DbMessage.UserMessage(ex);}
            finally {saving=false;cancel.Enabled=true;grid.Enabled=true;reason.Enabled=true;flags.Enabled=true;
                certificates.Enabled=true;startDate.Enabled=true;leaveDate.Enabled=true;billingDate.Enabled=true;updateWithdrawal.Enabled=true;withdrawalDate.Enabled=updateWithdrawal.Checked;
                save.Enabled=true;}
        };
        FormClosing+=(_,e)=>{if(saving)e.Cancel=true;};
        var editor=new TableLayoutPanel {Dock=DockStyle.Fill,AutoSize=true,ColumnCount=3,RowCount=2};
        for(int i=0;i<3;i++)editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));
        editor.Controls.Add(selectedRecord,0,0);editor.SetColumnSpan(selectedRecord,3);
        editor.Controls.Add(HumanUi.Field("Start date",startDate),0,1);editor.Controls.Add(HumanUi.Field("Leave date",leaveDate),1,1);editor.Controls.Add(HumanUi.Field("Billing start",billingDate),2,1);
        leaveDate.ShowCheckBox=true;billingDate.ShowCheckBox=true;
        bool loadingEditor=false;
        DataRow? SelectedRow()=> (grid.CurrentRow?.DataBoundItem as DataRowView)?.Row;
        void LoadEditor() {
            var row=SelectedRow();if(row==null)return;loadingEditor=true;
            selectedRecord.Text=$"Editing enrollment {row["EnrollmentId"]} — {row["Status"]} — {row["Placement"]}";
            bool editable=Convert.ToString(row["Status"])=="Active";
            startDate.Enabled=leaveDate.Enabled=billingDate.Enabled=editable;
            if(!editable)selectedRecord.Text+=" — view only";
            startDate.Value=Convert.ToDateTime(row["EnrollmentDate"]);
            leaveDate.Checked=row["LeavingDate"]!=DBNull.Value;if(leaveDate.Checked)leaveDate.Value=Convert.ToDateTime(row["LeavingDate"]);
            billingDate.Checked=row["BillingStartDate"]!=DBNull.Value;if(billingDate.Checked)billingDate.Value=Convert.ToDateTime(row["BillingStartDate"]);
            loadingEditor=false;
        }
        void SaveEditor() {
            if(loadingEditor || saving)return;var row=SelectedRow();if(row==null || Convert.ToString(row["Status"])!="Active")return;
            var newStart=startDate.Value.Date;object newLeave=leaveDate.Checked?(object)leaveDate.Value.Date:DBNull.Value;
            object newBilling=billingDate.Checked?(object)billingDate.Value.Date:DBNull.Value;
            loadingEditor=true;
            try {row["EnrollmentDate"]=newStart;row["LeavingDate"]=newLeave;row["BillingStartDate"]=newBilling;}
            finally {loadingEditor=false;}
            UpdateImpact();
        }
        grid.CurrentCellChanged+=(_,_)=>LoadEditor();startDate.ValueChanged+=(_,_)=>SaveEditor();leaveDate.ValueChanged+=(_,_)=>SaveEditor();billingDate.ValueChanged+=(_,_)=>SaveEditor();
        var withdrawalPanel=new FlowLayoutPanel {Dock=DockStyle.Fill,AutoSize=true,WrapContents=false};
        withdrawalPanel.Controls.Add(updateWithdrawal);withdrawalPanel.Controls.Add(withdrawalDate);
        var useEnrollmentDate=new ModernButton {Text="Use Enrollment Leave Date",Width=AppMetrics.TextWidth("Use Enrollment Leave Date",AppTheme.ButtonFont,44),Height=AppMetrics.ButtonHeight};
        useEnrollmentDate.Click+=(_,_)=>{
            if(certificates.CurrentRow?.DataBoundItem is not DataRowView certificate)return;
            var linked=dates.AsEnumerable().FirstOrDefault(r=>Equals(r["EnrollmentId"],certificate.Row["EnrollmentId"]));
            if(linked==null || linked["LeavingDate"]==DBNull.Value) {impact.Text="Set a leave date on the linked enrollment first";return;}
            certificate.Row["ProposedLeaveDate"]=linked["LeavingDate"];certificate.Row["Apply"]=true;UpdateImpact();
        };
        var certificateHeading=new FlowLayoutPanel {Dock=DockStyle.Fill,AutoSize=true,WrapContents=false};
        certificateHeading.Controls.Add(new Label {Text="Certificates — select only those to correct",AutoSize=true,Margin=new Padding(0,10,10,0)});
        certificateHeading.Controls.Add(useEnrollmentDate);
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=10,Padding=new Padding(14)};
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,45));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,55));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(note,0,0);root.Controls.Add(grid,0,1);
        root.Controls.Add(editor,0,2);
        root.Controls.Add(certificateHeading,0,3);
        root.Controls.Add(certificates,0,4);root.Controls.Add(withdrawalPanel,0,5);root.Controls.Add(flags,0,6);root.Controls.Add(HumanUi.Field("Repair reason",reason,true),0,7);
        root.Controls.Add(impact,0,8);root.Controls.Add(HumanUi.ButtonRow(save,cancel),0,9);
        Controls.Add(root);CancelButton=cancel;UpdateImpact();FormFlow.OrderTableFields(root);
        if(focused && selected!=null) {
            Text="Correct Enrollment Dates";
            MinimumSize=new Size(AppScale.Scale(720),AppScale.Scale(460));
            Size=new Size(AppScale.Scale(820),AppScale.Scale(520));
            note.Text=$"{student["AdmissionNumber"]} — {student["StudentName"]}\nReview the selected enrollment dates before saving.";
            save.Text="Save Dates";
            finances.Text="Keep existing charges and payments unchanged";
            impact.Text="";
            root.Controls.Remove(grid);
            root.Controls.Remove(certificateHeading);
            root.Controls.Remove(certificates);
            root.Controls.Remove(withdrawalPanel);
            foreach(int row in new[]{1,3,4,5})root.RowStyles[row]=new RowStyle(SizeType.Absolute,0);
            root.RowStyles[8]=new RowStyle(SizeType.Percent,100);
            // The hidden grid retains its selection and binding for the existing save logic.
            grid.BindingContext=new BindingContext();
            foreach(DataGridViewRow row in grid.Rows)
                if(row.DataBoundItem is DataRowView bound && Convert.ToInt32(bound.Row["EnrollmentId"])==selectedEnrollmentId) {
                    grid.CurrentCell=row.Cells["EnrollmentDate"];break;
                }
            LoadEditor();UpdateImpact();
            Disposed+=(_,_)=>{grid.Dispose();certificates.Dispose();certificateHeading.Dispose();withdrawalPanel.Dispose();};
        }
        grid.CellValueChanged+=(_,_)=>{if(!loadingEditor) {UpdateImpact();LoadEditor();}};
        Shown+=(_,_)=>{
            grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;
            grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;
            certificates.DefaultCellStyle.WrapMode=DataGridViewTriState.True;
            certificates.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;
            // Run after the common grid formatter so these compact timestamps stay readable.
            certificates.CellFormatting+=(_,e)=>{
                if(e.RowIndex<0 || e.ColumnIndex<0 || certificates.Rows[e.RowIndex].DataBoundItem is not DataRowView bound)return;
                var name=certificates.Columns[e.ColumnIndex].DataPropertyName;
                if(bound.Row[name] is DateTime value) {e.Value=value.ToString(name is "RecordedAt" or "FirstIssuedAt"?"dd MMM yy HH:mm":"dd MMM yyyy");e.FormattingApplied=true;}
            };
            foreach(var name in new[]{"EnrollmentDate","LeavingDate","BillingStartDate"})grid.Columns[name].DefaultCellStyle.Format="dd MMM yyyy";
            foreach(var name in new[]{"LeaveDate","ProposedLeaveDate"})certificates.Columns[name].DefaultCellStyle.Format="dd MMM yyyy";
            foreach(var name in new[]{"RecordedAt","FirstIssuedAt"})certificates.Columns[name].DefaultCellStyle.Format="dd MMM yy HH:mm";
            if(selectedEnrollmentId>0)foreach(DataGridViewRow row in grid.Rows)if(Convert.ToInt32(((DataRowView)row.DataBoundItem).Row["EnrollmentId"])==selectedEnrollmentId) {grid.CurrentCell=row.Cells["EnrollmentDate"];break;}LoadEditor();
        };
    }
}
