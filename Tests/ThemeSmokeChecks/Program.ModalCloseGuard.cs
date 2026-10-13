using System.Reflection;
using FMSC_SMS.Core;

internal static partial class Program
{
    private sealed class ModalGuardFixture : Control,IBusyPage,IUnsavedChanges
    {
        public bool IsBusy{get;set;}
        public string BusyMessage=>"Operation pending";
        public bool HasUnsavedChanges{get;set;}
        public string UnsavedChangesMessage=>"Discard this draft?";
    }
    private static void CheckModalPageCloseGuard()
    {
        const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
        using var dialog=new Form();using var page=new ModalGuardFixture();
        bool completed=false,discard=false;int asks=0;
        Func<bool> done=()=>completed;Func<string,bool> confirm=message=>{Check(message==page.UnsavedChangesMessage,"Modal guard names the current page draft");asks++;return discard;};
        var guard=typeof(ApplicationWindowForm).Assembly.GetType("FMSC_SMS.Core.ModalPageCloseGuard",true)!;
        guard.GetMethod("Attach",flags)!.Invoke(null,new object[]{dialog,page,done,confirm});
        bool Closing(CloseReason reason=CloseReason.UserClosing){var args=new FormClosingEventArgs(reason,false);typeof(Form).GetMethod("OnFormClosing",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(dialog,new object[]{args});return args.Cancel;}
        Check(!Closing()&&asks==0,"An untouched modal closes without a discard prompt");
        page.HasUnsavedChanges=true;Check(Closing()&&asks==1,"Stay blocks modal closure and retains its draft");
        discard=true;Check(!Closing()&&asks==2,"Deliberate discard permits a dirty modal to close");
        page.IsBusy=true;Check(Closing()&&Closing(CloseReason.ApplicationExitCall)&&asks==2,"Both user and application close are unconditionally blocked during a submitted operation");
        completed=true;Check(Closing()&&asks==2,"Completion cannot bypass an operation that is still running");
        page.IsBusy=false;Check(!Closing()&&asks==2,"A confirmed successful workflow closes without another draft-discard question");
    }
}
