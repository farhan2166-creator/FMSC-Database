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
}
