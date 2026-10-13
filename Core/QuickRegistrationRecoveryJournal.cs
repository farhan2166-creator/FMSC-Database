using System.Text.Json;

namespace FMSC_SMS.Core;

// A small flushed intent survives closing the page or restarting the application.
// No password/connection string or photo bytes are stored. One pending intent per Windows profile.
internal sealed class QuickRegistrationRecoveryJournal : IDisposable
{
    internal sealed record Pending(Guid Id,int UserId,string Scope,string PayloadHash,string Summary,DateTime StartedUtc);
    private static readonly Mutex Gate=new(false,@"Local\FMSC_SMS.QuickRegistrationRecovery");
    internal static string JournalPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FMSC_SMS","PendingQuickRegistration.json");
    private readonly string path;
    private FileStream? file;
    internal Pending Intent { get; }
    private QuickRegistrationRecoveryJournal(string path,FileStream file,Pending intent){this.path=path;this.file=file;Intent=intent;}
    internal static QuickRegistrationRecoveryJournal Begin(int user,string scope,string hash,string summary,string? testPath=null)=>Locked(()=>{
        var intent=new Pending(Guid.NewGuid(),user,scope,hash,summary,DateTime.UtcNow);Validate(intent);
        string path=testPath??JournalPath;Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FileStream stream;
        try{stream=new(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException ex){throw new InvalidOperationException("A registration save is still pending review, or another window is saving. Use Check Save Outcome before saving a new registration.",ex);}
        try{JsonSerializer.Serialize(stream,intent);stream.Flush(true);return new QuickRegistrationRecoveryJournal(path,stream,intent);}
        catch{stream.Dispose();File.Delete(path);throw;}
    });
    internal static Pending? ReadPending(string? testPath=null)=>Locked(()=>Read(testPath??JournalPath));
    private static Pending? Read(string path)
    {
        if(!File.Exists(path))return null;
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None);
        if(stream.Length>16*1024)throw new InvalidDataException("The registration recovery record is invalid. Keep it for administrator review; do not repeat the registration.");
        Pending pending;
        try{pending=JsonSerializer.Deserialize<Pending>(stream)??throw new JsonException();}
        catch(JsonException ex){throw new InvalidDataException("The registration recovery record is damaged. Keep it for administrator review; do not repeat the registration.",ex);}
        Validate(pending);return pending;
    }
    internal static void Validate(Pending value)
    {
        bool Hash(string? s)=>s is {Length:64}&&s.All(Uri.IsHexDigit);
        if(value.Id==Guid.Empty||value.UserId<=0||!Hash(value.Scope)||!Hash(value.PayloadHash)||
            string.IsNullOrWhiteSpace(value.Summary)||value.Summary.Length>1500||value.StartedUtc==default)
            throw new InvalidDataException("The registration recovery record is incomplete. Keep it for administrator review; do not repeat the registration.");
    }
    internal static void ClearVerified(Guid expected,string? testPath=null)=>Locked(()=>{
        string path=testPath??JournalPath;
        if(Read(path)?.Id!=expected)throw new InvalidOperationException("The pending registration changed. Check its save outcome again before clearing recovery.");
        File.Delete(path);return true;
    });
    public void Dispose(){file?.Dispose();file=null;}
    private static T Locked<T>(Func<T> action)
    {
        bool held=false;
        try
        {
            try{held=Gate.WaitOne(0);}catch(AbandonedMutexException){held=true;}
            if(!held)throw new InvalidOperationException("Another registration window is updating recovery. Wait for it to finish.");
            return action();
        }
        finally{if(held)Gate.ReleaseMutex();}
    }
}
