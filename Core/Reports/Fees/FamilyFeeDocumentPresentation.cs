namespace FMSC_SMS.Core;

// Values used only by the continuous family printout. Saved bills, collection
// snapshots and individual receipt calculations keep their original values.
internal static class FamilyFeeDocumentPresentation
{
    internal static readonly string[] IdentityFields={"AdmissionNumberText","StudentName","FatherName","ClassSection","CampusName"};
    internal static readonly string[] MoneyFields={"FeesCovered","DiscountApplied","Payable","CashReceived","CreditApplied","CreditRetained","CreditRemaining",
        "TotalPaid","NetPaid","BalanceRemaining","OriginalPaid","RefundAmount","CorrectionAmount","ReversedAmount","BillPreviouslySettled"};
    internal static readonly string[] FlagFields={"ShowDiscount","ShowBalance","ShowSettlement","ShowAdjustments","ShowCreditRemaining","ShowBillPreviouslySettled"};

    internal static void AddChildColumns(DataTable data)
    {
        foreach(string name in IdentityFields.Concat(new[]{"BillDiscountCaption","BillDiscountText","BillPaymentPrompt","ReceiptMessage"}))
            if(!data.Columns.Contains("Child"+name))data.Columns.Add("Child"+name,typeof(string)).DefaultValue="";
        foreach(string name in MoneyFields)
            if(!data.Columns.Contains("Child"+name))data.Columns.Add("Child"+name,typeof(decimal)).DefaultValue=0m;
        foreach(string name in FlagFields)
            if(!data.Columns.Contains("Child"+name))data.Columns.Add("Child"+name,typeof(bool)).DefaultValue=false;
    }

    internal static decimal Money(DataRow row,string field)=>row.Table.Columns.Contains(field)&&!row.IsNull(field)?Convert.ToDecimal(row[field]):0m;

    internal static DataTable Receipt(DataTable source)
    {
        using var prepared=FamilyReceiptReport.PrepareData(source);
        var data=prepared.Copy();AddChildColumns(data);AddSummaryColumns(data);
        foreach(var page in data.AsEnumerable().GroupBy(row=>row["ReceiptPageKey"]))
        {
            var first=page.First();
            foreach(var child in page.GroupBy(row=>row["ReceiptStudentKey"]))
            {
                var identity=child.First();
                // The legacy converter also supplies child fields. This fallback
                // keeps already prepared, older in-memory data readable.
                if(string.IsNullOrWhiteSpace(Convert.ToString(identity["ChildAdmissionNumberText"])))
                {
                    var heading=Convert.ToString(identity["ReceiptStudentName"])??"";
                    int separator=heading.IndexOf(" · ",StringComparison.Ordinal);
                    foreach(var row in child)
                    {
                        row["ChildAdmissionNumberText"]=separator<0?"":heading[..separator];
                        row["ChildStudentName"]=separator<0?heading:heading[(separator+3)..];
                        row["ChildFatherName"]=row["StudentName"];
                        row["ChildFeesCovered"]=child.Sum(line=>Money(line,"Amount"));
                        row["ChildTotalPaid"]=Money(identity,"StudentSettled");
                        row["ChildNetPaid"]=Money(identity,"StudentSettled");
                        row["ChildCashReceived"]=Money(identity,"StudentSettled");
                        row["ChildPayable"]=row["ChildFeesCovered"];
                        row["ChildBalanceRemaining"]=Money(identity,"StudentOutstanding");
                        row["ChildShowBalance"]=row["ShowBalance"];
                        row["ChildShowSettlement"]=row["ShowSettlement"];
                    }
                }
                bool visible=Money(identity,"ChildCashReceived")>0m||Money(identity,"ChildCreditApplied")>0m
                    ||Money(identity,"ChildBalanceRemaining")>0m||Money(identity,"ChildCreditRetained")>0m
                    ||Money(identity,"ChildNetPaid")>0m||Money(identity,"ChildOriginalPaid")>0m;
                foreach(var row in child)
                {
                    row["ShowChildDocument"]=visible;
                    row["ChildReceiptMessage"]=Money(identity,"ChildCashReceived")>0m||Money(identity,"ChildCreditApplied")>0m
                        ||Money(identity,"ChildNetPaid")>0m||Money(identity,"ChildOriginalPaid")>0m
                        ?"Thank you for your payment":"No payment applied to this child";
                }
            }
            decimal total=!Convert.ToBoolean(first["ShowSettlement"])
                ?page.Where(row=>Convert.ToBoolean(row["ShowChildDocument"])).Sum(row=>Money(row,"Amount"))
                :Money(first,"FeesCovered");
            foreach(var row in page)
            {
                row["GroupFeeTotal"]=total;
                row["GroupDiscount"]=Money(first,"DiscountApplied");
                row["GroupPayable"]=total-Money(first,"DiscountApplied");
                row["GroupReceived"]=Convert.ToBoolean(first["ShowSettlement"])?Money(first,"CashReceived")
                    :page.GroupBy(item=>item["ReceiptStudentKey"]).Sum(child=>Money(child.First(),"ChildNetPaid"));
                row["GroupDue"]=Money(first,"BalanceRemaining");
                row["GroupCreditApplied"]=Money(first,"CreditApplied");
                row["ShowGroupCreditApplied"]=Money(first,"CreditApplied")>0m;
            }
            SetGroupDisplay(page);
        }
        return data;
    }

