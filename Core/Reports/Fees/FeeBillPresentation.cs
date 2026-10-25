namespace FMSC_SMS.Core;

// Keep the saved unpaid amounts intact. The report alone expands the fee into
// its original amount, concession and amounts already settled.
internal static class FeeBillPresentation
{
    internal static DataTable Create()
    {
        var data=FeeDocumentData.Create();
        data.Columns.Add("BillGrossAmount",typeof(decimal));
        data.Columns.Add("BillLineDiscount",typeof(decimal));
        return data;
    }

    internal static DataTable Prepare(DataTable source)
    {
        var data=source.Copy();
        data.Columns.Add("BillDisplayAmount",typeof(decimal));
        data.Columns.Add("BillPreviouslySettled",typeof(decimal));
        data.Columns.Add("ShowBillPreviouslySettled",typeof(bool));
        foreach(string field in new[]{"BillDiscountCaption","BillDiscountText","BillPaymentPrompt"})
            data.Columns.Add(field,typeof(string));
        foreach(var page in data.AsEnumerable().GroupBy(row=>row["ReceiptPageKey"]))
        {
            var rows=page.ToArray();
            decimal discount=Convert.ToDecimal(rows[0]["DiscountApplied"]);
            bool breakdown=data.Columns.Contains("BillGrossAmount") && rows.All(row=>!row.IsNull("BillGrossAmount")&&!row.IsNull("BillLineDiscount"));
            // Older single-fee fully waived snapshots contain enough evidence
            // to recover the original fee without consulting today's fee plan.
            bool oldWaiver=!breakdown && rows.Length==1 && Convert.ToDecimal(rows[0]["Amount"])==0m && discount>0m;
            decimal settled=breakdown?rows.Sum(row=>Convert.ToDecimal(row["BillGrossAmount"])-Convert.ToDecimal(row["BillLineDiscount"])-Convert.ToDecimal(row["Amount"])):0m;
            bool free=rows.All(row=>(Convert.ToString(row["FeeDescription"])??"").EndsWith("— Free",StringComparison.OrdinalIgnoreCase));
            bool scholarship=rows.All(row=>(Convert.ToString(row["FeeDescription"])??"").EndsWith("— Scholarship",StringComparison.OrdinalIgnoreCase));
            foreach(var row in rows)
            {
                row["BillDisplayAmount"]=breakdown?row["BillGrossAmount"]:oldWaiver?discount:row["Amount"];
                row["BillPreviouslySettled"]=settled;
                row["ShowBillPreviouslySettled"]=settled>0m;
                row["BillDiscountCaption"]=breakdown||oldWaiver
                    ?free?"Less: Free concession":scholarship?"Less: Scholarship":"Less: Discount / concession"
                    :"Concessions included";
                row["BillDiscountText"]=(breakdown||oldWaiver?-discount:discount).ToString("N0");
                row["BillPaymentPrompt"]=Convert.ToDecimal(row["TotalPaid"])<=0m?"No payment required":"Please pay at the fee counter";
            }
        }
        return data;
    }
}
