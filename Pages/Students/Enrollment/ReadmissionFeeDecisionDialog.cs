using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Students.Enrollment;

internal sealed class ReadmissionFeeDecisionDialog : ApplicationWindowForm
{
    internal DateTime MonthlyStartMonth { get; private set; }
    internal bool ChargeYearlyFees { get; private set; }
    internal ReadmissionFeeDecisionDialog(DateTime enrollmentDate, DateTime yearEnd, decimal oldDebt,
        decimal oldMonthCharges, DataTable rates, string? admissionNumberSummary = null)
    {
        Text="Readmission Fees"; StartPosition=FormStartPosition.CenterParent;
        Size=new Size(AppScale.Scale(850),AppScale.Scale(570)); MinimumSize=new Size(700,500);
        var months=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,DisplayMember="Text",ValueMember="Month"};
        var choices=new DataTable(); choices.Columns.Add("Month",typeof(DateTime)); choices.Columns.Add("Text",typeof(string));
        for(var month=new DateTime(enrollmentDate.Year,enrollmentDate.Month,1);month<=yearEnd;month=month.AddMonths(1))
            choices.Rows.Add(month,month.ToString("MMMM yyyy"));
        months.DataSource=choices; months.SelectedIndex=-1;
        var charge=new RadioButton {Text="Charge yearly fees",AutoSize=true};
        var waive=new RadioButton {Text="Waive yearly fees for this placement",AutoSize=true};
        var annual=new FlowLayoutPanel {Dock=DockStyle.Fill,AutoSize=true}; annual.Controls.Add(charge); annual.Controls.Add(waive);
        var grid=HumanUi.Grid(true); grid.AutoGenerateColumns=false;
        foreach(var column in new[]{("FeeTypeName",UiText.Fees.FeeType),("Frequency",UiText.Fees.Frequency),("Amount",UiText.Fees.NewAmount)})
            grid.Columns.Add(new DataGridViewTextBoxColumn {DataPropertyName=column.Item1,HeaderText=column.Item2,ReadOnly=true});
        grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None; grid.DataSource=rates;
        grid.Columns[2].DefaultCellStyle.Format="N0";
        var note=new Label {Dock=DockStyle.Fill,AutoSize=true,Text=$"Old placement balance: Rs. {oldDebt:N0} — remains collectible.\nExisting charges for {enrollmentDate:MMM yyyy}: Rs. {oldMonthCharges:N0}.\nChoose when new monthly fees start and whether yearly fees apply. Existing bills and payments stay unchanged."};
        if (!string.IsNullOrWhiteSpace(admissionNumberSummary))
            note.Text = admissionNumberSummary + "\nThe student record and registration number stay the same.\n\n" + note.Text;
        var impact=new Label {Dock=DockStyle.Fill,AutoSize=true};
        var confirm=new ModernButton {Text="Apply and Readmit",Enabled=false};
        var cancel=new ModernButton {Text=UiText.Actions.Cancel,DialogResult=DialogResult.Cancel};
        decimal monthly=rates.AsEnumerable().Where(r=>Convert.ToString(r["Frequency"])=="Monthly").Sum(r=>Convert.ToDecimal(r["Amount"]));
        decimal yearly=rates.AsEnumerable().Where(r=>Convert.ToString(r["Frequency"])=="Yearly").Sum(r=>Convert.ToDecimal(r["Amount"]));
        void Update() {
            confirm.Enabled=months.SelectedIndex>=0 && (charge.Checked||waive.Checked);
            impact.Text=confirm.Enabled ? $"New monthly fees: Rs. {monthly:N0}, starting {((DateTime)months.SelectedValue!):MMM yyyy}.\nOpening charges: Rs. {monthly+(charge.Checked?yearly:0):N0}. Old debt remains Rs. {oldDebt:N0}."
                : "Select both decisions to continue. Nothing has been saved.";
        }
        months.SelectedIndexChanged+=(_,_)=>Update(); charge.CheckedChanged+=(_,_)=>Update(); waive.CheckedChanged+=(_,_)=>Update();
        confirm.Click+=(_,_)=>{MonthlyStartMonth=(DateTime)months.SelectedValue!; ChargeYearlyFees=charge.Checked; DialogResult=DialogResult.OK;};
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(16)};
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(note,0,0); root.Controls.Add(grid,0,1); root.Controls.Add(HumanUi.Field("Monthly fees start",months,true),0,2);
        root.Controls.Add(annual,0,3); root.Controls.Add(impact,0,4); root.Controls.Add(HumanUi.ButtonRow(confirm,cancel),0,5);
        Controls.Add(root); CancelButton=cancel; Update(); FormFlow.OrderTableFields(root);
        Shown+=(_,_)=>{months.Focus(); months.SelectedIndex=-1; charge.Checked=false; waive.Checked=false; Update();};
    }
}
