using System.Data;
using System.Reflection;
using FMSC_SMS;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Enrollment;
using Microsoft.Data.SqlClient;

internal static partial class Program
{
    private static void CheckPromotionHistoryEntry()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var previous = (CurrentUser.UserId, CurrentUser.Username, CurrentUser.RoleName,
            CurrentUser.MustChangePassword, CurrentUser.GetPermissions().ToArray());
        try
        {
            foreach (var access in new[] { (Role: "Operator", Permissions: new[] { "Enrollment.Manage" }, Allowed: true),
                (Role: "Accountant", Permissions: new[] { "Students.View" }, Allowed: false),
                (Role: "Administrator", Permissions: Array.Empty<string>(), Allowed: true) })
            {
                CurrentUser.SignIn(910, "Promotion fixture", access.Role, false, access.Permissions);
                using var shell = new MainDashboardForm((_, _) => { });
                var commands = (List<NavigationCommand>)typeof(MainDashboardForm).GetField("navigationCommands", flags)!.GetValue(shell)!;
                Check(commands.Count(c => c.Caption == PageTitles.StudentPromotion) == (access.Allowed ? 1 : 0),
                    "Student Promotion keeps the established permission and Administrator override");
                Check(commands.All(c => c.Caption is not ("Promotion History" or "Bulk Promotion")),
                    "The sidebar and command palette have one promotion destination");
            }
            CurrentUser.SignIn(911, "Promotion fixture", "Operator", false, new[] { "Enrollment.Manage" });
            using var promotion = (BulkPromotionPage)Activator.CreateInstance(typeof(BulkPromotionPage), flags, null,
                new object?[] { null, false }, null)!;
            var promotionType = typeof(BulkPromotionPage);
            T Field<T>(string name) => (T)promotionType.GetField(name, flags)!.GetValue(promotion)!;
            void Invoke(string name) => promotionType.GetMethod(name, flags)!.Invoke(promotion, null);
            DataTable Lookup(string key, string text, params (int Id, string Name)[] rows)
            {
                var table = new DataTable(); table.Columns.Add(key, typeof(int)); table.Columns.Add(text, typeof(string));
                foreach (var row in rows) table.Rows.Add(row.Id, row.Name);
                return table;
            }
            var years = Lookup("AcademicYearId", "YearName", (103, "Fixture source year"), (104, "Fixture next year"));
            var classes = Lookup("ClassId", "ClassName", (606, "Fixture Class 6"), (607, "Fixture Class 7"));
            var sections = Lookup("SectionId", "SectionName", (17, "Fixture Section A"));
            foreach (var item in new[] { (Field: "sourceYear", Data: years, Key: "AcademicYearId", Text: "YearName", Id: 103),
                (Field: "sourceClass", Data: classes, Key: "ClassId", Text: "ClassName", Id: 606),
                (Field: "sourceSection", Data: sections, Key: "SectionId", Text: "SectionName", Id: 17) })
            {
                var lookup = Field<LookupBox>(item.Field); lookup.Bind(item.Data, item.Key, item.Text); lookup.SelectedValue = item.Id;
            }
            var candidates = new DataTable(); candidates.Columns.Add("Selected", typeof(bool));
            candidates.Columns.Add("Decision", typeof(string)); candidates.Rows.Add(true, "PROMOTE");
            promotionType.GetField("candidateData", flags)!.SetValue(promotion, candidates);
            Field<ModernTextBox>("remarks").Text = "Retain this promotion draft";
            using var parent = new Form { ClientSize = new Size(1080, 650), AutoScaleMode = AutoScaleMode.None };
            parent.Controls.Add(promotion); parent.Show(); Application.DoEvents(); Invoke("ApplyState");
            var historyButton = Field<ModernButton>("historyButton");
            Check(promotion.NavigationTitle == PageTitles.StudentPromotion, "The actual page title agrees with navigation");
            Check(historyButton.Visible && historyButton.Enabled && historyButton.Parent is Panel,
                "History is accessible before candidates or a complete promotion setup are required");
            var header = historyButton.Parent!;
            foreach (int width in new[] { 1280, 1080, 850 })
            {
                parent.ClientSize = new Size(width, 650); Application.DoEvents();
                Check(historyButton.Left >= 0 && historyButton.Right <= header.ClientSize.Width &&
                    historyButton.Top >= 0 && historyButton.Bottom <= header.ClientSize.Height,
                    "The history accessory fits the shared header at different client widths");
                Check(header.Controls.OfType<Label>().All(l => !l.Bounds.IntersectsWith(historyButton.Bounds)),
                    "The history accessory leaves header wording readable");
            }
            Invoke("BeginBusy"); Check(!historyButton.Enabled, "Loading or processing disables the history shortcut");
            Invoke("OpenHistory"); Invoke("EndBusy");
            Check(historyButton.Enabled && Application.OpenForms.Count == 1, "Busy direct dispatch cannot open a window; idle restores history");
            CurrentUser.SignIn(912, "Restricted fixture", "Operator", false, Array.Empty<string>());
            Invoke("ApplyState"); Check(!historyButton.Enabled, "History follows the current account's permission");
            Invoke("OpenHistory");
            Check(Application.OpenForms.Count == 1, "Direct history dispatch cannot bypass revoked access");
            CurrentUser.SignIn(911, "Promotion fixture", "Operator", false, new[] { "Enrollment.Manage" }); Invoke("ApplyState");

            using var dialog = (ApplicationWindowForm)promotionType.GetMethod("CreateHistoryDialog", flags)!.Invoke(promotion, null)!;
            var originalHistory = (PromotionHistoryPage)dialog.Controls[0];
            var initial = ((int Year, int Class, int Section))typeof(PromotionHistoryPage).GetField("initialFilters", flags)!.GetValue(originalHistory)!;
            Check(initial == (103, 606, 17), "The window snapshots source year, class and section, not destination filters");
            Check(dialog.Text == "Promotion History" && !dialog.ShowInTaskbar && originalHistory.Dock == DockStyle.Fill,
                "The shared responsive window owns a full-sized history page");
            // Substitute only the reader in this native fixture; never show a production SQL reader.
            dialog.Controls.Remove(originalHistory); originalHistory.Dispose();
            var reads = new List<SqlParameter[]>();
            Func<string, SqlParameter[], DataTable> query = (procedure, parameters) => {
                if (procedure == "GetAcademicYears") return years;
                if (procedure == "GetClasses") return classes;
                if (procedure == "GetSections") return sections;
                Check(procedure == "GetPromotionBatchesFiltered", "History retains its production read-only procedure contract");
                reads.Add(parameters);
                var batches = new DataTable(); batches.Columns.Add("PromotionBatchId", typeof(int));
                batches.Columns.Add("PromotedCount", typeof(int)); batches.Rows.Add(201, 2); return batches;
            };
            PromotionHistoryPage History(int year, int cls, int section) => (PromotionHistoryPage)Activator.CreateInstance(
                typeof(PromotionHistoryPage), flags, null, new object[] { year, cls, section, query }, null)!;
            using var history = History(initial.Year, initial.Class, initial.Section); dialog.Controls.Add(history);
            dialog.Show(parent);
            PumpUntil(() => reads.Count == 1, "The history window loads its carried source filters");
            Check(reads[0].Select(p => p.ParameterName).SequenceEqual(new[] { "@SourceAcademicYearId", "@SourceClassId", "@SourceSectionId" }) &&
                reads[0].Select(p => Convert.ToInt32(p.Value)).SequenceEqual(new[] { 103, 606, 17 }),
                "The first history query uses the selected source IDs");
            var historyType = typeof(PromotionHistoryPage);
            T HistoryField<T>(string name) => (T)historyType.GetField(name, flags)!.GetValue(history)!;
            var export = HistoryField<ModernButton>("exportButton");
            Check(export.Enabled && HistoryField<Label>("summary").Text.Contains("1 batch"), "Loaded history retains summary and export availability");
            HistoryField<LookupBox>("year").SelectedValue = 104;
            Check(!export.Enabled && reads.Count == 1, "Changing history filters requires a fresh Load before export");
            historyType.GetMethod("LoadData", flags)!.Invoke(history, null);
            Check(export.Enabled && Convert.ToInt32(reads.Last()[0].Value) == 104, "Users can load a different year within the history window");
            historyType.GetMethod("ClearForm", flags)!.Invoke(history, null);
            Check(!export.Enabled && HistoryField<Label>("summary").Text.Length == 0, "Clear removes the displayed history and disables export");
            historyType.GetMethod("LoadData", flags)!.Invoke(history, null);
            Check(reads.Last().All(p => p.Value == DBNull.Value), "Blank history filters still support viewing all completed promotions");
            dialog.Close();
            Check(ReferenceEquals(promotionType.GetField("candidateData", flags)!.GetValue(promotion), candidates) &&
                (bool)candidates.Rows[0]["Selected"] && Field<ModernTextBox>("remarks").Text == "Retain this promotion draft" &&
                Field<LookupBox>("sourceYear").SelectedId == 103,
                "Closing history preserves promotion selections, filters and remarks");
            Check(history.IsDisposed && !promotion.IsDisposed, "The closed history window releases its page without disposing promotion");

            using var missing = History(999, 606, 17);
            int before = reads.Count; parent.Controls.Add(missing); missing.BringToFront();
            PumpUntil(() => ((LookupBox)historyType.GetField("year", flags)!.GetValue(missing)!).Items.Count > 0,
                "Stale carried filters bind the available lookup choices");
            Check(reads.Count == before && !(bool)((ModernButton)historyType.GetField("exportButton", flags)!.GetValue(missing)!).Enabled,
                "A missing carried filter refuses an accidental unfiltered history load");
            Check(((Label)typeof(V2Page).GetField("StatusLabel", flags)!.GetValue(missing)!).Text.Contains("no longer available"),
                "Unavailable carried filters give a clear recovery instruction");
            parent.Close();
        }
        finally { CurrentUser.SignIn(previous.UserId, previous.Username, previous.RoleName, previous.MustChangePassword, previous.Item5); }
    }
}
