using FMSC_SMS.Services.Backup;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckRestoreIdentity()
    {
        Guid original=Guid.NewGuid();
        BackupRestoreIdentity.RequireSame(original,original);
        Check(true,"The original logical school identity remains restorable after ordinary changes");
        foreach(var pair in new[]{(original,Guid.NewGuid()),(Guid.Empty,original),(original,Guid.Empty)})
        {
            try{BackupRestoreIdentity.RequireSame(pair.Item1,pair.Item2);throw new Exception("Different or missing identity accepted");}
            catch(BackupIdentityException ex)
            {
                Check(DbMessage.UserMessage(ex).Contains("different school database"),"An identity mismatch has an actionable restore-stopped message");
            }
        }
    }
}
