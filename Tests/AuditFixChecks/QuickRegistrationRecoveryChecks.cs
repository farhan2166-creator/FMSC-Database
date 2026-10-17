using FMSC_SMS.Core;

internal static class QuickRegistrationRecoveryChecks
{
    private sealed class Transaction:IQuickRegistrationTransaction
    {
        public bool SubmissionStarted{get;set;}
        public bool CommitFails,RollbackFails;
        public int Commits,Rollbacks;
        public void Commit(){Commits++;if(CommitFails)throw new IOException("Lost commit reply");}
        public void Rollback(){Rollbacks++;if(RollbackFails)throw new IOException("Lost rollback reply");}
    }
    internal static int Run()
    {
        int checks=0;
        void Check(bool value,string text){if(!value)throw new Exception(text);checks++;}
        void Reject(Action action){try{action();}catch(Exception e) when(e is InvalidDataException or InvalidOperationException or IOException){checks++;return;}throw new Exception("Unsafe recovery data was accepted");}
        DataSet Response(object id,object reference)
        {
            var result=new DataSet();var fees=new DataTable();fees.Columns.Add("FeeTypeId",typeof(int));fees.Rows.Add(1);result.Tables.Add(fees);
            var identity=new DataTable();identity.Columns.Add("AdmissionId",typeof(int));identity.Columns.Add("QuickReference",typeof(string));identity.Rows.Add(id,reference);result.Tables.Add(identity);return result;
        }
        using(var valid=Response(41,"QR-41"))Check(QuickRegistrationSaveCoordinator.ReadResponse(valid)==new SavedQuickRegistration(41,"QR-41"),"Fee result sets do not replace the exact saved identity");
        foreach(var (id,reference) in new (object,object)[]{(0,"QR-0"),(-1,"QR-1"),(DBNull.Value,"QR-1"),(1,""),(1,DBNull.Value)})
        {using var invalid=Response(id,reference);Reject(()=>QuickRegistrationSaveCoordinator.ReadResponse(invalid));}
        using(var empty=new DataSet())Reject(()=>QuickRegistrationSaveCoordinator.ReadResponse(empty));
        using(var duplicate=Response(41,"QR-41")){var copy=duplicate.Tables[1].Copy();copy.TableName="Other identity";duplicate.Tables.Add(copy);Reject(()=>QuickRegistrationSaveCoordinator.ReadResponse(duplicate));}
        using(var extra=Response(41,"QR-41")){extra.Tables[1].Rows.Add(42,"QR-42");Reject(()=>QuickRegistrationSaveCoordinator.ReadResponse(extra));}
        var saved=new SavedQuickRegistration(41,"QR-41");int writes=0;
        var successful=new Transaction{SubmissionStarted=true};
        var result=QuickRegistrationSaveCoordinator.Execute(successful,()=>{writes++;return saved;});
        Check(result.Outcome==QuickRegistrationWriteOutcome.Saved&&result.Student==saved&&successful.Commits==1&&successful.Rollbacks==0&&writes==1,"Only a confirmed commit reports success, with one write");
        foreach(bool submitted in new[]{false,true})
        {
            var rejected=new Transaction{SubmissionStarted=submitted};
            result=QuickRegistrationSaveCoordinator.Execute(rejected,()=>throw new InvalidOperationException("Rejected by SQL"));
            Check(result.Outcome==QuickRegistrationWriteOutcome.Rejected&&rejected.Commits==0&&rejected.Rollbacks==1,"Confirmed rollback permits deliberate retry");
        }
        var beforeWrite=new Transaction{RollbackFails=true};
        Check(QuickRegistrationSaveCoordinator.Execute(beforeWrite,()=>throw new IOException()).Outcome==QuickRegistrationWriteOutcome.Rejected,"Failure before submission cannot create a student");
        var interrupted=new Transaction{SubmissionStarted=true,RollbackFails=true};
        Check(QuickRegistrationSaveCoordinator.Execute(interrupted,()=>throw new IOException()).Outcome==QuickRegistrationWriteOutcome.Unconfirmed,"Lost command and rollback replies require exact recovery");
        foreach(bool rollbackFails in new[]{false,true})
        {
            var lost=new Transaction{SubmissionStarted=true,CommitFails=true,RollbackFails=rollbackFails};
            result=QuickRegistrationSaveCoordinator.Execute(lost,()=>{writes++;return saved;});
            Check(result.Outcome==QuickRegistrationWriteOutcome.Unconfirmed&&result.Student==saved&&lost.Commits==1&&lost.Rollbacks==1,"Every lost commit reply stays unconfirmed, even after a rollback attempt");
        }
        var malformed=new Transaction{SubmissionStarted=true};
        Check(QuickRegistrationSaveCoordinator.Execute(malformed,()=>new(0,"")).Outcome==QuickRegistrationWriteOutcome.Rejected&&malformed.Commits==0,"Invalid result identity is rejected before commit");
        string directory=Path.Combine(Path.GetTempPath(),"fmsc-qr-recovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string path=Path.Combine(directory,"pending.json"),scope=new('A',64),hash=new('B',64);
            QuickRegistrationRecoveryJournal.Pending pending;
            using(var journal=QuickRegistrationRecoveryJournal.Begin(7,scope,hash,"Test child",path))
            {
                pending=journal.Intent;Check(File.Exists(path)&&new FileInfo(path).Length>0,"Intent is persisted before any database write");
                Reject(()=>QuickRegistrationRecoveryJournal.ReadPending(path));
                Reject(()=>QuickRegistrationRecoveryJournal.Begin(7,scope,hash,"Another child",path));
            }
            Check(QuickRegistrationRecoveryJournal.ReadPending(path)==pending,"Dispose/reopen preserves exact request identity and scope");
            Reject(()=>QuickRegistrationRecoveryJournal.ClearVerified(Guid.NewGuid(),path));
            Check(QuickRegistrationRecoveryJournal.ReadPending(path)==pending,"A mismatched completion cannot erase another request");
            QuickRegistrationRecoveryJournal.ClearVerified(pending.Id,path);Check(!File.Exists(path),"Verified completion removes only the matching recovery intent");
            using(var second=QuickRegistrationRecoveryJournal.Begin(7,scope,hash,"Second child",path))Check(second.Intent.Id!=pending.Id,"Each deliberate new save receives a fresh request identity");
            byte[] corrupt=System.Text.Encoding.UTF8.GetBytes("{damaged");File.WriteAllBytes(path,corrupt);
            Reject(()=>QuickRegistrationRecoveryJournal.ReadPending(path));Check(File.ReadAllBytes(path).SequenceEqual(corrupt),"Corrupt recovery is retained, never silently discarded");
            File.WriteAllText(path,new string('x',17*1024));Reject(()=>QuickRegistrationRecoveryJournal.ReadPending(path));
            foreach(var invalid in new[]{pending with{Id=Guid.Empty},pending with{UserId=0},pending with{Scope="other"},pending with{PayloadHash=new string('Z',64)},pending with{Summary=""},pending with{StartedUtc=default}})
                Reject(()=>QuickRegistrationRecoveryJournal.Validate(invalid));
        }
        finally{Directory.Delete(directory,true);}
        return checks;
    }
}
