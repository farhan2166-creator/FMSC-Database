using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Certificates;

internal static partial class Program
{
    private static void CheckCharacterCertificateReprint()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,Permissions:CurrentUser.GetPermissions().ToArray());
        try
        {
            CurrentUser.SignIn(901,"Certificate fixture","Administrator",false,Array.Empty<string>());
            using var page=new CharacterCertificatePage(false);
            var type=typeof(CharacterCertificatePage);
            T Field<T>(string name)=>(T)type.GetField(name,flags)!.GetValue(page)!;
            var picker=Field<StudentPicker>("student");picker.SetStudent(3334,"Sample Student");
            var exam=Field<ModernComboBox>("examType");
            var print=Field<ModernButton>("printButton");var again=Field<ModernButton>("issueAgainButton");
            var grid=(DataGridView)typeof(V2Page).GetField("Grid",flags)!.GetValue(page)!;
            grid.Parent!.Controls.Remove(grid);
            using var host=new Form {ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000),ClientSize=new Size(1200,400)};
            grid.Dock=DockStyle.Fill;host.Controls.Add(grid);host.Show();Application.DoEvents();
            var records=Field<DataTable>("certificateResults");
            foreach(string column in new[]{"CertificateRecordId","CharacterCertificateIssueId","AdmissionId"})records.Columns.Add(column,typeof(int));
            records.Columns.Add("CollectionStatus");records.Columns.Add("IssuedAt",typeof(DateTime));
            records.Columns.Add("ExamType");records.Columns.Add("RollNumber");records.Columns.Add("Remarks");records.Columns.Add("CertificateDate",typeof(DateTime));
            records.Rows.Add(1,2,3334,"Collected",DateTime.Today,"HSSC","H-123","Saved remarks",DateTime.Today);
            records.Rows.Add(1,1,3334,"Collected",DateTime.Today.AddDays(-1),"SSC","S-123","Original remarks",DateTime.Today.AddDays(-1));
            typeof(V2Page).GetMethod("Bind",flags)!.Invoke(page,new object[]{records});
            void Select(int row)
            {
                grid.ClearSelection();grid.CurrentCell=grid.Rows[row].Cells.Cast<DataGridViewCell>().First(cell=>cell.Visible);grid.Rows[row].Selected=true;Application.DoEvents();
            }
            exam.SelectedIndex=0;Select(0);
            Check(Field<int>("selectedIssueId")==2&&exam.SelectedIndex==1&&!exam.Enabled,
                "Selecting a saved history row loads its stored HSSC type and prevents print-time changes.");
            Check(print.Enabled&&again.Enabled,"A collected character certificate permits both reprint and another issuance.");
            type.GetMethod("SelectSavedCertificate",flags)!.Invoke(page,new object[]{0});
            Check(Field<int>("selectedIssueId")==2&&exam.SelectedIndex==1,"Automatic history selection chooses the newest issue's saved type.");
            Select(1);
            Check(Field<int>("selectedIssueId")==1&&print.Enabled&&!again.Enabled&&exam.SelectedIndex==0,
                "An earlier copy keeps its own SSC type; another issue starts only from the latest copy.");
            exam.SelectedIndex=-1;
            Check(print.Enabled&&!again.Enabled,"Changing the UI selection cannot change the stored print type or enable an older reissue.");
            records.Rows[0]["CollectionStatus"]="Not Collected";Select(0);
            Check(!print.Enabled&&!again.Enabled&&Field<ModernButton>("collectButton").Enabled,
                "An uncollected latest issue exposes Mark Collected and blocks another issue.");
            Select(1);
            Check(print.Enabled&&!again.Enabled,"An older collected copy remains reprintable while a newer copy awaits collection.");
            records.Rows[0]["CollectionStatus"]="Collected";Select(0);
            CurrentUser.SignIn(902,"Create-only fixture","Operator",false,new[]{"Certificates.Create"});
            type.GetMethod("ApplyWorkflowState",flags)!.Invoke(page,null);
            Check(!print.Enabled,"Reprinting still requires Certificates.Print permission.");
            Check(!page.IsHandleCreated,"History selection checks never start page database loaders.");
            host.Close();
            var details=new DataTable();
            foreach(string name in new[]{"ExamType","StudentName","FatherName","Gender","RollNumber","Remarks"})details.Columns.Add(name);
            details.Columns.Add("LastExamYear",typeof(int));details.Columns.Add("CertificateDate",typeof(DateTime));
            var saved=details.Rows.Add("SSC","Synthetic Student","Synthetic Parent","Male","12345","Satisfactory conduct",2026,DateTime.Today);
            using var dialog=new CharacterCertificateCorrectionDialog(saved,false);
            dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(-20000,-20000);dialog.ShowInTaskbar=false;
            dialog.Show();Application.DoEvents();
            Check(Descendants(dialog).OfType<ModernTextBox>().Count()==3,"Correction editor includes only the entered roll, remarks and correction reason; Admission details and derived exam year are excluded.");
            Check(Descendants(dialog).OfType<ModernButton>().Any(button=>button.Text=="Save Replacement"&&button.Visible),"Correction editor exposes its explicit replacement action.");
            Directory.CreateDirectory("artifacts/character-certificate");
            using var bitmap=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
            bitmap.Save(Path.GetFullPath("artifacts/character-certificate/correction.png"));dialog.Close();
        }
        finally {CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Permissions);}
    }
}
