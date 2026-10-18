using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Finance.StudentAccounts;

/// <summary>Select one issued bill, including fully settled purchases, without editing money in the list.</summary>
internal sealed class IssuedBillPicker : ApplicationWindowForm
{
    private readonly DataGridView grid=HumanUi.Grid(false);
    private readonly ModernButton review=new(){Text="Review Selected Bill",ThemeRole="Primary",Enabled=false};
    internal long SelectedBillId {get;private set;}
    internal IssuedBillPicker(DataTable issuedBills,string fee)
    {
        Text=$"Issued Bills — {fee}";StartPosition=FormStartPosition.CenterParent;
        // Display a recognizable reference without issuing or changing database numbers.
        issuedBills = issuedBills.Copy();
        if (issuedBills.Columns.Contains("Charge Ref"))
        {
            issuedBills.Columns["Charge Ref"]!.ColumnName = "Bill Ref";
            foreach (DataRow row in issuedBills.Rows)
                row["Bill Ref"] = $"FMSC-BILL-{Convert.ToDateTime(row["Issued"]):yyyy}-{Convert.ToInt64(row["BillingChargeId"]):D4}";
        }
        ClientSize=new Size(AppScale.Scale(950),AppScale.Scale(500));MinimumSize=new Size(700,350);
        grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
        grid.DataSource=issuedBills;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
        grid.DataBindingComplete+=(_,_)=>{
            if(grid.Columns.Contains("BillingChargeId"))grid.Columns["BillingChargeId"].Visible=false;
            foreach(DataGridViewColumn column in grid.Columns)
                if(column.ValueType==typeof(decimal))column.DefaultCellStyle.Format="N0";
                else if(column.ValueType==typeof(DateTime))column.DefaultCellStyle.Format="dd-MMM-yyyy";
        };
        grid.SelectionChanged+=(_,_)=>review.Enabled=grid.CurrentRow?.DataBoundItem is DataRowView&&CurrentUser.HasPermission("Fees.Adjust");
        review.Click+=(_,_)=>{
            if(!CurrentUser.HasPermission("Fees.Adjust")||grid.CurrentRow?.DataBoundItem is not DataRowView row)return;
            SelectedBillId=Convert.ToInt64(row.Row["BillingChargeId"]);DialogResult=DialogResult.OK;Close();
        };
        var close=new ModernButton{Text="Close",DialogResult=DialogResult.Cancel};CancelButton=close;AcceptButton=review;
        var note=new Label{Text="Select the specific bill to review, including a fully settled bill. Each total includes its earlier corrections. Settled includes cash and credit applied.",AutoSize=true,Dock=DockStyle.Fill,Padding=new Padding(4)};
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(12)};
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(note,0,0);root.Controls.Add(grid,0,1);root.Controls.Add(HumanUi.ButtonRow(review,close),0,2);Controls.Add(root);
        UiHelper.SetGridEmptyMessage(grid,"No non-reversed issued bills exist for this fee assignment.");
    }
}
