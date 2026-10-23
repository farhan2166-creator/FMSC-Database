using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Admissions;

internal static partial class Program
{
    private static void CheckPlacementHistoryFlow()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(StudentPlacementCorrectionDialog);
        var history = new DataTable();
        foreach (string name in new[] { "EnrollmentId", "AcademicYearId", "ClassSectionId" }) history.Columns.Add(name, typeof(int));
        foreach (string name in new[] { "YearName", "ClassName", "SectionName", "Status" }) history.Columns.Add(name);
        foreach (string name in new[] { "EnrollmentDate", "LeavingDate", "BillingStartDate", "PlacementConfirmedAt" }) history.Columns.Add(name, typeof(DateTime));
        history.Rows.Add(1, 27, 3, "2025-2026", "Class 10", "F", "Left", new DateTime(2025, 4, 1), new DateTime(2026, 3, 31), new DateTime(2025, 4, 1), new DateTime(2025, 4, 1));
        history.Rows.Add(2, 28, 4, "2026-2027", "1st Year", "F", "Active", new DateTime(2026, 10, 23), DBNull.Value, new DateTime(2026, 10, 23), new DateTime(2026, 10, 23));
        Directory.CreateDirectory("artifacts/placement-history");
        using var browser = new StudentPlacementCorrectionDialog(0, "Placement preview", false);
        browser.StartPosition = FormStartPosition.Manual; browser.Location = new Point(-20000, -20000);
        Seed(browser, history);
        browser.Show(); Application.DoEvents();
        var list = Field<ListBox>(browser, "record");
        Check(list.Items.Count == 2 && Convert.ToInt32(list.SelectedValue) == 2, "History contains only existing records and selects the active placement.");
        Check(Field<ModernButton>(browser, "dates").Visible && !Field<ModernButton>(browser, "review").Visible && !Field<LookupBox>(browser, "year").Visible,
            "Confirmed placement shows date correction without entry fields.");
        Capture(browser, "confirmed");
        list.SelectedValue = 1;
        Check(Field<ModernTextBox>(browser, "impact").Text.Contains("31 Mar 2026"), "Selecting old history shows its recorded dates.");
        Check(browser.Text == "Placement History" && Field<ModernButton>(browser, "dates").Visible, "Switching history keeps the same screen and date action.");
        history.Rows[1]["PlacementConfirmedAt"] = DBNull.Value;
        list.SelectedValue = 2;
        Check(Field<ModernButton>(browser, "edit").Visible && !Field<ModernButton>(browser, "dates").Visible && !Field<ModernButton>(browser, "review").Visible,
            "A draft offers a separate review action without opening an inline editor.");
        Capture(browser, "draft");
        using (var draft = (StudentPlacementCorrectionDialog)Activator.CreateInstance(type, flags, null,
            new object?[] { 0, "Placement preview", (int?)2, false }, null)!)
        {
            draft.StartPosition = FormStartPosition.Manual; draft.Location = new Point(-20000, -20000);
            Seed(draft, history); draft.Show(); Application.DoEvents();
            Check(Field<LookupBox>(draft, "year").Visible && !Field<LookupBox>(draft, "year").Enabled &&
                Field<ModernButton>(draft, "review").Visible && !Field<ListBox>(draft, "record").Visible,
                "A separate draft editor preserves the recorded academic year and exposes review.");
            Capture(draft, "review-draft");
            draft.Close();
        }
        using var editor = (StudentPlacementCorrectionDialog)Activator.CreateInstance(type, flags, null,
            new object?[] { 0, "Placement preview", (int?)0, false }, null)!;
        editor.StartPosition = FormStartPosition.Manual; editor.Location = new Point(-20000, -20000);
        Seed(editor, history);
        editor.Show(); Application.DoEvents();
        Check(!Field<ListBox>(editor, "record").Visible && Field<LookupBox>(editor, "year").Enabled,
            "Missing history opens a separate form with academic-year selection.");
        Check(Field<ModernComboBox>(editor, "status").Text == "Left", "Missing history defaults to a closed record when an active placement exists.");
        Check(Field<ModernButton>(editor, "review").Visible && !Field<ModernButton>(editor, "addHistory").Visible && !Field<ModernButton>(editor, "apply").Enabled,
            "Missing history requires review before applying and cannot recursively open history entry.");
        Capture(editor, "missing-history");
        editor.Close();
        Seed(browser, history.Clone());
        Check(list.Items.Count == 0 && !Field<ModernButton>(browser, "dates").Visible && !Field<ModernButton>(browser, "edit").Visible,
            "Empty history offers no correction of a nonexistent record.");
        Check(Field<ModernTextBox>(browser, "impact").Text.Contains("No placement records"), "Empty history gives clear guidance.");
        browser.Close();

        T Field<T>(object instance, string name) => (T)type.GetField(name, flags)!.GetValue(instance)!;
        void Seed(object instance, DataTable data)
        {
            type.GetField("loading", flags)!.SetValue(instance, true);
            type.GetField("enrollments", flags)!.SetValue(instance, data);
            type.GetField("currentAcademicYear", flags)!.SetValue(instance, 28);
            var years = new DataTable(); years.Columns.Add("AcademicYearId", typeof(int)); years.Columns.Add("YearName");
            years.Rows.Add(27, "2025-2026"); years.Rows.Add(28, "2026-2027");
            Field<LookupBox>(instance, "year").Bind(years, "AcademicYearId", "YearName");
            type.GetMethod("BindHistory", flags)!.Invoke(instance, new object[] { -1 });
            type.GetField("loading", flags)!.SetValue(instance, false);
            type.GetMethod("LoadSelected", flags)!.Invoke(instance, null);
        }
        void Capture(Form form, string name)
        {
            foreach (var size in new[] { new Size(740, 600), new Size(850, 650) })
            {
                form.Size = size; form.PerformLayout(); Application.DoEvents();
                Check(!Descendants(form).OfType<TableLayoutPanel>().Single(panel => panel.AutoScroll).HorizontalScroll.Visible,
                    $"{name}: content fits without horizontal scrolling at {size.Width}px.");
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.GetFullPath($"artifacts/placement-history/{name}-{size.Width}.png"));
                foreach (var button in Descendants(form).OfType<ModernButton>().Where(button => button.Visible))
                {
                    var bounds = form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                    Check(form.ClientRectangle.Contains(bounds), $"{name}: {button.Text} fits at {size.Width}px ({bounds} within {form.ClientRectangle}).");
                }
            }
        }
    }
}
