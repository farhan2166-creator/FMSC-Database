using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Collection;

internal static partial class Program
{
    private static void CheckCollectionCredit()
    {
        Check(FMSC_SMS.DatabaseHelper.RequiredDatabaseSchemaVersion == "24", "Application expects the installed credit migration");
        using var page = new FeeCollectionPage(false);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        // Family summaries resolve official numbers; keep this UI check independent of SQL.
        typeof(AdmissionNumbers).GetField("_numbers", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, new Dictionary<int,string> { [6594]="6590", [6589]="6589" });
        typeof(AdmissionNumbers).GetField("_loadedAt", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, DateTime.UtcNow);
        var type = typeof(FeeCollectionPage);
        object Field(string name) => type.GetField(name, flags)!.GetValue(page)!;
        type.GetField("admissionId", flags)!.SetValue(page, 6594);
        type.GetField("enrollmentId", flags)!.SetValue(page, 732);
        type.GetField("suppressDiscountEvents", flags)!.SetValue(page, true);
        type.GetField("suppressPaymentEvents", flags)!.SetValue(page, true);
        var grid = (DataGridView)Field("charges");
        var credit = (Dictionary<int, decimal>)Field("availableCreditByStudent");
        var use = (CheckBox)Field("useAvailableCredit");
        var discount = (ModernTextBox)Field("discountAmount");
        void Bill(long id, int owner, decimal balance)
        {
            int index = grid.Rows.Add();
            var row = grid.Rows[index];
            row.Cells["BillingChargeId"].Value = id;
            row.Cells["BillingMonth"].Tag = DateTime.Today.AddMonths(1); // already posted future bill
            row.Cells["AdmissionId"].Value = owner;
            row.Cells["EnrollmentId"].Value = 732;
            row.Cells["Balance"].Value = balance;
        }
        decimal Payable() => (decimal)type.GetMethod("CurrentPayableAfterDiscount", flags)!.Invoke(page, null)!;
        Dictionary<long,decimal> Plan() => (Dictionary<long,decimal>)type.GetMethod("GetCreditAllocations", flags)!.Invoke(page, new object?[] { null })!;
        Bill(1,6594,400); Bill(2,6594,2500); Bill(3,6589,400);
        credit[6594]=8750; credit[6589]=100;
        Check(use.Checked, "Credit use is checked by default");
        var plan=Plan();
        Check(plan[1]==400 && plan[2]==2500 && plan[3]==100, "Each child uses only their own credit, including future posted bills");
        Check(Payable()==300, "Amount to pay excludes available credit without crossing sibling accounts");
        use.Checked=false;
        Check(Payable()==3300 && Plan().Count==0, "Unticking credit restores the cash balance");
        use.Checked=true;
        discount.Text="2000";
        plan=Plan();
        Check(plan[2]==500 && Payable()==300, "Largest-fee discount is applied before credit");
        var received = (ModernTextBox)Field("paymentAmount");
        var collect = (Button)Field("executeButton");
        var validator = (FormValidator)Field("collectionValidator");
        void RefreshTotals() => type.GetMethod("UpdateCollectionTotals", flags)!.Invoke(page,null);
        received.Text="301"; RefreshTotals();
        Check(!collect.Enabled && validator.IsMarked(received), "Family cash above the balance after credit is rejected inline");
        received.Text="300"; RefreshTotals();
        Check(collect.Enabled && !validator.IsMarked(received), "Exactly the remaining cash balance is accepted");
        use.Checked=false; discount.Text="0"; received.Text="3300"; RefreshTotals();
        Check(collect.Enabled, "Without credit the full family balance is accepted");
        received.Text="3301"; RefreshTotals();
        Check(!collect.Enabled && validator.IsMarked(received), "Overpayment is rejected even without credit");
        received.Text="3300"; discount.Text="2000"; RefreshTotals();
        Check(!collect.Enabled && validator.IsMarked(received), "Adding a discount revalidates the existing received amount");
        discount.Text="0"; use.Checked=true; RefreshTotals();
        Check(!collect.Enabled && validator.IsMarked(received), "Applying credit revalidates the existing received amount");
        received.Text="0";
        grid.Rows.Clear(); credit.Clear(); Bill(4,6594,8700); credit[6594]=8750; discount.Text="0";
        type.GetMethod("UpdateCollectionTotals", flags)!.Invoke(page,null);
        Check(Payable()==0 && ((Label)Field("remainingCredit")).Text=="50", "Credit-only settlement previews the exact remaining credit");
        received.Text="1"; RefreshTotals();
        Check(!collect.Enabled && validator.IsMarked(received) && ((Label)Field("remainingCredit")).Text=="50",
            "A fully credit-covered bill rejects cash and never previews an implicit credit deposit");
        received.Text="0"; RefreshTotals();
        Check(collect.Enabled && !validator.IsMarked(received), "Credit-only settlement remains available with zero cash");
        Check(((Label)Field("availableCredit")).Font.Bold && ((Label)Field("availableCredit")).ForeColor==Color.ForestGreen,
            "Available credit is bold green");
        var layout=(TableLayoutPanel)((Control)Field("creditSummary")).Parent!;
        Check(layout.RowStyles[2].Height>0 && layout.RowStyles[7].Height>0 && layout.RowStyles[6].Height==0,
            "Remaining credit replaces a settled balance and credit rows have space");
        using var form = new Form { ClientSize = new Size(1320,900) };
        page.Dock=DockStyle.Fill; form.Controls.Add(page); form.Show(); Application.DoEvents();
        type.GetMethod("UpdateCollectionTotals", flags)!.Invoke(page,null);
        Check(use.Visible && use.Width >= TextRenderer.MeasureText(use.Text,use.Font).Width + 20,
            "Credit checkbox fits the payment panel");
        Directory.CreateDirectory("artifacts/collection-credit");
        using var picture=new Bitmap(form.Width,form.Height);
        form.DrawToBitmap(picture,new Rectangle(Point.Empty,picture.Size));
        picture.Save("artifacts/collection-credit/payment-panel.png");
        grid.Rows.Clear(); credit[6594]=50;
        type.GetMethod("UpdateCollectionTotals", flags)!.Invoke(page,null);
        Check(layout.RowStyles[0].Height==0 && ((Label)Field("remainingCredit")).Text=="50",
            "After full settlement, remaining credit replaces the empty outstanding summary");
        Check(((Label)Field("collectionResult")).Parent is null, "Transaction text is absent below Next Student");
    }
}
