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
            AppTheme.SetTheme((AppThemeType)999);
            Check(AppTheme.CurrentTheme == AppThemeType.OceanBlue, "Unknown saved/future theme values fall back to a supported appearance");
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
            CheckInformationDialogs(form);
            CheckPhotoWriteGuard();
            CheckSignInGuard();
            Console.WriteLine($"PASS: {checks} Windows theme switching, structure, rendering and resource checks.");
        }
        finally { AppTheme.SetTheme(originalTheme); }
    }

    private static void PumpUntil(Func<bool> completed, string message)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!completed() && deadline.Elapsed < TimeSpan.FromSeconds(5))
        { Application.DoEvents(); Thread.Yield(); }
        Check(completed(), message);
    }

    private static void CheckSignInGuard()
    {
        int attempts = 0;
        using var release = new ManualResetEventSlim();
        Func<string, string, (bool Success, string Message, Exception? ConnectionFailure)> attempt = (username, password) => {
            Interlocked.Increment(ref attempts);
            Check(username == "operator" && password == "captured password", "Sign-in uses the captured credentials");
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Fake sign-in was not released");
            return (true, "", null);
        };
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var login = (Form)Activator.CreateInstance(typeof(FMSC_SMS.Pages.Security.LoginForm), flags, null, new object[] { attempt }, null)!;
        var type = login.GetType();
        var username = (ModernTextBox)type.GetField("user", flags)!.GetValue(login)!;
        var password = (ModernTextBox)type.GetField("pass", flags)!.GetValue(login)!;
        var button = (ModernButton)type.GetField("login", flags)!.GetValue(login)!;
        username.Text = "operator"; password.Text = "captured password";
        var click = type.GetMethod("Login_Click", flags)!;
        click.Invoke(login, new object?[] { null, EventArgs.Empty });
        PumpUntil(() => Volatile.Read(ref attempts) == 1, "Sign-in starts on the worker thread");
        Check(!button.Enabled && !username.Enabled && !password.Enabled, "One sign-in holds its credentials and action");
        click.Invoke(login, new object?[] { null, EventArgs.Empty });
        Check(attempts == 1, "Repeated sign-in gestures must not start another authentication");
        var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
        typeof(Form).GetMethod("OnFormClosing", flags)!.Invoke(login, new object[] { closing });
        Check(closing.Cancel, "Closing is blocked while authentication is in progress");
        release.Set();
        PumpUntil(() => button.Enabled, "Sign-in completion restores the form state");
        Check(login.DialogResult == DialogResult.OK && username.Enabled && password.Enabled, "A successful sign-in closes with restored field state");
    }

    private static void CheckPhotoWriteGuard()
    {
        // Unshown fixture: the deferred student-cache loader never starts.
        FMSC_SMS.Pages.Security.CurrentUser.SignIn(999, "Fixture", "Administrator", false, Array.Empty<string>());
        try
        {
            using var photo = new FMSC_SMS.Pages.Students.Profile.StudentPhotoPage();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = photo.GetType();
            var student = (StudentPicker)type.GetField("student", flags)!.GetValue(photo)!;
            var students = new System.Data.DataTable();
            students.Columns.Add("AdmissionId", typeof(int)); students.Columns.Add("AdmissionNumber", typeof(string));
            students.Columns.Add("StudentName", typeof(string)); students.Columns.Add("FatherName", typeof(string));
            students.Rows.Add(1, "1", "Student", "Parent");
            student.SetStudent(students.Rows[0]);
            type.GetField("_pendingPhoto", flags)!.SetValue(photo, new byte[] { 1, 2, 3 });
            type.GetMethod("UpdateActionAvailability", flags)!.Invoke(photo, null);
            Check(photo.HasUnsavedChanges && !student.Enabled, "A photo draft holds its student and participates in navigation checks");
            int writes = 0;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<Task> write = () => { writes++; return gate.Task; };
            var run = type.GetMethod("RunPhotoWriteAsync", flags)!;
            var first = (Task)run.Invoke(photo, new object[] { write })!;
            Check(photo.IsBusy && !((Control)type.GetField("clearButton", flags)!.GetValue(photo)!).Enabled, "A photo update blocks leaving and Clear");
            var duplicate = (Task)run.Invoke(photo, new object[] { write })!;
            Check(duplicate.IsCompletedSuccessfully && writes == 1, "Photo updates cannot overlap");
            gate.SetResult();
            PumpUntil(() => first.IsCompleted, "Photo write guard finishes without SQL");
            first.GetAwaiter().GetResult();
            Check(!photo.IsBusy && photo.HasUnsavedChanges && !student.Enabled, "An unresolved photo draft remains protected after the operation");
            Check(!photo.IsHandleCreated, "The photo fixture must never start its deferred SQL loader");
        }
        finally { FMSC_SMS.Pages.Security.CurrentUser.SignOut(); }
    }

    private static void CheckInformationDialogs(Form owner)
    {
        Assembly application = typeof(ApplicationWindowForm).Assembly;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var failureType = application.GetType("FMSC_SMS.Core.DatabaseCompatibilityFailure", true)!;
        var issueType = application.GetType("FMSC_SMS.Core.DatabaseCompatibilityIssue", true)!;
        var exceptionType = application.GetType("FMSC_SMS.Core.DatabaseCompatibilityException", true)!;
        var connectionType = application.GetType("FMSC_SMS.Core.DatabaseConnectionForm", true)!;
        using var connection = (Form)Activator.CreateInstance(connectionType, flags, null, new object[] { false }, null)!;
        var showFailure = connectionType.GetMethod("ShowFailure", flags)!;
        var setBusy = connectionType.GetMethod("SetBusy", flags)!;
        foreach (var (version, title) in new[] { ("1", "Database update required"), ("3", "Application update required") })
        {
            var failure = Activator.CreateInstance(failureType, new object?[] {
                Enum.Parse(issueType, "SchemaVersion"), "2", version, "diagnostic detail", null })!;
            var exception = Activator.CreateInstance(exceptionType, flags, null, new[] { failure }, null)!;
            showFailure.Invoke(connection, new[] { "diagnostic detail", exception });
            setBusy.Invoke(connection, new object[] { false });
            Check(((Label)connectionType.GetField("_status", flags)!.GetValue(connection)!).Text == title, "Connection dialog must display the correct compatibility direction");
            var message = (Label)connectionType.GetField("_message", flags)!.GetValue(connection)!;
            Check(message.Text.Contains($"version: {version}") && message.Text.Contains("application: 2"), "Versions must be visible in the main message without expanding diagnostics");
            Check(!((Control)connectionType.GetField("_restart", flags)!.GetValue(connection)!).Enabled, "Restart must be disabled for compatibility failures");
            Check(!((Control)connectionType.GetField("_repair", flags)!.GetValue(connection)!).Enabled, "Dependency repair must be disabled for compatibility failures");
            Check(((Control)connectionType.GetField("_reconnect", flags)!.GetValue(connection)!).Enabled, "Reconnect remains available after an administrator fixes compatibility");
        }
        Check(!connection.IsHandleCreated, "Unshown connection fixture must never run its automatic SQL connection");
        showFailure.Invoke(connection, new object?[] { "Connection unavailable", null });
        setBusy.Invoke(connection, new object[] { false });
        Check(((Control)connectionType.GetField("_restart", flags)!.GetValue(connection)!).Enabled == File.Exists(FMSC_SMS.DatabaseHelper.DatabaseFile), "Restart is available for a connection failure only when the school file exists");
        var recoveryType = application.GetType("FMSC_SMS.Core.DatabaseRecoveryException", true)!;
        var recovery = Activator.CreateInstance(recoveryType, flags, null,
            new object[] { "EngineBusy", "Close other database programs first.", "Engine diagnostic 123" }, null)!;
        showFailure.Invoke(connection, new[] { "Generic outer text", recovery });
        setBusy.Invoke(connection, new object[] { false });
        Check(((Label)connectionType.GetField("_message", flags)!.GetValue(connection)!).Text.Contains("Close other database programs first."), "The main connection message retains the engine's explanation");
        Check(((Control)connectionType.GetField("_details", flags)!.GetValue(connection)!).Text.Contains("Engine diagnostic 123"), "Connection details retain engine command output");
        Check(!((Control)connectionType.GetField("_repair", flags)!.GetValue(connection)!).Enabled, "Busy engines must not suggest reinstalling software");
        Check(connection.AutoScaleMode == AutoScaleMode.None, "Connection dialog uses the application's existing scale metrics");

        var aboutType = application.GetType("FMSC_SMS.Core.AboutForm", true)!;
        using var about = (Form)Activator.CreateInstance(aboutType, flags, null, Array.Empty<object>(), null)!;
        about.Show(owner); Application.DoEvents();
        Control[] descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(descendants(child))).ToArray();
        var labels = descendants(about).OfType<Label>().Select(label => label.Text).ToArray();
        Check(labels.Contains("Required database schema") && labels.Contains(FMSC_SMS.DatabaseHelper.RequiredDatabaseSchemaVersion), "About shows the application's required schema");
        Check(labels.Contains("Application version") && labels.Contains("Build"), "About distinguishes application version and build");
        Check(about.AcceptButton is not null && ReferenceEquals(about.AcceptButton, about.CancelButton), "Enter and Escape provide a clear About close action");
        Check(descendants(about).OfType<ModernButton>().Any(button => button.Text == "Copy details"), "Support details can be copied without a database read");
        about.Close();
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
