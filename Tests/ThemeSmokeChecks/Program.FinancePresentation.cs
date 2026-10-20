using System.Data;
using System.Reflection;
using FMSC_SMS;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Collection;

internal static partial class Program
{
    private static void CheckFinancePresentation()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string output = Path.GetFullPath("artifacts/finance-presentation");
        Directory.CreateDirectory(output);
        T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, flags)!.GetValue(instance)!;
        void Snapshot(Form form, string name)
        {
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, name + ".png"));
        }
        var creditType = typeof(FeeCollectionPage).Assembly.GetType("FMSC_SMS.Pages.Finance.Collection.StudentCreditDialog")!;
        foreach(float scale in new[]{1f, 1.25f, 1.5f})
        {
            using var dialog = (Form)Activator.CreateInstance(creditType, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[]{41, false}, null)!;
            dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-30000,-30000); dialog.ShowInTaskbar = false;
            dialog.Show(); Application.DoEvents();
            var action = Field<ModernComboBox>(dialog, "action");
            var sibling = Field<ModernComboBox>(dialog, "sibling");
            var reason = Field<ModernTextBox>(dialog, "reason");
            var amount = Field<ModernTextBox>(dialog, "amount");
            var history = Field<DataGridView>(dialog, "history");
            var available = Field<Label>(dialog, "creditBalance");
            available.Text = "Available advance: Rs. 12,345";
            Field<Label>(dialog,"balance").Text = "41 · Example Student\nA plus amount adds to the advance balance; a minus amount reduces it. Transfers move advance money between siblings without receiving new money.";
            sibling.Items.Add("Example Sibling"); amount.Text="250"; reason.Text="Keep this draft";
            if(scale!=1f)dialog.Scale(new SizeF(scale,scale));
            foreach(var size in new[]{new Size(850,520),new Size(1050,700)})
            {
                dialog.ClientSize=new Size((int)(size.Width*scale),(int)(size.Height*scale));Application.DoEvents();
                action.SelectedItem="Deposit";Application.DoEvents();
                Check(!sibling.Visible && !sibling.Enabled && reason.Parent!.Width>amount.Parent!.Width,
                    "Deposits hide the recipient while giving the reason the full row");
                action.SelectedItem="Transfer";Application.DoEvents();
                Check(sibling.Visible && sibling.Enabled && sibling.SelectedIndex==0,
                    "Transfer retains the original enabled recipient and default selection");
                Check(reason.Parent!.Left>=sibling.Parent!.Right,"Transfer fields occupy separate columns without overlap");
                action.SelectedItem="Refund";Application.DoEvents();
                Check(!sibling.Visible && !sibling.Enabled && sibling.SelectedIndex==-1,
                    "Refunds retain the original recipient clearing behavior");
                Check(amount.Text=="250" && reason.Text=="Keep this draft","Changing action preserves entered amount and reason");
                Check(history.Height>100*scale,"Credit history retains usable vertical space at smaller sizes");
                var save=Field<ModernButton>(dialog,"save");
                // ApplicationWindowForm deliberately preserves the original readable
                // content size in a scroll viewport when the window gets smaller.
                for(Control? parent=save.Parent;parent is not null;parent=parent.Parent)
                    if(parent is ScrollableControl{AutoScroll:true} viewport)viewport.ScrollControlIntoView(save);
                Application.DoEvents();
                Snapshot(dialog,$"credit-layout-{scale:0.##}-{size.Width}");
                Check(save.Visible && save.RectangleToScreen(save.ClientRectangle).Bottom<=dialog.RectangleToScreen(dialog.ClientRectangle).Bottom,
                    "Credit actions remain reachable through the established smaller-window scroll viewport");
                Check(available.ForeColor==AppTheme.Success && available.Font.Bold,"Available credit is visually distinct");
            }
            Snapshot(dialog,$"student-credit-{scale:0.##}");
        }

        // Use the existing modal opening/closing path; never click a financial action.
        using var page = new FeeCollectionPage(false);
        var type=typeof(FeeCollectionPage);
        typeof(AdmissionNumbers).GetField("_numbers",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,new Dictionary<int,string>{{41,"41"}});
        typeof(AdmissionNumbers).GetField("_loadedAt",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,DateTime.UtcNow);
        type.GetField("admissionId",flags)!.SetValue(page,41);type.GetField("enrollmentId",flags)!.SetValue(page,1);
        var grid=Field<DataGridView>(page,"recentPayments");
        int Add(string reference,decimal amount,decimal adjusted,decimal net,decimal? family,bool printable)
        {
            int index=grid.Rows.Add("19-Oct-2026",reference,"Fees","Payment",amount,"Cash",1);
            grid.Rows[index].Cells["Adjusted"].Value=adjusted;grid.Rows[index].Cells["NetPaid"].Value=net;
            grid.Rows[index].Cells["ReceiptTotal"].Value=family;grid.Rows[index].Tag=printable;
            return index;
        }
        int mixed=Add("FR-2026-00001",400,0,400,3000,true);
        grid.Rows[mixed].Cells["Amount"].ToolTipText="New money applied: Rs. 100; credit used: Rs. 300.";
        int corrected=Add("R-2026-00002",1000,200,800,null,true);
        grid.Rows[corrected].Cells["Adjusted"].ToolTipText="Refunds: Rs. 150; Corrections: Rs. 50";
        int reversed=Add("R-2026-00003",500,500,0,null,false);
        var before=grid.Rows.Cast<DataGridViewRow>().Select(r=>r.Cells.Cast<DataGridViewCell>().Select(c=>c.Value).ToArray()).ToArray();
        Exception? failure=null; int passes=0;
        using var timer=new System.Windows.Forms.Timer{Interval=150};
        timer.Tick+=(_,_)=>{
            timer.Stop();var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Text.StartsWith("Recent Payments —"));
            if(dialog is null){failure=new Exception("Recent Payments dialog did not open");return;}
            try
            {
                var details=Field<Label>(page,"paymentHistoryDetails");
                var reprint=Field<ModernButton>(page,"reprintReceiptButton");
                foreach(var size in new[]{new Size(1180,520),new Size(720,420)})
                {
                    dialog.ClientSize=size;Application.DoEvents();grid.CurrentCell=grid.Rows[mixed].Cells["ReceiptNumber"];
                    grid.ClearSelection();grid.Rows[mixed].Selected=true;Application.DoEvents();
                    Check(details.Text.Contains("credit used: Rs. 300") && details.Text.Contains("3,000") && reprint.Enabled,
                        "A mixed family payment visibly distinguishes student credit from whole-family total and remains printable");
                    Check(details.RectangleToScreen(details.ClientRectangle).Bottom<=reprint.Parent!.RectangleToScreen(reprint.Parent.ClientRectangle).Top && grid.Height>80,"Details, grid and actions fit without overlapping");
                    grid.CurrentCell=grid.Rows[corrected].Cells["ReceiptNumber"];
                    grid.ClearSelection();grid.Rows[corrected].Selected=true;Application.DoEvents();
                    Check(details.Text.Contains("Refunds: Rs. 150") && details.Text.Contains("Corrections: Rs. 50") && details.Text.Contains("800") && !details.Text.Contains("Whole family"),
                        $"Individual adjustments are visible without an unrelated family total (row {grid.CurrentRow?.Index}): {details.Text}");
                    grid.CurrentCell=grid.Rows[reversed].Cells["ReceiptNumber"];
                    grid.ClearSelection();grid.Rows[reversed].Selected=true;Application.DoEvents();
                    Check(!reprint.Enabled,"Reversed rows retain their established reprint restriction");
                    for(Control? parent=reprint.Parent;parent is not null;parent=parent.Parent)
                        if(parent is ScrollableControl{AutoScroll:true} viewport)viewport.ScrollControlIntoView(reprint);
                    Application.DoEvents();
                    Check(reprint.RectangleToScreen(reprint.ClientRectangle).Bottom<=dialog.RectangleToScreen(dialog.ClientRectangle).Bottom,
                        "Receipt actions remain reachable through the original dialog viewport");
                    Snapshot(dialog,$"recent-payments-{size.Width}");
                }
                grid.Rows.Clear();Application.DoEvents();
                Check(!reprint.Enabled && details.Text.StartsWith("Select a payment"),"Cleared history clears details and reprint eligibility");
                passes++;
            }
            catch(Exception ex){failure=ex;}
            finally{dialog.Close();}
        };
        // Capture grid preservation before clearing it in the modal test.
        using var detailsOnly=new Label();type.GetField("paymentHistoryDetails",flags)!.SetValue(page,detailsOnly);
        grid.CurrentCell=grid.Rows[mixed].Cells["ReceiptNumber"];
        type.GetMethod("UpdatePaymentHistoryDetails",flags)!.Invoke(page,null);
        Check(grid.Rows.Cast<DataGridViewRow>().Select((r,i)=>r.Cells.Cast<DataGridViewCell>().Select(c=>c.Value).SequenceEqual(before[i])).All(s=>s),
            "Showing receipt details never modifies financial grid values");
        type.GetField("paymentHistoryDetails",flags)!.SetValue(page,null);
        timer.Start();type.GetMethod("OpenPaymentHistory",flags)!.Invoke(page,null);
        if(failure is not null)throw failure;
        Check(passes==1 && !grid.IsDisposed && Field<Label?>(page,"paymentHistoryDetails") is null &&
            Field<ModernButton?>(page,"reprintReceiptButton") is null,"Closing the history dialog preserves the page-owned grid and releases dialog-only references");

        // Do not show this page: its deferred database load must remain uninvoked.
        using var closing=new DailyCollectionClosingPage();
        using var closingData=new DataTable();closingData.Columns.Add("ClosingStatus");closingData.Columns.Add("Revision",typeof(int));
        var state=closingData.Rows.Add("Submitted",2);
        var closingType=typeof(DailyCollectionClosingPage);
        closingType.GetField("state",flags)!.SetValue(closing,state);
        closingType.GetField("loadedDay",flags)!.SetValue(closing,Field<ModernDatePicker>(closing,"day").Value.Date);
        void RefreshClosing()=>closingType.GetMethod("UpdateActions",flags)!.Invoke(closing,null);
        RefreshClosing();
        Check(Field<Label>(closing,"closingState").Text=="Submitted · Revision 2" && Field<ModernButton>(closing,"approve").Enabled,
            "Closing badge reflects the existing submitted state without changing approval eligibility");
        closingType.GetField("dirty",flags)!.SetValue(closing,true);RefreshClosing();
        Check(Field<Label>(closing,"closingState").Text=="Unsubmitted changes" && !Field<ModernButton>(closing,"approve").Enabled && !Field<ModernButton>(closing,"print").Enabled,
            "Unsubmitted edits retain existing approval and printing restrictions");
        closingType.GetField("dirty",flags)!.SetValue(closing,false);state["ClosingStatus"]="Approved";RefreshClosing();
        Check(Field<Label>(closing,"closingState").ForeColor==AppTheme.Success && Field<ModernButton>(closing,"print").Enabled,
            "Approved badge uses the loaded status and preserves print eligibility");
        Field<ModernDatePicker>(closing,"day").Value=Field<ModernDatePicker>(closing,"day").Value.AddDays(-1);
        Check(Field<Label>(closing,"closingState").Text=="Load selected day" && !Field<ModernButton>(closing,"submit").Enabled && !Field<ModernButton>(closing,"print").Enabled,
            "Changing the day still requires loading that day before financial actions");
    }
}
