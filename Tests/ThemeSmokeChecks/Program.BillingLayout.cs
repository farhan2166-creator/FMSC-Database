using System.Data;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Billing;

internal static partial class Program
{
    private static void CheckBillingLayout()
    {
        IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(All(c)));
        string output = Path.GetFullPath("artifacts/billing-ui"); Directory.CreateDirectory(output);
        foreach (var (name, page) in new (string, V2Page)[] {
            ("status", new BillingStatusPage(null, () => Task.FromResult(new DataSet()), false)),
            ("manual", new FeePostingPage(false)),
            ("additional", new AdditionalFeePostingPage(null, false)),
            ("reversal", new FeePostingReversalPage(false)) })
        {
            using (page)
            using (var form = new Form { ClientSize = new Size(1500, 850), ShowInTaskbar = false })
            {
                page.PrepareForEmbedding(keepActions: true);
                page.Dock = DockStyle.Fill; form.Controls.Add(page);
                form.Show(); Application.DoEvents(); form.PerformLayout(); Application.DoEvents();
                var labels = All(page).OfType<Label>().ToArray();
                if (name == "status")
                {
                    var summary = (Label)typeof(BillingStatusPage).GetField("summary", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(page)!;
                    summary.Text = "October 2026: 0 charges pending · Rs. 0\n0 students need attention. Select a student to open setup or collection. Legacy references are excluded from automatic billing.";
                    foreach (int width in new[] { 1000, 1500 })
                    {
                        form.ClientSize = new Size(width, 850);
                        Application.DoEvents(); form.PerformLayout(); Application.DoEvents();
                        var view = (ModernComboBox)typeof(BillingStatusPage).GetField("view", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(page)!;
                        var scope = summary.Parent!;
                        var bounds = scope.RectangleToClient(view.RectangleToScreen(view.ClientRectangle));
                        Check(bounds.Bottom <= scope.ClientSize.Height - scope.Padding.Bottom, "Populated status dropdown fits its scope at width " + width);
                        var action = All(page).OfType<ModernButton>().First(b => b.Text == "Refresh");
                        Check(view.PointToScreen(new Point(0, view.Height)).Y < action.PointToScreen(Point.Empty).Y,
                            "Status actions do not overlap the dropdown at width " + width);
                    }
                }
                Check(labels.Any(l => l.Text == "Billing Review" || l.Text == "Posting Scope" || l.Text == "Reversal Scope"), name + " has a scope card");
                Check(labels.Any(l => l.Text == "Billing Results" || l.Text == "Monthly Fees" || l.Text == "Students to Charge" || l.Text == "Charges to Reverse"), name + " has a results card");
                var buttons = All(page).OfType<ModernButton>().Where(b => b.Visible).ToArray();
                Check(buttons.Length >= 3, name + " retains its workflow actions");
                if (name == "manual")
                {
                    var fields = All(page).OfType<TableLayoutPanel>().First(t => t.Controls.Count == 8);
                    Check(fields.ColumnCount == 4 && fields.RowCount == 2, "Manual posting uses two readable rows");
                }
                using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
                page.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(output, "billing-" + name + ".png"));
            }
        }
    }
}
