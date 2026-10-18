using System.Data;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckFamilyConfirmationLayout()
    {
        using var rows = new DataTable();
        rows.Columns.Add("Adm. No."); rows.Columns.Add("Student");
        rows.Columns.Add("Payment (Rs.)", typeof(decimal)); rows.Columns.Add("Discount (Rs.)", typeof(decimal));
        rows.Rows.Add("6589", "6589 - Fictional student s/o Fictional parent (this student)", 200m, 0m);
        rows.Rows.Add("6590", "Test sibling", 2800m, 0m);
        using var dialog = ModernMessageDialog.CreateDialog(null, new MessageDialogContent(
            "Record payment for 2 students?", "One family receipt covers this payment. Each child's fees stay under their own record.",
            new[] { ("Student", "6589 - Fictional student s/o Fictional parent"), ("Payment", "Rs. 3,000"),
                ("Discount", "Rs. 0"), ("Payment date", "18-Oct-2026 09:23 PM"), ("Method", "Cash") }, rows,
            "This records the payment immediately and is final. Check every amount before choosing Yes."),
            "Confirm Family Collection", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        dialog.Show(); Application.DoEvents();
        IEnumerable<Control> All(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(All(x)));
        var grid = All(dialog).OfType<DataGridView>().Single();
        Check(grid.Height >= grid.ColumnHeadersHeight + grid.Rows.Cast<DataGridViewRow>().Sum(r => r.Height), "Both wrapped student rows fit the grid");
        var viewport = All(dialog).Single(c => c.Name == "MessageViewport");
        Check(grid.RectangleToScreen(grid.ClientRectangle).Bottom <= viewport.RectangleToScreen(viewport.ClientRectangle).Bottom, "Grid fits above the decision buttons");
        Check(dialog.ClientSize.Height < 800, "Two-student confirmation avoids a tall empty dialog");
        Directory.CreateDirectory("artifacts/billing-ui");
        using var bitmap = new Bitmap(dialog.Width, dialog.Height); dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save("artifacts/billing-ui/family-confirmation.png");
    }
}
