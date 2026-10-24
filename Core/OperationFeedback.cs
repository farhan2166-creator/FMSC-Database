namespace FMSC_SMS.Core;

/// <summary>Explicit visual state for a message explaining why an action cannot continue.</summary>
internal static class OperationFeedback
{
    private sealed class State { internal bool Setting; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,State> States=new();
    internal static void Set(Control control,string message,bool blocked)
    {
        var state=States.GetValue(control,target=>{
            var created=new State();
            target.TextChanged+=(_,_)=>{if(!created.Setting)Apply(target,false);};
            return created;
        });
        state.Setting=true;
        try{control.Text=message;Apply(control,blocked);}
        finally{state.Setting=false;}
    }

    internal static void Apply(Control control,bool blocked)
    {
        control.ForeColor=blocked?AppTheme.Danger:AppTheme.MutedText;
        control.AccessibleDescription=control.Text;
    }
}
