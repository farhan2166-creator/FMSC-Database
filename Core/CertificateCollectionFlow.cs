using FMSC_SMS.Pages.Security;

namespace FMSC_SMS.Core;

/// <summary>Shared physical handover and deliberate-copy actions. Reading or printing never collects a copy.</summary>
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
    internal static void FormatGrid(DataGridView grid)
    {
        foreach(string name in new[]{"ContentVersion","PreviousCertificateId","VoidedAt","EnrollmentId"})if(grid.Columns.Contains(name))grid.Columns[name].Visible=false;
        foreach(var pair in new[]{("CollectionStatus","Status"),("CollectedAt","Collected On"),("CollectedBy","Collected By"),("VoidReason","Void Reason")})if(grid.Columns.Contains(pair.Item1))grid.Columns[pair.Item1].HeaderText=pair.Item2;
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
    // Read-only compatibility for copies cancelled before schema 51; no cancellation action remains.
    internal static bool Voided(DataRow? row) => row is not null && CertificateUi.GetDate(row,"VoidedAt").HasValue;
    private static bool HasNewerCopy(DataTable rows,string key,int id)
    {
        if(!rows.Columns.Contains("PreviousCertificateId"))return false;
        var pending=new Queue<int>();var visited=new HashSet<int>();pending.Enqueue(id);
        while(pending.TryDequeue(out int source))
        {
            if(!visited.Add(source))continue;
            foreach(var row in rows.AsEnumerable().Where(row=>CertificateUi.GetInt(row,"PreviousCertificateId")==source))
            {
                if(!Voided(row))return true;
                pending.Enqueue(CertificateUi.GetInt(row,key));
            }
        }
        return false;
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
        var row=Find(rows,key,id); bool locked=Locked(row),voided=Voided(row);
        expectedVersion=Version(rows,key,id);
        Collect.Enabled=Available&&!busy&&row is not null&&!locked&&!voided&&!dirty;
        IssueAgain.Enabled=Available&&!busy&&(locked||voided)&&!HasNewerCopy(rows,key,id);
        if(row is not null&&(locked||voided))
        {
            foreach(var field in fields)field.Enabled=false;
            save.Enabled=false;
        }
        if(voided)print.Enabled=false;
        Status=voided?"Historical cancelled copy — retained as read-only history.":"Collected copy — details are locked. Print this saved copy or use Issue Again for a new copy.";
        return row is not null&&(locked||voided);
    }
    internal void Wire(Control owner,Func<int> selected,Action<int> reload,Func<bool> dirty)
    {
        Collect.Click+=(_,_)=>{
            if(busy||!Collect.Enabled||dirty())return;
            string? name=Prompt(owner,"Mark Certificate Collected","Name of person collecting this certificate:\nCollection permanently locks this copy.",200);
            if(name is null)return;
            Run(owner,()=>DatabaseHelper.ExecuteUser("CollectCertificateCopy",DatabaseHelper.P("@Kind",kind),DatabaseHelper.Int("@CopyId",selected()),DatabaseHelper.P("@CollectedBy",name),DatabaseHelper.P("@ExpectedVersion",expectedVersion)),reload,selected());
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
    internal static string? Prompt(Control owner,string title,string caption,int maxLength)
    {
        using var dialog=new ApplicationWindowForm { Text=title,StartPosition=FormStartPosition.CenterParent,ClientSize=new Size(AppScale.Scale(500),AppScale.Scale(220)),MinimizeBox=false,MaximizeBox=false };
        var input=HumanUi.TextBox(false,maxLength);input.Dock=DockStyle.Top;
        var label=new Label {Text=caption,Dock=DockStyle.Top,Height=AppScale.Scale(78),Padding=new Padding(12),AutoSize=false};
        var okay=new ModernButton {Text="Confirm",DialogResult=DialogResult.OK};
        var cancel=new ModernButton {Text="Cancel",DialogResult=DialogResult.Cancel};
        var buttons=CertificateButtonRow.Create(okay,cancel);buttons.Dock=DockStyle.Bottom;
        dialog.Controls.Add(input);dialog.Controls.Add(label);dialog.Controls.Add(buttons);dialog.AcceptButton=okay;dialog.CancelButton=cancel;
        if(dialog.ShowDialog(owner.FindForm())!=DialogResult.OK)return null;
        return string.IsNullOrWhiteSpace(input.Text)?null:input.Text.Trim();
    }
}
