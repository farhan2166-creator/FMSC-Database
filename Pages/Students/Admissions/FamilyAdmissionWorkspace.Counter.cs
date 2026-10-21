using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Collection;

namespace FMSC_SMS.Pages.Students.Admissions;

public sealed partial class FamilyAdmissionWorkspace
{
    private void ReadWorkspaceRecovery()
    {
        try { recoveryPending=QuickRegistrationRecoveryJournal.ReadPending() is not null; }
        catch(Exception ex)
        {
            recoveryPending=true;
            AppExceptionHandler.LogNonFatal(ex,"Reading family counter recovery");
        }
    }

    private async Task RecoverPreviousSaveAsync()
    {
        if(IsBusy)return;
        busy=true;UpdateWorkspace();
        try
        {
            using var page=new QuickRegistrationFormPage(navigate,0,initialize:false){Dock=DockStyle.Fill};
            using var dialog=new ApplicationWindowForm {
                Text="Check Previous Registration Save",StartPosition=FormStartPosition.CenterParent,
                ClientSize=new Size(AppScale.Scale(1150),AppScale.Scale(760)),MinimumSize=new Size(850,600)};
            dialog.Controls.Add(page);
            // Recovery has its own editor. It can never replace a loaded family child.
            dialog.FormClosing+=(_,e)=>{if(page.IsBusy)e.Cancel=true;};
            dialog.Shown+=async(_,_)=>{
                try{await page.InitializeRecoveryAsync();}
                catch(Exception ex){if(!dialog.IsDisposed)page.ShowRegistrationContextWarning(DbMessage.UserMessage(ex));}
            };
            dialog.ShowDialog(FindForm());
            ReadWorkspaceRecovery();
            foreach(var child in members)child.Registration?.RefreshWorkspaceRecovery();
            if(!recoveryPending)SetStatus("Previous save checked. This family's drafts are retained. Find the recovered child by their reference if they belong to another family.",PageStatusKind.Success);
        }
        catch(Exception ex){Fail(ex);ReadWorkspaceRecovery();}
        finally{busy=false;if(!IsDisposed)UpdateWorkspace();}
        await Task.CompletedTask;
    }

    private async Task CollectSelectedAsync()
    {
        if(IsBusy || !CurrentUser.HasPermission("Fees.View") || !CurrentUser.HasPermission("Fees.Collect"))return;
        if(initialize)ReadWorkspaceRecovery();
        var selected=members.Where(c=>c.Included).ToArray();
        if(recoveryPending || selected.Length==0 || selected.Any(c=>!CanBill(c)))
        {Warn("Save the selected children and finish required returns or save checks before payment.");return;}
        busy=true;UpdateWorkspace();
        try
        {
            using var fresh=await readFamily(anchor);
            int[] ids=FamilyBillingData.SelectMembers(fresh,selected.Select(c=>c.Id));
            if(fresh.AsEnumerable().Where(r=>ids.Contains(Convert.ToInt32(r["AdmissionId"])))
                .Any(r=>FieldText(r,"Status")!="Pending" && FamilyBillingData.Readiness(r) is not ("Ready for Billing" or "On Hold")))
                throw new InvalidOperationException("A selected child's readiness changed. Refresh this family before payment.");
            using var page=new FeeCollectionPage(initialize:false){Dock=DockStyle.Fill};
            page.ConfigureCounter(ids);
            using var dialog=new ApplicationWindowForm {
                Text="Collect Payment — "+father,StartPosition=FormStartPosition.CenterParent,
                ClientSize=new Size(AppScale.Scale(1400),AppScale.Scale(850)),MinimumSize=new Size(900,650)};
            dialog.Controls.Add(page);ModalPageCloseGuard.Attach(dialog,page);
            dialog.Shown+=async(_,_)=>await page.InitializeCounterAsync();
            dialog.ShowDialog(FindForm());
            foreach(var child in selected)
                if(page.CounterReceipts.TryGetValue(child.Id,out string? receipt))child.Outcome="Payment recorded · "+receipt;
            bool refreshed=await RefreshSavedChildrenAsync();
            if(refreshed)SetStatus(page.CounterReceipts.Count>0
                ?"Payment recorded. Updated dues are shown for each child. Print or reprint the receipt in Collect Payment / Receipt."
                :"Family retained. No new payment was confirmed by this visit to Fee Collection.",page.CounterReceipts.Count>0?PageStatusKind.Success:PageStatusKind.Info);
        }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
        finally{busy=false;if(!IsDisposed)UpdateWorkspace();}
    }
}
