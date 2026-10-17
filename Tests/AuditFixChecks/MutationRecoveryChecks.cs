using FMSC_SMS.Core;

internal static class MutationRecoveryChecks
{
    private sealed class Transaction : IMutationTransaction
    {
        internal bool CommitFails,RollbackFails;internal int Commits,Rollbacks;
        public bool SubmissionStarted{get;set;}
        public void Commit(){Commits++;if(CommitFails)throw new IOException("Fixture lost commit reply");}
        public void Rollback(){Rollbacks++;if(RollbackFails)throw new IOException("Fixture rollback unknown");}
    }
    internal static int Run()
    {
        int checks=0;void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
        var saved=new MutationResult(MutationKind.FeeAdjustment,41,8,9,"R-9","Refund",200m,800m);
        var tx=new Transaction{SubmissionStarted=true};var result=MutationSaveCoordinator.Execute(tx,()=>saved);
        Check(result.Outcome==MutationOutcome.Saved&&result.Result==saved&&tx.Commits==1,"Only a verified commit returns mutation success");
        tx=new Transaction{SubmissionStarted=true,CommitFails=true,RollbackFails=true};result=MutationSaveCoordinator.Execute(tx,()=>saved);
        Check(result.Outcome==MutationOutcome.Unconfirmed&&tx.Commits==1,"Lost commit acknowledgement stays unknown without automatic replay");
        tx=new Transaction{SubmissionStarted=true};result=MutationSaveCoordinator.Execute(tx,()=>throw new InvalidOperationException("Server refusal"));
        Check(result.Outcome==MutationOutcome.Rejected&&tx.Rollbacks==1,"A confirmed rollback allows a deliberate corrected attempt");
        tx=new Transaction{SubmissionStarted=true,RollbackFails=true};result=MutationSaveCoordinator.Execute(tx,()=>throw new IOException("Interrupted write"));
        Check(result.Outcome==MutationOutcome.Unconfirmed,"Submitted write with unconfirmed rollback remains blocked for recovery");
        tx=new Transaction{CommitFails=true};result=MutationSaveCoordinator.Execute(tx,()=>saved);
        Check(result.Outcome==MutationOutcome.Unconfirmed,"Commit attempt is conservatively unresolved even if later rollback returns");
        var table=saved.ToTable();Check((int)table.Rows[0]["FeePaymentId"]==9&&(string)table.Rows[0]["ReceiptNumber"]=="R-9"&&((decimal)table.Rows[0]["Amount"])==200m,"Recovered results retain exact identity, receipt and amount");
        string folder=Path.Combine(Path.GetTempPath(),"FMSC-MutationChecks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string path=Path.Combine(folder,"pending.json");
        try
        {
            MutationIntent intent;
            using(var journal=MutationRecoveryJournal.Begin(MutationKind.FeeAdjustment,7,new string('A',64),new string('B',64),"Refund payment 8",path))
            {
                intent=journal.Intent;
                try{MutationRecoveryJournal.Begin(MutationKind.FeeAdjustment,7,new string('A',64),new string('B',64),"Other request",path);throw new Exception("Second pending request accepted");}catch(InvalidOperationException){checks++;}
            }
            Check(MutationRecoveryJournal.ReadPending(MutationKind.FeeAdjustment,path)==intent,"Flushed intent survives closing its writer and reopening the profile");
            try{MutationRecoveryJournal.ClearVerified(intent with{Id=Guid.NewGuid()},path);throw new Exception("Wrong recovery request cleared");}catch(InvalidOperationException){checks++;}
            Check(File.Exists(path),"Wrong request identity cannot erase a pending recovery file");
            MutationRecoveryJournal.ClearVerified(intent,path);Check(!File.Exists(path),"Confirmed outcome retires exactly its recovery intent");
            MutationRecoveryJournal.ClearVerified(intent,path);Check(!File.Exists(path),"A separately verified outcome tolerates another window already retiring that same intent");
            File.WriteAllText(path,"{broken");try{MutationRecoveryJournal.ReadPending(MutationKind.FeeAdjustment,path);throw new Exception("Corrupt recovery accepted");}catch(InvalidDataException){checks++;}
            Check(File.ReadAllText(path)=="{broken","Corrupt recovery data is retained instead of overwritten");
        }
        finally{Directory.Delete(folder,true);}
        return checks;
    }
}
