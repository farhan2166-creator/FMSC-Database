using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using Microsoft.Data.SqlClient;
using FMSC_SMS.Pages.Finance.Billing;
using System.Reflection;

internal static partial class Program
{
    private static void CheckAdditionalFeeRecovery()
    {
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,Permissions:CurrentUser.GetPermissions().ToArray());
        string path=Path.Combine(Path.GetTempPath(),"FMSC-additional-recovery-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            CurrentUser.SignIn(7777,"Recovery fixture","Administrator",false,Array.Empty<string>());
            var saved=new Dictionary<Guid,int>();int writes=0;bool loseReply=true,wrongDatabase=false;
            Task<int> Save(MutationIntent intent,SqlParameter[] args)
            {
                Check(File.Exists(path),"Additional-fee intent is durable before submission");
                try{MutationRecoveryJournal.ReadPending(MutationKind.AdditionalFeePosting,path);throw new Exception("In-flight intent was readable");}
                catch(IOException){Check(true,"An in-flight posting holds its recovery file exclusively");}
                writes++;saved[intent.Id]=2;
                if(loseReply)throw new IOException("Synthetic lost reply after commit");
                return Task.FromResult(2);
            }
            Task<int?> Read(MutationIntent intent)
            {
                if(wrongDatabase)throw new InvalidOperationException("Different database");
                return Task.FromResult(saved.TryGetValue(intent.Id,out int count)?(int?)count:null);
            }
            AdditionalFeeRecovery Open()=>new(path,()=>Task.FromResult(new string('A',64)),Save,Read);
            var args=AdditionalFeePostingWriter.Parameters(3,DateTime.Today,100m,0m,"Recovery fixture",new[]{42,41});
            string hash=RecoverableMutationWriter.PayloadHash(MutationKind.AdditionalFeePosting,7777,args);
            var reread=AdditionalFeePostingWriter.Parameters(3,DateTime.SpecifyKind(DateTime.Today,DateTimeKind.Unspecified),100.00m,0.00m,"Recovery fixture",new[]{41,42});
            Check(hash==RecoverableMutationWriter.PayloadHash(MutationKind.AdditionalFeePosting,7777,reread),
                "Reordered selections, SQL decimal scale and date kinds preserve the submitted payload identity");
            var first=Open();
            try{first.SaveAsync(args,"Two fictional charges").GetAwaiter().GetResult();throw new Exception("Missing lost reply");}catch(IOException){}
            Guid original=first.Pending!.Id;
            Check(first.Blocked&&File.Exists(path)&&writes==1,"A lost commit reply retains recovery and blocks another posting");
            var reopened=Open();reopened.Load();
            Check(reopened.Blocked&&reopened.Pending!.Id==original,"Reopening either entry point retains the original request identity");
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            using(var bulk=new AdditionalFeePostingPage(null,false,reopened))
            using(var window=new Form{ClientSize=new Size(1100,740),ShowInTaskbar=false})
            {
                bulk.PrepareForEmbedding(keepActions:true);window.Controls.Add(bulk);window.Show();Application.DoEvents();
                var button=(ModernButton)typeof(AdditionalFeePostingPage).GetField("checkSave",flags)!.GetValue(bulk)!;
                var post=(ModernButton)typeof(AdditionalFeePostingPage).GetField("post",flags)!.GetValue(bulk)!;
                Check(button.Visible&&button.Enabled&&!post.Enabled,"Bulk recovery action remains available while posting is blocked");
                window.Close();
            }
            using(var close=new ModernButton{Text="Cancel",Width=AppScale.Scale(110),Height=AppMetrics.ButtonHeight})
            using(var single=new AdditionalFeeEntry(close,reopened))
            using(var window=new Form{ClientSize=new Size(AppScale.Scale(660),AdditionalFeeEntry.PreferredHeight),ShowInTaskbar=false})
            {
                window.Controls.Add(single);window.Show();Application.DoEvents();
                var button=(ModernButton)typeof(AdditionalFeeEntry).GetField("checkSave",flags)!.GetValue(single)!;
                Check(button.Visible&&button.Enabled&&!single.PostButton.Enabled,"Single-student recovery remains available while Add Charge is blocked");
                Check(window.ClientRectangle.Contains(window.RectangleToClient(button.RectangleToScreen(button.ClientRectangle))),
                    "The recovery action fits the existing individual charge dialog");
                window.Close();
            }
            try{reopened.SaveAsync(args,"Duplicate attempt").GetAwaiter().GetResult();throw new Exception("Duplicate submission allowed");}catch(InvalidOperationException){}
            Check(writes==1,"Reopening cannot create a new request while the original outcome is unknown");
            wrongDatabase=true;
            try{reopened.CheckAsync().GetAwaiter().GetResult();throw new Exception("Wrong database accepted");}catch(InvalidOperationException){}
            Check(reopened.Blocked&&File.Exists(path),"A failed outcome check retains the original recovery record");
            wrongDatabase=false;
            Check(reopened.CheckAsync().GetAwaiter().GetResult()==2&&writes==1&&!File.Exists(path)&&!reopened.Blocked,
                "Read-only recovery confirms committed charges without submitting them again");
            Check(Open().CheckAsync().GetAwaiter().GetResult() is null,"A completed recovery leaves no pending operation after reopening");
            loseReply=false;
            Check(Open().SaveAsync(args,"New deliberate posting").GetAwaiter().GetResult()==2&&writes==2&&!File.Exists(path),
                "A new deliberate posting is allowed after the earlier outcome is verified");
            using(var journal=MutationRecoveryJournal.Begin(MutationKind.AdditionalFeePosting,7777,new string('A',64),hash,"Never submitted",path)){}
            var absent=Open();absent.Load();
            Check(absent.CheckAsync().GetAwaiter().GetResult()==0&&!absent.Blocked&&!File.Exists(path),
                "A verified absent request can be cleared without an automatic write retry");
            File.WriteAllText(path,"{damaged");var damaged=Open();
            try{damaged.Load();throw new Exception("Corrupt recovery accepted");}catch(InvalidDataException){}
            Check(damaged.Blocked&&File.Exists(path),"Damaged recovery blocks posting and retains the evidence");
        }
        finally
        {
            File.Delete(path);
            CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Permissions);
        }
    }
}
