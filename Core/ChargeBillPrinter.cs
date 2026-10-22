namespace FMSC_SMS.Core;

/// <summary>Prints posted outstanding bills through the established fee-document template.</summary>
public static class ChargeBillPrinter
{
    public static async Task<string?> PrintAsync(int admissionId,int enrollmentId,string studentName,string classSectionText)
    {
        if(admissionId<=0 || enrollmentId<=0) return "Select the student first, then print the bill.";
        var charges=await DatabaseHelper.QueryUserAsync("GetOutstandingCharges",DatabaseHelper.Int("@AdmissionId",admissionId),
            DatabaseHelper.Int("@EnrollmentId",enrollmentId),DatabaseHelper.Date("@AsOfDate",DateTime.Today));
        var lines=charges.AsEnumerable().Where(row=>Convert.ToDecimal(row["Balance"])>0)
            .Select(row=>new StudentBillReport.Line(Convert.ToString(row["FeeTypeName"])??"",
                row["BillingMonth"] is DateTime month?month.ToString("MMM yyyy"):"",Convert.ToDecimal(row["Balance"]),
                Math.Max(0m,Convert.ToDecimal(row["GrossAmount"])-Convert.ToDecimal(row["NetAmount"])),
                Convert.ToInt64(row["BillingChargeId"]),admissionId)).ToArray();
        if(lines.Length==0) return "There is nothing outstanding to bill for this student.";
        var institution=await InstitutionProfile.GetPrintIdentityAsync();
        if(string.IsNullOrWhiteSpace(institution.Name)) return "Check School Profile before printing the bill.";
        var identity=await FeeReceiptQueries.QueryStudentAsync(admissionId);
        await StudentBillReport.ShowAsync(null,institution,studentName,admissionId,classSectionText,lines,true,
            fatherName:identity.Rows.Count>0?Convert.ToString(identity.Rows[0]["FatherName"])??"":"",
            sourceKey:string.Join(",",charges.AsEnumerable().Where(row=>Convert.ToDecimal(row["Balance"])>0)
                .Select(row=>Convert.ToString(row["BillingChargeId"])).OrderBy(value=>value)));
        return null;
    }
}
