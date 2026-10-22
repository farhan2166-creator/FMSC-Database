using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Admissions;

internal static partial class Program
{
    private static void CheckAdmissionHeader()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var page = new StudentAdmissionPage();
        var type = typeof(StudentAdmissionPage);
        T Field<T>(string name) => (T)type.GetField(name, flags)!.GetValue(page)!;
        Field<ModernTextBox>("registrationNo").Text = "FMSC-2026-0018";
        Field<ModernTextBox>("admissionIdValue").Text = "6592";
        var badge = Field<Label>("studentStatusBadge");
        badge.Text = "Active";
        badge.ForeColor = AppTheme.StatusText(Color.Green);
        badge.BackColor = AppTheme.Tint(Color.Green, .88f);
        var row = (TableLayoutPanel)badge.Parent!.Parent!;
        var header = (Panel)row.Parent!;
        var title = row.Controls.OfType<Label>().Single();
        using var reference = HumanUi.Card("Admission / Placement", null, null, new Panel());
        var referenceTitle = reference.Header.Controls.OfType<Label>().Single();
        Check(title.Font.Equals(referenceTitle.Font) && header.Height == reference.Header.Height &&
            header.Padding == reference.Header.Padding && header.BackColor == reference.Header.BackColor,
            "Admission identity keeps the standard card header's font, height, padding and background");
        Check(header.Controls.OfType<Panel>().Any(control => control.Dock == DockStyle.Left && control.Width == 3),
            "Admission header retains the shared accent stripe");
        using var host = new Form { AutoScaleMode = AutoScaleMode.None, ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        host.Controls.Add(header);
        host.Show();
        foreach (int width in new[] { 1040, 1576 })
        {
            host.ClientSize = new Size(width, header.Height);
            host.PerformLayout(); header.PerformLayout(); row.PerformLayout();
            Application.DoEvents();
            Check(row.GetColumnWidths().All(value => value > 0), "Identity columns fit at width " + width);
            Check(title.Bounds.Right <= badge.Parent.Bounds.Left &&
                row.GetControlFromPosition(3, 0)!.Bounds.Right <= row.GetControlFromPosition(4, 0)!.Bounds.Left,
                "Title, status and identifiers do not overlap at width " + width);
        }
        Directory.CreateDirectory("artifacts");
        using var bitmap = new Bitmap(header.Width, header.Height);
        header.DrawToBitmap(bitmap, header.ClientRectangle);
        bitmap.Save(Path.GetFullPath("artifacts/admission-header.png"));
        Check(!page.IsHandleCreated, "Header preview never starts the admission SQL loader");
        host.Close();
    }
}
