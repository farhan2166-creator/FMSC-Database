using Microsoft.Data.SqlClient;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;

internal static class CharacterCertificateChecks
{
    internal static async Task Run(string connection,string owner,int admin,int deniedUser)
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
        int Number(string sql)=>Convert.ToInt32(Query(sql).Rows[0][0]);
        int admission=Number("SELECT TOP(1) E.AdmissionId FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId WHERE NOT EXISTS(SELECT 1 FROM dbo.CharacterCertificates C WHERE C.AdmissionId=E.AdmissionId) ORDER BY E.EnrollmentId;");
        SqlParameter[] Create(string exam)=>new[]{DatabaseHelper.P("@Action","Create"),DatabaseHelper.Int("@AdmissionId",admission),DatabaseHelper.P("@ExamType",exam),
            DatabaseHelper.Date("@CertificateDate",DateTime.Today),DatabaseHelper.P("@RollNumber","SSC-Original"),DatabaseHelper.P("@Remarks","Original remarks")};
        async Task<MutationIntent> Intent(SqlParameter[] args)=>new(Guid.NewGuid(),MutationKind.CharacterCertificateSave,CurrentUser.UserId,await RecoverableMutationWriter.GetScopeAsync(),
            RecoverableMutationWriter.PayloadHash(MutationKind.CharacterCertificateSave,CurrentUser.UserId,args),"Character certificate fixture",DateTime.UtcNow);
        async Task<MutationWriteResult> Save(SqlParameter[] args)=>await RecoverableMutationWriter.SaveAsync(await Intent(args),args);
        var args=Create("SSC");var createIntent=await Intent(args);
        var ssc=await RecoverableMutationWriter.SaveAsync(createIntent,args);
        Check(ssc.Outcome==MutationOutcome.Saved,"SSC creation saves its type.");
        int certificate=ssc.Result!.OriginId,original=ssc.Result.RecordId;
        Check((await RecoverableMutationWriter.SaveAsync(createIntent,args)).Result!.RecordId==original,"Replaying a saved create does not duplicate its issue.");
        Check((await Save(Create("SSC"))).Outcome==MutationOutcome.Rejected,"Another SSC certificate is refused.");
        var hssc=await Save(Create("HSSC"));Check(hssc.Outcome==MutationOutcome.Saved&&hssc.Result!.OriginId!=certificate,"HSSC is a separate saved certificate for the same student.");
        Check((await Save(Create("Other"))).Outcome==MutationOutcome.Rejected,"Unknown exam type is refused.");
        DataTable Print(int cert,int issue,int user)=>Query("EXEC dbo.GetCharacterCertificatePrintData @UserId=@User,@CertificateRecordId=@Certificate,@CharacterCertificateIssueId=@Issue;",
            DatabaseHelper.Int("@User",user),DatabaseHelper.Int("@Certificate",cert),DatabaseHelper.Int("@Issue",issue));
        bool Refused(Action action,int error){try{action();return false;}catch(SqlException ex)when(ex.Number==error){return true;}}
        Check(Refused(()=>Print(certificate,original,admin),58302),"Uncollected original is refused by the database, not just the UI.");
        void Collect(int issue)=>Query("EXEC dbo.MarkCharacterCertificateCollected @UserId=@User,@CharacterCertificateIssueId=@Issue,@CollectedBy=N'Fixture collector';",DatabaseHelper.Int("@User",admin),DatabaseHelper.Int("@Issue",issue));
        Collect(original);
        Check(Print(certificate,original,admin).Rows[0].Field<string>("ExamTypeDisplay")=="S.S.C (Matric)","An SSC copy prints its saved SSC wording.");
        Check(Refused(()=>Print(certificate,hssc.Result!.RecordId,admin),58301),"An HSSC issue cannot be printed using an SSC identity.");
        Check(Refused(()=>Print(certificate,original,deniedUser),50003),"Printing still requires its own permission.");
        SqlParameter[] Correct(int latest,string exam,string reason)=>new[]{DatabaseHelper.P("@Action","Correct"),DatabaseHelper.Int("@CertificateRecordId",certificate),
            DatabaseHelper.Int("@ExpectedLatestIssueId",latest),DatabaseHelper.P("@ExamType",exam),DatabaseHelper.Date("@CertificateDate",DateTime.Today),
            DatabaseHelper.P("@RollNumber","SSC-Corrected"),DatabaseHelper.P("@Remarks","Corrected remarks"),DatabaseHelper.P("@Reason",reason)};
        Check((await Save(Correct(original,"HSSC","Wrong type"))).Outcome==MutationOutcome.Rejected,"Correction cannot collide with an existing HSSC certificate.");
        Check((await Save(Correct(original,"SSC",""))).Outcome==MutationOutcome.Rejected,"Correction requires a reason.");
        string originalName=Print(certificate,original,admin).Rows[0].Field<string>("StudentName")!;
        Query($"UPDATE dbo.Students SET StudentName=N'Fresh Admission Name',FatherName=N'Fresh Admission Parent',Gender=N'Female' WHERE AdmissionId={admission};");
        var forbidden=Correct(original,"SSC","Attempt personal edit").Concat(new[]{DatabaseHelper.P("@StudentName","Bypass Admission")}).ToArray();
        Check((await Save(forbidden)).Outcome==MutationOutcome.Rejected,"Correction cannot change personal details outside Admission.");
        var corrected=await Save(Correct(original,"SSC","Roll entered incorrectly"));Check(corrected.Outcome==MutationOutcome.Saved,"A correction creates a replacement copy.");
        int replacement=corrected.Result!.RecordId;
        Check(replacement!=original&&Number($"SELECT PreviousIssueId FROM dbo.CharacterCertificateIssueSnapshots WHERE CharacterCertificateIssueId={replacement};")==original,"Replacement links to the previous copy.");
        Check(Print(certificate,original,admin).Rows[0].Field<string>("RollNumber")=="SSC-Original","An earlier collected copy retains its original roll number.");
        Check(Refused(()=>Print(certificate,replacement,admin),58302),"A corrected replacement requires its own collection.");
        Collect(replacement);Check(Print(certificate,replacement,admin).Rows[0].Field<string>("RollNumber")=="SSC-Corrected","Corrected copy prints its own saved details.");
        Check(Print(certificate,replacement,admin).Rows[0].Field<string>("StudentName")==originalName,"Correction changes only entered certificate fields, even when Admission has newer details.");
        Check((await Save(Correct(original,"SSC","Stale correction"))).Outcome==MutationOutcome.Rejected,"A stale correction cannot overwrite a newer copy.");
        SqlParameter[] Issue(int latest)=>new[]{DatabaseHelper.P("@Action","Issue"),DatabaseHelper.Int("@CertificateRecordId",certificate),DatabaseHelper.Int("@ExpectedLatestIssueId",latest),DatabaseHelper.P("@ExpectedSourceHash",Query($"SELECT dbo.CertificateAdmissionFingerprint({admission});").Rows[0][0])};
        var staleSource=Issue(replacement);Query($"UPDATE dbo.Students SET FatherName=N'Corrected Admission Parent' WHERE AdmissionId={admission};");
        Check((await Save(staleSource)).Outcome==MutationOutcome.Rejected,"Reissue refuses Admission details changed after review.");
        var issueArgs=Issue(replacement);var issueIntent=await Intent(issueArgs);
        var issued=await RecoverableMutationWriter.SaveAsync(issueIntent,issueArgs);Check(issued.Outcome==MutationOutcome.Saved,"Reissue copies the latest corrected snapshot.");
        Collect(issued.Result!.RecordId);
        Check(Print(certificate,issued.Result.RecordId,admin).Rows[0].Field<string>("RollNumber")=="SSC-Corrected","Reissue never returns to the original incorrect details.");
        Check(Print(certificate,issued.Result.RecordId,admin).Rows[0].Field<string>("StudentName")=="Fresh Admission Name"&&Print(certificate,issued.Result.RecordId,admin).Rows[0].Field<string>("FatherName")=="Corrected Admission Parent"&&Print(certificate,original,admin).Rows[0].Field<string>("StudentName")==originalName,"Reissue refreshes personal details from Admission and preserves earlier copies.");
        Check((await RecoverableMutationWriter.SaveAsync(issueIntent,issueArgs)).Result!.RecordId==issued.Result.RecordId,"Reissue request replay keeps one copy.");
        int latestIssue=issued.Result.RecordId;
        var raceArgs=Correct(latestIssue,"SSC","Concurrent fixture correction");
        var raceIntents=new[]{await Intent(raceArgs),await Intent(raceArgs)};
        var races=await Task.WhenAll(raceIntents.Select(intent=>RecoverableMutationWriter.SaveAsync(intent,raceArgs)));
        Check(races.Count(result=>result.Outcome==MutationOutcome.Saved)==1&&races.Count(result=>result.Outcome==MutationOutcome.Rejected)==1,"Concurrent corrections produce only one replacement for the reviewed copy.");
        var winner=races.Single(result=>result.Outcome==MutationOutcome.Saved).Result!;Collect(winner.RecordId);
        var lostArgs=Issue(winner.RecordId);var lostIntent=await Intent(lostArgs);
        var lost=await RecoverableMutationWriter.SaveAsync(lostIntent,lostArgs,transaction=>{transaction.Commit();throw new IOException("Simulated lost response");});
        Check(lost.Outcome==MutationOutcome.Unconfirmed,"Lost commit response remains unconfirmed.");
        Check((await RecoverableMutationWriter.CheckAsync(lostIntent))?.OriginId==certificate,"Outcome checking recovers the actual saved certificate identity.");
        CurrentUser.UserId=deniedUser;Check((await Save(Create("SSC"))).Outcome==MutationOutcome.Rejected,"An operator without creation permission cannot save a certificate.");CurrentUser.UserId=admin;
        Console.WriteLine($"PASS: {checks} character SSC/HSSC, correction history, collection, concurrency and recovery checks.");
    }
}
