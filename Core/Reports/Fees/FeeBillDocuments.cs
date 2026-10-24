using System.Xml;

namespace FMSC_SMS.Core;

internal static class FeeBillDocuments
{
    internal static string Snapshot(DataTable source)
    {
        using var data=source.Copy();
        foreach(DataRow row in data.Rows) {row["ReceiptNumber"]="";row["PaymentDateText"]="";}
        using var writer=new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        data.WriteXml(writer,XmlWriteMode.WriteSchema);
        return writer.ToString();
    }

    internal static DataTable ReadSnapshot(string snapshot)
    {
        using var reader=XmlReader.Create(new StringReader(snapshot),new XmlReaderSettings {
            DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null });
        var data=new DataTable();data.ReadXml(reader);return data;
    }

    internal static async Task ShowAsync(Form? owner,DataTable data,int primaryAdmission,int[] students)
    {
        using var saved=await IssueAsync(data,primaryAdmission,students);
        if(owner?.IsDisposed==true) return;
        ShowSaved(owner,saved.Rows[0]);
    }

    internal static async Task<DataTable> IssueAsync(DataTable data,int primaryAdmission,int[] students)
    {
        var saved=await DatabaseHelper.QueryUserAsync("IssueFeeBill",
            DatabaseHelper.Int("@PrimaryAdmissionId",primaryAdmission),
            DatabaseHelper.P("@StudentIdsJson",System.Text.Json.JsonSerializer.Serialize(students.Distinct().OrderBy(id=>id))),
            DatabaseHelper.P("@SnapshotXml",Snapshot(data)));
        if(saved.Rows.Count!=1){saved.Dispose();throw new InvalidOperationException("The issued bill could not be confirmed. Reload before printing.");}
        return saved;
    }

    internal static void ShowSaved(Form? owner,DataRow saved)
    {
        using var data=ReadSavedData(saved);
        if(data.Columns.Contains("FamilyRowKind"))FamilyBillReport.ShowData(owner,data);
        else StudentBillReport.ShowData(owner,data);
    }

    internal static DataTable ReadSavedData(DataRow saved)
    {
        var data=ReadSnapshot(Convert.ToString(saved["SnapshotXml"])!);
        foreach(DataRow row in data.Rows)
        {
            row["ReceiptNumber"]=saved["BillNumber"];
            row["PaymentDateText"]=Convert.ToDateTime(saved["IssuedAt"]).ToString("dd-MM-yyyy hh:mm tt");
        }
        RecordTextPolicy.FormatDisplayRows(data);
        return data;
    }

    internal static async Task ShowHistoryAsync(Form? owner,int admissionId,string studentName,Func<bool> isCurrent)
    {
        int user=CurrentUser.UserId;
        bool Current()=>owner?.IsDisposed!=true && CurrentUser.UserId==user && CurrentUser.HasPermission("Fees.View") && isCurrent();
        using var bills=await DatabaseHelper.QueryUserAsync("GetStudentIssuedFeeBills",DatabaseHelper.Int("@AdmissionId",admissionId));
        if(!Current())return;
        using var dialog=new ApplicationWindowForm {Text=$"Issued Fee Bills — {studentName}",
            StartPosition=FormStartPosition.CenterParent,ClientSize=new Size(AppScale.Scale(850),AppScale.Scale(480)),
            MinimumSize=new Size(650,350),ShowInTaskbar=false};
        var grid=HumanUi.Grid(false);grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.MultiSelect=false;
        grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.AutoGenerateColumns=false;
        grid.Columns.Add(new DataGridViewTextBoxColumn {DataPropertyName="BillNumber",HeaderText="Bill No.",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill});
        grid.Columns.Add(new DataGridViewTextBoxColumn {DataPropertyName="IssuedAt",HeaderText="Date",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,
            DefaultCellStyle=new DataGridViewCellStyle {Format="dd-MM-yyyy hh:mm tt"}});
        grid.Columns.Add(new DataGridViewTextBoxColumn {DataPropertyName="DocumentType",HeaderText="Type",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill});
        grid.CellFormatting+=(_,e)=> {if(e.ColumnIndex==2 && e.Value is string kind)e.Value=kind=="FB"?"Family Fee Bill":"Student Fee Bill";};
        grid.DataSource=bills;
        var print=new ModernButton {Text="Reprint Bill",ThemeRole="Primary",Enabled=bills.Rows.Count>0};
        print.Click+=(_,_)=>{if(Current()&&grid.CurrentRow?.DataBoundItem is DataRowView row)ShowSaved(dialog,row.Row);};
        var close=new ModernButton {Text="Close",DialogResult=DialogResult.Cancel};dialog.CancelButton=close;
        var card=HumanUi.Card("Issued Fee Bills","Reprint the saved bill with its original number, date and charges.");
        card.Content.Controls.Add(grid);
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(12)};
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(card,0,0);root.Controls.Add(HumanUi.ButtonRow(print,close),0,1);dialog.Controls.Add(root);
        dialog.ShowDialog(owner);
    }
}
