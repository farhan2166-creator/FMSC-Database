using FMSC_SMS.Pages.Security;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Core;

/// <summary>Shared physical handover and deliberate-copy actions. Reading or printing never collects a copy.</summary>
// BD-21: printing does not mark collected; collection locks that saved copy.
// Preserve issue/collection history and use Issue Again for a separate copy, never certificate cancellation.
internal sealed class CertificateCollectionFlow(string kind)
{
    internal readonly ModernButton Collect = new() { Text="Mark Collected",Width=150 };
    internal readonly ModernButton IssueAgain = new() { Text="Issue Again",Width=140 };
    private bool busy;
    private Guid? pendingIssue;
    private int pendingSource;
    private byte[]? expectedVersion;
    internal bool Available { get; set; } = true;
    internal static byte[]? Version(DataTable rows,string key,int id) => Find(rows,key,id) is DataRow row && row.Table.Columns.Contains("ContentVersion") ? row["ContentVersion"] as byte[] : null;
    internal static SqlParameter VersionParameter(DataTable rows,string key,int id) => VersionParameter(Version(rows,key,id));
    internal static SqlParameter VersionParameter(byte[]? value) => new("@ExpectedVersion",SqlDbType.Binary,8) {Value=(object?)value??DBNull.Value};
    internal static void FormatGrid(DataGridView grid)
    {
        // Binding can generate columns only after the grid receives a BindingContext.
        // Keep the certificate presentation in effect on first display and every rebind.
        grid.DataBindingComplete -= GridDataBindingComplete;
        grid.DataBindingComplete += GridDataBindingComplete;
        ApplyGridFormat(grid);
    }
    private static void GridDataBindingComplete(object? sender, DataGridViewBindingCompleteEventArgs e)
    {
        if(sender is DataGridView grid)ApplyGridFormat(grid);
    }
    private static void ApplyGridFormat(DataGridView grid)
    {
        foreach(string name in new[]{"ContentVersion","PreviousCertificateId","EnrollmentId"})if(grid.Columns.Contains(name))grid.Columns[name].Visible=false;
        foreach(var pair in new[]{("CollectionStatus","Status"),("CollectedAt","Collected On"),("CollectedBy","Collected By")})if(grid.Columns.Contains(pair.Item1))grid.Columns[pair.Item1].HeaderText=pair.Item2;
    }
    internal static void RefreshStudent(StudentPicker picker)
    {
        if(picker.SelectedRow is not DataRow existing)return;
        var details=DatabaseHelper.QueryUser("GetStudentDetails",DatabaseHelper.Int("@AdmissionId",picker.SelectedId));
        if(details.Rows.Count!=1)throw new InvalidOperationException("The selected student could not be refreshed.");
        foreach(DataColumn column in existing.Table.Columns)
            if(!column.ReadOnly&&details.Columns.Contains(column.ColumnName))existing[column]=details.Rows[0][column.ColumnName];
        picker.SetStudent(existing);
    }
    internal static DataRow? Find(DataTable rows,string key,int id) => rows.Columns.Contains(key)
        ? rows.AsEnumerable().FirstOrDefault(row=>CertificateUi.GetInt(row,key)==id) : null;
    internal static bool Locked(DataRow? row) => row is not null && (CertificateUi.GetDate(row,"CollectedAt").HasValue || CertificateUi.GetString(row,"CollectionStatus")=="Collected");
    private static bool HasNewerCopy(DataTable rows,string key,int id)
    {
        return rows.Columns.Contains("PreviousCertificateId") && rows.AsEnumerable().Any(row=>CertificateUi.GetInt(row,"PreviousCertificateId")==id);
    }
    internal static void SelectPromotion(LookupBox lookup,string value)
    {
        if(string.IsNullOrWhiteSpace(value))return;
        for(int index=0;index<lookup.Items.Count;index++)
            if(lookup.Items[index] is DataRowView row&&CertificateUi.GetString(row.Row,"ClassName")==value){lookup.SelectedIndex=index;return;}
        if(lookup.DataSource is DataTable table && table.Columns.Contains("ClassId"))
        {
            var row=table.NewRow();row["ClassId"]=int.MaxValue;row["ClassName"]=value;row["Display"]=value;table.Rows.Add(row);lookup.SelectedIndex=lookup.Items.Count-1;
        }
    }
    internal string Status { get; private set; } = "";
    internal bool Apply(DataTable rows,string key,int id,ModernButton save,ModernButton print,bool dirty,params Control[] fields)
    {
        var row=Find(rows,key,id); bool locked=Locked(row);
        expectedVersion=Version(rows,key,id);
        Collect.Enabled=Available&&!busy&&row is not null&&!locked&&!dirty;
        IssueAgain.Enabled=Available&&!busy&&locked&&!HasNewerCopy(rows,key,id);
        if(row is not null&&locked)
        {
            foreach(var field in fields)field.Enabled=false;
            save.Enabled=false;
        }
        Status="Collected copy — details are locked. Print this saved copy or use Issue Again for a new copy.";
        return row is not null&&locked;
    }
    internal void Wire(Control owner,Func<int> selected,Action<int> reload,Func<bool> dirty)
    {
        Collect.Click+=(_,_)=>{
            if(busy||!Collect.Enabled||dirty())return;
            string? name=CertificateCollectionDialog.Show(owner);
            if(name is null)return;
            Run(owner,()=>DatabaseHelper.ExecuteUser("CollectCertificateCopy",DatabaseHelper.P("@Kind",kind),DatabaseHelper.Int("@CopyId",selected()),DatabaseHelper.P("@CollectedBy",name),VersionParameter(expectedVersion)),reload,selected());
        };
        IssueAgain.Click+=(_,_)=>{
            if(busy||!IssueAgain.Enabled)return;
            if(!ModernMessageDialog.Confirm(owner.FindForm(),"Create another copy using current student/staff details and the saved certificate details? The collected copy stays locked. Review and correct the new copy before handing it over.","Issue Certificate Again",defaultYes:false))return;
            int id=selected();if(pendingIssue is null||pendingSource!=id){pendingIssue=Guid.NewGuid();pendingSource=id;}
            int next=id;
            Run(owner,()=>{
                var result=DatabaseHelper.QueryUser("IssueCertificateCopy",DatabaseHelper.P("@Kind",kind),DatabaseHelper.Int("@CopyId",id),DatabaseHelper.P("@RequestId",pendingIssue.Value));
                next=Convert.ToInt32(result.Rows[0]["CopyId"]);pendingIssue=null;
            },_=>reload(next),id);
        };
    }
    private void Run(Control owner,Action action,Action<int> reload,int id)
    {
        busy=true;owner.Enabled=false;
        try {action();busy=false;reload(id);}
        catch(Exception ex) {busy=false;ModernMessageDialog.Show(owner.FindForm(),DbMessage.UserMessage(ex,"The action could not be confirmed. Refresh the history before trying again."),"Certificate",MessageBoxButtons.OK,MessageBoxIcon.Warning);reload(id);}
        finally {busy=false;owner.Enabled=true;}
    }
}
