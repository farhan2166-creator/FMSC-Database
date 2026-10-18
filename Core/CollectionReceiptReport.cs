namespace FMSC_SMS.Core;

internal static class CollectionReceiptReport
{
    internal static async Task<bool> TryShowAsync(Form? owner,InstitutionProfile.PrintIdentity institution,string receiptNumber)
    {
        using var result=await DatabaseHelper.QueryUserSetAsync("GetCollectionReceipt",DatabaseHelper.P("@ReceiptNumber",receiptNumber));
        if(result.Tables.Count<3 || result.Tables[0].Rows.Count==0) return false;
        using var data=Build(institution,result.Tables[0].Rows[0],result.Tables[1],result.Tables[2]);
        Show(owner,data);
        return true;
    }

    internal static DataTable Build(InstitutionProfile.PrintIdentity institution,DataRow header,DataTable students,DataTable lines)
    {
        var data=ReceiptReportData.Create();
        bool family=students.Rows.Count>1;
        if(family) {data.Columns.Add("FamilyRowKind",typeof(string));data.Columns.Add("FamilyAmountText",typeof(string));}
        decimal Money(DataRow row,string name)=>Convert.ToDecimal(row[name]);
        string Text(DataRow row,string name)=>Convert.ToString(row[name])??"";
        var first=students.Rows[0];
        decimal settled=Money(header,"CashApplied")+Money(header,"CreditApplied");
        string references=string.Join(", ",students.AsEnumerable().Select(row=>Text(row,"AdmissionNumber")));
        string parent=Text(first,"FatherName");
        void Add(string description,string period,decimal amount,string kind,DataRow? detail=null)
        {
            var row=data.Rows.Add(1,institution.Name,family?"FAMILY PAYMENT RECEIPT":"STUDENT PAYMENT RECEIPT","PARENT COPY",
                Text(header,"ReceiptNumber"),family?parent:Text(first,"StudentName"),references,
                family || detail is null?"":Text(detail,"ClassName")+(string.IsNullOrWhiteSpace(Text(detail,"SectionName"))?"":" - "+Text(detail,"SectionName")),
                Convert.ToDateTime(header["TransactionDate"]).ToString("dd-MMM-yyyy hh:mm tt"),family?$"{students.Rows.Count} students":"",
                description,amount,Money(header,"DiscountApplied"),settled,Money(header,"BalanceRemaining"),Text(header,"PaymentMethod"),
                Text(header,"ReceiptStatus"),Money(header,"DiscountApplied")>0,true,institution.ContactNumber,institution.Address,
                family || detail is null?"":Text(detail,"CampusName"),family?"":parent,period);
            row["NetPaid"]=settled;row["FeesCovered"]=lines.AsEnumerable().Sum(line=>Money(line,"AmountBefore"));
            row["ShowSettlement"]=true;row["CashReceived"]=Money(header,"CashReceived");row["CreditApplied"]=Money(header,"CreditApplied");
            row["CreditRemaining"]=Money(header,"CreditRemaining");row["CreditRetained"]=Money(header,"CashReceived")-Money(header,"CashApplied");
            row["ShowCreditRemaining"]=Money(header,"CreditApplied")>0 || Money(header,"CreditRemaining")>0;
            if(family) {row["FamilyRowKind"]=kind;row["FamilyAmountText"]=kind is "Fee" or "Summary"?amount.ToString("N0"):"";}
        }
        foreach(DataRow student in students.Rows)
        {
            var child=lines.AsEnumerable().Where(row=>Convert.ToInt32(row["AdmissionId"])==Convert.ToInt32(student["AdmissionId"])).ToArray();
            if(family) Add($"{Text(student,"StudentName")} · {Text(student,"AdmissionNumber")}","",0,"Student");
            foreach(var month in child.GroupBy(row=>Convert.ToDateTime(row["FeePeriod"]).ToString("MMM yyyy")))
            {
                if(family) Add("  "+month.Key,"",0,"Month");
                foreach(var line in month) Add((family?"    ":"")+Text(line,"FeeTypeName"),month.Key,Money(line,"Cash")+Money(line,"Credit"),"Fee",line);
            }
            if(family)
            {
                Add("  Student total","",Money(student,"CashApplied")+Money(student,"CreditApplied"),"Summary");
                if(Money(student,"BalanceRemaining")>0) Add("  Outstanding","",Money(student,"BalanceRemaining"),"Summary");
            }
        }
        if(data.Rows.Count==0) Add("Advance retained as student credit","",Money(header,"CashReceived"),"Fee");
        return data;
    }

