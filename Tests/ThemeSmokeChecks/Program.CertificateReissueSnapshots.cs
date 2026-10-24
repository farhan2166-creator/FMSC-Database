using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Certificates;

internal static partial class Program
{
    private static void CheckCertificateReissueSnapshots()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,Permissions:CurrentUser.GetPermissions().ToArray());
        try
        {
            CurrentUser.SignIn(901,"Certificate fixture","Administrator",false,Array.Empty<string>());
            foreach(bool regular in new[]{true,false})
            {
                using var students=new DataTable();students.Columns.Add("AdmissionId",typeof(int));
                foreach(string name in new[]{"AdmissionNumber","StudentName","FatherName","ClassName","Status"})students.Columns.Add(name);
                students.Columns.Add("DateOfBirth",typeof(DateTime));
                DateTime oldDob=new(2001,1,2),newDob=new(2002,3,4);
                var studentRow=students.Rows.Add(3334,"3334","Sample Student","Sample Parent","Ten","Active",newDob);
                using var issues=new DataTable();
                string certificateKey=regular?"RegularStudentCertificateId":"CertificateRecordId";
                string issueKey=regular?"RegularStudentCertificateIssueId":"DateOfBirthCertificateIssueId";
                foreach(string key in new[]{certificateKey,issueKey,"AdmissionId"})issues.Columns.Add(key,typeof(int));
                issues.Columns.Add("ClassName");issues.Columns.Add("CollectionStatus");
                foreach(string date in new[]{"DateOfBirth","CertificateDate","IssuedAt"})issues.Columns.Add(date,typeof(DateTime));
                issues.Rows.Add(7,12,3334,"Ten","Collected",newDob,DateTime.Today,DateTime.Now);
                issues.Rows.Add(7,11,3334,"Ten","Collected",oldDob,DateTime.Today.AddDays(-1),DateTime.Now.AddDays(-1));
                using V2Page page=regular?new RegularStudentCertificatePage(false,readCertificates:_=>issues):new DateOfBirthCertificatePage(false,readCertificates:_=>issues);
                var type=page.GetType();T Field<T>(string name)=>(T)type.GetField(name,flags)!.GetValue(page)!;
                var grid=(DataGridView)typeof(V2Page).GetField("Grid",flags)!.GetValue(page)!;
                grid.Parent!.Controls.Remove(grid);
                using var host=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000),ClientSize=new Size(1200,400)};
                grid.Dock=DockStyle.Fill;host.Controls.Add(grid);host.Show();Application.DoEvents();
                var picker=Field<StudentPicker>("student");picker.SetStudent(studentRow);
                type.GetMethod("StudentChanged",flags)!.Invoke(page,null);Application.DoEvents();
                var print=Field<ModernButton>("printButton");
                var again=Field<ModernButton>(regular?"updateCertificateButton":"issueAgainButton");
                var dob=Field<ModernTextBox>("dateOfBirth");
                Check(Field<int>("selectedIssueId")==12&&print.Enabled&&again.Enabled,"The latest saved "+type.Name+" copy is selected and printable.");
                Check(again.Text==UiText.Certificates.IssueAgainAction,"The new-copy action is clearly named Issue Again.");
                var oldRow=grid.Rows.Cast<DataGridViewRow>().Single(r=>((DataRowView)r.DataBoundItem).Row.Field<int>(issueKey)==11);
                grid.ClearSelection();grid.CurrentCell=oldRow.Cells.Cast<DataGridViewCell>().First(c=>c.Visible);oldRow.Selected=true;Application.DoEvents();
                Check(Field<int>("selectedIssueId")==11&&dob.Text==oldDob.ToString(regular?"dd-MMM-yyyy":"dd-MM-yyyy")&&print.Enabled,
                    "Keyboard history selection displays the earlier copy's own birth date for "+type.Name+".");
                studentRow["DateOfBirth"]=new DateTime(2003,5,6);
                type.GetMethod("ApplyWorkflowState",flags)!.Invoke(page,null);
                Check(dob.Text==oldDob.ToString(regular?"dd-MMM-yyyy":"dd-MM-yyyy")&&print.Enabled,"A later school-record correction cannot silently change the selected saved copy.");
                studentRow["DateOfBirth"]=DBNull.Value;
                type.GetMethod("ApplyWorkflowState",flags)!.Invoke(page,null);
                Check(picker.FindMatches(students,"3334").Entries.Count==1&&print.Enabled,"Saved copies stay searchable and printable when the current school DOB is missing.");
                var flow=Field<CertificateReissueFlow>("reissue");
                typeof(CertificateReissueFlow).GetProperty("Blocked",flags)!.SetValue(flow,true);
                type.GetMethod("ApplyWorkflowState",flags)!.Invoke(page,null);
                Check(!again.Enabled&&Field<ModernButton>("checkReissue").Enabled&&((IUnsavedChanges)page).HasUnsavedChanges,
                    "An uncertain reissue blocks another copy and exposes outcome checking.");
                type.GetField("reissuing",flags)!.SetValue(page,true);
                type.GetMethod("ApplyWorkflowState",flags)!.Invoke(page,null);
                Check(((IBusyPage)page).IsBusy&&!again.Enabled&&!print.Enabled,"A running reissue holds conflicting actions and navigation.");
                Check(!page.IsHandleCreated,"Snapshot UI checks never start page database loaders.");
                host.Close();
            }
        }
        finally{CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Permissions);}
    }
}
