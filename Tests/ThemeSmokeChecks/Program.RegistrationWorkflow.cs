using System.Reflection;
using System.Data;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Admissions;

internal static partial class Program
{
    private static void CheckRegistrationWorkflow()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var previous = (CurrentUser.UserId, CurrentUser.Username, CurrentUser.RoleName, CurrentUser.MustChangePassword, Grants: CurrentUser.GetPermissions().ToArray());
        try
        {
            CurrentUser.SignIn(1, "Fictional admission operator", "Administrator", false, Array.Empty<string>());
            using var page = new StudentAdmissionPage(false);
            var type = typeof(StudentAdmissionPage);
            void Set(string field, object value) => type.GetField(field, flags)!.SetValue(page, value);
            T Field<T>(string field) => (T)type.GetField(field, flags)!.GetValue(page)!;
            void Update() => type.GetMethod("UpdateStudentWorkspace", flags)!.Invoke(page, null);
            using var host = new Form { ClientSize = new Size(1600, 900) };
            host.Controls.Add(page); host.Show(); Application.DoEvents();
            type.GetMethod("SwitchTab", flags)!.Invoke(page, new object[] { 1 }); Application.DoEvents();
            using var family = new DataTable();
            family.Columns.Add("AdmissionId", typeof(int));
            foreach (string column in new[] { "Student Name", "Father Name", "Class", "Campus", "Status" }) family.Columns.Add(column);
            family.Rows.Add(123, "Fictional Pending Child", "Fictional Parent", "Four", "Test Campus", "Pending");
            family.Rows.Add(456, "Fictional Cancelled Child", "Fictional Parent", "Five", "Test Campus", "Cancelled");
            family.Rows.Add(789, "Fictional Active Child", "Fictional Parent", "Six", "Test Campus", "Active");
            var siblingGrid = Field<DataGridView>("siblingsGrid"); siblingGrid.AutoGenerateColumns = true; siblingGrid.DataSource = family;
            type.GetMethod("ConfigureSiblingRegistrationRows", flags)!.Invoke(page, null);
            siblingGrid.Columns["AdmissionId"].Visible = false;
            foreach (DataGridViewColumn column in siblingGrid.Columns) column.MinimumWidth = 90;
            Application.DoEvents();
            Set("currentAdmissionId", 123); Set("_workspaceStudentStatus", "Pending"); Update();
            Check(Convert.ToString(siblingGrid.Rows[0].Cells["RegistrationConfirm"].Value) == "Confirm & Save", "Pending children get Confirm & Save on their own row");
            Check(Convert.ToString(siblingGrid.Rows[0].Cells["RegistrationCancel"].Value) == "Cancel", "Pending children retain row cancellation");
            var addSibling = Field<ModernButton>("addNewSiblingButton");
            Check(addSibling.Parent is FlowLayoutPanel && addSibling.Enabled && addSibling.Visible && addSibling.Parent.ClientRectangle.Contains(addSibling.Bounds), "Add New Sibling is visible inside the existing sibling action row");
            using var rows = new DataTable(); rows.Columns.Add("Status"); var pending = rows.Rows.Add("Pending"); var cancelled = rows.Rows.Add("Cancelled");
            var picker = Field<StudentPicker>("quickStudent");
            Check(picker.RowFilter!(pending) && picker.RowFilter!(cancelled), "Admission quick search accepts pending and cancelled records");
            ((Task)type.GetMethod("RunSiblingRegistrationActionAsync", flags)!.Invoke(page, new object[] { 0, siblingGrid.Columns["RegistrationConfirm"].Index })!).GetAwaiter().GetResult();
            Check(type.GetField("unconfirmedAdmission", flags)!.GetValue(page) is null, "Incomplete pending details refuse confirmation before a write");
            type.GetMethod("SwitchTab", flags)!.Invoke(page, new object[] { 1 });
            Set("_workspaceStudentStatus", "Cancelled"); family.Rows[0]["Status"] = "Cancelled"; Update();
            Check(Convert.ToString(siblingGrid.Rows[0].Cells["RegistrationReopen"].Value) == "Reopen", "Cancelled registration exposes row reopening");
            Check(string.IsNullOrEmpty(Convert.ToString(siblingGrid.Rows[0].Cells["RegistrationConfirm"].Value)) && !Field<ModernButton>("saveButton").Enabled && !addSibling.Enabled, "Cancelled records cannot be confirmed, edited through Save or used to create siblings");
            Set("_workspaceStudentStatus", "Active"); family.Rows[0]["Status"] = "Active"; Update();
            Check(string.IsNullOrEmpty(Convert.ToString(siblingGrid.Rows[0].Cells["RegistrationReopen"].Value)), "Admitted students do not get registration reopening");
            CurrentUser.SignIn(2, "Read only", "Operator", false, new[] { "Students.View" }); Update();
            Check(!Field<ModernButton>("quickRegistrationButton").Enabled && !addSibling.Enabled, "Viewing access does not grant registration writes");
            CurrentUser.SignIn(1, "Fictional admission operator", "Administrator", false, Array.Empty<string>());
            Set("_workspaceStudentStatus", "Pending"); family.Rows[0]["Status"] = "Pending"; Update();
            typeof(V2Page).GetMethod("SetStatus", flags, null, new[] { typeof(string), typeof(bool) }, null)!.Invoke(page, new object[] { "Pending registration: complete details, then Confirm & Save.", false });
            Application.DoEvents();
            var status = (Label)typeof(V2Page).GetField("StatusLabel", flags)!.GetValue(page)!;
            var layout = (TableLayoutPanel)status.Parent!;
            Check(layout.ColumnCount == 1 && layout.GetRow(status) == 1, "Admission status has its own full-width row below actions");
            Check(status.Height <= AppMetrics.TextHeight(status.Font) + 8, "Status row stays compact");
            Directory.CreateDirectory("artifacts/registration-workflow-ui");
            foreach (int width in new[] { 1600, 1050 })
            {
                host.ClientSize = new Size(width, 900); Application.DoEvents();
                Check(status.Width >= layout.ClientSize.Width - layout.Padding.Horizontal - 2, "Status retains full width after resize");
                using var picture = new Bitmap(host.Width, host.Height); host.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                picture.Save($"artifacts/registration-workflow-ui/admission-{width}.png");
            }
        }
        finally { CurrentUser.SignIn(previous.UserId, previous.Username, previous.RoleName, previous.MustChangePassword, previous.Grants); }
    }
}
