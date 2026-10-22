using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using Microsoft.Data.SqlClient;

// The calling runner must own a disposable clone. This test commits synthetic fees.
internal static class AdditionalFeeRecoverySqlChecks
{
    internal static async Task RunAsync(int admin)
    {
        CurrentUser.UserId=admin;int checks=0;
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
        DataTable Query(string sql,params SqlParameter[] args)
        {
            using var c=DatabaseHelper.GetConnection();c.Open();using var q=new SqlCommand(sql,c){CommandTimeout=20};q.Parameters.AddRange(args);
            var rows=new DataTable();using var adapter=new SqlDataAdapter(q);adapter.Fill(rows);return rows;
        }
        int[] ids=Query("""
            SELECT TOP(2) E.EnrollmentId FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId
            JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
            WHERE E.Status=N'Active' AND S.Status=N'Active' AND E.BillingReady=1 AND E.PlacementConfirmedAt IS NOT NULL
                AND S.LegacySetupPending=0 AND CONVERT(date,GETDATE()) BETWEEN COALESCE(CONVERT(date,E.ReturnConfirmedAt),E.EnrollmentDate) AND Y.EndDate
                AND NOT EXISTS(SELECT 1 FROM dbo.StudentPostingHolds H WHERE H.AdmissionId=S.AdmissionId AND H.ResumeDate IS NULL)
            ORDER BY E.EnrollmentId;
            """).AsEnumerable().Select(r=>Convert.ToInt32(r[0])).ToArray();
        if(ids.Length==0)throw new InvalidOperationException("The disposable clone needs an eligible synthetic student.");
        int fee=Convert.ToInt32(Query("SELECT TOP(1) FeeTypeId FROM dbo.FeeTypes WHERE DefaultFrequency=N'As Needed' AND FeeTypeCode NOT IN(N'ADMISSION',N'TUITION',N'TRANSPORT') ORDER BY FeeTypeId;").Rows[0][0]);
        int Count(Guid id)=>Convert.ToInt32(Query("SELECT COUNT(*) FROM dbo.BillingCharges WHERE ManualRequestId=@Request;",new SqlParameter("@Request",SqlDbType.UniqueIdentifier){Value=id}).Rows[0][0]);
        string path=Path.Combine(Path.GetTempPath(),"FMSC-additional-sql-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            var args=AdditionalFeePostingWriter.Parameters(fee,DateTime.Today,37m,0m,"Synthetic recovery test",ids);
            var first=new AdditionalFeeRecovery(path,save:(intent,parameters)=>AdditionalFeePostingWriter.SaveAsync(intent,parameters,t=>{
                t.Commit();throw new IOException("Injected lost acknowledgement after SQL commit");
            }));
            try{await first.SaveAsync(args,"Two synthetic recovery charges");throw new Exception("Lost commit acknowledgement was not injected");}catch(IOException){}
            Guid request=first.Pending!.Id;
            Check(first.Blocked&&Count(request)==ids.Length,"Lost acknowledgement retains one committed charge per student and a blocked recovery intent");
            var reopened=new AdditionalFeeRecovery(path);reopened.Load();
            Check(reopened.Pending!.Id==request&&reopened.Blocked,"Fresh entry instance retains the original committed request");
            try{await reopened.SaveAsync(args,"Attempted duplicate");throw new Exception("Reopened posting accepted another request");}catch(InvalidOperationException){}
            Check(Count(request)==ids.Length,"Reopened entry cannot submit a second posting before recovery");
            var original=reopened.Pending;
            try{await AdditionalFeePostingWriter.CheckAsync(original with{Scope=new string('0',64)});throw new Exception("Wrong database accepted");}catch(InvalidOperationException){}
            Check(File.Exists(path),"Wrong-database recovery retains the durable intent");
            try{await AdditionalFeePostingWriter.CheckAsync(original with{PayloadHash=new string('0',64)});throw new Exception("Changed payload accepted");}catch(InvalidDataException){}
            Check(await reopened.CheckAsync()==ids.Length&&Count(request)==ids.Length&&!File.Exists(path),"Read-only recovery verifies the committed payload and leaves exactly the original charges");
            using(var journal=MutationRecoveryJournal.Begin(MutationKind.AdditionalFeePosting,admin,await RecoverableMutationWriter.GetScopeAsync(),
                RecoverableMutationWriter.PayloadHash(MutationKind.AdditionalFeePosting,admin,args),"Never submitted",path)){}
            Check(await new AdditionalFeeRecovery(path).CheckAsync()==0&&!File.Exists(path),"A missing request is verified without executing posting SQL");
            // Fail before commit: the outer transaction must roll back all nested procedure writes.
            var failed=new AdditionalFeeRecovery(path,save:(intent,parameters)=>AdditionalFeePostingWriter.SaveAsync(intent,parameters,_=>throw new IOException("Injected failure before commit")));
            try{await failed.SaveAsync(args,"Synthetic rollback");throw new Exception("Missing pre-commit failure");}catch(IOException){}
            Guid failedId=failed.Pending!.Id;
            Check(Count(failedId)==0&&await new AdditionalFeeRecovery(path).CheckAsync()==0,"Pre-commit failure rolls back charges and permits only a verified absent-outcome recovery");
            Console.WriteLine($"PASS: {checks} real SQL additional-fee recovery checks on the disposable clone.");
        }
        finally{File.Delete(path);}
    }
}
