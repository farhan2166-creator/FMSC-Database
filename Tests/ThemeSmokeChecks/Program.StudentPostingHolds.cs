using System.ComponentModel;
using System.Data;
using System.Reflection;
using System.Text.Json;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Admissions;
using Microsoft.Data.SqlClient;

internal static partial class Program
{
    private static void CheckStudentPostingHoldSqlResponse()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var previous = (CurrentUser.UserId, CurrentUser.Username, CurrentUser.RoleName, CurrentUser.MustChangePassword, CurrentUser.GetPermissions().ToArray());
        CurrentUser.SignIn(901, "Read-only response fixture", "Administrator", false, Array.Empty<string>());
        using var context = new DataTable(); context.ReadXml("artifacts/hold-diagnostic-context.xml");
        using var plan = new DataTable(); plan.ReadXml("artifacts/hold-diagnostic-plan.xml");
        int writes = 0;
        async Task<DataTable> Query(string procedure, SqlParameter[] args)
        {
            await Task.Delay(10);
            if (procedure == "GetStudentPostingHoldContext") return context.Copy();
            if (procedure == "SaveStudentPostingDecision" && Convert.ToBoolean(args.Single(a => a.ParameterName == "@Preview").Value)) return plan.Copy();
            writes++; throw new InvalidOperationException("This rendering check cannot submit a database write.");
        }
        try
        {
            using var dialog = new StudentPostingHoldDialog(6589, "Read-only review response", false, query: Query,
                recoveryPath: Path.GetFullPath("artifacts/student-holds/response-" + Guid.NewGuid().ToString("N")));
            T Field<T>(string name) => (T)dialog.GetType().GetField(name, flags)!.GetValue(dialog)!;
            dialog.Show(); PumpUntil(() => Field<ModernButton>("review").Enabled, "Actual SQL context binds after an asynchronous response");
            Field<ModernTextBox>("reason").Text = "Read-only response rendering check";
            typeof(Control).GetMethod("OnClick", flags)!.Invoke(Field<ModernButton>("review"), new object[] { EventArgs.Empty });
            PumpUntil(() => Field<ModernButton>("review").Enabled, "Actual SQL review response finishes rendering");
            Check(Field<ModernButton>("save").Enabled && Field<ModernTextBox>("impact").Text.Contains("Outstanding after saving:"),
                "The actual SQL preview renders fully and enables the reviewed save action");
            Check(writes == 0, "Rendering the actual preview saves no hold and changes no fees");
            using var picture = new Bitmap(dialog.Width, dialog.Height); dialog.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
            picture.Save("artifacts/student-holds/hold-read-only-review.png"); dialog.Close();
        }
        finally { CurrentUser.SignIn(previous.UserId, previous.Username, previous.RoleName, previous.MustChangePassword, previous.Item5); }
    }

    private static void CheckStudentPostingHolds()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var previous = (CurrentUser.UserId, CurrentUser.Username, CurrentUser.RoleName, CurrentUser.MustChangePassword, CurrentUser.GetPermissions().ToArray());
        var previousTheme = AppTheme.CurrentTheme;
        CurrentUser.SignIn(901, "Hold fixture", "Administrator", false, Array.Empty<string>());
        string folder = Path.GetFullPath("artifacts/student-holds"); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Guid.NewGuid().ToString("N"));
        var requests = new List<(string Procedure, SqlParameter[] Args)>();
        TaskCompletionSource<DataTable>? reviewReply = null, saveReply = null;
        Guid savedRequest = Guid.Empty; string databaseScope = new('A', 64);
        T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, flags)!.GetValue(owner)!;
        void Click(Control control) => typeof(Control).GetMethod("OnClick", flags)!.Invoke(control, new object[] { EventArgs.Empty });
        DataTable Plan()
        {
            var table = new DataTable(); table.Columns.Add("Snapshot");
            foreach (string name in new[] { "Outstanding", "Waived", "ReleasedPaid", "Restored", "NewCharges", "AvailableCredit", "CreditToApply", "ResultingOutstanding" }) table.Columns.Add(name, typeof(decimal));
            table.Columns.Add("ChangedFees"); table.Columns.Add("AddedFees");
            table.Rows.Add("reviewed", 1000.50m, 0m, 0m, 0m, 0m, 200m, 0m, 1000.50m, "[]", "[]"); return table;
        }
        Task<DataTable> Query(string procedure, SqlParameter[] args)
        {
            requests.Add((procedure, args));
            if (procedure == "GetStudentPostingHoldContext")
            {
                var table = new DataTable(); table.Columns.Add("EnrollmentId", typeof(int)); table.Columns.Add("Placement"); table.Columns.Add("EnrollmentStatus");
                table.Rows.Add(12, "2026–2027 / Fictional Campus / Class A (Active)", "Active"); return Task.FromResult(table);
            }
            if (procedure == "GetStudentPostingHoldBills")
            {
                var table = new DataTable(); table.Columns.Add("BillingChargeId", typeof(long)); table.Columns.Add("FeeTypeName"); table.Columns.Add("BillingMonth", typeof(DateTime));
                table.Columns.Add("RestoreAmount", typeof(decimal)); table.Columns.Add("CanRestore", typeof(bool));
                table.Rows.Add(100L, "Tuition", new DateTime(2026, 10, 1), 400m, true);
                table.Rows.Add(101L, "Transport — changed since hold", new DateTime(2026, 10, 1), 500m, false);
                return Task.FromResult(table);
            }
            if (procedure == "GetStudentPostingDecisionOutcome")
            {
                var table = new DataTable(); table.Columns.Add("ResultJson");
                table.Rows.Add(JsonSerializer.Serialize(new { RequestId = savedRequest, AdmissionId = 41, HoldId = 4, Action = "Hold" })); return Task.FromResult(table);
            }
            if (Convert.ToBoolean(args.Single(a => a.ParameterName == "@Preview").Value))
            { reviewReply = new(); return reviewReply.Task; }
            savedRequest = (Guid)args.Single(a => a.ParameterName == "@RequestId").Value;
            saveReply = new(); return saveReply.Task;
        }
        bool RefusesClose(Form dialog)
        {
            var e = new FormClosingEventArgs(CloseReason.UserClosing, false);
            typeof(Form).GetMethod("OnFormClosing", flags)!.Invoke(dialog, new object[] { e }); return e.Cancel;
        }
        try
        {
            using (var admission = new StudentAdmissionPage())
            {
                typeof(StudentAdmissionPage).GetField("currentAdmissionId", flags)!.SetValue(admission, 41);
                typeof(StudentAdmissionPage).GetField("studentPostingHeld", flags)!.SetValue(admission, true);
                var openReturn = (Task)typeof(StudentAdmissionPage).GetMethod("OpenConfirmReturnAsync", flags)!.Invoke(admission, null)!;
                var status = (Label)typeof(V2Page).GetField("StatusLabel", flags)!.GetValue(admission)!;
                Check(openReturn.IsCompletedSuccessfully && status.Text.Contains("Actions > Lift Hold / Resume"),
                    "Admission directs a held student to reviewed resume instead of opening Confirm Return");
                Check(!admission.IsHandleCreated, "Held return routing opens no window or database request");
            }
            using (var dialog = new StudentPostingHoldDialog(41, "Fictional student", false, query: Query, scope: () => Task.FromResult(databaseScope), recoveryPath: path))
            {
                dialog.Show(); PumpUntil(() => Field<ModernButton>(dialog, "review").Enabled, "Hold context loads");
                var review = Field<ModernButton>(dialog, "review"); var save = Field<ModernButton>(dialog, "save");
                var reason = Field<ModernTextBox>(dialog, "reason"); var policy = Field<ModernComboBox>(dialog, "policy");
                var month = Field<ModernDatePicker>(dialog, "month");
                Check(!month.Enabled, "School-cutoff hold does not require a waiver month");
                policy.SelectedIndex = 2; Check(month.Enabled, "Selected-month waiver enables its month field");
                reason.Text = "Fictional absence"; Click(review);
                Check(RefusesClose(dialog) && !reason.Enabled, "Pending hold review blocks closing and editing: " + Field<ModernTextBox>(dialog, "impact").Text);
                reason.Text = "Changed while review was pending"; reviewReply!.SetResult(Plan());
                PumpUntil(() => review.Enabled, "Obsolete hold review settles");
                Check(!save.Enabled, "An obsolete hold review cannot be saved");
                Click(review); reviewReply!.SetResult(Plan()); PumpUntil(() => save.Enabled, "Current hold review enables saving");
                Check(Field<ModernTextBox>(dialog, "impact").Text.Contains("1,000.50"), "Review preserves legitimate historical fractional balances");
                foreach (object emptyFees in new object[] { DBNull.Value, "", "  ", "[]" })
                {
                    // SQL's empty JSON scalar subquery is DBNull in production;
                    // the earlier fixture used only a non-null empty JSON array.
                    var empty = Plan(); empty.Rows[0]["ChangedFees"] = emptyFees; empty.Rows[0]["AddedFees"] = emptyFees;
                    Click(review); reviewReply!.SetResult(empty);
                    PumpUntil(() => review.Enabled, "Empty fee-list review settles");
                    Check(save.Enabled && Field<ModernTextBox>(dialog, "impact").Text.Contains("Outstanding after saving:"),
                        "Null, blank and empty JSON fee lists permit a complete hold review");
                }
                var populated = Plan();
                populated.Rows[0]["ChangedFees"] = "[{\"BillingMonth\":\"2026-11-01\",\"FeeTypeName\":\"Tuition Fee\",\"Amount\":400.00,\"Due\":0.00}]";
                populated.Rows[0]["AddedFees"] = DBNull.Value;
                Click(review); reviewReply!.SetResult(populated); PumpUntil(() => review.Enabled, "Mixed populated/null fee-list review settles");
                Check(save.Enabled && Field<ModernTextBox>(dialog, "impact").Text.Contains("Waive: Tuition Fee / Nov 2026 / Rs. 400"),
                    "A real SQL-shaped hold review renders waived fees alongside an empty new-fee list");
                using (var picture = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size)); picture.Save(Path.Combine(folder, "hold.png")); }
                Click(save);
                PumpUntil(() => saveReply is not null, "Hold save is submitted after durable intent");
                Check(RefusesClose(dialog), "Pending hold save blocks closing");
                Check(File.Exists(path + ".StudentHold"), "Recovery intent exists before the SQL write completes");
                int writes = requests.Count(r => r.Procedure == "SaveStudentPostingDecision" && !Convert.ToBoolean(r.Args.Single(a => a.ParameterName == "@Preview").Value));
                Click(save); Check(writes == 1 && requests.Count(r => r.Procedure == "SaveStudentPostingDecision" && !Convert.ToBoolean(r.Args.Single(a => a.ParameterName == "@Preview").Value)) == 1, "Hold save submits one write");
                Check(Convert.ToString(requests.Last().Args.Single(a => a.ParameterName == "@ExpectedSnapshot").Value) == "reviewed", "Hold save retains the exact reviewed snapshot");
                saveReply!.SetException(new IOException("Synthetic lost response"));
                PumpUntil(() => Field<ModernButton>(dialog, "check").Enabled && Field<ModernButton>(dialog, "check").Visible, "Unconfirmed save exposes outcome recovery");
                Check(!review.Enabled && !save.Enabled, "An unconfirmed hold cannot be reviewed or saved again");
                Check(!RefusesClose(dialog), "An uncertain save can be closed with durable recovery retained");
                dialog.Close();
            }
            // Reopening through Resume must still recover a pending Hold, even
            // when a refreshed status already shows that the hold was committed.
            using (var dialog = new StudentPostingHoldDialog(41, "Fictional student", true, query: Query, scope: () => Task.FromResult(databaseScope), recoveryPath: path))
            {
                dialog.Show(); PumpUntil(() => Field<ModernButton>(dialog, "check").Visible, "Reopened resume discovers pending hold outcome");
                var grid = Field<DataGridView>(dialog, "bills");
                Check(grid.Rows.Count == 2 && grid.Rows[1].Cells["Restore"].ReadOnly, "Changed waived fees cannot be selected for restoration");
                Check(grid.DisplayedRowCount(false) >= 2, "The usual resume dialog shows waived-fee rows below the header");
                Check(!Field<CheckBox>(dialog, "credit").Checked, "Resume does not apply advance without an explicit choice");
                using (var picture = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size)); picture.Save(Path.Combine(folder, "resume.png")); }
                databaseScope = new string('B', 64); Click(Field<ModernButton>(dialog, "check")); Application.DoEvents();
                Check(!requests.Any(r => r.Procedure == "GetStudentPostingDecisionOutcome"), "Recovery refuses a different database before querying its outcome");
                Check(File.Exists(path + ".StudentHold"), "Wrong-database refusal retains recovery evidence");
                databaseScope = new string('A', 64); Click(Field<ModernButton>(dialog, "check"));
                PumpUntil(() => dialog.Changed, "Recovery verifies the original committed hold");
                Check(!File.Exists(path + ".StudentHold") && dialog.DialogResult == DialogResult.OK, "Verified recovery clears its intent and refreshes the parent");
                Check(requests.Count(r => r.Procedure == "SaveStudentPostingDecision" && !Convert.ToBoolean(r.Args.Single(a => a.ParameterName == "@Preview").Value)) == 1,
                    "Recovery after restart checks the original result without a second write");
            }
            var students = new DataTable(); students.Columns.Add("AdmissionNumber"); students.Columns.Add("StudentName"); students.Columns.Add("Status"); students.Columns.Add("IsOnHold", typeof(bool));
            students.Rows.Add("6589", "Fictional student", "Active", true);
            Check(StudentPickerRowProjector.DisplayFor(students.Rows[0]).Contains("[On Hold]"), "Search identifies an independently held active student");
            Check(!FamilyBillingData.AwaitsReturn(students.Rows[0]) && FamilyBillingData.Readiness(students.Rows[0]) == "On Hold",
                "A family counter identifies a hold separately from promotion return confirmation");
            foreach (var theme in AppTheme.SelectableThemes.Take(2))
                foreach (var size in new[] { new Size(940, 780), new Size(780, 660) })
                {
                    AppTheme.SetTheme(theme);
                    using var dialog = new StudentPostingHoldDialog(41, "Fictional student", true, query: Query, scope: () => Task.FromResult(databaseScope), recoveryPath: path + ".layout");
                    dialog.ClientSize = size; dialog.Show();
                    PumpUntil(() => Field<ModernButton>(dialog, "review").Enabled, "Resume layout context loads");
                    var grid = Field<DataGridView>(dialog, "bills"); var body = Field<TableLayoutPanel>(dialog, "body");
                    Check(grid.DisplayedRowCount(false) >= 2, "Compact and standard layouts retain visible waived-fee rows");
                    var annual = Field<CheckBox>(dialog, "annual"); Check(!annual.Checked, "Annual fees are an explicit resume choice");
                    var save = Field<ModernButton>(dialog, "save"); body.ScrollControlIntoView(save); Application.DoEvents();
                    var visible = body.RectangleToClient(save.RectangleToScreen(save.ClientRectangle));
                    Check(visible.Top >= 0 && visible.Bottom <= body.ClientSize.Height, "Resume actions remain reachable in compact and standard layouts");
                    using var picture = new Bitmap(dialog.Width, dialog.Height); dialog.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                    picture.Save(Path.Combine(folder, $"resume-{theme}-{size.Width}.png")); dialog.Close();
                }
        }
        finally { AppTheme.SetTheme(previousTheme); CurrentUser.SignIn(previous.UserId, previous.Username, previous.RoleName, previous.MustChangePassword, previous.Item5); }
    }
}
