namespace FMSC_SMS.Core;

// Print-only deductions. Account credit is a payment source, so retain the saved
// discount, payable and settlement values; show their separate meanings on paper.
internal static class FeeReceiptPresentation
{
    internal static DataTable Prepare(DataTable source)
    {
        var data=source.Copy();
        Apply(data);
        return data;
    }

    internal static void Apply(DataTable data,string prefix="")
    {
        foreach(string field in new[]{"ReceiptDeductionCaption","ReceiptSummaryDeductionCaption","ReceiptSettlementCaption"})
            data.Columns.Add(prefix+field,typeof(string));
        foreach(string field in new[]{"ReceiptDeductionAmount","ReceiptNetPayable"})
            data.Columns.Add(prefix+field,typeof(decimal));
        foreach(string field in new[]{"ShowReceiptDeduction","ShowReceiptCredit","ShowReceiptTotal"})
            data.Columns.Add(prefix+field,typeof(bool));
        foreach(DataRow row in data.Rows)
        {
            bool settlement=Convert.ToBoolean(row[prefix=="Group"?"ShowSettlement":prefix+"ShowSettlement"]);
            decimal discount=FamilyFeeDocumentPresentation.Money(row,prefix=="Group"?"GroupDiscount":prefix+"DiscountApplied");
            decimal credit=settlement?FamilyFeeDocumentPresentation.Money(row,prefix+"CreditApplied"):0m;
            var amounts=ReceiptPrintAmounts.Create(discount,FamilyFeeDocumentPresentation.Money(row,prefix+"Payable"),credit,settlement);
            row[prefix+"ReceiptDeductionCaption"]="Less: Discount / Waived";
            row[prefix+"ReceiptSummaryDeductionCaption"]="Discount / Waived";
            row[prefix+"ReceiptDeductionAmount"]=amounts.Discount;
            row[prefix+"ReceiptNetPayable"]=amounts.FeesPayable;
            row[prefix+"ShowReceiptDeduction"]=discount>0m;
            row[prefix+"ShowReceiptCredit"]=amounts.ShowCredit;
            row[prefix+"ShowReceiptTotal"]=amounts.ShowTotal;
            row[prefix+"ReceiptSettlementCaption"]=amounts.SettlementCaption;
        }
    }
}
