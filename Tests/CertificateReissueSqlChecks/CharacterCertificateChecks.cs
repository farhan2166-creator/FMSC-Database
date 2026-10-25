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
        int academicYear=Number("SELECT TOP(1) AcademicYearId FROM dbo.AcademicYears WHERE YEAR(EndDate)-1<=YEAR(GETDATE()) ORDER BY EndDate DESC;");
        int passingYear=Number($"SELECT YEAR(EndDate)-1 FROM dbo.AcademicYears WHERE AcademicYearId={academicYear};");
        SqlParameter[] Create(string exam)=>new[]{DatabaseHelper.P("@Action","Create"),DatabaseHelper.Int("@AdmissionId",admission),DatabaseHelper.P("@ExamType",exam),
            DatabaseHelper.Date("@CertificateDate",DateTime.Today),DatabaseHelper.P("@RollNumber","SSC-Original"),DatabaseHelper.P("@Remarks","Good"),DatabaseHelper.Int("@AcademicYearId",academicYear)};
        async Task<MutationIntent> Intent(SqlParameter[] args)=>new(Guid.NewGuid(),MutationKind.CharacterCertificateSave,CurrentUser.UserId,await RecoverableMutationWriter.GetScopeAsync(),
            RecoverableMutationWriter.PayloadHash(MutationKind.CharacterCertificateSave,CurrentUser.UserId,args),"Character certificate fixture",DateTime.UtcNow);
        async Task<MutationWriteResult> Save(SqlParameter[] args)=>await RecoverableMutationWriter.SaveAsync(await Intent(args),args);
        var badConduct=Create("SSC");badConduct.Single(p=>p.ParameterName=="@Remarks").Value="alskfj33333lkajsfld33333";
        Check((await Save(badConduct)).Outcome==MutationOutcome.Rejected,"Arbitrary conduct text is refused by the database.");
        var missingYear=Create("SSC");missingYear.Single(p=>p.ParameterName=="@AcademicYearId").Value=0;
        Check((await Save(missingYear)).Outcome==MutationOutcome.Rejected,"Creation requires an explicitly chosen academic year.");
        var futureYear=Create("SSC");futureYear.Single(p=>p.ParameterName=="@CertificateDate").Value=new DateTime(passingYear-1,12,31);
        Check((await Save(futureYear)).Outcome==MutationOutcome.Rejected,"The passing year cannot be later than the certificate date.");
        var args=Create("SSC");var createIntent=await Intent(args);
        var ssc=await RecoverableMutationWriter.SaveAsync(createIntent,args);
        Check(ssc.Outcome==MutationOutcome.Saved,"SSC creation saves its type.");
        int certificate=ssc.Result!.OriginId,original=ssc.Result.RecordId;
        Check(Number($"SELECT LastExamYear FROM dbo.CharacterCertificateIssueSnapshots WHERE CharacterCertificateIssueId={original};")==passingYear,"Creation saves the chosen academic year's end year minus one.");
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
        SqlParameter[] Update(int latest)=>new[]{DatabaseHelper.P("@Action","Update"),DatabaseHelper.Int("@CertificateRecordId",certificate),DatabaseHelper.Int("@ExpectedLatestIssueId",latest),
            DatabaseHelper.P("@ExamType","SSC"),DatabaseHelper.Date("@CertificateDate",DateTime.Today),DatabaseHelper.P("@RollNumber","SSC-Original"),
            DatabaseHelper.P("@Remarks","Very Good"),DatabaseHelper.Int("@AcademicYearId",academicYear)};
        int copiesBefore=Number($"SELECT COUNT(*) FROM dbo.CharacterCertificateIssues WHERE CertificateRecordId={certificate};");
        var direct=await Save(Update(original));
        Check(direct.Outcome==MutationOutcome.Saved&&direct.Result!.RecordId==original&&Number($"SELECT COUNT(*) FROM dbo.CharacterCertificateIssues WHERE CertificateRecordId={certificate};")==copiesBefore,
            "Save Changes edits the existing copy without creating another issuance or requiring a reason.");
        Check(Print(certificate,original,admin).Rows[0].Field<string>("Remarks")=="Very Good","The saved edit prints directly and preserves the copy's collection state.");
        var wrongType=Update(original);wrongType.Single(p=>p.ParameterName=="@ExamType").Value="HSSC";
        Check((await Save(wrongType)).Outcome==MutationOutcome.Rejected,"Save Changes cannot collide with an existing HSSC certificate.");
        string originalName=Print(certificate,original,admin).Rows[0].Field<string>("StudentName")!;
        Query($"UPDATE dbo.Students SET StudentName=N'Fresh Admission Name',FatherName=N'Fresh Admission Parent',Gender=N'Female' WHERE AdmissionId={admission};");
        var forbidden=Update(original).Concat(new[]{DatabaseHelper.P("@StudentName","Bypass Admission")}).ToArray();
        Check((await Save(forbidden)).Outcome==MutationOutcome.Rejected,"Save Changes cannot change personal details outside Admission.");
        var edit=Update(original);edit.Single(p=>p.ParameterName=="@RollNumber").Value="SSC-Corrected";
        Check((await Save(edit)).Outcome==MutationOutcome.Saved,"An entered roll number can be corrected with a direct save.");
        Check(Print(certificate,original,admin).Rows[0].Field<string>("StudentName")==originalName,"Save Changes leaves personal details as saved; fresh Admission details require Issue Again.");
        SqlParameter[] Issue(int latest)=>new[]{DatabaseHelper.P("@Action","Issue"),DatabaseHelper.Int("@CertificateRecordId",certificate),DatabaseHelper.Int("@ExpectedLatestIssueId",latest),DatabaseHelper.P("@ExpectedSourceHash",Query($"SELECT dbo.CertificateAdmissionFingerprint({admission});").Rows[0][0])};
        var staleSource=Issue(original);Query($"UPDATE dbo.Students SET FatherName=N'Corrected Admission Parent' WHERE AdmissionId={admission};");
        Check((await Save(staleSource)).Outcome==MutationOutcome.Rejected,"Reissue refuses Admission details changed after review.");
        var issueArgs=Issue(original);var issueIntent=await Intent(issueArgs);
        var issued=await RecoverableMutationWriter.SaveAsync(issueIntent,issueArgs);Check(issued.Outcome==MutationOutcome.Saved&&issued.Result!.RecordId!=original,"Issue Again creates a fresh copy.");
        Check((await Save(Update(original))).Outcome==MutationOutcome.Rejected,"An earlier copy cannot be edited after another copy has been issued.");
        Check(Refused(()=>Print(certificate,issued.Result!.RecordId,admin),58302),"The newly issued copy requires its own collection.");
        Collect(issued.Result!.RecordId);
        Check(Print(certificate,issued.Result.RecordId,admin).Rows[0].Field<string>("RollNumber")=="SSC-Corrected","Reissue never returns to the original incorrect details.");
        Check(Print(certificate,issued.Result.RecordId,admin).Rows[0].Field<string>("StudentName")=="Fresh Admission Name"&&Print(certificate,issued.Result.RecordId,admin).Rows[0].Field<string>("FatherName")=="Corrected Admission Parent"&&Print(certificate,original,admin).Rows[0].Field<string>("StudentName")==originalName,"Reissue refreshes personal details from Admission and preserves earlier copies.");
        Check((await RecoverableMutationWriter.SaveAsync(issueIntent,issueArgs)).Result!.RecordId==issued.Result.RecordId,"Reissue request replay keeps one copy.");
        int latestIssue=issued.Result.RecordId;
        var raceArgs=Issue(latestIssue);
        var raceIntents=new[]{await Intent(raceArgs),await Intent(raceArgs)};
        var races=await Task.WhenAll(raceIntents.Select(intent=>RecoverableMutationWriter.SaveAsync(intent,raceArgs)));
        Check(races.Count(result=>result.Outcome==MutationOutcome.Saved)==1&&races.Count(result=>result.Outcome==MutationOutcome.Rejected)==1,"Concurrent Issue Again requests produce only one new copy for the reviewed issuance.");
        var winner=races.Single(result=>result.Outcome==MutationOutcome.Saved).Result!;Collect(winner.RecordId);
        var lostArgs=Issue(winner.RecordId);var lostIntent=await Intent(lostArgs);
        var lost=await RecoverableMutationWriter.SaveAsync(lostIntent,lostArgs,transaction=>{transaction.Commit();throw new IOException("Simulated lost response");});
        Check(lost.Outcome==MutationOutcome.Unconfirmed,"Lost commit response remains unconfirmed.");
        Check((await RecoverableMutationWriter.CheckAsync(lostIntent))?.OriginId==certificate,"Outcome checking recovers the actual saved certificate identity.");
        CurrentUser.UserId=deniedUser;Check((await Save(Create("SSC"))).Outcome==MutationOutcome.Rejected,"An operator without creation permission cannot save a certificate.");CurrentUser.UserId=admin;
        Console.WriteLine($"PASS: {checks} character year, conduct, direct save, reissue, collection, concurrency and recovery checks.");
    }
}
