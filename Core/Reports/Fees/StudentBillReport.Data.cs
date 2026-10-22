namespace FMSC_SMS.Core;

internal static partial class StudentBillReport
{
    internal sealed record Line(string FeeName,string Period,decimal Amount,decimal Discount,long ChargeId=0,int AdmissionId=0);

    internal static DataTable BuildData(string institutionName,string contactNumber,string address,string parent,DataTable charges)
    {
        using var grouped=FeeBillDataBuilder.BuildGrouped(institutionName,contactNumber,address,parent,charges);
        return FromGroupedData(grouped);
    }

    internal static DataTable BuildData(string institutionName,string contactNumber,string address,string studentName,string reference,
        string classSection,IReadOnlyList<Line> lines,bool showDiscount,string campusName,string fatherName,string sourceKey)
    {
        var data=FeeDocumentData.Create();
        decimal discount=showDiscount?lines.Sum(line=>line.Discount):0m,total=lines.Sum(line=>line.Amount);
        foreach(var line in lines)
        {
            var row=data.Rows.Add(1,institutionName,"STUDENT FEE BILL","PARENT COPY","",studentName,reference,classSection,
                DateTime.Now.ToString("dd-MMM-yyyy hh:mm tt"),"",line.FeeName,line.Amount,discount,total,0m,"","",discount>0m,false,
                contactNumber,address,campusName,fatherName,FeeDocumentData.FormatPeriod(line.Period));
            row["DocumentKind"]="Bill";row["BillSourceKey"]=sourceKey;
            row["BillChargeId"]=line.ChargeId;row["BillAdmissionId"]=line.AdmissionId;
        }
        return data;
    }
    internal static DataTable FromGroupedData(DataTable grouped)
    {
        var data=FeeDocumentData.Create();string month="",previousPeriod="";
        foreach(DataRow source in grouped.Rows)
        {
            string kind=Convert.ToString(source["FamilyRowKind"])??"";
            if(kind=="Month") {month=Convert.ToString(source["FeeDescription"])!.Trim();continue;}
            if(kind!="Fee") continue;
            var row=data.NewRow();foreach(DataColumn column in data.Columns) row[column.ColumnName]=source[column.ColumnName];
            string period=FeeDocumentData.FormatPeriod(month);
            row["FeeDescription"]=Convert.ToString(source["FeeDescription"])!.Trim();row["FeePeriod"]=period==previousPeriod?"":period;
            previousPeriod=period;data.Rows.Add(row);
        }
        return data;
    }
}
