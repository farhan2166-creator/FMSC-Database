using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Admissions;

internal static partial class Program
{
    private static void CheckAdmissionSharedBusy()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var page=new StudentAdmissionPage();
        var run=typeof(V2Page).GetMethod("RunBusyAsync",flags)!;
        foreach(var message in new[]{"Confirming placement…","Completing admission…","Verifying legacy record…","Preparing bill…","Preparing admission form…"})
        {
            var reply=new TaskCompletionSource();Func<Task> operation=()=>reply.Task;
            var task=(Task<bool>)run.Invoke(page,new object[]{message,operation,Array.Empty<Control>()})!;
            Check(page.IsBusy&&page.BusyMessage.Contains(message),"Admission exposes the exact shared operation while its response is pending");
            int duplicates=0;Func<Task> again=()=>{duplicates++;return Task.CompletedTask;};
            var refused=(Task<bool>)run.Invoke(page,new object[]{message,again,Array.Empty<Control>()})!;
            Check(refused.IsCompletedSuccessfully&&!refused.Result&&duplicates==0,"An active shared admission operation cannot be submitted twice");
            reply.SetResult();PumpUntil(()=>task.IsCompleted,"Shared admission operation settles");
            Check(task.Result&&!page.IsBusy,"Admission releases its busy contract after the full shared operation finishes");
        }
        var failedReply=new TaskCompletionSource();Func<Task> fail=()=>failedReply.Task;
        var failed=(Task<bool>)run.Invoke(page,new object[]{"Confirming placement…",fail,Array.Empty<Control>()})!;
        failedReply.SetException(new IOException("Fixture server refusal"));PumpUntil(()=>failed.IsCompleted,"Failed admission operation settles");
        Check(failed.IsFaulted&&!page.IsBusy,"A refused operation releases the shared busy state without swallowing the failure");
        Check(!page.IsHandleCreated,"Shared admission busy fixture never starts real SQL loaders");
    }
    private static void CheckPostingReversalBusy()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var page=new FMSC_SMS.Pages.Finance.Billing.FeePostingReversalPage();
        var reply=new TaskCompletionSource();Func<Task> operation=()=>reply.Task;
        var run=typeof(V2Page).GetMethod("RunBusyAsync",flags)!;
        var task=(Task<bool>)run.Invoke(page,new object[]{((IBusyPage)page).BusyMessage,operation,new Control[]{page}})!;
        Check(((IBusyPage)page).IsBusy&&!page.Enabled,"Posting reversal publishes its busy state and locks conflicting page controls during the operation");
        reply.SetResult();PumpUntil(()=>task.IsCompleted,"Reversal guard operation settles");
        Check(task.Result&&!((IBusyPage)page).IsBusy&&page.Enabled,"Reversal completion releases both navigation and conflicting controls");
        Check(!page.IsHandleCreated,"Reversal guard fixture never invokes real database posting or reversal");
    }

    private static void CheckFamilyMaintenanceBusy()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var type=typeof(FMSC_SMS.Pages.Students.Profile.StudentFamilyPage);
        using var page=(Control)Activator.CreateInstance(type,flags,null,new object[]{0,false},null)!;
        var run=type.GetMethod("RunFamilyActionAsync",flags)!;var reply=new TaskCompletionSource();Func<Task> action=()=>reply.Task;
        var task=(Task)run.Invoke(page,new object[]{action})!;
        Check(((IBusyPage)page).IsBusy,"Family maintenance publishes the pending link operation to shell and modal guards");
        int repeats=0;Func<Task> repeat=()=>{repeats++;return Task.CompletedTask;};
        var denied=(Task)run.Invoke(page,new object[]{repeat})!;
        Check(denied.IsCompletedSuccessfully&&repeats==0,"A second family action cannot overlap a pending link change");
        reply.SetResult();PumpUntil(()=>task.IsCompleted,"Family operation settles");
        Check(task.IsCompletedSuccessfully&&!((IBusyPage)page).IsBusy,"The family guard stays active until its full action including reload completes");
        Check(!page.IsHandleCreated,"Family fixture starts no SQL lookup or family write");
    }

}
