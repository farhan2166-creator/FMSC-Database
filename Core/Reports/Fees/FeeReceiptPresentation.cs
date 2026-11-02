namespace FMSC_SMS.Core;

// Print-only deductions. Account credit is a payment source, so retain the saved
// discount, payable and settlement values and combine them only on the paper.
// BD-28: captions name only applicable Discount/Waived/Credit; unknown historical types use Discount.
// Free is a sibling concession, not Waived; caption selection must not change settlement arithmetic.
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
        foreach(string field in new[]{"ReceiptDeductionCaption","ReceiptSummaryDeductionCaption"})
            data.Columns.Add(prefix+field,typeof(string));
        foreach(string field in new[]{"ReceiptDeductionAmount","ReceiptNetPayable"})
            data.Columns.Add(prefix+field,typeof(decimal));
        data.Columns.Add(prefix+"ShowReceiptDeduction",typeof(bool));
        foreach(DataRow row in data.Rows)
        {
            bool settlement=Convert.ToBoolean(row[prefix=="Group"?"ShowSettlement":prefix+"ShowSettlement"]);
            decimal discount=FamilyFeeDocumentPresentation.Money(row,prefix=="Group"?"GroupDiscount":prefix+"DiscountApplied");
            decimal credit=settlement?FamilyFeeDocumentPresentation.Money(row,prefix+"CreditApplied"):0m;
            string field=prefix=="Group"?"ReceiptConcessionCaption":prefix+"ReceiptConcessionCaption";
            string concession=data.Columns.Contains(field)?Convert.ToString(row[field])??"":"";
            string caption=discount>0m?(concession.Length>0?concession:"Discount"):"";
            if(credit>0m)caption+=(caption.Length>0?" / ":"")+"Credit";
            row[prefix+"ReceiptDeductionCaption"]="Less: "+caption;
            row[prefix+"ReceiptSummaryDeductionCaption"]="Less: "+caption;
            row[prefix+"ReceiptDeductionAmount"]=discount+credit;
            row[prefix+"ReceiptNetPayable"]=FamilyFeeDocumentPresentation.Money(row,prefix+"Payable")-credit;
            row[prefix+"ShowReceiptDeduction"]=discount>0m||credit>0m;
        }
    }

    internal static string ConcessionCaption(IEnumerable<DataRow> lines)
    {
        bool discount=false,waived=false,unknown=false;
        foreach(var line in lines)
        {
            if(FamilyFeeDocumentPresentation.Money(line,"Discount")<=0m)continue;
            bool changed=line.Table.Columns.Contains("ReceiptConcessionTypeUnknown")&&!line.IsNull("ReceiptConcessionTypeUnknown")
                &&Convert.ToBoolean(line["ReceiptConcessionTypeUnknown"]);
            string kind=!changed&&line.Table.Columns.Contains("ConcessionKind")?Convert.ToString(line["ConcessionKind"])??"":"";
            if(kind.Equals("Waived",StringComparison.OrdinalIgnoreCase))waived=true;
            // Free is the stored value for a sibling concession, not a waiver.
            else if(new[]{"Discount","Scholarship","Free","Sibling concession"}.Contains(kind,StringComparer.OrdinalIgnoreCase))discount=true;
            else unknown=true;
        }
        discount|=unknown;
        return discount&&waived?"Discount / Waived":waived?"Waived":discount?"Discount":"";
    }
}
