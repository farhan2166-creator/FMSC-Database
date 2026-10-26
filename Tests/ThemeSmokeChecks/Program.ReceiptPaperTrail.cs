using System.Data;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckReceiptPaperTrail(InstitutionProfile.PrintIdentity school,Func<string,DataTable,int> render)
    {
        using var header=new DataTable();
        foreach(string field in new[]{"ReceiptNumber","PaymentMethod","ReceiptStatus"})header.Columns.Add(field);
        header.Columns.Add("TransactionDate",typeof(DateTime));header.Columns.Add("SnapshotVersion",typeof(int));
        foreach(string field in new[]{"CashReceived","CashApplied","CreditApplied","DiscountApplied","BalanceRemaining","CreditRemaining"})header.Columns.Add(field,typeof(decimal));
        var h=header.Rows.Add("FR-2026-00009","Cash","",new DateTime(2026,10,26,20,26,38),2,9900m,9900m,0m,7800m,0m,0m);
        using var students=new DataTable();students.Columns.Add("AdmissionId",typeof(int));
        foreach(string field in new[]{"AdmissionNumber","StudentName","FatherName"})students.Columns.Add(field);
        foreach(string field in new[]{"CashReceived","CashApplied","CreditApplied","DiscountApplied","BalanceRemaining","CreditRemaining"})students.Columns.Add(field,typeof(decimal));
        students.Rows.Add(17,"17","Falak Naz","Example Parent",7400m,7400m,0m,4800m,0m,0m);
        students.Rows.Add(18,"18","Robina","Example Parent",2500m,2500m,0m,3000m,0m,0m);
        using var lines=new DataTable();lines.Columns.Add("ChargeId",typeof(long));lines.Columns.Add("AdmissionId",typeof(int));
        foreach(string field in new[]{"FeeTypeName","ClassName","SectionName","CampusName","FeeStatus","ConcessionKind","ChangeReason"})lines.Columns.Add(field);
        lines.Columns.Add("FeePeriod",typeof(DateTime));
        foreach(string field in new[]{"AmountBefore","GrossAmount","PreviouslyPaid","Discount","Cash","Credit","CancelledAmount"})lines.Columns.Add(field,typeof(decimal));
        void Fee(long id,int child,string name,int month,decimal gross,decimal discount,decimal previous,decimal cash,string kind="",string reason="")=>
            lines.Rows.Add(id,child,name,"Ten","F","Campus Girls","Paid",kind,reason,new DateTime(2026,month,1),gross-previous,gross,previous,discount,cash,0m,0m);
        Fee(35,17,"Books Fee",10,4000,300,0,3700,"Discount","Books concession approved");
        Fee(28,17,"Tuition Fee",11,1600,400,0,1200,"Discount");
        Fee(40,17,"Transport Fee",11,2500,0,0,2500);
        Fee(52,17,"Tuition Fee",12,1600,1600,0,0,"Waived","December tuition waived");
        Fee(53,17,"Transport Fee",12,2500,2500,0,0,"Waived","December transport waived");
        Fee(29,18,"Admission Fee",10,5100,3000,2100,0,"Discount");
        Fee(30,18,"Tuition Fee",10,1500,0,1500,0);
        Fee(36,18,"Books Fee",10,4000,0,1500,2500);
        Fee(41,18,"Tuition Fee",11,1500,0,1500,0);
        Fee(54,18,"Tuition Fee",12,1500,0,1500,0);
        using var receipt=FeeReceiptDocuments.BuildData(school,h,students,lines);
        using var display=FamilyFeeDocumentPresentation.Receipt(receipt);
        var falak=display.AsEnumerable().First(row=>row.Field<string>("ChildAdmissionNumberText")=="17");
        var robina=display.AsEnumerable().First(row=>row.Field<string>("ChildAdmissionNumberText")=="18");
        Check(falak.Field<decimal>("ChildFeesCovered")==12200m&&falak.Field<decimal>("ChildDiscountApplied")==4800m
            &&falak.Field<decimal>("ChildPayable")==7400m&&falak.Field<decimal>("ChildCashReceived")==7400m
            &&falak.Field<decimal>("ChildBalanceRemaining")==0m,"Admission 17 complete receipt shows all 12200 fees less 4800 concessions and 7400 received");
        var december=display.AsEnumerable().Where(row=>row.Field<string>("ChildAdmissionNumberText")=="17"
            &&row.Field<string>("FeeDescription") is "Tuition Fee" or "Transport Fee"&&row.Field<decimal>("Amount") is 1600m or 2500m).ToArray();
        Check(december.Length==4&&display.AsEnumerable().All(row=>!row.Field<string>("FeeDescription")!.Contains("Reason:")
            &&!row.Field<string>("FeeDescription")!.Contains("Discount:")&&!row.Field<string>("FeeDescription")!.Contains("Waived:")),
            "Discounted and waived fees print plain names and full amounts, with concessions only in the summary");
        Check(display.AsEnumerable().Any(row=>row.Field<string>("FeeDescription")=="Less: Previously paid"&&row.Field<decimal>("Amount")==-8100m)
            &&robina.Field<decimal>("ChildPayable")==2500m,"Earlier payment is visibly deducted without being counted as a discount or new cash");
        Check(falak.Field<decimal>("GroupFeeTotal")==17700m&&falak.Field<decimal>("GroupDiscount")==7800m
            &&falak.Field<decimal>("GroupPayable")==9900m&&falak.Field<decimal>("GroupReceived")==9900m&&falak.Field<decimal>("GroupDue")==0m,
            "The complete family document reconciles fees, earlier payments, concessions and this collection");
        Check(render("family-receipt-complete-paper-trail",receipt)==1,"Complete admission 17 paper trail renders on one continuous receipt");
        using var individualStudents=students.Clone();individualStudents.ImportRow(students.Rows[0]);
        using var individualLines=lines.Clone();foreach(var line in lines.AsEnumerable().Where(row=>row.Field<int>("AdmissionId")==17))individualLines.ImportRow(line);
        using var individualHeader=header.Copy();var ih=individualHeader.Rows[0];
        ih["CashReceived"]=7400m;ih["CashApplied"]=7400m;ih["DiscountApplied"]=4800m;
        using(var individual=FeeReceiptDocuments.BuildData(school,ih,individualStudents,individualLines))
        {
            Check(individual.Rows[0].Field<decimal>("Payable")==7400m,"Individual and family receipts use the same net payable");
            Check(render("individual-receipt-complete-paper-trail",individual)==1,"Individual receipt shows separate discounts and waived lines");
        }
        // A zero-money acknowledgement still shows concessions and prior cash.
        ih["PaymentMethod"]="Fee changes";ih["CashReceived"]=0m;ih["CashApplied"]=0m;
        individualStudents.Rows[0]["CashReceived"]=0m;individualStudents.Rows[0]["CashApplied"]=0m;
        foreach(DataRow line in individualLines.Rows)
        {
            line["PreviouslyPaid"]=line.Field<decimal>("Cash");line["Cash"]=0m;
            line["AmountBefore"]=line.Field<decimal>("GrossAmount")-line.Field<decimal>("PreviouslyPaid");
        }
        individualLines.Rows.Add(999L,17,"Books Fee","Ten","F","Campus Girls","Cancelled","Cancelled","Cancelled duplicate charge",
            new DateTime(2026,10,1),0m,0m,0m,0m,0m,0m,3000m);
        using var acknowledgement=FeeReceiptDocuments.BuildData(school,ih,individualStudents,individualLines);
        Check(acknowledgement.Rows[0].Field<decimal>("Payable")==0m&&acknowledgement.Rows[0].Field<decimal>("CashReceived")==0m
            &&acknowledgement.Rows[0].Field<string>("DocumentTitle")=="FEE CHANGE ACKNOWLEDGEMENT",
            "A fee-change acknowledgement never claims money was received");
        Check(acknowledgement.AsEnumerable().Any(row=>row.Field<string>("FeeDescription")!.Contains("Cancelled (Rs. 3,000 removed)")),
            "Cancelled charges are disclosed without contributing to fees or discounts");
        Check(render("fee-change-acknowledgement",acknowledgement)==1,"Zero-money fee changes remain printable after settlement");
        using var agreements=new DataTable();agreements.Columns.Add("AdmissionId",typeof(int));
        foreach(string field in new[]{"FeeTypeName","Frequency"})agreements.Columns.Add(field);
        foreach(string field in new[]{"Amount","DiscountAmount"})agreements.Columns.Add(field,typeof(decimal));
        foreach(string field in new[]{"EffectiveFrom","EffectiveTo","CancelledAt"})agreements.Columns.Add(field,typeof(DateTime));
        agreements.Rows.Add(17,"Tuition Fee","Monthly",1600m,400m,new DateTime(2027,1,1),new DateTime(2027,3,1),DBNull.Value);
        FeeReceiptDocuments.AddAgreementNotes(acknowledgement,agreements,individualStudents);
        Check(acknowledgement.AsEnumerable().Any(row=>row.Field<string>("FeeDescription")!.Contains("Jan 2027 through Mar 2027"))
            &&acknowledgement.Rows[^1].Field<decimal>("Amount")==0m&&acknowledgement.Rows[^1].Field<decimal>("Payable")==0m,
            "Future agreement changes are disclosed separately without adding them to billed dues");
        Check(render("fee-change-acknowledgement-with-plan",acknowledgement)==1,"A future agreement acknowledgement prints its effective period and concession");
    }
}
