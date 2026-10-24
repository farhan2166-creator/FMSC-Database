using Microsoft.Data.SqlClient;
using FMSC_SMS.Core;

internal static class LeaveCertificateChecks
{
    internal static void Run(string connection,string owner,int admin)
    {
        int checks=0;
        void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
        DataTable Query(string sql,params SqlParameter[] args)
        {
            using var c=new SqlConnection(connection);c.Open();
            using var guard=new SqlCommand("SELECT CONVERT(nvarchar(32),value) FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_CertificateChecksOwner';",c);
            if(Convert.ToString(guard.ExecuteScalar())!=owner)throw new Exception("Wrong fixture owner.");
            using var q=new SqlCommand(sql,c){CommandTimeout=45};q.Parameters.AddRange(args);
            var table=new DataTable();using var adapter=new SqlDataAdapter(q);adapter.Fill(table);return table;
        }
        foreach(string type in new[]{"School","College"})
        {
            var created=Query("""
                DECLARE @Admission int=(SELECT TOP(1) S.AdmissionId FROM dbo.Students S JOIN dbo.Enrollments E ON E.AdmissionId=S.AdmissionId
                 WHERE NULLIF(S.AdmissionNumber,N'') IS NOT NULL AND ISNULL(S.LegacySetupPending,0)=0 AND NOT EXISTS(SELECT 1 FROM dbo.LeaveCertificates C WHERE C.AdmissionId=S.AdmissionId) ORDER BY S.AdmissionId);
                INSERT dbo.LeaveCertificates(AdmissionId,StudentName,FatherName,DateOfBirth,DateOfAdmission,ClassStudied,ClassPromotedTo,LeaveDate,AcademicRecord,AttendanceRecord,Remarks,CreatedByUserId,CertificateType)
                 SELECT AdmissionId,StudentName,FatherName,DateOfBirth,AdmissionDate,N'Ten',N'Eleven',CONVERT(date,GETDATE()),N'Good',N'Regular',N'Original leaving details',@User,@Type FROM dbo.Students WHERE AdmissionId=@Admission;
                DECLARE @Certificate int=CONVERT(int,SCOPE_IDENTITY());
                INSERT dbo.LeaveCertificateIssues(LeaveRecordId,IssuedAt,IssuedByUserId,IssueType) VALUES(@Certificate,SYSDATETIME(),@User,N'Original');
                SELECT @Admission AdmissionId,@Certificate CertificateId,CONVERT(int,SCOPE_IDENTITY()) IssueId;
                """,DatabaseHelper.Int("@User",admin),DatabaseHelper.P("@Type",type));
            int admission=Convert.ToInt32(created.Rows[0]["AdmissionId"]),certificate=Convert.ToInt32(created.Rows[0]["CertificateId"]),original=Convert.ToInt32(created.Rows[0]["IssueId"]);
            DataTable Print(int issue)=>Query("EXEC dbo.GetLeaveCertificatePrintData @UserId=@User,@LeaveRecordId=@Certificate,@LeaveCertificateIssueId=@Issue;",
                DatabaseHelper.Int("@User",admin),DatabaseHelper.Int("@Certificate",certificate),DatabaseHelper.Int("@Issue",issue));
            void Collect(int issue)=>Query("EXEC dbo.MarkLeaveCertificateCollected @UserId=@User,@LeaveCertificateIssueId=@Issue,@CollectedBy=N'Fixture collector';",DatabaseHelper.Int("@User",admin),DatabaseHelper.Int("@Issue",issue));
            var before=Print(original).Rows[0];string oldName=Convert.ToString(before["StudentName"])!;
            Query($"UPDATE dbo.Students SET StudentName=N'Fresh Leaving Name',FatherName=N'Fresh Leaving Parent',DateOfBirth='2002-03-04',AdmissionDate='2020-01-02' WHERE AdmissionId={admission};");
            Check(Convert.ToString(Print(original).Rows[0]["StudentName"])==oldName,type+": Admission changes do not rewrite the original leaving copy.");
            string Lifecycle()=>Convert.ToString(Query($"SELECT (SELECT * FROM dbo.Students WHERE AdmissionId={admission} FOR JSON PATH,INCLUDE_NULL_VALUES) StudentJson,(SELECT * FROM dbo.Enrollments WHERE AdmissionId={admission} ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES) EnrollmentJson;").Rows[0][0])+Convert.ToString(Query($"SELECT (SELECT * FROM dbo.Enrollments WHERE AdmissionId={admission} ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES);").Rows[0][0]);
            string lifecycle=Lifecycle();
            DataTable Issue(int latest)=>Query("EXEC dbo.IssueLeaveCertificate @UserId=@User,@LeaveRecordId=@Certificate,@ExpectedLatestIssueId=@Latest,@ExpectedSourceHash=@Hash;",
                DatabaseHelper.Int("@User",admin),DatabaseHelper.Int("@Certificate",certificate),DatabaseHelper.Int("@Latest",latest),
                DatabaseHelper.P("@Hash",Query($"SELECT dbo.CertificateAdmissionFingerprint({admission});").Rows[0][0]));
            bool Refused(Action action,int error){try{action();return false;}catch(SqlException ex)when(ex.Number==error){return true;}}
            Check(Refused(()=>Issue(original),57402),type+": collection is still required before another issuance.");
            Collect(original);int duplicate=Convert.ToInt32(Issue(original).Rows[0]["LeaveCertificateIssueId"]);
            var after=Print(duplicate).Rows[0];
            Check(Convert.ToString(after["StudentName"])=="Fresh Leaving Name"&&Convert.ToString(after["FatherName"])=="Fresh Leaving Parent"&&Convert.ToDateTime(after["DateOfBirth"])==new DateTime(2002,3,4),type+": reissue uses fresh Admission name, father name and DOB.");
            Check(Convert.ToDateTime(after["DateOfAdmission"])==new DateTime(2020,1,2),type+": reissue refreshes the corrected Admission date.");
            Check(Convert.ToString(after["ClassStudied"])=="Ten"&&Convert.ToDateTime(after["LeaveDate"])==Convert.ToDateTime(before["LeaveDate"])&&Convert.ToString(after["CopyType"])=="Duplicate",type+": historical leaving episode and duplicate wording remain saved.");
            Check(Convert.ToString(Print(original).Rows[0]["StudentName"])==oldName,type+": old and new leaving copies remain independently printable.");
            Check(Refused(()=>Print(int.MaxValue),57311),type+": invalid selected issue cannot fall back to another copy.");
            Check(Refused(()=>Issue(original),58533),type+": a stale issuance review cannot create another duplicate.");
            Collect(duplicate);int second=Convert.ToInt32(Issue(duplicate).Rows[0]["LeaveCertificateIssueId"]);Collect(second);
            Check(Refused(()=>Issue(second),57403),type+": the two-duplicate limit is preserved.");
            Check(Lifecycle()==lifecycle,type+": reissue does not change student status or enrollment/lifecycle records.");
        }
        Console.WriteLine($"PASS: {checks} school/college leaving fresh-Admission, historical-copy, lifecycle and duplicate-limit checks.");
    }
}
