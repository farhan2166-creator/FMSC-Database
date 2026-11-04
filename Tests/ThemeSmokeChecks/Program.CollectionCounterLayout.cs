using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Collection;

internal static partial class Program
{
    private static void CheckCollectionCounterLayout()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        // The enrollment caption renders an official admission number. Supply a
        // fictional cache so programmatic binding never opens a school database.
        typeof(AdmissionNumbers).GetField("_numbers",BindingFlags.Static|BindingFlags.NonPublic)!
            .SetValue(null,new Dictionary<int,string>{{41,"41"},{42,"42"}});
        typeof(AdmissionNumbers).GetField("_loadedAt",BindingFlags.Static|BindingFlags.NonPublic)!
            .SetValue(null,DateTime.UtcNow);
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
            typeof(FeeCollectionPage).GetField("admissionId",flags)!.SetValue(page,41);
            typeof(FeeCollectionPage).GetField("enrollmentId",flags)!.SetValue(page,91);
            typeof(FeeCollectionPage).GetField("counterLoading",flags)!.SetValue(page,false);
            typeof(FeeCollectionPage).GetField("suppressPaymentEvents",flags)!.SetValue(page,true);
            // FocusEntryField intentionally requires a real outstanding charge.
            // Seed one before expecting Amount to become the preferred control.
            var charges=Field<DataGridView>("charges");
            var initialCharge=charges.Rows[charges.Rows.Add()];
            initialCharge.Cells["BillingChargeId"].Value=7001L;
            initialCharge.Cells["Balance"].Value=500m;
            var amount=Field<ModernTextBox>("paymentAmount");amount.Text="1250";
            Field<ModernTextBox>("reference").Focus();
            Check(page.FocusEntryField()&&amount.ContainsFocus,
                "A loaded account opens at Amount for both ordinary navigation and the family counter.");
            Check(amount.Text=="1250"&&amount.SelectionLength==4,
                "Entry focus selects the existing Amount without changing its value.");
            var next = Field<ModernButton>("nextStudentButton");
            var summary = (TableLayoutPanel)next.Parent!;
            var scroll = (Panel)summary.Parent!;
            int row = summary.GetRow(next);
            int normalHeight = summary.Height;
            float reserved = summary.RowStyles[row].Height;
            if(selected.Length == 1)
            {
                Check(next.Visible && reserved > 0, "Ordinary collection retains Next Student and its action row");
                // Programmatic enrollment binding used to launch an independent
                // asynchronous reload, invalidating the just-selected student.
                var enrollment=Field<LookupBox>("enrollment");
                int beforeBind=Field<int>("loadVersion");
                var placements=new DataTable();
                placements.Columns.Add("EnrollmentId",typeof(int));
                placements.Columns.Add("Display",typeof(string));
                placements.Rows.Add(91,"Synthetic active placement");
                enrollment.Bind(placements,"EnrollmentId","Display");
                enrollment.SelectedValue=91;
                Check(Field<int>("loadVersion")==beforeBind,
                    "Binding and selecting the default enrollment do not launch competing charge loads");

                var picker=Field<StudentPicker>("student");
                picker.SetStudent(41,"41 - Synthetic student");
                var queueFocus=typeof(FeeCollectionPage).GetMethod("QueueLoadedAmountFocus",flags)!;
                Field<ModernTextBox>("reference").Focus();
                picker.Focus();
                queueFocus.Invoke(page,new object[]{41,true});
                Application.DoEvents();
                Check(amount.ContainsFocus && amount.SelectionLength==amount.Text.Length,
                    "Completed picker selection defers and selects Amount after the UI settles");

                picker.Focus();
                queueFocus.Invoke(page,new object[]{41,true});
                Field<ModernTextBox>("reference").Focus();
                Application.DoEvents();
                Check(Field<ModernTextBox>("reference").ContainsFocus && !amount.ContainsFocus,
                    "Deferred student focus does not steal an operator's new entry-field focus");

                picker.Focus();
                queueFocus.Invoke(page,new object[]{41,true});
                picker.SetStudent(42,"42 - Different synthetic student");
                Application.DoEvents();
                Check(!amount.ContainsFocus,
                    "A queued focus request for a superseded student is ignored");
                picker.SetStudent(41,"41 - Synthetic student");
                charges.Rows.Clear();
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
                $"Payment fields fit the family counter (scroll={scroll.VerticalScroll.Visible}, summaryBottom={summary.Bottom}, viewport={scroll.ClientSize.Height}, selected={selected.Length})");
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
