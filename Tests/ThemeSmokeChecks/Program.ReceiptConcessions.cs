using System.Data;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckReceiptConcessions(InstitutionProfile.PrintIdentity school,Func<string,DataTable,int> render)
    {
        using var header=new DataTable();
        foreach(string name in new[]{"ReceiptNumber","PaymentMethod","ReceiptStatus"})header.Columns.Add(name);
        header.Columns.Add("TransactionDate",typeof(DateTime));
        foreach(string name in new[]{"CashReceived","CashApplied","CreditApplied","DiscountApplied","BalanceRemaining","CreditRemaining"})
            header.Columns.Add(name,typeof(decimal));
        header.Rows.Add("FR-2026-00006","Cash","",new DateTime(2026,10,26,18,56,37),2000m,2000m,0m,0m,8100m,0m);
        using var students=new DataTable();students.Columns.Add("AdmissionId",typeof(int));
        foreach(string name in new[]{"AdmissionNumber","StudentName","FatherName"})students.Columns.Add(name);
        foreach(string name in new[]{"CashApplied","CreditApplied","DiscountApplied","BalanceRemaining","CreditRemaining"})
            students.Columns.Add(name,typeof(decimal));
        students.Rows.Add(1462,"1462","Saima","Abdul Wahab",0m,0m,0m,5600m,0m);
        students.Rows.Add(5429,"5429","Naik Muhammad Afridi","Abdul Wahab",2000m,0m,0m,2500m,0m);
        using var lines=new DataTable();lines.Columns.Add("ChargeId",typeof(long));lines.Columns.Add("AdmissionId",typeof(int));
        foreach(string name in new[]{"FeeTypeName","ClassName","SectionName","CampusName"})lines.Columns.Add(name);
        lines.Columns.Add("FeePeriod",typeof(DateTime));
        foreach(string name in new[]{"AmountBefore","Discount","Cash","Credit"})lines.Columns.Add(name,typeof(decimal));
        lines.Rows.Add(25L,5429,"Tuition Fee","Nine","F","Campus Boys",new DateTime(2026,10,1),1500m,0m,1500m,0m);
        lines.Rows.Add(27L,5429,"Books Fee","Nine","F","Campus Boys",new DateTime(2026,10,1),3000m,0m,500m,0m);
        using var changes=new DataTable();changes.Columns.Add("ChargeId",typeof(long));changes.Columns.Add("DiscountChange",typeof(decimal));
        changes.Rows.Add(25L,0m);changes.Rows.Add(27L,2500m);
        changes.Rows.Add(999L,900m); // A concession on another charge is outside this receipt.
        int reads=0;
        Task<DataTable> Changes(string receipt)
        {
            Check(receipt=="FR-2026-00006","Concession refresh identifies the issued receipt");
            reads++;return Task.FromResult(changes.Copy());
        }
        using var latestHeader=header.Copy();using var latestStudents=students.Copy();using var latestLines=lines.Copy();
        var h=latestHeader.Rows[0];
        FeeReceiptDocuments.RefreshBalancesAsync(h,latestStudents,id=>Task.FromResult(id==1462?5600m:0m),
            _=>Task.FromResult(new FeeReceiptQueries.ReceiptBalanceState(true,new Dictionary<int,decimal>()))).GetAwaiter().GetResult();
        FeeReceiptDocuments.RefreshChargeConcessionsAsync(h,latestStudents,latestLines,true,Changes).GetAwaiter().GetResult();
        Check(h.Field<decimal>("DiscountApplied")==2500m && latestStudents.Rows[1].Field<decimal>("DiscountApplied")==2500m
            && latestStudents.Rows[0].Field<decimal>("DiscountApplied")==0m,"The later books waiver appears only on its child and in the group discount");
        Check(latestLines.Rows[1].Field<decimal>("AmountBefore")==3000m && latestLines.Rows[1].Field<decimal>("Discount")==2500m,
            "A waiver retains the full original fee and shows its deduction separately");
        Task<DataTable> Unpaid(int id)
        {
            Check(id==1462,"The unpaid sibling retains their own fee details");
            var data=lines.Clone();
            data.Rows.Add(24L,1462,"Tuition Fee","Ten","F","Campus Boys",new DateTime(2026,10,1),1600m,0m,0m,0m);
            data.Rows.Add(26L,1462,"Books Fee","Ten","F","Campus Boys",new DateTime(2026,10,1),4000m,0m,0m,0m);
            return Task.FromResult(data);
        }
        FeeReceiptDocuments.AddUnpaidChildFeesAsync(h,latestStudents,latestLines,Unpaid).GetAwaiter().GetResult();
        using var family=FeeReceiptDocuments.BuildData(school,h,latestStudents,latestLines);
        using(var display=FamilyFeeDocumentPresentation.Receipt(family))
        {
            var naik=display.AsEnumerable().First(row=>row.Field<string>("ChildAdmissionNumberText")=="5429");
            Check(naik.Field<decimal>("ChildFeesCovered")==4500m && naik.Field<decimal>("ChildDiscountApplied")==2500m
                && naik.Field<decimal>("ChildPayable")==2000m && naik.Field<decimal>("ChildCashReceived")==2000m
                && naik.Field<decimal>("ChildBalanceRemaining")==0m && naik.Field<bool>("ChildShowDiscount"),
                "The audited child's full fee, visible discount, payable, payment and zero balance reconcile");
            Check(naik.Field<decimal>("GroupFeeTotal")==10100m && naik.Field<decimal>("GroupDiscount")==2500m
                && naik.Field<decimal>("GroupPayable")==7600m && naik.Field<decimal>("GroupReceived")==2000m
                && naik.Field<decimal>("GroupDue")==5600m,"The audited family summary reconciles after the later waiver");
        }
        Check(render("family-receipt-later-waiver",family)==1,"The later waiver renders through the unchanged family receipt template");
        // Admission 17: an unpaid sibling's existing tuition discount must be
        // shown separately, while their net debt and the family payment stay fixed.
        using(var siblingHeader=header.Copy())
        using(var siblingStudents=students.Copy())
        using(var siblingLines=lines.Copy())
        {
            Task<DataTable> DiscountedUnpaid(int id)
            {
                var fees=lines.Clone();
                fees.Rows.Add(28L,id,"Tuition Fee","Ten","F","Campus Girls",new DateTime(2026,11,1),1600m,400m,0m,0m);
                return Task.FromResult(fees);
            }
            var sibling=siblingStudents.Rows[0];sibling["AdmissionNumber"]="17";
            sibling["BalanceRemaining"]=1200m;
            siblingHeader.Rows[0]["BalanceRemaining"]=3700m;
            FeeReceiptDocuments.AddUnpaidChildFeesAsync(siblingHeader.Rows[0],siblingStudents,siblingLines,DiscountedUnpaid).GetAwaiter().GetResult();
            FeeReceiptDocuments.AddUnpaidChildFeesAsync(siblingHeader.Rows[0],siblingStudents,siblingLines,DiscountedUnpaid).GetAwaiter().GetResult();
            using var receipt=FeeReceiptDocuments.BuildData(school,siblingHeader.Rows[0],siblingStudents,siblingLines);
            using var display=FamilyFeeDocumentPresentation.Receipt(receipt);
            var child=display.AsEnumerable().First(row=>row.Field<string>("ChildAdmissionNumberText")=="17");
            Check(child.Field<decimal>("ChildFeesCovered")==1600m && child.Field<decimal>("ChildDiscountApplied")==400m
                && child.Field<decimal>("ChildPayable")==1200m && child.Field<decimal>("ChildTotalPaid")==0m
                && child.Field<decimal>("ChildBalanceRemaining")==1200m && child.Field<bool>("ChildShowDiscount"),
                "An unpaid sibling prints 1600 tuition less 400 discount with unchanged 1200 debt and zero payment");
            Check(siblingHeader.Rows[0].Field<decimal>("DiscountApplied")==400m
                && siblingHeader.Rows[0].Field<decimal>("CashReceived")==2000m
                && child.Field<decimal>("GroupFeeTotal")==6100m && child.Field<decimal>("GroupPayable")==5700m
                && child.Field<decimal>("GroupDue")==3700m,
                "The family discount is added once without changing money received");
            Check(render("family-receipt-unpaid-sibling-discount",receipt)==1,"The unpaid sibling discount renders on the family receipt");
        }
        using var individualStudents=latestStudents.Clone();individualStudents.ImportRow(latestStudents.Rows[1]);
        using var individualLines=latestLines.Clone();
        foreach(var line in latestLines.AsEnumerable().Where(row=>row.Field<int>("AdmissionId")==5429))individualLines.ImportRow(line);
        using var individualHeader=latestHeader.Copy();individualHeader.Rows[0]["BalanceRemaining"]=0m;
        using var individual=FeeReceiptDocuments.BuildData(school,individualHeader.Rows[0],individualStudents,individualLines);
        Check(individual.Rows[0].Field<decimal>("FeesCovered")==4500m && individual.Rows[0].Field<decimal>("DiscountApplied")==2500m
            && individual.Rows[0].Field<bool>("ShowDiscount") && individual.Rows[0].Field<decimal>("CashReceived")==2000m,
            "Individual receipts expose the same later discount without changing their payment");
        Check(render("individual-receipt-later-waiver",individual)==1,"The later waiver renders through the unchanged individual template");
        FeeReceiptDocuments.RefreshChargeConcessionsAsync(header.Rows[0],students,lines,false,Changes).GetAwaiter().GetResult();
        Check(reads==1 && header.Rows[0].Field<decimal>("DiscountApplied")==0m && header.Rows[0].Field<decimal>("BalanceRemaining")==8100m
            && lines.Rows[1].Field<decimal>("Discount")==0m,"An older reprint never reads later concessions or changes its original figures");
        Check(h.Field<decimal>("CashApplied")==2000m && h.Field<decimal>("CreditApplied")==0m && h.Field<decimal>("CreditRemaining")==0m,
            "Refreshing the printed concession creates no payment, refund or advance credit");
        // Existing collection discounts are retained, rather than replaced or
        // counted twice when another part of that fee is subsequently waived.
        using var discountedHeader=header.Copy();using var discountedStudents=students.Copy();using var discountedLines=lines.Copy();
        discountedHeader.Rows[0]["DiscountApplied"]=100m;discountedStudents.Rows[1]["DiscountApplied"]=100m;
        discountedLines.Rows[1]["Discount"]=100m;
        FeeReceiptDocuments.RefreshChargeConcessionsAsync(discountedHeader.Rows[0],discountedStudents,discountedLines,true,Changes).GetAwaiter().GetResult();
        Check(discountedHeader.Rows[0].Field<decimal>("DiscountApplied")==2600m && discountedLines.Rows[1].Field<decimal>("Discount")==2600m,
            "Later concessions add to an existing collection discount without changing the fee total");
        using var unchanged=changes.Clone();unchanged.Rows.Add(27L,0m);
        FeeReceiptDocuments.RefreshChargeConcessionsAsync(header.Rows[0],students,lines,true,_=>Task.FromResult(unchanged.Copy())).GetAwaiter().GetResult();
        Check(header.Rows[0].Field<decimal>("DiscountApplied")==0m && lines.Rows[1].Field<decimal>("AmountBefore")==3000m,
            "A receipt with no later concession retains its original amounts");
    }
}
