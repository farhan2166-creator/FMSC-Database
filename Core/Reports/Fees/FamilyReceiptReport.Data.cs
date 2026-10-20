namespace FMSC_SMS.Core;

internal static partial class FamilyReceiptReport
{
    internal static DataTable BuildData(InstitutionProfile.PrintIdentity institution,DataRow header,DataTable students,DataTable lines)
    {
        if(students.Rows.Count<2)throw new ArgumentException("A family receipt requires at least two students.",nameof(students));
        using var grouped=FeeReceiptDataBuilder.BuildGrouped(institution,header,students,lines,true);
        return PrepareData(grouped);
    }
    internal static DataTable CombineLegacyData(DataTable source)
    {
        var children=source.AsEnumerable().GroupBy(row=>Convert.ToInt32(row["ReceiptPageKey"])).ToArray();
        if(children.Length<2) return source.Copy();
        var data=source.Clone();data.Columns.Add("FamilyRowKind",typeof(string));data.Columns.Add("FamilyAmountText",typeof(string));
        var first=children[0].First();
        decimal Sum(string column)=>children.Sum(child=>Convert.ToDecimal(child.First()[column]));
        decimal total=Sum("NetPaid");
        bool showBalance=children.All(child=>Convert.ToBoolean(child.First()["ShowBalance"]));
        string references=string.Join(", ",children.Select(child=>Convert.ToString(child.First()["AdmissionNumberText"])));
        string campusHeading=FeeDocumentData.CampusHeading(source);
        void Add(DataRow template,string description,decimal amount,string kind)
        {
            var row=data.NewRow();
            for(int index=0;index<source.Columns.Count;index++) row[index]=template[index];
            row["ReceiptPageKey"]=1;row["DocumentTitle"]="FAMILY PAYMENT RECEIPT";
            row["StudentName"]=first["FatherName"];row["FatherName"]="";row["AdmissionNumberText"]=references;row["ClassSection"]="";row["CampusName"]=campusHeading;
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
    // Presentation only: turn the existing child/month/summary rows into real
    // fee rows for RDLC student groups. All settlement values stay unchanged.
    internal static DataTable PrepareData(DataTable source)
    {
        if(source.Columns.Contains("ReceiptStudentKey")) return source.Copy();
        var data=source.Clone();
        data.Columns.Add("ReceiptStudentKey",typeof(int));
        data.Columns.Add("ReceiptStudentName",typeof(string));
        data.Columns.Add("StudentSettled",typeof(decimal));
        data.Columns.Add("StudentOutstanding",typeof(decimal));
        data.Columns.Add("ShowStudentSettlement",typeof(bool));
        int key=0;string heading="",period="",lastPrintedPeriod="";
        var childRows=new List<DataRow>();
        foreach(DataRow sourceRow in source.Rows)
        {
            string kind=Convert.ToString(sourceRow["FamilyRowKind"])??"";
            string description=Convert.ToString(sourceRow["FeeDescription"])?.Trim()??"";
            if(kind=="Student") {key++;heading=description;period="";lastPrintedPeriod="";childRows.Clear();continue;}
            if(kind=="Month") {period=description;continue;}
            if(kind=="Summary")
            {
                foreach(var row in childRows)
                {
                    row["ShowStudentSettlement"]=true;
                    row[description=="Outstanding"?"StudentOutstanding":"StudentSettled"]=sourceRow["Amount"];
                }
                continue;
            }
            if(kind!="Fee") continue;
            var fee=data.NewRow();
            foreach(DataColumn column in source.Columns) fee[column.ColumnName]=sourceRow[column];
            fee["FeeDescription"]=description;
            string printedPeriod=FeeDocumentData.FormatPeriod(string.IsNullOrWhiteSpace(Convert.ToString(sourceRow["FeePeriod"]))
                ?period:Convert.ToString(sourceRow["FeePeriod"])!);
            fee["FeePeriod"]=printedPeriod==lastPrintedPeriod?"":printedPeriod;lastPrintedPeriod=printedPeriod;
            fee["ReceiptStudentKey"]=key;fee["ReceiptStudentName"]=heading;
            fee["StudentSettled"]=0m;fee["StudentOutstanding"]=0m;fee["ShowStudentSettlement"]=false;
            data.Rows.Add(fee);childRows.Add(fee);
        }
        return data;
    }


}
