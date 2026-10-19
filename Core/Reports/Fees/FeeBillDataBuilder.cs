namespace FMSC_SMS.Core;

internal static partial class FeeBillDataBuilder
{
    internal static DataTable BuildGrouped(string institutionName,string contactNumber,string address,string parent,DataTable charges)
    {
        var data=FeeDocumentData.Create();
        data.Columns.Add("FamilyRowKind",typeof(string));
        data.Columns.Add("FamilyAmountText",typeof(string));
        data.Columns.Add("BillStudentKey",typeof(string));
        data.Columns.Add("BillStudentName",typeof(string));
        decimal total=charges.AsEnumerable().Sum(r=>Convert.ToDecimal(r["Due"]));
        decimal discount=charges.AsEnumerable().Sum(r=>Convert.ToDecimal(r["DiscountAmount"]));
        string references=string.Join(", ",charges.AsEnumerable().Select(r=>Convert.ToString(r["Reference"])).Distinct());
        bool multipleStudents=charges.AsEnumerable().Select(r=>Convert.ToString(r["Reference"])).Distinct().Count()>1;
        string studentKey="",studentHeading="";
        void AddLine(string description,decimal amount,string kind,string period=""){
            var line=data.Rows.Add(1,institutionName,multipleStudents?"FAMILY FEE BILL":"STUDENT FEE BILL","PARENT COPY","",
                multipleStudents?parent:Convert.ToString(charges.Rows[0]["StudentName"]),references,"",
                DateTime.Now.ToString("dd-MMM-yyyy hh:mm tt"),"",
                description,amount,discount,total,0m,"","",discount>0m,false,
                contactNumber,address,"",multipleStudents?"":parent,"");
            line["DocumentKind"]="Bill";line["FamilyRowKind"]=kind;line["FamilyAmountText"]=kind=="Fee"?amount.ToString("N0"):"";
            line["BillSourceKey"]=charges.Columns.Contains("BillingChargeId")
                ?string.Join(",",charges.AsEnumerable().Select(row=>Convert.ToString(row["BillingChargeId"])).OrderBy(value=>value)):"";
            line["BillStudentKey"]=studentKey;line["BillStudentName"]=studentHeading;line["FeePeriod"]=FeeDocumentData.FormatPeriod(period);
        }
        // Group by the reference, never by name: siblings can share a name.
        foreach(var student in charges.AsEnumerable().GroupBy(r=>Convert.ToString(r["Reference"]))){
            studentKey=student.Key??"";studentHeading=$"{student.First()["StudentName"]} · {student.Key}";
            AddLine($"{student.First()["StudentName"]} · {student.Key}",0m,"Student");
            foreach(var month in student.GroupBy(r=>r["BillingMonth"] is DateTime date?new DateTime(date.Year,date.Month,1):new DateTime(1,1,1)).OrderBy(g=>g.Key)){
                AddLine("  "+(month.Key.Year==1?"One-time fees":month.Key.ToString("MMM yyyy")),0m,"Month");
                foreach(var fee in month.OrderBy(r=>charges.Columns.Contains("FeeDisplayOrder")?Convert.ToInt32(r["FeeDisplayOrder"]):0).ThenBy(r=>Convert.ToString(r["FeeTypeName"])))
                    AddLine(Convert.ToString(fee["FeeTypeName"])??"",Convert.ToDecimal(fee["Due"]),"Fee",month.Key.Year==1?"One-time fees":month.Key.ToString("MMM yyyy"));
            }
        }
        return data;
    }


}
