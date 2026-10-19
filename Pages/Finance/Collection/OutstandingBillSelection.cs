namespace FMSC_SMS.Pages.Finance.Collection;

internal enum BillBalanceComparison { Any, AtLeast, MoreThan, AtMost, LessThan, Exactly, Between }

internal sealed record OutstandingBillScope(int Campus,int Year,int Class,int Section,
    BillBalanceComparison Comparison,decimal Amount,decimal Through)
{
    internal bool Matches(decimal balance) => balance>0m && (Comparison switch {
        BillBalanceComparison.AtLeast => balance>=Amount,
        BillBalanceComparison.MoreThan => balance>Amount,
        BillBalanceComparison.AtMost => balance<=Amount,
        BillBalanceComparison.LessThan => balance<Amount,
        BillBalanceComparison.Exactly => balance==Amount,
        BillBalanceComparison.Between => balance>=Amount && balance<=Through,
        _ => true
    });
}

internal static class OutstandingBillSelection
{
    // Placement chooses students; their complete posted balance determines the
    // amount filter and the bill. No partial bill is implied by a placement filter.
    internal const string Query="""
        EXEC dbo.RequirePermission @UserId,N'Fees.View';
        SELECT S.AdmissionId,COALESCE(S.AdmissionNumber,S.QuickReference,CONCAT(N'ID-',S.AdmissionId)) AdmissionNumber,
          S.StudentName,S.FatherName,CA.CampusName,AY.YearName AcademicYear,
          CCS.CampusId,E.AcademicYearId,CCS.ClassId,CCS.SectionId,
          CONCAT(C.ClassName,CASE WHEN NULLIF(LTRIM(RTRIM(SEC.SectionName)),N'') IS NULL THEN N'' ELSE CONCAT(N' – ',SEC.SectionName) END) ClassSection,
          B.Outstanding
        FROM dbo.Students S
        OUTER APPLY(SELECT TOP(1) E.* FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId
          ORDER BY CASE WHEN E.Status=N'Active' THEN 0 ELSE 1 END,E.EnrollmentId DESC) E
        LEFT JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
        LEFT JOIN dbo.Campuses CA ON CA.CampusId=CCS.CampusId
        LEFT JOIN dbo.Classes C ON C.ClassId=CCS.ClassId
        LEFT JOIN dbo.Sections SEC ON SEC.SectionId=CCS.SectionId
        LEFT JOIN dbo.AcademicYears AY ON AY.AcademicYearId=E.AcademicYearId
        CROSS APPLY(SELECT SUM(dbo.GetFeeChargeBalance(BC.BillingChargeId)) Outstanding
          FROM dbo.BillingCharges BC WHERE BC.AdmissionId=S.AdmissionId AND BC.Status<>N'Cancelled') B
        WHERE B.Outstanding>0 AND (@CampusId=0 OR CCS.CampusId=@CampusId)
          AND (@AcademicYearId=0 OR E.AcademicYearId=@AcademicYearId)
          AND (@ClassId=0 OR CCS.ClassId=@ClassId) AND (@SectionId=0 OR CCS.SectionId=@SectionId)
        ORDER BY CA.DisplayOrder,C.DisplayOrder,SEC.DisplayOrder,
          COALESCE(S.AdmissionNumberBase,TRY_CONVERT(bigint,REPLACE(S.QuickReference,N'QR-',N''))),
          S.AdmissionNumberSuffix,S.AdmissionNumber,S.StudentName,S.AdmissionId;
        """;

    internal static DataTable Select(DataTable source,OutstandingBillScope scope)
    {
        var result=source.Clone();
        result.Columns.Add("Include",typeof(bool));
        foreach(DataRow row in source.Rows)
        {
            if(!scope.Matches(Convert.ToDecimal(row["Outstanding"])))continue;
            var selected=result.NewRow();
            foreach(DataColumn column in source.Columns)selected[column.ColumnName]=row[column];
            selected["Include"]=true;result.Rows.Add(selected);
        }
        return result;
    }

    internal static void Search(DataTable source,string text)
    {
        if(!source.Columns.Contains("SearchMatch"))source.Columns.Add("SearchMatch",typeof(bool));
        string term=text.Trim();
        foreach(DataRow row in source.Rows)row["SearchMatch"]=new[]{"AdmissionNumber","StudentName","FatherName"}
            .Any(column=>(Convert.ToString(row[column])??"").Contains(term,StringComparison.CurrentCultureIgnoreCase));
        source.DefaultView.RowFilter=term.Length==0?"":"SearchMatch = true";
    }
}
