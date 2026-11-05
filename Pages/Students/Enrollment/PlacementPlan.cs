using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace FMSC_SMS.Pages.Students.Enrollment;

internal sealed record PlacementPlanItem(int EnrollmentId, int SourcePlacementId, string Stamp,
    string Action, int DestinationPlacementId, string? FeePolicy = null, int UndoItemId = 0);

internal static class PlacementPlan
{
    internal static Task<DataTable> ReadSourcesAsync(int year) => DatabaseHelper.QuerySqlAsync("""
        EXEC dbo.RequirePermission @UserId,N'Enrollment.Manage';
        SELECT DISTINCT P.ClassSectionId,P.CampusId,P.ClassId,P.SectionId
        FROM dbo.Enrollments E JOIN dbo.CampusClassSections P ON P.ClassSectionId=E.ClassSectionId
        WHERE E.Status=N'Active' AND E.AcademicYearId=@Year;
        """, DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@Year",year));

    internal static string Serialize(IEnumerable<PlacementPlanItem> items) => JsonSerializer.Serialize(items);

    internal static Task<DataTable> ReadAsync(int year, int campus, int cls, int section) => DatabaseHelper.QuerySqlAsync("""
        EXEC dbo.RequirePermission @UserId,N'Enrollment.Manage';
        SELECT E.EnrollmentId,E.AdmissionId,S.AdmissionNumber,S.StudentName,
            E.ClassSectionId SourcePlacementId,CONVERT(varchar(33),E.UpdatedAt,126) Stamp,
            P.CampusId,P.ClassId,P.SectionId,C.CampusName,K.ClassName,T.SectionName,
            E.BillingReady,E.PlacementConfirmedAt,S.LegacySetupPending,
            CASE WHEN E.BillingReady=0 THEN N'Awaiting return / billing review' ELSE N'Active' END PlacementState
        FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId
        JOIN dbo.CampusClassSections P ON P.ClassSectionId=E.ClassSectionId
        JOIN dbo.Campuses C ON C.CampusId=P.CampusId JOIN dbo.Classes K ON K.ClassId=P.ClassId
        JOIN dbo.Sections T ON T.SectionId=P.SectionId
        WHERE E.Status=N'Active' AND E.AcademicYearId=@Year
            AND (@Campus=0 OR P.CampusId=@Campus) AND (@Class=0 OR P.ClassId=@Class)
            AND (@Section=0 OR P.SectionId=@Section)
        ORDER BY C.CampusName,K.DisplayOrder,T.DisplayOrder,S.StudentName,E.EnrollmentId;
        """, DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@Year",year),
        DatabaseHelper.Int("@Campus",campus),DatabaseHelper.Int("@Class",cls),DatabaseHelper.Int("@Section",section));

    internal static Task<DataTable> RunAsync(Guid request,string json,int destinationYear,DateTime date,
        string reason,bool carry,bool preview,string? reviewHash=null) => Task.Run(()=> {
        using var connection=DatabaseHelper.GetConnection(); DatabaseHelper.OpenConnection(connection);
        using var command=new SqlCommand("ApplyStudentPlacementPlan",connection){CommandType=CommandType.StoredProcedure,CommandTimeout=300};
        command.Parameters.Add(DatabaseHelper.Int("@UserId",CurrentUser.UserId));
        command.Parameters.AddRange(new SqlParameter[]{
            new SqlParameter("@RequestId",SqlDbType.UniqueIdentifier){Value=request},
            new SqlParameter("@Plan",SqlDbType.NVarChar,-1){Value=json},
            DatabaseHelper.Int("@DestinationYearId",destinationYear),DatabaseHelper.Date("@EffectiveDate",date),
            DatabaseHelper.P("@Reason",reason),DatabaseHelper.Bit("@CarryConcessions",carry),DatabaseHelper.Bit("@Preview",preview),DatabaseHelper.P("@ExpectedReviewHash",reviewHash)});
        using var reader=command.ExecuteReader();
        DataTable? result=null;
        do {
            if(reader.FieldCount!=1 || reader.GetName(0)!="ResultJson") { while(reader.Read()) {} continue; }
            var table=new DataTable(); table.Columns.Add("ResultJson");
            while(reader.Read())table.Rows.Add(reader.GetString(0)); result=table;
        } while(reader.NextResult());
        return result??throw new InvalidOperationException("The placement plan did not return a saved outcome. Check plan history before retrying.");
    });
}
