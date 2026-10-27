using System.Data;
using Microsoft.Data.SqlClient;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Admissions;

internal static partial class Program
{
    private static void CheckHistoricalReturnSql()
    {
        string text=Environment.GetEnvironmentVariable("FMSC_HISTORICAL_RETURN_TEST_CONNECTION")??throw new InvalidOperationException("An owned clone is required.");
        var builder=new SqlConnectionStringBuilder(text);
        if(!System.Text.RegularExpressions.Regex.IsMatch(builder.InitialCatalog,@"^FMSC_HistoricalReturn_[0-9a-f]{32}$"))throw new InvalidOperationException("Only the owned historical-return clone may be changed.");
        using var connection=new SqlConnection(text);connection.Open();
        DataTable Query(string sql,params SqlParameter[] args)
        {
            using var cmd=new SqlCommand(sql,connection);cmd.CommandTimeout=60;cmd.Parameters.AddRange(args);
            var data=new DataTable();using var adapter=new SqlDataAdapter(cmd);adapter.Fill(data);return data;
        }
        int actor=Convert.ToInt32(Query("SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator';").Rows[0][0]);
        using var original=Query("""
            SELECT S.AdmissionId,S.AdmissionNumber,S.Status,E.EnrollmentId,E.AcademicYearId,E.EnrollmentDate,E.Status EnrollmentStatus,E.BillingReady,E.PlacementConfirmedAt,
              Y.StartDate AcademicYearStartDate,Y.EndDate AcademicYearEndDate,P.ClassId,P.CampusId,P.SectionId,P.ClassSectionId
            FROM dbo.Students S JOIN dbo.Enrollments E ON E.AdmissionId=S.AdmissionId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
            JOIN dbo.CampusClassSections P ON P.ClassSectionId=E.ClassSectionId
            WHERE S.AdmissionNumber IN(N'6590',N'6589a') AND E.Status=N'Active' AND Y.EndDate<CONVERT(date,GETDATE());
            """);
        Check(original.Rows.Count==2,"Both reported previous-year admissions are present in the owned clone");
        using var pending=Query("EXEC dbo.GetPendingReturnAdmissions @Actor;",new SqlParameter("@Actor",actor));
        foreach(DataRow student in original.Rows)
        {
            int id=student.Field<int>("AdmissionId"),enrollment=student.Field<int>("EnrollmentId");
            Check(!pending.AsEnumerable().Any(row=>row.Field<int>("AdmissionId")==id),"A historical admission is absent from the individual Confirm Return list");
            using var family=Query(FamilyAdmissionData.ReadQuery,new SqlParameter("@UserId",actor),new SqlParameter("@Admission",id),new SqlParameter("@Fees",false));
            var child=family.AsEnumerable().Single(row=>row.Field<int>("AdmissionId")==id);
            Check(!FamilyBillingData.AwaitsReturn(child)&&FamilyBillingData.Readiness(child)=="Previous Academic Year","Family Admission classifies the actual saved child as previous year");
            bool refused=false;
            try
            {
                using var choices=new DataTable();choices.Columns.Add("FeeTypeId",typeof(int));choices.Columns.Add("Amount",typeof(decimal));choices.Columns.Add("DiscountAmount",typeof(decimal));
                Query("EXEC dbo.ConfirmPromotedAdmission @Actor,@Enrollment,@ReturnDate,0,@Choices;",new SqlParameter("@Actor",actor),new SqlParameter("@Enrollment",enrollment),
                    new SqlParameter("@ReturnDate",student["EnrollmentDate"]),new SqlParameter("@Choices",SqlDbType.Structured){TypeName="dbo.ReturnFeeChoiceType",Value=choices});
            }
            catch(SqlException ex)when(ex.Number==53018){refused=true;}
            Check(refused,"Even a backdated direct return request is refused for an expired placement");
        }
        var source=original.Rows[0];int admission=source.Field<int>("AdmissionId");
        using var candidates=Query("EXEC dbo.GetBulkPromotionCandidates @Actor,@Year,@Campus,@Class,@Section;",new SqlParameter("@Actor",actor),
            new SqlParameter("@Year",source["AcademicYearId"]),new SqlParameter("@Campus",source["CampusId"]),new SqlParameter("@Class",source["ClassId"]),new SqlParameter("@Section",source["SectionId"]));
        Check(candidates.AsEnumerable().Any(row=>row.Field<int>("AdmissionId")==admission),"The historical student remains an eligible promotion source");
        int currentYear=Convert.ToInt32(Query("SELECT TOP(1) AcademicYearId FROM dbo.AcademicYears WHERE CONVERT(date,GETDATE()) BETWEEN StartDate AND EndDate ORDER BY StartDate DESC;").Rows[0][0]);
        using var decisions=new DataTable();decisions.Columns.Add("AdmissionId",typeof(int));decisions.Columns.Add("Decision");decisions.Rows.Add(admission,"HOLD");
        Query("EXEC dbo.BulkPromoteStudents @Actor,@SourceYear,@DestinationYear,@Campus,@Class,@Section,@Placement,@Date,N'Owned historical-return test',@Decisions;",
            new SqlParameter("@Actor",actor),new SqlParameter("@SourceYear",source["AcademicYearId"]),new SqlParameter("@DestinationYear",currentYear),
            new SqlParameter("@Campus",source["CampusId"]),new SqlParameter("@Class",source["ClassId"]),new SqlParameter("@Section",source["SectionId"]),
            new SqlParameter("@Placement",source["ClassSectionId"]),new SqlParameter("@Date",DateTime.Today),
            new SqlParameter("@Decisions",SqlDbType.Structured){TypeName="dbo.BulkPromotionDecisionType",Value=decisions});
        using var after=Query("EXEC dbo.GetPendingReturnAdmissions @Actor;",new SqlParameter("@Actor",actor));
        var returning=after.AsEnumerable().Single(row=>row.Field<int>("AdmissionId")==admission);int newEnrollment=returning.Field<int>("EnrollmentId");
        Check(newEnrollment!=source.Field<int>("EnrollmentId"),"An actual promotion batch creates a new current-year return candidate");
        using var freshFamily=Query(FamilyAdmissionData.ReadQuery,new SqlParameter("@UserId",actor),new SqlParameter("@Admission",admission),new SqlParameter("@Fees",false));
        Check(FamilyBillingData.AwaitsReturn(freshFamily.AsEnumerable().Single(row=>row.Field<int>("AdmissionId")==admission)),"Family Admission offers return after the new placement exists");
        using var newChoices=Query("SELECT A.FeeTypeId,A.Amount,A.DiscountAmount FROM dbo.StudentFeeAssignments A JOIN dbo.FeeTypes F ON F.FeeTypeId=A.FeeTypeId WHERE A.EnrollmentId=@Enrollment AND A.CancelledAt IS NULL AND F.IsMandatory=1;",new SqlParameter("@Enrollment",newEnrollment));
        Query("EXEC dbo.ConfirmPromotedAdmission @Actor,@Enrollment,@Date,0,@Choices;",new SqlParameter("@Actor",actor),new SqlParameter("@Enrollment",newEnrollment),new SqlParameter("@Date",DateTime.Today),
            new SqlParameter("@Choices",SqlDbType.Structured){TypeName="dbo.ReturnFeeChoiceType",Value=newChoices});
        using var finished=Query("EXEC dbo.GetPendingReturnAdmissions @Actor;",new SqlParameter("@Actor",actor));
        Check(!finished.AsEnumerable().Any(row=>row.Field<int>("AdmissionId")==admission),"A confirmed current-year return leaves the pending list");
    }
}
