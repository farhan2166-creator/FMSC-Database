using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Finance.Collection;

public sealed partial class FeeCollectionPage
{
    private async Task<(int ClassPosition,string Number,HashSet<long> MonthlyCharges)> ReadSettlementMetadataAsync(int studentId)
    {
        var classesTask=DatabaseHelper.QueryUserAsync("GetClasses");
        var contextTask=DatabaseHelper.QuerySqlAsync("""
            EXEC dbo.RequirePermission @UserId,N'Fees.View';
            SELECT COALESCE(S.AdmissionNumber,S.QuickReference,N'') AdmissionNumber,P.ClassId,
                B.BillingChargeId,
                CONVERT(bit,CASE WHEN COALESCE(Original.ChargeKind,B.ChargeKind)=N'Monthly' THEN 1 ELSE 0 END) IsMonthly
            FROM dbo.Students S
            OUTER APPLY(SELECT TOP(1) E.ClassSectionId FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId
                ORDER BY CASE WHEN E.Status=N'Active' THEN 0 WHEN E.Status=N'Pending' THEN 1 ELSE 2 END,E.EnrollmentId DESC) E
            LEFT JOIN dbo.CampusClassSections P ON P.ClassSectionId=E.ClassSectionId
            LEFT JOIN dbo.BillingCharges B ON B.AdmissionId=S.AdmissionId
            LEFT JOIN dbo.BillingCharges Original ON Original.BillingChargeId=B.CorrectionOfBillingChargeId
            WHERE S.AdmissionId=@AdmissionId;
            """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@AdmissionId",studentId));
        await Task.WhenAll(classesTask,contextTask);
        var context=await contextTask;
        if(context.Rows.Count==0 || context.Rows[0].IsNull("ClassId"))
            throw new InvalidOperationException("The student's class could not be loaded for the payment split. Review their placement and reload.");
        var catalog=LookupDisplayOrder.ForBinding(await classesTask,"ClassId");
        int classId=Convert.ToInt32(context.Rows[0]["ClassId"]);
        int position=catalog.AsEnumerable().Select(row=>Convert.ToInt32(row["ClassId"])).ToList().IndexOf(classId);
        if(position<0)throw new InvalidOperationException("The student's class is missing from the class order. Review class setup and reload.");
        return (position,Convert.ToString(context.Rows[0]["AdmissionNumber"])??"",
            context.AsEnumerable().Where(row=>!row.IsNull("BillingChargeId") && Convert.ToBoolean(row["IsMonthly"]))
                .Select(row=>Convert.ToInt64(row["BillingChargeId"])).ToHashSet());
    }

    // One child's charges stay together. Class order comes from the configured catalog,
    // and official admission numbers (including letter suffixes) break same-class ties.
    private static (int Class,long Number,string Suffix,int Student,int Kind,DateTime MonthlyPeriod,decimal Payable,DateTime Period,long Charge)
        SettlementKey(DataGridViewRow row,Dictionary<long,decimal>? discounts=null,Dictionary<long,decimal>? credits=null)
    {
        int studentId=GetInt(row.Cells["AdmissionId"].Value);
        var identity=row.Cells["StudentName"].Tag is ValueTuple<int,string> loaded
            ? loaded : (int.MaxValue,studentId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        long number=long.MaxValue;string suffix=identity.Item2;
        if(AdmissionNumbers.TryNormalize(identity.Item2,out string normalized))
        {
            string digits=new(normalized.TakeWhile(char.IsDigit).ToArray());
            number=long.Parse(digits,System.Globalization.CultureInfo.InvariantCulture);suffix=normalized[digits.Length..];
        }
        bool monthly=row.Cells["FeeTypeName"].Tag is bool value ? value : row.Cells["BillingChargeId"].Tag is int rank && rank==0;
        long id=GetLong(row.Cells["BillingChargeId"].Value);
        decimal payable=Math.Max(0m,GetDecimal(row.Cells["Balance"].Value)-
            (discounts?.GetValueOrDefault(id)??0m)-(credits?.GetValueOrDefault(id)??0m));
        return (identity.Item1,number,suffix,studentId,monthly?0:1,monthly?ChargePeriod(row):DateTime.MinValue,
            monthly?0m:payable,ChargePeriod(row),id);
    }

    private IEnumerable<DataGridViewRow> SettlementRows(Dictionary<long,decimal>? discounts=null,Dictionary<long,decimal>? credits=null) =>
        charges.Rows.Cast<DataGridViewRow>().Where(row=>!row.IsNewRow&&!IsWaiverReviewRow(row)).OrderBy(row=>SettlementKey(row,discounts,credits));

    // Keep discount distribution independent: largest balance first, with its
    // established tuition/date tie order. This change is to payment allocation.
    private IEnumerable<DataGridViewRow> DiscountTieRows() => charges.Rows.Cast<DataGridViewRow>()
        .Where(row=>!row.IsNewRow&&!IsWaiverReviewRow(row))
        .OrderBy(row=>row.Cells["BillingChargeId"].Tag is int rank?rank:12)
        .ThenBy(ChargePeriod).ThenBy(row=>GetLong(row.Cells["BillingChargeId"].Value));
}
