using System.Text.Json;

namespace FMSC_SMS.Core;

internal enum MutationKind { FeeAdjustment, BankCertificateCreate }
internal enum MutationOutcome { Saved, Rejected, Unconfirmed }
internal sealed record MutationIntent(Guid Id,MutationKind Kind,int UserId,string Scope,string PayloadHash,string Summary,DateTime StartedUtc);
internal sealed record MutationResult(MutationKind Kind,int SubjectId,int OriginId,int RecordId,string Receipt,string TransactionType,decimal Amount,decimal? Balance)
{
    internal void Validate()
    {
        if(!Enum.IsDefined(Kind)||SubjectId<=0||RecordId<=0||Receipt is null||Receipt.Length>100||
            Kind==MutationKind.FeeAdjustment&&(OriginId<=0||TransactionType is not ("Correction" or "Refund" or "Reversal"))||
            Kind==MutationKind.BankCertificateCreate&&TransactionType!="BankCertificateCreate")
            throw new InvalidDataException("The saved operation identity could not be verified.");
        WholeRupees.Require(Amount);
    }
    internal DataTable ToTable()
    {
        Validate();var table=new DataTable();
        if(Kind==MutationKind.BankCertificateCreate){table.Columns.Add("BankAccountOpeningCertificateId",typeof(int));table.Columns.Add("StaffId",typeof(int));table.Rows.Add(RecordId,SubjectId);}
        else{table.Columns.Add("FeePaymentId",typeof(int));table.Columns.Add("ReceiptNumber");table.Columns.Add("TransactionType");table.Columns.Add("Amount",typeof(decimal));table.Columns.Add("Balance",typeof(decimal));table.Rows.Add(RecordId,Receipt,TransactionType,Amount,(object?)Balance??DBNull.Value);}
        return table;
    }
}
internal sealed record MutationWriteResult(MutationOutcome Outcome,MutationResult? Result=null,Exception? Error=null);
internal interface IMutationTransaction { bool SubmissionStarted{get;} void Commit();void Rollback(); }
internal static class MutationSaveCoordinator
{
    internal static MutationWriteResult Execute(IMutationTransaction transaction,Func<MutationResult> write)
    {
        bool commitAttempted=false;MutationResult? saved=null;
        try{saved=write();saved.Validate();commitAttempted=true;transaction.Commit();return new(MutationOutcome.Saved,saved);}
        catch(Exception ex)
        {
            bool rolledBack=false;try{transaction.Rollback();rolledBack=true;}catch{ }
            return new(commitAttempted||transaction.SubmissionStarted&&!rolledBack?MutationOutcome.Unconfirmed:MutationOutcome.Rejected,saved,ex);
        }
    }
}

/// <summary>Small flushed intent only; never a financial draft or automatic write retry.</summary>
internal sealed class MutationRecoveryJournal : IDisposable
{
    private static readonly Mutex Gate=new(false,
#if FMSC_LEVEL2
        @"Local\FMSC_SMS.Level2."+Level2Target.Owner.ToString("N")+".MutationRecovery");
#else
        @"Local\FMSC_SMS.MutationRecovery");
#endif
    private FileStream? file;
    internal MutationIntent Intent{get;}
    private MutationRecoveryJournal(FileStream file,MutationIntent intent){this.file=file;Intent=intent;}
#if FMSC_LEVEL2
    private static string PathFor(MutationKind kind)=>Path.Combine(FMSC_SMS.Core.Level2Target.Folder(Environment.SpecialFolder.LocalApplicationData),"FMSC_SMS","Pending"+kind+".json");
#else
    private static string PathFor(MutationKind kind)=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FMSC_SMS","Pending"+kind+".json");
#endif
    internal static void Validate(MutationIntent value)
    {
        bool Hash(string s)=>s is {Length:64}&&s.All(Uri.IsHexDigit);
        if(value.Id==Guid.Empty||!Enum.IsDefined(value.Kind)||value.UserId<=0||!Hash(value.Scope)||!Hash(value.PayloadHash)||string.IsNullOrWhiteSpace(value.Summary)||value.Summary.Length>1500||value.StartedUtc==default)
            throw new InvalidDataException("The operation recovery record is incomplete. Keep it for administrator review; do not repeat the operation.");
    }
    internal static MutationRecoveryJournal Begin(MutationKind kind,int user,string scope,string hash,string summary,string? testPath=null)=>Locked(()=>{
        var intent=new MutationIntent(Guid.NewGuid(),kind,user,scope,hash,summary,DateTime.UtcNow);Validate(intent);
        string path=testPath??PathFor(kind);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FileStream stream;try{stream=new(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException ex){throw new InvalidOperationException("An earlier operation still needs checking, or another window is saving. Use Check Save Outcome before submitting again.",ex);}
        try{JsonSerializer.Serialize(stream,intent);stream.Flush(true);return new MutationRecoveryJournal(stream,intent);}
        catch{stream.Dispose();File.Delete(path);throw;}
    });
    private static MutationIntent? Read(string path,MutationKind kind)
    {
        if(!File.Exists(path))return null;
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None);
        if(stream.Length>16*1024)throw new InvalidDataException("The operation recovery file is invalid. Retain it for review; do not repeat the operation.");
        MutationIntent intent;try{intent=JsonSerializer.Deserialize<MutationIntent>(stream)??throw new JsonException();}
        catch(JsonException ex){throw new InvalidDataException("The operation recovery file is damaged. Retain it for review; do not repeat the operation.",ex);}
        Validate(intent);if(intent.Kind!=kind)throw new InvalidDataException("The recovery record belongs to a different operation. Retain it for review.");return intent;
    }
    internal static MutationIntent? ReadPending(MutationKind kind,string? testPath=null)=>Locked(()=>Read(testPath??PathFor(kind),kind));
    internal static void ClearVerified(MutationIntent expected,string? testPath=null)=>Locked(()=>{
        string path=testPath??PathFor(expected.Kind);if(Read(path,expected.Kind) is {} actual&&actual.Id!=expected.Id)throw new InvalidOperationException("The pending operation changed. Reopen this page to check the newer recovery record.");
        File.Delete(path);return true;
    });
    public void Dispose(){file?.Dispose();file=null;}
    private static T Locked<T>(Func<T> action)
    {
        bool held=false;try{try{held=Gate.WaitOne(0);}catch(AbandonedMutexException){held=true;}if(!held)throw new InvalidOperationException("Another window is updating operation recovery. Wait for it to finish.");return action();}
        finally{if(held)Gate.ReleaseMutex();}
    }
}
