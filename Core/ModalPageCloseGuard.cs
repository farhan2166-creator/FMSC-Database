namespace FMSC_SMS.Core;

/// <summary>Modal owners must check the hosted page, just as the dashboard does.</summary>
internal static class ModalPageCloseGuard
{
    internal static void Attach(Form dialog,Control page,Func<bool>? completed=null,Func<string,bool>? confirmDiscard=null)
    {
        dialog.FormClosing+=(_,e)=>{
            // A submitted operation is never a confirmable discard.
            if(page is IBusyPage busy&&busy.IsBusy){e.Cancel=true;return;}
            if(completed?.Invoke()==true)return;
            if(page is IUnsavedChanges dirty&&dirty.HasUnsavedChanges)
                e.Cancel=!(confirmDiscard?.Invoke(dirty.UnsavedChangesMessage)
                    ??ModernMessageDialog.Confirm(dialog,dirty.UnsavedChangesMessage,UiText.Dialogs.UnsavedChanges));
        };
    }
}
