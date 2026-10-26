using System.Data;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckFeeVisitDocuments(InstitutionProfile.PrintIdentity school,Func<string,DataTable,int> render)
    {
        using var header=new DataTable();
        foreach(string field in new[]{"ReceiptNumber","PaymentMethod","ReceiptStatus"})header.Columns.Add(field);
        header.Columns.Add("TransactionDate",typeof(DateTime));header.Columns.Add("SnapshotVersion",typeof(int));
        foreach(string field in new[]{"CashReceived","CashApplied","CreditApplied","DiscountApplied","BalanceRemaining","CreditRemaining"})header.Columns.Add(field,typeof(decimal));
        using var students=new DataTable();students.Columns.Add("AdmissionId",typeof(int));
        foreach(string field in new[]{"AdmissionNumber","StudentName","FatherName"})students.Columns.Add(field);
        foreach(string field in new[]{"CashReceived","CashApplied","CreditApplied","DiscountApplied","BalanceRemaining","CreditRemaining"})students.Columns.Add(field,typeof(decimal));
        students.Columns.Add("StudentClassOrder",typeof(int));
        using var lines=new DataTable();lines.Columns.Add("ChargeId",typeof(long));lines.Columns.Add("AdmissionId",typeof(int));
        foreach(string field in new[]{"FeeTypeName","ClassName","SectionName","CampusName","FeeStatus","ConcessionKind"})lines.Columns.Add(field);
        lines.Columns.Add("FeePeriod",typeof(DateTime));
        foreach(string field in new[]{"AmountBefore","GrossAmount","BaseAmount","PreviouslyPaid","Discount","Cash","Credit","OpeningBalance","RateAdjustment","DiscountReversed","SettlementAdjustment","FeeDisplayOrder"})lines.Columns.Add(field,typeof(decimal));
        lines.Columns.Add("IsCarriedForward",typeof(bool));
        void Student(int id,string name,int grade,decimal paid,decimal discount,decimal balance)=>students.Rows.Add(id,id.ToString(),name,"Khan Muhammad",paid,paid,0m,discount,balance,0m,grade);
        long key=0;
        void Fee(int id,string name,int month,decimal amount,decimal discount,decimal paid,bool carried=false)
        {
            string grade=id==5239?"Four":id==3944?"Eight":"Ten";
            lines.Rows.Add(++key,id,name,grade,"F",id==5239?"Campus Girls":"Campus Boys","Open","",new DateTime(2026,month,1),
                amount,amount,amount,0m,discount,paid,0m,carried?amount:0m,0m,0m,0m,key,carried);
        }
        DataRow Start(int visit,decimal cash,decimal discount,decimal due)
        {
            students.Rows.Clear();lines.Rows.Clear();header.Rows.Clear();
            return header.Rows.Add($"FR-2026-{visit+11:00000}","Cash","",new DateTime(2026,10,26,23,visit+11,0),3,cash,cash,0m,discount,due,0m);
        }
        var first=Start(1,4000,5400,14400);
        Student(3944,"Muhammad Shayan Afridi",11,0,2500,6400);Student(4623,"Abdullah Khan",13,0,2500,6800);Student(5239,"Faryal Khan Afridi",7,4000,400,1200);
        Fee(3944,"Admission Fee",10,5000,0,0);Fee(3944,"Tuition Fee",10,1400,0,0);Fee(3944,"Transport Fee",10,2500,2500,0);
        Fee(4623,"Admission Fee",10,5200,0,0);Fee(4623,"Tuition Fee",11,1600,0,0);Fee(4623,"Transport Fee",11,2500,2500,0);
        Fee(5239,"Admission Fee",10,4600,400,3000);Fee(5239,"Tuition Fee",10,1000,0,1000);
        using(var data=FeeReceiptDocuments.BuildData(school,first,students,lines))
        {
            using var display=FamilyFeeDocumentPresentation.Receipt(data);
            Check(display.AsEnumerable().Select(row=>row.Field<string>("ChildAdmissionNumberText")).Distinct().SequenceEqual(new[]{"5239","3944","4623"}),
                "Receipt children follow the exact allocator class sequence, placing the paid junior first");
            Check(display.Rows[0].Field<decimal>("GroupFeeTotal")==23800m&&display.Rows[0].Field<decimal>("GroupDiscount")==5400m
                &&display.Rows[0].Field<decimal>("GroupReceived")==4000m&&display.Rows[0].Field<decimal>("GroupDue")==14400m,"Actual first-visit figures reconcile");
            Check(render("family-visit-1",data)==1,"Actual first visit renders with allocator order");
        }
        var second=Start(2,7000,0,7400);
        Student(3944,"Muhammad Shayan Afridi",11,5800,0,600);Student(4623,"Abdullah Khan",13,0,0,6800);Student(5239,"Faryal Khan Afridi",7,1200,0,0);
        Fee(3944,"Admission Fee",10,5000,0,4400,true);Fee(3944,"Tuition Fee",10,1400,0,1400,true);
        Fee(4623,"Admission Fee",10,5200,0,0,true);Fee(4623,"Tuition Fee",11,1600,0,0,true);Fee(5239,"Admission Fee",10,1200,0,1200,true);
        using(var data=FeeReceiptDocuments.BuildData(school,second,students,lines))
        {
            using var display=FamilyFeeDocumentPresentation.Receipt(data);
            Check(display.Rows[0].Field<decimal>("GroupFeeTotal")==14400m&&display.Rows[0].Field<decimal>("GroupDiscount")==0m
                &&display.Rows[0].Field<decimal>("GroupDue")==7400m,"Second visit carries outstanding only, with no repeated concession");
            Check(!display.AsEnumerable().Any(row=>row.Field<string>("FeeDescription")!.Contains("Previously paid")),"Visit receipts do not repeat cumulative payment deductions");
            Check(render("family-visit-2",data)==1,"Second visit renders only carried fees and current payment");
        }
        var third=Start(3,2000,5400,0);
        Student(3944,"Muhammad Shayan Afridi",11,400,200,0);Student(4623,"Abdullah Khan",13,1600,5200,0);
        Fee(3944,"Admission Fee",10,600,200,400,true);Fee(4623,"Admission Fee",10,5200,5200,0,true);Fee(4623,"Tuition Fee",11,1600,0,1600,true);
        using(var data=FeeReceiptDocuments.BuildData(school,third,students,lines))
        {
            using var display=FamilyFeeDocumentPresentation.Receipt(data);
            Check(display.Rows[0].Field<decimal>("GroupFeeTotal")==7400m&&display.Rows[0].Field<decimal>("GroupDiscount")==5400m
                &&display.Rows[0].Field<decimal>("GroupPayable")==2000m&&display.Rows[0].Field<decimal>("GroupDue")==0m,"Third visit shows only the further 200 and 5200 concessions");
            Check(!display.AsEnumerable().Any(row=>row.Field<string>("ChildAdmissionNumberText")=="5239"),"The child settled at visit two is absent at visit three");
            Check(render("family-visit-3",data)==1,"Third visit renders new concessions and closes the family balance");
        }
        using var charges=new DataTable();
        foreach(string field in new[]{"Reference","StudentName","FatherName","FeeTypeName","ClassName","SectionName","CampusName"})charges.Columns.Add(field);
        charges.Columns.Add("BillingMonth",typeof(DateTime));charges.Columns.Add("BillingChargeId",typeof(long));charges.Columns.Add("AdmissionId",typeof(int));
        foreach(string field in new[]{"DiscountAmount","Due","GrossAmount","BaseAmount","OpeningBalance","RateAdjustment","DiscountReversed","SettlementAdjustment","AccountGrossAmount","AccountDiscountAmount"})charges.Columns.Add(field,typeof(decimal));
        charges.Columns.Add("IsVisitBill",typeof(bool));charges.Columns.Add("IsCarriedForward",typeof(bool));charges.Columns.Add("StudentClassOrder",typeof(int));
        foreach(DataRow line in lines.Rows)
        {
            int id=line.Field<int>("AdmissionId");var child=students.AsEnumerable().First(row=>row.Field<int>("AdmissionId")==id);
            decimal amount=line.Field<decimal>("GrossAmount"),discount=line.Field<decimal>("Discount");
            charges.Rows.Add(id.ToString(),child["StudentName"],"Khan Muhammad",line["FeeTypeName"],line["ClassName"],"F",line["CampusName"],line["FeePeriod"],line["ChargeId"],id,
                discount,amount-discount,amount,amount,amount,0m,0m,0m,5000m,0m,true,true,child["StudentClassOrder"]);
        }
        using(var bill=FamilyBillReport.BuildData(school.Name,school.ContactNumber,school.Address,"Khan Muhammad",charges))
        {
            using var display=FamilyFeeDocumentPresentation.Bill(bill);
            Check(display.Rows[0].Field<decimal>("GroupFeeTotal")==7400m&&display.Rows[0].Field<decimal>("GroupDiscount")==5400m
                &&display.Rows[0].Field<decimal>("GroupDue")==2000m&&!display.Rows[0].Field<bool>("ShowGroupPreviouslySettled"),"A bill before visit three uses the same carry-forward and further concessions");
            Check(render("family-visit-3-before-payment-bill",bill)==1,"Visit bill renders without historical fees or payment deductions");
        }
        using var ties=students.Clone();
        ties.Rows.Add(10,"10","Child","Parent",0m,0m,0m,0m,0m,0m,7);
        ties.Rows.Add(20,"2","Child","Parent",0m,0m,0m,0m,0m,0m,7);
        ties.Rows.Add(30,"2a","Child","Parent",0m,0m,0m,0m,0m,0m,7);
        Check(FeeStudentOrder.Rows(ties,"AdmissionNumber").Select(row=>row.Field<string>("AdmissionNumber")).SequenceEqual(new[]{"2","2a","10"}),"Same-class document order matches numeric and suffix allocation ties");
        using(var adjustedHeader=header.Copy())
        using(var adjustedStudents=students.Copy())
        using(var adjustedLines=lines.Clone())
        {
            var h=adjustedHeader.Rows[0];h["PaymentMethod"]="Fee changes";
            foreach(string field in new[]{"CashReceived","CashApplied","DiscountApplied","BalanceRemaining"})h[field]=0m;
            foreach(DataRow student in adjustedStudents.Rows)
                foreach(string field in new[]{"CashReceived","CashApplied","DiscountApplied","BalanceRemaining"})student[field]=0m;
            adjustedLines.ImportRow(lines.Rows[0]);var line=adjustedLines.Rows[0];
            foreach(string field in new[]{"GrossAmount","BaseAmount","AmountBefore","Discount","Cash","Credit","OpeningBalance"})line[field]=0m;
            line["RateAdjustment"]=-50m;line["SettlementAdjustment"]=50m;
            using var acknowledgement=FeeReceiptDocuments.BuildData(school,h,adjustedStudents,adjustedLines);
            using var display=FamilyFeeDocumentPresentation.Receipt(acknowledgement);
            Check(display.AsEnumerable().Where(row=>row.Field<bool>("ShowChildDocument")).Select(row=>row.Field<string>("ChildAdmissionNumberText")).Distinct().SequenceEqual(new[]{"3944"}),
                "Offsetting fee and released-settlement adjustments remain visible even when their final due is zero");
            Check(display.AsEnumerable().Any(row=>row.Field<decimal>("Amount")==-50m)&&display.AsEnumerable().Any(row=>row.Field<decimal>("Amount")==50m)
                &&display.Rows[0].Field<decimal>("GroupReceived")==0m,"A zero-net correction shows both sides without claiming a payment");
            Check(render("visit-paid-reduction-acknowledgement",acknowledgement)==1,"Offsetting adjustments render without disappearing as settled history");
        }
        string fixtureIndex=Path.GetFullPath("artifacts/fee-visits/latest-fixture.txt");
        if(File.Exists(fixtureIndex))
        {
            string folder=File.ReadAllText(fixtureIndex).Trim();
            for(int visit=1;visit<=3;visit++)
            {
                using var saved=new DataSet();saved.ReadXml(Path.Combine(folder,$"actual-visit-{visit}.xml"),XmlReadMode.ReadSchema);
                using var actual=FeeReceiptDocuments.BuildData(school,saved.Tables[0].Rows[0],saved.Tables[1],saved.Tables[2]);
                using var display=FamilyFeeDocumentPresentation.Receipt(actual);
                string[] expected=visit==3?new[]{"3944","4623"}:new[]{"5239","3944","4623"};
                Check(display.AsEnumerable().Where(row=>row.Field<bool>("ShowChildDocument")).Select(row=>row.Field<string>("ChildAdmissionNumberText")).Distinct().SequenceEqual(expected),
                    "The actual database receipt reader renders children in allocation order");
                Check(render($"actual-family-visit-{visit}",actual)==1,"The actual saved family visit renders through the production receipt builder and RDLC");
            }
        }
    }
}
