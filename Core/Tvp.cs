namespace FMSC_SMS.Core;
public static class Tvp
{
    public static DataTable FeeAllocations(IEnumerable<(long chargeId, decimal amount)> rows)
    {
        var t = new DataTable();
        t.Columns.Add("BillingChargeId", typeof(long));
        t.Columns.Add("Amount", typeof(decimal));
        foreach (var r in rows)
            t.Rows.Add(r.chargeId, r.amount);
        return t;
    }
    /// <summary>The allocation table for one whole family collection, matching dbo.FamilyFeeAllocationType. Discount lines stay negative, exactly as they are in the per-student table, so dbo.ProcessFeeTransaction keeps receiving the same shape it always did.</summary>
    public static DataTable FamilyAllocations(
        IEnumerable<(int admissionId, int enrollmentId, IEnumerable<(long chargeId, decimal amount)> allocations)> groups)
    {
        var t = new DataTable();
        t.Columns.Add("AdmissionId", typeof(int));
        t.Columns.Add("EnrollmentId", typeof(int));
        t.Columns.Add("BillingChargeId", typeof(long));
        t.Columns.Add("Amount", typeof(decimal));
        foreach (var g in groups)
            foreach (var a in g.allocations)
                t.Rows.Add(g.admissionId, g.enrollmentId, a.chargeId, a.amount);
        return t;
    }
    /// <summary>( / ) - the whole fee-configuration plan for dbo.ApplyFeeConfigurationPlan, matching dbo.FeeConfigurationPlanType column for column.</summary>
    public static DataTable FeeConfigurationPlan(
        IEnumerable<(
            int feeConfigurationId,
            int campusId,
            int classId,
            int feeTypeId,
            decimal amount,
            string? frequency,
            string? remarks)> rows)
    {
        var t = new DataTable();
        t.Columns.Add("FeeConfigurationId", typeof(int));
        t.Columns.Add("CampusId", typeof(int));
        t.Columns.Add("ClassId", typeof(int));
        t.Columns.Add("FeeTypeId", typeof(int));
        t.Columns.Add("Amount", typeof(decimal));
        t.Columns.Add("Frequency", typeof(string));
        t.Columns.Add("Remarks", typeof(string));
        foreach (var r in rows)
            t.Rows.Add(
                r.feeConfigurationId,
                r.campusId,
                r.classId,
                r.feeTypeId,
                r.amount,
                (object?)r.frequency ?? DBNull.Value,
                (object?)r.remarks ?? DBNull.Value);
        return t;
    }
    public static DataTable Enrollments(IEnumerable<int> ids)
    {
        var t = new DataTable();
        t.Columns.Add("EnrollmentId", typeof(int));
        foreach (var i in ids)
            t.Rows.Add(i);
        return t;
    }
    public static DataTable FeeTypeIds(IEnumerable<int> ids)
    {
        var t = new DataTable();
        t.Columns.Add("FeeTypeId", typeof(int));
        foreach (var i in ids)
            t.Rows.Add(i);
        return t;
    }
    public static DataTable AdmissionIds(IEnumerable<int> ids)
    {
        var t = new DataTable();
        t.Columns.Add("AdmissionId", typeof(int));
        foreach (var i in ids)
            t.Rows.Add(i);
        return t;
    }
    /// <summary>rows for dbo.StudentFeeDiscountType, consumed by the current edit API, dbo.SaveStudentDetailsWithFirstEnrollment. One row means "this assignment keeps this discount".</summary>
    public static DataTable StudentFeeDiscounts(IEnumerable<(int studentFeeAssignmentId, decimal discountAmount)> rows)
    {
        var t = new DataTable();
        t.Columns.Add("StudentFeeAssignmentId", typeof(int));
        t.Columns.Add("DiscountAmount", typeof(decimal));
        foreach (var r in rows)
            t.Rows.Add(r.studentFeeAssignmentId, r.discountAmount);
        return t;
    }
    public static DataTable AdmissionFeeDiscounts(IEnumerable<(int feeTypeId, decimal discount)> rows)
    {
        var t = new DataTable();
        t.Columns.Add("FeeTypeId", typeof(int));
        t.Columns.Add("DiscountAmount", typeof(decimal));
        foreach (var r in rows)
            t.Rows.Add(r.feeTypeId, r.discount);
        return t;
    }
    public static DataTable ReturnFeeChoices(
        IEnumerable<(int feeTypeId, decimal amount, decimal discount)> rows)
    {
        var t = new DataTable();
        t.Columns.Add("FeeTypeId", typeof(int));
        t.Columns.Add("Amount", typeof(decimal));
        t.Columns.Add("DiscountAmount", typeof(decimal));
        foreach (var r in rows)
            t.Rows.Add(r.feeTypeId, r.amount, r.discount);
        return t;
    }
    public static DataTable ManualChargeSelection(IEnumerable<int> enrollmentIds)
    {
        var t = new DataTable();
        t.Columns.Add("EnrollmentId", typeof(int));
        foreach (var id in enrollmentIds.Distinct())
            t.Rows.Add(id);
        return t;
    }
    /// <summary>Rows for dbo.PostingSelectionType, consumed by dbo.PostSelectedMonthlyFees. BillingMonth is sent as the first of its month because the procedure's duplicate rule and filtered unique index are both keyed on the month value itself.</summary>
    public static DataTable PostingSelections(IEnumerable<(int enrollmentId, int feeTypeId, DateTime billingMonth)> rows)
    {
        var t = new DataTable();
        t.Columns.Add("EnrollmentId", typeof(int));
        t.Columns.Add("FeeTypeId", typeof(int));
        t.Columns.Add("BillingMonth", typeof(DateTime));
        foreach (var r in rows)
            t.Rows.Add(
                r.enrollmentId,
                r.feeTypeId,
                new DateTime(r.billingMonth.Year, r.billingMonth.Month, 1));
        return t;
    }
    /// <summary>Builds dbo.PostingSelectionType from preview rows. If a column the selection needs is missing, that must be stated in terms of the preview, not as a DataRow indexer error.</summary>
    public static DataTable PostingSelectionsFromPreview(DataTable preview, Func<DataRow, bool> include)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(include);
        foreach (string required in new[] { "EnrollmentId", "FeeTypeId", "BillingMonth" })
        {
            if (!preview.Columns.Contains(required))
            {
                throw new InvalidOperationException(
                    $"The eligible-fees preview did not return '{required}', so a " +
                    "posting selection cannot be built from it. The preview and " +
                    "dbo.PostingSelectionType must agree on " +
                    "(EnrollmentId, FeeTypeId, BillingMonth).");
            }
        }
        return PostingSelections(
            preview.Rows
                .Cast<DataRow>()
                .Where(include)
                .Select(row => (
                    Convert.ToInt32(row["EnrollmentId"]),
                    Convert.ToInt32(row["FeeTypeId"]),
                    Convert.ToDateTime(row["BillingMonth"]))));
    }
}
