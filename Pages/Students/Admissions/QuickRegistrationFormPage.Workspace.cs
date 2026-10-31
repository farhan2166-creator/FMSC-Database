using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Students.Admissions;

// A narrow host adapter. The standalone registration screen keeps its original
// validation, writes, snapshots and durable unknown-outcome recovery.
internal sealed partial class QuickRegistrationFormPage
{
    internal event EventHandler? WorkspaceChanged;
    private bool workspaceLoaded,workspaceHosted,recoveryOnly;
    private Guid? workspaceSaveRequest;
    private bool siblingEntry;
    private readonly ModernTextBox siblingBirth = HumanUi.TextBox(false, 500);
    internal void ConfigureSiblingEntry(int anchor, string father, string phone, string home, string birth)
    {
        if (anchor <= 0) throw new ArgumentException("Load a student before admitting a sibling.");
        siblingEntry = workspaceHosted = true;
        PrepareForEmbedding(keepActions: false);
        SetWorkspaceFamily(anchor, father, phone, home);
        siblingBirth.Text = birth; siblingBirth.ReadOnly = true;
        var previous = registrationContent.GetControlFromPosition(0, 1);
        var fields = Fields(("Joining date", joining, true, 1), ("Academic year", year, true, 1),
            ("Student name", name, true, 1), ("Campus / Class / Section", placement, true, 1),
            ("Father name", parent, true, 1), ("Place of birth", siblingBirth, false, 1));
        // Keep inherited values alive for validation and saving when removing the old grid.
        foreach (var input in new[] { contact, address })
        {
            registrationContent.Controls.Add(input);
            input.Visible = false;
        }
        registrationContent.Controls.Remove(previous); previous?.Dispose();
        registrationContent.Controls.Add(fields, 0, 1);
        foreach (Control input in new Control[] { name, joining, year, placement, parent, contact, siblingBirth, address }) input.MaximumSize = Size.Empty;
        root.RowStyles[0].Height = AppMetrics.FieldCardHeight(3);
        registrationCard.SetHeaderText("Admit Sibling", "Shared details come from the loaded student. Complete this child's name, placement and fees.");
        year.SelectedValue = defaultAcademicYear;
        UpdateActions();
    }
    internal void FocusSiblingName() => name.Focus();
    internal Func<Task>? WorkspaceRecoveryRequested;
    internal string WorkspaceName => name.Text.Trim();
    internal string WorkspaceParent => parent.Text.Trim();
    internal string WorkspaceContact => contact.Text.Trim();
    internal string WorkspaceAddress => address.Text.Trim();
    internal string WorkspacePlacement => placement.Text;
    internal string WorkspaceStatus=>StatusLabel.Text;
    internal void ShowAdmissionRegistrationGuidance(string message) => SetStatus(message);
    internal bool WorkspaceNeedsRefresh => needsRefresh;
    internal bool CommitWorkspaceEdit()=>fees.EndEdit();
    internal decimal WorkspaceMonthly => PlanAmount(true);
    internal decimal WorkspaceOther => PlanAmount(false);
    private decimal PlanAmount(bool monthly) => rates.Columns.Contains("Include") ? rates.AsEnumerable()
        .Where(r => Convert.ToBoolean(r["Include"]) && (Convert.ToString(r["Frequency"]) == "Monthly") == monthly)
        .Sum(r => Convert.ToDecimal(r["Amount"]) - Convert.ToDecimal(r["Discount"])) : 0;

    internal void ConfigureFamilyWorkspace()
    {
        workspaceHosted=true;
        PrepareForEmbedding(keepActions:false);
        RelocateFromActions(save,next,bill,collect,newFamily,linkFamily,unlinkFamily,checkSave,reload);
        // Shared parent fields live in the family header. Keep their actual
        // controls/values for the established admission validator and writer.
        parent.Parent!.Visible=contact.Parent!.Visible=address.Parent!.Visible=false;
        if(parent.Parent.Parent is TableLayoutPanel basics)
        {basics.RowStyles[2].Height=0;basics.RowStyles[3].Height=0;}
        root.RowStyles[0].Height=AppMetrics.FieldCardHeight(2);
        foreach(var column in new[]{("Include",55),("Frequency",85),("Amount",90),("Discount",95),("NetAmount",100),("ConcessionLabel",105),("Remove",32)})
            fees.Columns[column.Item1].Width=AppScale.Scale(column.Item2);
        if(root.GetControlFromPosition(0,1) is HumanCard feePlan)
            feePlan.SetHeaderText("Fees & Concessions","Choose included fees and each concession. Monthly concessions belong to this child's placement.");
        SetStatus("Review this child, then use Save & Prepare Selected above.");
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
        addFee.Parent?.Controls.Add(checkSave);addFee.Parent?.Controls.Add(reload);
        WorkspaceChanged += (_,_) => {
            waive.Visible=savedStudent==0;
            waive.Enabled=savedStudent==0 && !IsBusy && !recoveryBlocked && feesLoaded && CurrentUser.HasPermission("Fees.Adjust");
        };
        UpdateActions();
    }
    internal async Task<bool> InitializeWorkspaceAsync(int admission=0)
    {
        if(!workspaceLoaded)
        {
            await LoadLookupsAsync();workspaceLoaded=true;
            UpdateActions();
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
    internal void RefreshWorkspaceRecovery(){ReadRecoveryRecord();UpdateActions();}
    internal async Task InitializeRecoveryAsync()
    {
        recoveryOnly=true;
        PrepareForEmbedding(keepActions:true);
        foreach(var button in new[]{save,next,bill,collect,newFamily,linkFamily,unlinkFamily,reload})button.Visible=false;
        UpdateActions();
        await LoadLookupsAsync();
        ReadRecoveryRecord();UpdateActions();
        await CheckSaveOutcomeAsync();
    }
}