    internal static DataTable Bill(DataTable source)
    {
        using var prepared=FamilyBillReport.PrepareData(source);
        if(prepared.AsEnumerable().Any(row=>Money(row,"DiscountApplied")>0m)
            &&(!prepared.Columns.Contains("BillLineDiscount")||prepared.AsEnumerable().Any(row=>row.IsNull("BillLineDiscount"))))
            throw new InvalidOperationException("This older saved bill has no per-child discount breakdown. Print a fresh bill from the student's current fees.");
        var data=prepared.Copy();AddChildColumns(data);AddSummaryColumns(data);
        foreach(var page in data.AsEnumerable().GroupBy(row=>row["ReceiptPageKey"]))
        {
            decimal total=0m,discount=0m,due=0m,settled=0m;
            foreach(var child in page.GroupBy(row=>row["BillStudentKey"]))
            {
                var first=child.First();
                decimal childDue=child.Sum(row=>Money(row,"Amount"));
                bool breakdown=data.Columns.Contains("BillGrossAmount")&&child.All(row=>!row.IsNull("BillGrossAmount")&&!row.IsNull("BillLineDiscount"));
                decimal childTotal=child.Sum(row=>breakdown?Money(row,"BillGrossAmount"):Money(row,"Amount"));
                decimal childDiscount=breakdown?child.Sum(row=>Money(row,"BillLineDiscount")):0m;
                decimal childSettled=childTotal-childDiscount-childDue;
                string heading=Convert.ToString(first["BillStudentName"])??"";
                string reference=Convert.ToString(first["BillStudentKey"])??"";
                string name=heading.StartsWith(reference+" · ",StringComparison.Ordinal)?heading[(reference.Length+3)..]:heading;
                foreach(var row in child)
                {
                    row["FeeDescription"]=(Convert.ToString(row["FeeDescription"])??"").Replace(" — Waived","");
                    row["ShowChildDocument"]=childDue>0m||childDiscount>0m;
                    row["ChildAdmissionNumberText"]=reference;row["ChildStudentName"]=name;
                    // New snapshots retain the parent and campus per child. Old
                    // family snapshots only retained the common parent heading.
                    if(string.IsNullOrWhiteSpace(Convert.ToString(row["ChildFatherName"])))row["ChildFatherName"]=row["StudentName"];
                    row["ChildFeesCovered"]=childTotal;row["ChildDiscountApplied"]=childDiscount;
                    row["ChildPayable"]=childTotal-childDiscount;row["ChildTotalPaid"]=childDue;
                    row["ChildBillPreviouslySettled"]=childSettled;row["ChildShowBillPreviouslySettled"]=childSettled>0m;
                    row["ChildShowDiscount"]=childDiscount>0m;
                    row["ChildBillDiscountCaption"]="Less: Discount / Waived";
                    row["ChildBillDiscountText"]=(-childDiscount).ToString("N0");
                    row["ChildBillPaymentPrompt"]=childDue>0m?"Please pay at the fee counter":"No payment required";
                }
                if(childDue>0m||childDiscount>0m){total+=childTotal;discount+=childDiscount;due+=childDue;settled+=childSettled;}
            }
            foreach(var row in page)
            {
                row["GroupFeeTotal"]=total;row["GroupDiscount"]=discount;row["GroupPayable"]=total-discount;
                row["GroupDue"]=due;row["GroupPreviouslySettled"]=settled;row["ShowGroupPreviouslySettled"]=settled>0m;
            }
            SetGroupDisplay(page);
        }
        // The line display field is used by the individual bill rows too.
        data.Columns.Add("BillDisplayAmount",typeof(decimal));
        foreach(DataRow row in data.Rows)row["BillDisplayAmount"]=data.Columns.Contains("BillGrossAmount")&&!row.IsNull("BillGrossAmount")?row["BillGrossAmount"]:row["Amount"];
        return data;
    }

    private static void SetGroupDisplay(IEnumerable<DataRow> page)
    {
        var rows=page.ToArray();
        var visible=rows.Where(row=>Convert.ToBoolean(row["ShowChildDocument"])).ToArray();
        string childKey=rows[0].Table.Columns.Contains("ReceiptStudentKey")?"ReceiptStudentKey":"BillStudentKey";
        bool showSummary=visible.Select(row=>row[childKey]).Distinct().Count()!=1;
        var campuses=visible
            .Select(row=>Convert.ToString(row["ChildCampusName"])??"").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string heading=campuses.Length==0||campuses.Any(string.IsNullOrWhiteSpace)?"":campuses.Length==1?campuses[0]:"Multiple campuses";
        foreach(var row in rows){row["CampusName"]=heading;row["ShowGroupSummary"]=showSummary;}
    }

    private static void AddSummaryColumns(DataTable data)
    {
        data.Columns.Add("ShowChildDocument",typeof(bool)).DefaultValue=false;
        data.Columns.Add("ShowGroupSummary",typeof(bool)).DefaultValue=false;
        foreach(string field in new[]{"GroupFeeTotal","GroupDiscount","GroupPayable","GroupReceived","GroupDue","GroupCreditApplied","GroupPreviouslySettled"})
            data.Columns.Add(field,typeof(decimal)).DefaultValue=0m;
        data.Columns.Add("ShowGroupCreditApplied",typeof(bool)).DefaultValue=false;
        data.Columns.Add("ShowGroupPreviouslySettled",typeof(bool)).DefaultValue=false;
    }
}
