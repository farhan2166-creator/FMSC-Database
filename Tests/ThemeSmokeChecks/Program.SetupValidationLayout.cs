using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Setup;
using FMSC_SMS.Pages.Setup.Academics;

internal static partial class Program
{
    private static void CheckSetupValidationLayout()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Directory.CreateDirectory("artifacts/setup-validation");
        foreach (bool campus in new[] { true, false })
        // Campus now uses the full-width stacked editor: 710px can fit two
        // columns. Use 560px to exercise the actual one-column fallback too.
        foreach (int width in new[] { 560, 1700 })
        {
            using MasterPage page = campus ? new CampusesPage(false) : new AcademicYearsPage(false);
            var fields = (Dictionary<string, Control>)typeof(MasterPage).GetField("_controls", flags)!.GetValue(page)!;
            using var host = new Form { ClientSize = new Size(width, 760) };
            page.Dock = DockStyle.Fill;
            host.Controls.Add(page);
            host.Show();
            Application.DoEvents();
            var editor = (TableLayoutPanel)typeof(MasterPage).GetField("_editor", flags)!.GetValue(page)!;
            Check(editor.ColumnCount == (width == 560 ? 1 : 2), "Validation layout is checked with the expected one/two-column editor");
            if (campus)
            {
                fields["CampusName"].Text = "Test Campus";
                ((ModernComboBox)fields["CampusType"]).SelectedIndex = 0;
                fields["ContactNumber"].Text = "443333";
                fields["Email"].Text = "eee222";
            }
            else
            {
                fields["YearName"].Text = "4457777";
                ((ModernDatePicker)fields["StartDate"]).Value = new DateTime(2028, 10, 31);
                ((ModernDatePicker)fields["EndDate"]).Value = new DateTime(2027, 10, 31);
            }
            bool valid = (bool)page.GetType().GetMethod("CanSaveRecord", flags)!.Invoke(page, null)!;
            Check(!valid, "Malformed " + (campus ? "campus contacts" : "academic year/date range") + " refuses Save before SQL");
            Application.DoEvents();
            var labels = (Dictionary<Control, Label>)typeof(MasterPage).GetField("_editorValidationLabels", flags)!.GetValue(page)!;
            foreach (string key in campus ? new[] { "ContactNumber", "Email" } : new[] { "YearName", "EndDate" })
            {
                Label label = labels[fields[key]];
                Check(label.Visible && label.Text.Length > 0, key + " has a dedicated validation message");
                var panel = (TableLayoutPanel)label.Parent!;
                Check(panel.GetRow(label) == 1 && panel.GetColumnSpan(label) == 3, key + " message uses the full row beneath the input");
                var measured = TextRenderer.MeasureText(label.Text, label.Font, new Size(label.Width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                Check(measured.Height <= label.Height, key + " message fits its reserved height");
            }
            using var image = new Bitmap(host.ClientSize.Width, host.ClientSize.Height);
            host.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save("artifacts/setup-validation/" + (campus ? "campus" : "academic-year") + "-" + width + ".png");
            if (!campus)
            {
                fields["YearName"].Text = "2027-2028";
                ((ModernDatePicker)fields["StartDate"]).Value = new DateTime(2027, 4, 1);
                ((ModernDatePicker)fields["EndDate"]).Value = new DateTime(2028, 3, 31);
                Check((bool)page.GetType().GetMethod("CanSaveRecord", flags)!.Invoke(page, null)!, "Correcting the name and date order permits validation");
            }
        }
    }
}
