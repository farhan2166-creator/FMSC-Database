using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Collection;

internal static partial class Program
{
    private static void CheckLargestDiscount()
    {
        using var page = new FeeCollectionPage(false);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(FeeCollectionPage);
        type.GetField("suppressDiscountEvents", flags)!.SetValue(page, true);
        type.GetField("suppressPaymentEvents", flags)!.SetValue(page, true);
        var grid = (DataGridView)type.GetField("charges", flags)!.GetValue(page)!;
        var discount = (ModernTextBox)type.GetField("discountAmount", flags)!.GetValue(page)!;
        foreach (var item in new[] { (Id: 1L, Due: 400m), (Id: 2L, Due: 2500m), (Id: 3L, Due: 900m) })
        {
            int index = grid.Rows.Add();
            grid.Rows[index].Cells["BillingChargeId"].Value = item.Id;
            grid.Rows[index].Cells["Balance"].Value = item.Due;
        }
        Dictionary<long, decimal> Allocate(decimal value)
        {
            discount.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return (Dictionary<long, decimal>)type.GetMethod("GetDiscountAllocations", flags)!.Invoke(page, null)!;
        }
        var first = Allocate(2000);
        Check(first.Count == 1 && first[2] == 2000, "Discount goes to the largest fee before earlier smaller fees");
        var next = Allocate(3000);
        Check(next[2] == 2500 && next[3] == 500 && !next.ContainsKey(1), "Remainder goes to the next-largest fee");
        var all = Allocate(3800);
        Check(all[2] == 2500 && all[3] == 900 && all[1] == 400, "Discount covers descending fee balances exactly");
        Check(Allocate(0).Count == 0, "Zero discount does not allocate any reduction");
        Check(!grid.Columns["AppliedDiscount"].Visible && grid.Columns["AppliedDiscount"].ReadOnly,
            "Separate bill-discount editor is hidden and read-only");
    }
}
