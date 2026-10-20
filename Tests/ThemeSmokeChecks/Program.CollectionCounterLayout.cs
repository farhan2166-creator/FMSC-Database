using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Collection;

internal static partial class Program
{
    private static void CheckCollectionCounterLayout()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Directory.CreateDirectory("artifacts/collection-counter-layout");
        foreach (int[] selected in new[] { new[] { 41 }, new[] { 41, 42 } })
        {
            using var page = new FeeCollectionPage(false);
            using var owner = new ApplicationWindowForm {
                ClientSize = new Size(1400, 850), StartPosition = FormStartPosition.Manual,
                Location = new Point(-30000, -30000), ShowInTaskbar = false
            };
            T Field<T>(string name) => (T)typeof(FeeCollectionPage).GetField(name, flags)!.GetValue(page)!;
            page.Dock = DockStyle.Fill; owner.Controls.Add(page);
            if(selected.Length > 1) page.ConfigureCounter(selected); // Real counter opening order.
            owner.Show(); Application.DoEvents();
            var next = Field<ModernButton>("nextStudentButton");
            var summary = (TableLayoutPanel)next.Parent!;
            var scroll = (Panel)summary.Parent!;
            int row = summary.GetRow(next);
            int normalHeight = summary.Height;
            float reserved = summary.RowStyles[row].Height;
            if(selected.Length == 1)
            {
                Check(next.Visible && reserved > 0, "Ordinary collection retains Next Student and its action row");
                page.ConfigureCounter(selected); Application.DoEvents();
                Check(normalHeight - summary.Height >= reserved, "Changing to counter mode releases the hidden action's full height");
            }
            Check(!next.Visible && summary.RowStyles[row].Height == 0,
                "The family payment dialog reserves no hidden Next Student row, including on first opening");
            using (var picture = new Bitmap(owner.Width, owner.Height))
            {
                owner.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                picture.Save($"artifacts/collection-counter-layout/payment-{selected.Length}.png");
            }
            Check(!scroll.VerticalScroll.Visible && summary.Bottom <= scroll.ClientSize.Height,
                "Payment fields and receipt actions fit the usual family dialog without an unnecessary scrollbar");
            foreach(string name in new[]{"discountAmount","paymentAmount","method","reference","remarks"})
            {
                var input=Field<Control>(name);
                Check(input.Height>=AppMetrics.InputControlHeight && input.Bottom<=input.Parent!.ClientSize.Height,
                    "Compact counter spacing preserves full-height payment inputs without clipping");
            }
            scroll.Height = 260;
            // Test the payment panel's own small viewport independently of the
            // dialog's monitor-fit wrapper, which can preserve a larger page.
            scroll.Dock = DockStyle.None; scroll.Size = new Size(scroll.Width, 260);
            scroll.PerformLayout(); Application.DoEvents();
            Check(scroll.VerticalScroll.Visible, "Smaller payment viewports still scroll when the actual fields need room");
            var receipt = Field<ModernButton>("printReceiptButton");
            scroll.ScrollControlIntoView(receipt); Application.DoEvents();
            Rectangle visible = scroll.RectangleToClient(receipt.RectangleToScreen(receipt.ClientRectangle));
            Check(visible.Bottom <= scroll.ClientSize.Height && visible.Top >= 0,
                "The receipt action remains reachable when scrolling is necessary");
        }
    }
}