    internal static DataTable CombineLegacyFamily(DataTable source)
    {
        var children=source.AsEnumerable().GroupBy(row=>Convert.ToInt32(row["ReceiptPageKey"])).ToArray();
        if(children.Length<2) return source.Copy();
        var data=source.Clone();data.Columns.Add("FamilyRowKind",typeof(string));data.Columns.Add("FamilyAmountText",typeof(string));
        var first=children[0].First();
        decimal Sum(string column)=>children.Sum(child=>Convert.ToDecimal(child.First()[column]));
        decimal total=Sum("NetPaid");
        bool showBalance=children.All(child=>Convert.ToBoolean(child.First()["ShowBalance"]));
        string references=string.Join(", ",children.Select(child=>Convert.ToString(child.First()["AdmissionNumberText"])));
        void Add(DataRow template,string description,decimal amount,string kind)
        {
            var row=data.NewRow();
            for(int index=0;index<source.Columns.Count;index++) row[index]=template[index];
            row["ReceiptPageKey"]=1;row["DocumentTitle"]="FAMILY PAYMENT RECEIPT";
            row["StudentName"]=first["FatherName"];row["FatherName"]="";row["AdmissionNumberText"]=references;row["ClassSection"]="";row["CampusName"]="";
            row["ReceiptStatus"]=string.Join("; ",children.Select(child=>Convert.ToString(child.First()["ReceiptStatus"])).Where(text=>!string.IsNullOrWhiteSpace(text)).Distinct());
            row["FamilyBatch"]=$"{children.Length} students";row["FeeDescription"]=description;row["FeePeriod"]="";
            row["Amount"]=amount;row["TotalPaid"]=total;row["NetPaid"]=total;
            foreach(string column in new[]{"OriginalPaid","RefundAmount","CorrectionAmount","ReversedAmount","DiscountApplied"}) row[column]=Sum(column);
            row["ShowAdjustments"]=children.Any(child=>Convert.ToBoolean(child.First()["ShowAdjustments"]));
            row["ShowBalance"]=showBalance;row["BalanceRemaining"]=showBalance?Sum("BalanceRemaining"):0m;
            row["FamilyRowKind"]=kind;row["FamilyAmountText"]=kind is "Fee" or "Summary"?amount.ToString("N0"):"";data.Rows.Add(row);
        }
        foreach(var child in children)
        {
            var identity=child.First();Add(identity,$"{identity["StudentName"]} · {identity["AdmissionNumberText"]}",0,"Student");
            foreach(var month in child.GroupBy(row=>Convert.ToString(row["FeePeriod"])))
            {
                if(!string.IsNullOrWhiteSpace(month.Key)) Add(identity,"  "+month.Key,0,"Month");
                foreach(var line in month) Add(identity,"    "+Convert.ToString(line["FeeDescription"]),Convert.ToDecimal(line["Amount"]),"Fee");
            }
        }
        return data;
    }
    internal static void Show(Form? owner,DataTable data)
    {
        string path=Path.Combine(AppContext.BaseDirectory,"Reports",data.Columns.Contains("FamilyRowKind")?"FamilyReceipt.rdlc":"StudentReceipt.rdlc");
        if(!data.Columns.Contains("FamilyRowKind")) ReceiptReportData.GroupPeriods(data);
        RdlcReportPreview.Show(owner,"Payment Receipt",path,"FeeReceiptData",data,
            reportDefinition:ReceiptReportLayout.SizeToContent(path,data));
    }
}