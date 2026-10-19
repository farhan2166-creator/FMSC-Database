using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Students.Admissions;

// A narrow host adapter. The standalone registration screen keeps its original
// validation, writes, snapshots and durable unknown-outcome recovery.
internal sealed partial class QuickRegistrationFormPage
{
    internal event EventHandler? WorkspaceChanged;
    private bool workspaceLoaded;
    internal string WorkspaceName => name.Text.Trim();
    internal string WorkspaceParent => parent.Text.Trim();
    internal string WorkspaceContact => contact.Text.Trim();
    internal string WorkspaceAddress => address.Text.Trim();
    internal string WorkspacePlacement => placement.Text;
    internal bool WorkspaceNeedsRefresh => needsRefresh;
    internal decimal WorkspaceMonthly => PlanAmount(true);
    internal decimal WorkspaceOther => PlanAmount(false);
    private decimal PlanAmount(bool monthly) => rates.Columns.Contains("Include") ? rates.AsEnumerable()
        .Where(r => Convert.ToBoolean(r["Include"]) && (Convert.ToString(r["Frequency"]) == "Monthly") == monthly)
        .Sum(r => Convert.ToDecimal(r["Amount"]) - Convert.ToDecimal(r["Discount"])) : 0;

    internal void ConfigureFamilyWorkspace()
    {
        PrepareForEmbedding(keepActions:true);
        RelocateFromActions(save,next,bill,collect,newFamily,linkFamily,unlinkFamily);
        var waive = new ModernButton { Text="Waive All Included Fees",ThemeRole="Secondary",AutoSize=true };
        waive.Click += (_,_) => {
            if(savedStudent!=0 || IsBusy || recoveryBlocked || !CurrentUser.HasPermission("Fees.Adjust") || !fees.EndEdit())return;
            foreach(DataRow fee in rates.Rows)
                if(Convert.ToBoolean(fee["Include"]))
                {
                    fee["Discount"]=fee["Amount"];
                    if(Convert.ToString(fee["Frequency"])=="Monthly")fee["ConcessionLabel"]="Waived";
                }
            fees.Refresh();UpdateTotal();UpdateActions();
        };
        addFee.Parent?.Controls.Add(waive);
        WorkspaceChanged += (_,_) => waive.Enabled=savedStudent==0 && !IsBusy && !recoveryBlocked && feesLoaded && CurrentUser.HasPermission("Fees.Adjust");
        UpdateActions();
    }
    internal async Task<bool> InitializeWorkspaceAsync(int admission=0)
    {
        if(!workspaceLoaded)
        {
            await LoadLookupsAsync();workspaceLoaded=true;
            ReadRecoveryRecord();UpdateActions();
        }
        return admission<=0 || await OpenRegistrationAsync(admission);
    }
    internal void SetWorkspaceFamily(int anchor,string father,string phone,string home)
    {
        if(savedStudent>0 || IsBusy || recoveryBlocked)return;
        applyingRegistration=true;
        try
        {
            family.Clear();familyAnchor=anchor;familyReference=anchor>0?AdmissionNumbers.ForId(anchor):"";
            parent.Text=father;contact.Text=phone;address.Text=home;
        }
        finally{applyingRegistration=false;}
        UpdateActions();
    }
    internal async Task<bool> SaveWorkspaceAsync()
    {
        if(IsBusy || recoveryBlocked || needsRefresh || !fees.EndEdit())return false;
        if(savedStudent>0 && !HasPendingChanges)return true;
        await SaveAsync();
        return savedStudent>0 && !recoveryBlocked && !needsRefresh && !HasPendingChanges;
    }
}
