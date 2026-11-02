namespace FMSC_SMS.Core;

// Credit settles fees; it does not reduce their price. These are derived print
// values only and must never be written back to an issued receipt snapshot.
internal sealed record ReceiptPrintAmounts(decimal Discount,decimal FeesPayable,bool ShowCredit,bool ShowTotal,string SettlementCaption)
{
    internal static ReceiptPrintAmounts Create(decimal discount,decimal payable,decimal credit,bool settlement) =>
        new(discount,payable,settlement&&credit>0m,!settlement||credit>0m,settlement?"Total settled":"Net settled");
}
