namespace FMSC_SMS.Core;

internal static partial class FeeReceiptDataBuilder
{
    internal static DataTable BuildGrouped(InstitutionProfile.PrintIdentity institution,DataRow header,DataTable students,DataTable lines,bool family)
    {
        var data=FeeDocumentData.Create();
        data.Columns.Add("ReceiptConcessionCaption",typeof(string));
        data.Columns.Add("ChildReceiptConcessionCaption",typeof(string));
        string concessionCaption=FeeReceiptPresentation.ConcessionCaption(lines.AsEnumerable());
        bool complete=FeeReceiptDocuments.IsCompleteSnapshot(header);
        bool visit=complete&&Convert.ToInt32(header["SnapshotVersion"])>=3;
        bool acknowledgement=Text(header,"PaymentMethod")=="Fee changes";

        if(family) {data.Columns.Add("FamilyRowKind",typeof(string));data.Columns.Add("FamilyAmountText",typeof(string));FamilyFeeDocumentPresentation.AddChildColumns(data);}
        decimal Money(DataRow row,string name)=>Convert.ToDecimal(row[name]);
        string Text(DataRow row,string name)=>Convert.ToString(row[name])??"";
        var first=students.Rows[0];
        decimal settled=Money(header,"CashApplied")+Money(header,"CreditApplied");
        string references=string.Join(", ",FeeStudentOrder.Rows(students,"AdmissionNumber").Select(row=>Text(row,"AdmissionNumber")));
        string parent=Text(first,"FatherName");
        string campusHeading=FeeDocumentData.CampusHeading(lines);
        DataRow? currentStudent=null;DataRow[] currentLines=Array.Empty<DataRow>();
        void Add(string description,string period,decimal amount,string kind,DataRow? detail=null)
        {
            var row=data.Rows.Add(1,institution.Name,family?"FAMILY PAYMENT RECEIPT":"STUDENT PAYMENT RECEIPT","PARENT COPY",
                Text(header,"ReceiptNumber"),family?parent:Text(first,"StudentName"),references,
                family || detail is null?"":Text(detail,"ClassName")+(string.IsNullOrWhiteSpace(Text(detail,"SectionName"))?"":" - "+Text(detail,"SectionName")),
                Convert.ToDateTime(header["TransactionDate"]).ToString("dd-MM-yyyy hh:mm tt"),family?$"{students.Rows.Count} students":"",
                description,amount,Money(header,"DiscountApplied"),settled,Money(header,"BalanceRemaining"),Text(header,"PaymentMethod"),
                Text(header,"ReceiptStatus"),Money(header,"DiscountApplied")>0,true,institution.ContactNumber,institution.Address,
                family?campusHeading:detail is null?"":Text(detail,"CampusName"),family?"":parent,period);
            row["NetPaid"]=settled;row["FeesCovered"]=lines.AsEnumerable().Sum(line=>Money(line,"AmountBefore"));
            row["ReceiptConcessionCaption"]=concessionCaption;
            row["ChildReceiptConcessionCaption"]=FeeReceiptPresentation.ConcessionCaption(currentLines);
            row["Payable"]=Convert.ToDecimal(row["FeesCovered"])-Money(header,"DiscountApplied");
            row["SnapshotVersion"]=complete?Convert.ToInt32(header["SnapshotVersion"]):1;
            if(acknowledgement)row["DocumentTitle"]="FEE CHANGE ACKNOWLEDGEMENT";
            row["ShowSettlement"]=true;row["CashReceived"]=Money(header,"CashReceived");row["CreditApplied"]=Money(header,"CreditApplied");
            row["CreditRemaining"]=Money(header,"CreditRemaining");row["CreditRetained"]=Money(header,"CashReceived")-Money(header,"CashApplied");
            row["ShowCreditRemaining"]=Money(header,"CreditApplied")>0 || Money(header,"CreditRemaining")>0;
            if(family)
            {
                row["FamilyRowKind"]=kind;row["FamilyAmountText"]=kind is "Fee" or "Summary"?amount.ToString("N0"):"";
                if(currentStudent is not null)
                {
                    decimal ChildMoney(string name)=>FamilyFeeDocumentPresentation.Money(currentStudent,name);
                    row["ChildAdmissionNumberText"]=currentStudent["AdmissionNumber"];
                    row["ChildStudentName"]=currentStudent["StudentName"];row["ChildFatherName"]=currentStudent["FatherName"];
                    row["ChildClassSection"]=currentLines.Select(line=>Text(line,"ClassName")+(string.IsNullOrWhiteSpace(Text(line,"SectionName"))?"":" - "+Text(line,"SectionName")))
                        .Distinct().Count()==1?currentLines.Select(line=>Text(line,"ClassName")+(string.IsNullOrWhiteSpace(Text(line,"SectionName"))?"":" - "+Text(line,"SectionName"))).First():"";
                    var campuses=currentLines.Select(line=>Text(line,"CampusName")).Where(value=>value.Length>0).Distinct().ToArray();
                    row["ChildCampusName"]=campuses.Length==1?campuses[0]:campuses.Length>1?"Multiple campuses":"";
                    row["ChildFeesCovered"]=currentLines.Sum(line=>Money(line,"AmountBefore"));
                    row["ChildDiscountApplied"]=ChildMoney("DiscountApplied");
                    row["ChildPayable"]=Convert.ToDecimal(row["ChildFeesCovered"])-ChildMoney("DiscountApplied");
                    row["ChildCashReceived"]=currentStudent.Table.Columns.Contains("CashReceived")?ChildMoney("CashReceived"):ChildMoney("CashApplied");
                    row["ChildCreditRetained"]=Convert.ToDecimal(row["ChildCashReceived"])-ChildMoney("CashApplied");
                    row["ChildCreditApplied"]=ChildMoney("CreditApplied");row["ChildCreditRemaining"]=ChildMoney("CreditRemaining");
                    row["ChildTotalPaid"]=ChildMoney("CashApplied")+ChildMoney("CreditApplied");row["ChildNetPaid"]=row["ChildTotalPaid"];
                    row["ChildBalanceRemaining"]=ChildMoney("BalanceRemaining");
                    row["ChildShowDiscount"]=ChildMoney("DiscountApplied")>0m;row["ChildShowBalance"]=true;row["ChildShowSettlement"]=true;
                    row["ChildShowCreditRemaining"]=ChildMoney("CreditApplied")>0m||ChildMoney("CreditRemaining")>0m;
                }
            }
        }
        foreach(DataRow student in FeeStudentOrder.Rows(students,"AdmissionNumber"))
        {
            var child=lines.AsEnumerable().Where(row=>Convert.ToInt32(row["AdmissionId"])==Convert.ToInt32(student["AdmissionId"])).ToArray();
            currentStudent=student;currentLines=child;
            if(family) Add($"{Text(student,"AdmissionNumber")} · {Text(student,"StudentName")}","",0,"Student");
            if(family && child.Length==0)
                Add(FamilyFeeDocumentPresentation.Money(student,"CashReceived")>0m?"    Advance retained as student credit":"    No payment applied to this child","",0m,"Fee");
            foreach(var month in child.OrderBy(row=>Convert.ToDateTime(row["FeePeriod"]))
                .ThenBy(row=>FamilyFeeDocumentPresentation.Money(row,"FeeDisplayOrder"))
                .GroupBy(row=>Convert.ToDateTime(row["FeePeriod"]).ToString("MMM yyyy")))
            {
                if(family) Add("  "+month.Key,"",0,"Month");
                foreach(var line in month)
                {
                    string fee=FeeDocumentData.FeeName(Text(line,"FeeTypeName"));
                    if(visit)
                    {
                        decimal rate=Money(line,"RateAdjustment"),reversed=Money(line,"DiscountReversed"),settlement=Money(line,"SettlementAdjustment"),amount=Money(line,"BaseAmount");
                        bool carried=Convert.ToBoolean(line["IsCarriedForward"])&&Money(line,"OpeningBalance")>0m;
                        if(amount!=0m||rate==0m&&reversed==0m&&settlement==0m)
                            Add((family?"    ":"")+fee+(carried?" - Due":""),month.Key,amount,"Fee",line);
                        if(rate!=0m)Add("Fee adjustment - "+fee,month.Key,rate,"Fee",line);
                        if(reversed!=0m)Add("Discount withdrawn - "+fee,month.Key,reversed,"Fee",line);
                        if(settlement!=0m)Add((settlement>0m?"Refund / payment adjustment - ":"Payment / credit adjustment - ")+fee,month.Key,settlement,"Fee",line);
                        continue;
                    }
                    if(complete)
                    {
                        string kind=Text(line,"ConcessionKind");
                        if(kind=="Cancelled")fee+=$" — Cancelled (Rs. {Money(line,"CancelledAmount"):N0} removed)";
                    }
                    Add((family?"    ":"")+fee,month.Key,complete?Money(line,"GrossAmount"):Money(line,"AmountBefore"),"Fee",line);
                }
            }
            decimal previouslyPaid=complete&&!visit?child.Sum(line=>Money(line,"PreviouslyPaid")):0m;
            if(previouslyPaid>0m)Add("Less: Previously paid","",-previouslyPaid,"Fee",child.FirstOrDefault());
            if(family)
            {
                Add("  Student total","",Money(student,"CashApplied")+Money(student,"CreditApplied"),"Summary");
                if(Money(student,"BalanceRemaining")>0) Add("  Outstanding","",Money(student,"BalanceRemaining"),"Summary");
            }
        }
        if(data.Rows.Count==0) Add(acknowledgement?"No billed charges":"Advance retained as student credit","",0m,"Fee");
        return data;
    }


}
