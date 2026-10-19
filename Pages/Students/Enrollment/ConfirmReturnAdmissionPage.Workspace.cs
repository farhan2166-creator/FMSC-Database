using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Students.Enrollment;

public sealed partial class ConfirmReturnAdmissionPage
{
    private bool familyWorkspace,workspaceConfirmed;
    private int workspaceAdmission;
    internal decimal WorkspaceOpeningPlan=>!workspaceConfirmed && feeRows is not null
        ?feeRows.AsEnumerable().Sum(r=>Convert.ToDecimal(r["Amount"])-Convert.ToDecimal(r["DiscountAmount"])):0;
    internal event EventHandler? WorkspaceChanged;
    internal void ConfigureFamilyWorkspace()
    {
        familyWorkspace=true;
        PrepareForEmbedding(keepActions:true);
        ReturnConfirmed+=(_,_)=>workspaceConfirmed=true;
        var waive=AddAction("Waive All Opening Fees",(_,_)=>{
            if(confirmingReturn || feeRows is null || !CurrentUser.HasPermission("Fees.Adjust") || !fees.EndEdit())return;
            foreach(DataRow row in feeRows.Rows)row["DiscountAmount"]=row["Amount"];
            fees.Refresh();UpdateTotal();
        },"Secondary");
        GateAction(waive,"Fees.Adjust");
        fees.CellValueChanged+=(_,_)=>WorkspaceChanged?.Invoke(this,EventArgs.Empty);
        fees.DataBindingComplete+=(_,_)=>WorkspaceChanged?.Invoke(this,EventArgs.Empty);
    }
    internal async Task InitializeWorkspaceAsync(int admission)
    {
        workspaceAdmission=admission;
        await LoadPendingAsync(admission);
        pendingStudent.Enabled=false;
        WorkspaceChanged?.Invoke(this,EventArgs.Empty);
    }
    internal async Task<bool> SaveWorkspaceAsync()
    {
        if(workspaceConfirmed)return true;
        if(pendingStudent.SelectedRow is not DataRow selected || Convert.ToInt32(selected["AdmissionId"])!=workspaceAdmission)
        {Warn("The returning child could not be verified. Refresh this family before confirming.");return false;}
        await ConfirmAsync();
        return workspaceConfirmed;
    }
}
