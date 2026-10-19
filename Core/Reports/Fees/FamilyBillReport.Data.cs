namespace FMSC_SMS.Core;

internal static partial class FamilyBillReport
{
    internal static DataTable BuildData(string institutionName,string contactNumber,string address,string parent,DataTable charges)
    {
        using var grouped=FeeBillDataBuilder.BuildGrouped(institutionName,contactNumber,address,parent,charges);
        return PrepareData(grouped);
    }
    internal static DataTable PrepareData(DataTable grouped)
    {
        var data=grouped.Clone();
        string previousStudent="",previousPeriod="";
        foreach(DataRow row in grouped.Rows)
            if(Convert.ToString(row["FamilyRowKind"])=="Fee")
            {
                data.ImportRow(row);
                string student=Convert.ToString(row["BillStudentKey"])??"",period=Convert.ToString(row["FeePeriod"])??"";
                if(student==previousStudent && period==previousPeriod)data.Rows[^1]["FeePeriod"]="";
                previousStudent=student;previousPeriod=period;
            }
        return data;
    }


}
