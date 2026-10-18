using System.Data;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Pages.Students.Enrollment;

internal static class MatchingSectionPromotion
{
    internal static Task<DataTable> RunAsync(DataTable decisions, int sourceYear, int destinationYear,
        int campus, int sourceClass, int sourceSection, int destinationClass, DateTime effective, string remarks,
        Dictionary<int,int> destinations, Dictionary<int,int> expectedSources,bool carryConcessions=false) =>
        Task.Run(() => Run(decisions, sourceYear, destinationYear, campus, sourceClass, sourceSection, destinationClass, effective, remarks, destinations, expectedSources,carryConcessions));

    private static DataTable Run(DataTable decisions, int sourceYear, int destinationYear,
        int campus, int sourceClass, int sourceSection, int destinationClass, DateTime effective, string remarks,
        Dictionary<int,int> destinations, Dictionary<int,int> expectedSources,bool carryConcessions)
    {
        using var connection = DatabaseHelper.GetConnection();
        DatabaseHelper.OpenConnection(connection);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        using var mapping = new SqlCommand("""
            EXEC dbo.RequirePermission @UserId,N'Enrollment.Manage';
            SELECT E.AdmissionId,S.SectionId,SEC.SectionName
            FROM @Decisions V
            JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.AdmissionId=V.AdmissionId
                AND E.AcademicYearId=@SourceYear AND E.Status=N'Active'
            JOIN dbo.CampusClassSections S WITH(HOLDLOCK) ON S.ClassSectionId=E.ClassSectionId
            JOIN dbo.Sections SEC ON SEC.SectionId=S.SectionId
            WHERE S.CampusId=@Campus AND S.ClassId=@SourceClass
                AND (@SourceSection=0 OR S.SectionId=@SourceSection);
            """, connection, transaction);
        mapping.Parameters.AddRange(new[] {
            DatabaseHelper.Int("@UserId",CurrentUser.UserId), DatabaseHelper.Int("@SourceYear",sourceYear),
            DatabaseHelper.Int("@Campus",campus), DatabaseHelper.Int("@SourceClass",sourceClass),
            DatabaseHelper.Int("@SourceSection",sourceSection), DatabaseHelper.Int("@DestinationClass",destinationClass),
            new SqlParameter("@Decisions",SqlDbType.Structured) { TypeName="dbo.BulkPromotionDecisionType",Value=decisions }
        });
        var rows = new DataTable();
        using (var reader = mapping.ExecuteReader()) rows.Load(reader);
        var byStudent = rows.AsEnumerable().GroupBy(row => Convert.ToInt32(row["AdmissionId"])).ToDictionary(group => group.Key, group => group.ToArray());
        var missing = new HashSet<string>();
        foreach (DataRow decision in decisions.Rows)
        {
            if (!byStudent.TryGetValue(Convert.ToInt32(decision["AdmissionId"]),out var matches) || matches.Length!=1)
                throw new InvalidOperationException("A student's source placement changed or is ambiguous. Reload candidates before promoting.");
            int source=Convert.ToInt32(matches[0]["SectionId"]);
            if(!expectedSources.TryGetValue(Convert.ToInt32(decision["AdmissionId"]),out int expected) || expected!=source)
                throw new InvalidOperationException("A student's source section changed. Reload and review the section mapping again.");
            if (Convert.ToString(decision["Decision"]) == "PROMOTE" && (!destinations.TryGetValue(source,out int target) || target<=0))
                missing.Add(Convert.ToString(matches[0]["SectionName"]) ?? "Unknown");
        }
        if (missing.Count>0)
            throw new InvalidOperationException("Choose destination sections for: " + string.Join(", ",missing.OrderBy(name=>name)) + ". No students were processed.");
        using(var check=new SqlCommand("SELECT ClassSectionId FROM dbo.CampusClassSections WITH(HOLDLOCK) WHERE CampusId=@Campus AND ClassId=@Class;",connection,transaction))
        {
            check.Parameters.AddRange(new[]{DatabaseHelper.Int("@Campus",campus),DatabaseHelper.Int("@Class",destinationClass)});
            var valid=new HashSet<int>();
            using(var reader=check.ExecuteReader()) while(reader.Read()) valid.Add(reader.GetInt32(0));
            if(destinations.Values.Any(target=>!valid.Contains(target)))
                throw new InvalidOperationException("A destination section is no longer available in this class and campus. Review the mapping again.");
        }
        int processed=0,promoted=0,held=0;
        foreach (var group in decisions.AsEnumerable().GroupBy(row=>Convert.ToInt32(byStudent[Convert.ToInt32(row["AdmissionId"])][0]["SectionId"])))
        {
            var subset=decisions.Clone();
            foreach(var row in group) subset.ImportRow(row);
            var promotedStudent=group.FirstOrDefault(row=>Convert.ToString(row["Decision"])=="PROMOTE");
            int destination=promotedStudent is null ? 0 : destinations[group.Key];
            using var command=new SqlCommand("BulkPromoteStudents",connection,transaction) { CommandType=CommandType.StoredProcedure,CommandTimeout=120 };
            command.Parameters.AddRange(new[] {
                DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@SourceAcademicYearId",sourceYear),
                DatabaseHelper.Int("@DestinationAcademicYearId",destinationYear),DatabaseHelper.Int("@SourceCampusId",campus),
                DatabaseHelper.Int("@SourceClassId",sourceClass),DatabaseHelper.Int("@SourceSectionId",group.Key),
                DatabaseHelper.Int("@DestinationClassSectionId",destination),DatabaseHelper.Date("@EffectiveDate",effective),
                DatabaseHelper.P("@Remarks",remarks),DatabaseHelper.Bit("@CarryConcessions",carryConcessions),new SqlParameter("@Decisions",SqlDbType.Structured) { TypeName="dbo.BulkPromotionDecisionType",Value=subset }
            });
            using var reader=command.ExecuteReader();
            if(!reader.Read()) throw new InvalidOperationException("Promotion did not return a result. No students were processed.");
            int count=Convert.ToInt32(reader["ProcessedCount"]);
            if(count!=subset.Rows.Count) throw new InvalidOperationException("Not all selected students could be processed. The entire promotion was rolled back.");
            processed+=count; promoted+=Convert.ToInt32(reader["PromotedCount"]); held+=Convert.ToInt32(reader["HeldBackCount"]);
        }
        transaction.Commit();
        var result=new DataTable();
        foreach(var name in new[]{"ProcessedCount","PromotedCount","HeldBackCount"}) result.Columns.Add(name,typeof(int));
        result.Rows.Add(processed,promoted,held);
        return result;
    }
}
