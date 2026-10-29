using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Certificates;

internal static partial class Program
{
    private static void CheckSportsCertificateInput()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,Permissions:CurrentUser.GetPermissions().ToArray());
        try
        {
            CurrentUser.SignIn(992,"Sports input fixture","Administrator",false,Array.Empty<string>());
            using var history=new DataTable();
            foreach(string name in new[]{"SportsCertificateId","AdmissionId"})history.Columns.Add(name,typeof(int));
            foreach(string name in new[]{"StudentName","FatherName","SportName","Performance","CollectionStatus"})history.Columns.Add(name);
            foreach(string name in new[]{"CertificateDate","CreatedAt","UpdatedAt","CollectedAt"})history.Columns.Add(name,typeof(DateTime));
            history.Columns.Add("ContentVersion",typeof(byte[]));
            var saved=history.NewRow();saved["SportsCertificateId"]=91;saved["AdmissionId"]=41;saved["StudentName"]="Fixture Student";saved["FatherName"]="Fixture Parent";
            saved["SportName"]="Cricket";saved["Performance"]="Saved performance";saved["CollectionStatus"]="Not Collected";saved["CertificateDate"]=DateTime.Today;saved["CreatedAt"]=DateTime.Now;
            byte[] token={0,0,0,0,0,0,0,17};saved["ContentVersion"]=token;history.Rows.Add(saved);
            using var page=new SportsCertificatePage(false,readCertificates:_=>history);
            T Field<T>(string name)=>(T)typeof(SportsCertificatePage).GetField(name,flags)!.GetValue(page)!;
            var student=Field<StudentPicker>("student");var suggestions=Field<ModernComboBox>("performanceSuggestions");var performance=Field<ModernTextBox>("performance");
            var grid=(DataGridView)typeof(V2Page).GetField("Grid",flags)!.GetValue(page)!;
            var errors=new List<Exception>();grid.DataError+=(_,e)=>{if(e.Exception is not null)errors.Add(e.Exception);e.ThrowException=false;e.Cancel=true;};
            Control card=student;while(card is not HumanCard)card=card.Parent!;
            var root=(TableLayoutPanel)card.Parent!;root.Parent!.Controls.Remove(root);
            using var host=new Form {ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000),ClientSize=new Size(1200,800)};
            root.Dock=DockStyle.Fill;host.Controls.Add(root);host.Show();Application.DoEvents();
            using var students=new DataTable();students.Columns.Add("AdmissionId",typeof(int));students.Columns.Add("StudentName");students.Columns.Add("FatherName");students.Columns.Add("Status");
            typeof(SportsCertificatePage).GetMethod("ResetToStart",flags)!.Invoke(page,null);
            student.SetStudent(students.Rows.Add(41,"Fixture Student","Fixture Parent","Active"));
            void Load()=>typeof(SportsCertificatePage).GetMethod("StudentChanged",flags)!.Invoke(page,null);
            Load();Application.DoEvents();
            using(var bitmap=new Bitmap(host.Width,host.Height))host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
            Check(errors.Count==0,"Populated sports history binds and paints without grid errors: "+string.Join("; ",errors.Select(error=>error.Message)));
            Check(grid.Columns["ContentVersion"] is DataGridViewTextBoxColumn&&!grid.Columns["ContentVersion"].Visible,"The binary concurrency token is hidden metadata, not an image column");
            Check(CertificateCollectionFlow.Version(Field<DataTable>("certificateResults"),"SportsCertificateId",91)!.SequenceEqual(token),"Binding preserves the original version bytes for save and collection checks");
            var savedVersion=CertificateCollectionFlow.VersionParameter(Field<DataTable>("certificateResults"),"SportsCertificateId",91);
            Check(savedVersion.SqlDbType==SqlDbType.Binary&&savedVersion.Size==8&&((byte[])savedVersion.Value).SequenceEqual(token),"Saved version parameters retain their binary type and exact bytes");
            var missingVersion=CertificateCollectionFlow.VersionParameter(Field<DataTable>("certificateResults"),"SportsCertificateId",0);
            Check(missingVersion.SqlDbType==SqlDbType.Binary&&missingVersion.Size==8&&missingVersion.Value==DBNull.Value,"A new certificate sends a typed binary NULL instead of an untyped string NULL");
            var input=suggestions.Controls.OfType<ComboBox>().Single();input.Focus();
            SearchInputMessage(input.Handle,0x0100,new IntPtr(0x28),IntPtr.Zero);Application.DoEvents();
            Check(suggestions.SelectedIndex==1&&performance.Text.Contains("sportsmanship"),"Down selects the first sentence and keeps its visible selection");
            SearchInputMessage(input.Handle,0x0100,new IntPtr(0x28),IntPtr.Zero);Application.DoEvents();
            Check(suggestions.SelectedIndex==2&&performance.Text.Contains("enthusiasm"),"Another Down advances to the next sentence rather than returning to the placeholder");
            input.DroppedDown=false;SearchInputMessage(input.Handle,0x0102,new IntPtr('T'),IntPtr.Zero);Application.DoEvents();
            Check(suggestions.SelectedIndex==3&&performance.Text.Contains("teamwork"),"Typing T selects Teamwork and fills its sentence");
            performance.Text="Custom sports achievement";
            Check(performance.Text=="Custom sports achievement","Suggested text remains editable");
            Load();Application.DoEvents();
            Check(suggestions.SelectedIndex==0&&performance.Text=="Saved performance"&&grid.Focused&&grid.CurrentRow?.Index==0,
                "A fresh student load selects the latest saved certificate and focuses its history row");
            typeof(SportsCertificatePage).GetMethod("Grid_CellMouseClick",flags)!.Invoke(page,new object?[]{grid,new DataGridViewCellMouseEventArgs(0,0,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0))});
            Check(performance.Text=="Saved performance"&&suggestions.SelectedIndex==0,"Selecting a saved certificate preserves its stored wording");
            var bound=Field<DataTable>("certificateResults");bound.Rows[0]["CollectedAt"]=DateTime.Now;bound.Rows[0]["CollectionStatus"]="Collected";
            typeof(SportsCertificatePage).GetMethod("ApplyWorkflowState",flags)!.Invoke(page,null);
            suggestions.SelectedIndex=1;
            Check(!suggestions.Enabled&&performance.Text=="Saved performance","Suggestions cannot overwrite a collected certificate");
            Check(errors.Count==0&&!page.IsHandleCreated,"Repeated student loads cause no grid errors and start no page SQL loaders");
            history.Clear();Load();Application.DoEvents();
            Check(suggestions.SelectedIndex==0&&performance.Text.Length==0,"A student without saved certificates starts with fresh sentence choice and wording");
            host.Close();
        }
        finally {CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Permissions);}
    }
}
