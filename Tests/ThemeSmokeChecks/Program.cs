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
            Console.WriteLine($"PASS: {checks} Windows theme switching, structure, rendering and resource checks.");
        }
        finally { AppTheme.SetTheme(originalTheme); }
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
