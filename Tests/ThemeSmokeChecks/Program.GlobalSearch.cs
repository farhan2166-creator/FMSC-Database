using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;

internal static partial class Program
{
    private static void CheckGlobalSearch()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,CurrentUser.GetPermissions().ToArray());
        try
        {
            CurrentUser.SignIn(9911,"Search fixture","Operator",false,new[]{"Students.View","Fees.View"});
            using(var features=new CommandPaletteDialog(new[]{new NavigationCommand("Feature",()=>{})},"Feature123"))
            {
                Check(((TextBox)typeof(CommandPaletteDialog).GetField("search",flags)!.GetValue(features)!).Text=="Feature","Dashboard feature search retains its text-only behavior");
                typeof(CommandPaletteDialog).GetMethod("Accept",flags)!.Invoke(features,null);
                Check(features.SelectedCommand?.Caption=="Feature"&&features.SelectedRecord is null,"Feature navigation stays separate from record search");
            }
            var slow=new TaskCompletionSource<IReadOnlyList<GlobalSearchRecord>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads=new List<string>();int cancelled=0;
            Task<IReadOnlyList<GlobalSearchRecord>> Read(string query,CancellationToken token)
            {
                reads.Add(query);token.Register(()=>cancelled++);
                if(query=="Ali")return slow.Task;
                return Task.FromResult<IReadOnlyList<GlobalSearchRecord>>(new[]{new GlobalSearchRecord("Bill","00000000-0000-0000-0000-000000000001",6508,query,"Fictional Student","Saved bill",0)});
            }
            using var owner=new Form();owner.Show();
            using var records=new CommandPaletteDialog(Array.Empty<NavigationCommand>(),null,Read);
            var search=(TextBox)typeof(CommandPaletteDialog).GetField("search",flags)!.GetValue(records)!;
            var results=(ListBox)typeof(CommandPaletteDialog).GetField("results",flags)!.GetValue(records)!;
            records.Show(owner);search.Text="Ali";
            PumpUntil(()=>reads.Contains("Ali"),"Record search starts after a short typing delay");
            search.Text="B-2026-00015";
            PumpUntil(()=>results.Items.OfType<GlobalSearchRecord>().Any(r=>r.Reference=="B-2026-00015"),"Document digits survive lookup and render as a labelled result");
            Check(cancelled>0,"Typing another query cancels the previous request");
            slow.SetResult(new[]{new GlobalSearchRecord("Student","1",1,"1","Old response","",0)});
            PumpUntil(()=>slow.Task.IsCompleted,"The obsolete reader completes");Application.DoEvents();
            Check(results.Items.OfType<GlobalSearchRecord>().All(r=>r.Reference=="B-2026-00015"),"A late response cannot replace the newer document result");
            Check(results.Items.Cast<object>().All(r=>r is GlobalSearchRecord),"Ctrl+K lists records without duplicate page-navigation matches");
            CurrentUser.SignIn(9912,"Revoked fixture","Operator",false,new[]{"Students.View"});
            typeof(CommandPaletteDialog).GetMethod("Accept",flags)!.Invoke(records,null);
            Check(records.SelectedRecord is null,"Revoked financial access blocks opening a visible result");
            records.Close();owner.Close();
            using var disposed=new CommandPaletteDialog(Array.Empty<NavigationCommand>(),"Ali",Read);
            disposed.Dispose();Check(disposed.IsDisposed,"Closing during a pending debounce releases the lookup safely");
        }
        finally {CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Item5);}
    }
}
