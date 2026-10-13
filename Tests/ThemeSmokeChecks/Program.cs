using System.Reflection;
using FMSC_SMS.Core;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    [STAThread]
    private static void Main()
    {
        // Run on a disposable Windows test profile; no SQL/database is used.
        Application.EnableVisualStyles();
        var originalTheme = AppTheme.CurrentTheme;
        try
        {
            Check((int)AppThemeType.Amber == 6, "Existing persisted enum values must stay stable");
            Check(AppTheme.SelectableThemes.Contains(AppThemeType.ProfessionalSimple), "Simple theme must be selectable");
            AppTheme.SetTheme(AppThemeType.OceanBlue);
            using var form = new Form { ClientSize = new Size(720, 500), AutoScaleMode = AutoScaleMode.None };
            var card = new RoundedPanel { Bounds = new Rectangle(20, 20, 680, 460), CornerRadius = 12 };
            var field = new ModernTextBox { Bounds = new Rectangle(20, 20, 280, 42), Text = "Unsaved student details", IsInvalid = true };
            var choice = new ModernComboBox { Bounds = new Rectangle(20, 85, 280, 42) };
            choice.Items.AddRange(new object[] { "Class 1", "Class 2" }); choice.SelectedIndex = 1;
            var day = new ModernDatePicker { Bounds = new Rectangle(20, 150, 280, 42), Value = new DateTime(2026, 10, 13) };
            var student = new StudentPicker { Dock = DockStyle.None, Bounds = new Rectangle(20, 215, 280, 42) };
            var button = new ModernButton { Bounds = new Rectangle(20, 280, 200, 42), Text = "Save", TabIndex = 4 };
            var disabled = new ModernButton { Bounds = new Rectangle(240, 280, 200, 42), Text = "Disabled", Enabled = false };
            card.Controls.AddRange(new Control[] { field, choice, day, student, button, disabled });
            form.Controls.Add(card); form.Show(); Application.DoEvents();
            Control[] controls = { card, field, choice, day, student, button, disabled };
            var before = controls.Select(c => (c.Bounds, c.Padding, c.Margin, c.Font, c.TabIndex, c.Enabled, c.Visible)).ToArray();
            var focus = form.ActiveControl;
            var timerField = typeof(UiColorTransition).GetField("timer", BindingFlags.NonPublic | BindingFlags.Instance)!;
            using var motion = new UiColorTransition(button, button.BackColor, color => button.BackColor = color);
            // Start in a decorated theme; starting an animation may be disabled
            // by Windows accessibility settings. Either case must switch safely.
            motion.MoveTo(Color.Red);
            foreach (var theme in new[] { AppThemeType.ProfessionalSimple, AppThemeType.Emerald, AppThemeType.ProfessionalSimple, AppThemeType.OceanBlue })
            {
                AppTheme.SetTheme(theme); Application.DoEvents();
                for (int i = 0; i < controls.Length; i++)
                    Check(before[i] == (controls[i].Bounds, controls[i].Padding, controls[i].Margin, controls[i].Font, controls[i].TabIndex, controls[i].Enabled, controls[i].Visible), "Theme must preserve control structure and geometry");
                Check(field.Text == "Unsaved student details" && field.IsInvalid && choice.SelectedIndex == 1 && day.Value.Date == new DateTime(2026, 10, 13), "Theme must preserve draft values and validation state");
                Check(form.ActiveControl == focus, "Theme must preserve focus");
                Check(AppTheme.UseSimpleRendering == (theme == AppThemeType.ProfessionalSimple), "Rendering mode must follow theme selection");
                foreach (var control in controls)
                    Check(AppTheme.UseSimpleRendering ? control.Region is null : control.Region is not null, "Switching must release or restore rounded clipping regions");
                if (AppTheme.UseSimpleRendering)
                {
                    string preference = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FMSC_SMS", "theme.txt");
                    Check(File.ReadAllText(preference).Trim() == nameof(AppThemeType.ProfessionalSimple), "Simple choice must use the saved theme preference");
                    motion.MoveTo(AppTheme.PrimaryHover);
                    Check(timerField.GetValue(motion) is null && motion.Value == AppTheme.PrimaryHover, "Simple feedback must apply immediately without an animation timer");
                    Check(Contrast(AppTheme.Text, AppTheme.Surface) >= 4.5 && Contrast(Color.White, AppTheme.Primary) >= 4.5, "Simple text and primary actions need readable contrast");
                }
                using var image = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(image, form.ClientRectangle);
                Check(image.Width > 0, "Both themes must render without an exception");
            }
            AppTheme.SetTheme(AppThemeType.ProfessionalSimple);
            using (var flatShape = new UiPaintShape(new Rectangle(0, 0, 100, 30), 8))
                Check(typeof(UiPaintShape).GetField("path", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(flatShape) is null, "Flat shapes must avoid allocating a rounded path");
            using var menu = new StyledContextMenu(); menu.Items.Add("Save");
            Check(menu.Items.Count == 1, "Popup actions must remain available");
            using var students = new System.Data.DataTable();
            students.Columns.Add("AdmissionId", typeof(int)); students.Columns.Add("AdmissionNumber", typeof(string));
            students.Columns.Add("StudentName", typeof(string)); students.Columns.Add("FatherName", typeof(string));
            students.Rows.Add(1, "1", "Ali", "Parent One"); students.Rows.Add(2, "2", "Bilal", "Parent Two");
            student.SetSource(students); student.Focus();
            var input = student.Controls.OfType<TextBox>().Single();
            var debounceField = typeof(StudentPicker).GetField("_searchDebounce", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var keyHandler = typeof(StudentPicker).GetMethod("OnTextKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!;
            void Key(Keys key) => keyHandler.Invoke(student, new object[] { input, new KeyEventArgs(key) });
            input.Text = "1";
            var debounce = (System.Windows.Forms.Timer)debounceField.GetValue(student)!;
            Check(debounce.Enabled && student.SelectedId == 0, "Typing must debounce without committing a student");
            Key(Keys.Enter);
            Check(student.SelectedId == 1 && !debounce.Enabled, "Enter must flush the search immediately");
            input.Text = "2";
            Check(student.SelectedId == 0 && debounce.Enabled, "Editing must clear the prior identity and debounce the new term");
            Key(Keys.Enter);
            Check(student.SelectedId == 2, "Enter after editing must never commit the old highlighted student");
            input.Text = "1"; Key(Keys.Escape);
            Check(!debounce.Enabled && student.SelectedId == 0, "Escape must cancel a pending search");
            input.Text = "2"; input.Clear();
            Check(!debounce.Enabled && student.MatchCount == 0, "Clearing input must stop pending work");
            var output = new ModernTextBox { Bounds = new Rectangle(360, 85, 260, 42), ReadOnly = true, Text = "Calculated output", TabIndex = 1 };
            var tick = new CheckBox { Bounds = new Rectangle(360, 150, 220, 30), Text = "Active", TabIndex = 2 };
            card.Controls.AddRange(new Control[] { output, tick }); Application.DoEvents();
            int saves = 0; button.Click += (_, _) => saves++;
            field.Focus();
            Check(!MasterEditorFocusNavigator.FocusInside(output), "Entry focus must skip read-only outputs");
            Check(MasterEditorFocusNavigator.TryAdvance(field.Controls.OfType<TextBox>().Single(), new Control[] { field, output, tick }, button), "Enter must skip outputs and deliberate checkbox choices, then reach Save");
            Check(button.Focused && saves == 0, "Completing a field must focus Save without submitting it");
            button.PerformClick(); Check(saves == 1, "A deliberate Save action still runs");

            using (var workspace = new WorkspacePage("Fee Posting", "", ("Monthly", () => new Panel()), ("Additional", () => new Panel())))
            {
                var tabs = (TabControl)typeof(WorkspacePage).GetField("_tabs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(workspace)!;
                int updates = 0; workspace.CurrentSectionChanged += (_, _) => updates++;
                tabs.SelectedIndex = 1;
                Check(workspace.CurrentSection == "Additional" && updates == 1, "Workspace context must follow the selected section");
            }
            CheckSameRecordDraft();
            CheckChoiceFilters(form);
            Console.WriteLine($"PASS: {checks} Windows theme switching, structure, rendering and resource checks.");
        }
        finally { AppTheme.SetTheme(originalTheme); }
    }

    private static void CheckSameRecordDraft()
    {
        // Keep this editor unparented: its deferred SQL loader is never started.
        using var editor = new MasterPage("Subjects", "", "UnusedRead", "UnusedSave",
            new[] { new FieldSpec("SubjectName", "Subject") }, _ => Array.Empty<Microsoft.Data.SqlClient.SqlParameter>(), recordIdColumn: "SubjectId");
        Check(!editor.IsHandleCreated, "SQL-free editor fixture must remain unparented and unshown");
        var grid = (DataGridView)typeof(V2Page).GetField("Grid", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        grid.Columns.Clear(); grid.Columns.Add("SubjectId", "Id"); grid.Columns.Add("SubjectName", "Subject");
        grid.Rows.Add(7, "Maths");
        var load = typeof(MasterPage).GetMethod("LoadRow", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Check((bool)load.Invoke(editor, new object[] { grid.Rows[0] })!, "Fixture record must load");
        var controls = (Dictionary<string, Control>)typeof(MasterPage).GetField("_controls", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        controls["SubjectName"].Text = "Unsaved Maths";
        Check(editor.HasUnsavedChanges, "Draft must be recognized");
        Check((bool)load.Invoke(editor, new object[] { grid.Rows[0] })! && controls["SubjectName"].Text == "Unsaved Maths" && editor.HasUnsavedChanges,
            "Reopening the current record must preserve unsaved fields and baseline");
        typeof(MasterPage).GetMethod("CaptureEditorBaseline", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
        grid.Rows[0].Cells["SubjectName"].Value = "Refreshed Maths";
        Check((bool)load.Invoke(editor, new object[] { grid.Rows[0] })! && controls["SubjectName"].Text == "Refreshed Maths",
            "A clean editor must still reload refreshed values for the same record");
    }

    private static void CheckChoiceFilters(Form form)
    {
        var reportUi = typeof(AppTheme).Assembly.GetType("FMSC_SMS.Pages.Reports.ReportUi")!;
        using var filters = new Panel { Height = 50 };
        var method = new ModernComboBox { Width = 250 };
        method.Items.AddRange(new object[] { "All", "Cash", "Bank" }); method.SelectedIndex = 2;
        var view = new ModernComboBox { Left = 270, Width = 250 };
        view.Items.AddRange(new object[] { "Details", "Summary" }); view.SelectedIndex = 1;
        filters.Controls.AddRange(new Control[] { method, view });
        reportUi.GetMethod("RegisterChoiceFilter", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { method, 0 });
        using var grid = new DataGridView();
        var layoutMethod = reportUi.GetMethod("CompactLayout", new[] { typeof(Control), typeof(DataGridView), typeof(string), typeof(Control[]) })!;
        using var layout = (Control)layoutMethod.Invoke(null, new object[] { filters, grid, "Results", Array.Empty<Control>() })!;
        layout.Bounds = new Rectangle(20, 20, 680, 400); form.Controls.Add(layout); layout.BringToFront(); Application.DoEvents();
        IEnumerable<Control> Children(Control root) => root.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Children(child)));
        Check(Children(layout).OfType<Label>().Any(label => label.Text == "1 filter active"), "Payment method must appear in active-filter feedback");
        Children(layout).OfType<ModernButton>().Single(control => control.Text == "Clear Filters").PerformClick();
        Check(method.SelectedIndex == 0 && view.SelectedIndex == 1, "Clear Filters must reset actual filters while preserving report view choices");
        Check(Children(layout).OfType<Label>().Any(label => label.Text == "No optional filters"), "Counter must update after clearing the choice filter");
    }

    private static double Contrast(Color a, Color b)
    {
        static double Luminance(Color c)
        {
            static double Linear(byte value) { double v = value / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        }
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
}
