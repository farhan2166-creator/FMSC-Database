using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Students.Admissions;

public sealed partial class StudentAdmissionPage
{
    private void BuildPlacementActions()
    {
        var hint=enrollmentActionsMenu.Items.Add("Select a student first.");hint.Enabled=false;
        var setup=enrollmentActionsMenu.Items.Add("Activate Current Student…");
        setup.ToolTipText="Review current placement, class fees and billing start before activating a legacy student.";
        setup.Click+=async(_,_)=>await OpenCurrentStudentSetupAsync();
        var complete=enrollmentActionsMenu.Items.Add("Complete Placement…");
        complete.ToolTipText="Review the existing draft, save any corrections, then confirm this placement.";
        complete.Click+=async(_,_)=>await OpenDraftPlacementAsync();
        var history=enrollmentActionsMenu.Items.Add("Placement History…");
        history.ToolTipText="View earlier placements, add verified missing history or correct dates. Adding history does not activate the student or post fees.";
        history.Click+=async(_,_)=>await OpenPlacementHistoryAsync();
        var section=new ToolStripSeparator();enrollmentActionsMenu.Items.Add(section);
        var hold=enrollmentActionsMenu.Items.Add("Put on Hold…");hold.Click+=async(_,_)=>await OpenPostingHoldAsync(false);
        var resume=enrollmentActionsMenu.Items.Add("Lift Hold / Resume…");resume.Click+=async(_,_)=>await OpenPostingHoldAsync(true);
        var departure=enrollmentActionsMenu.Items.Add("Record Departure…");departure.Click+=async(_,_)=>await CloseAdmissionAsync(false);
        var readmit=enrollmentActionsMenu.Items.Add("Re-Admit…");readmit.Click+=async(_,_)=>await OpenEnrollmentActionAsync("Re-Admit");
        var transfer=new ToolStripMenuItem("Transfer");enrollmentActionsMenu.Items.Add(transfer);
        foreach(string operation in new[]{"Transfer Section","Transfer Class","Transfer Campus"})
        {
            var item=transfer.DropDownItems.Add(operation);
            item.Click+=async(_,_)=>await OpenEnrollmentActionAsync(operation);
        }
        var advanced=new ToolStripMenuItem("Corrections / Advanced");enrollmentActionsMenu.Items.Add(advanced);
        var adopt=advanced.DropDownItems.Add("Review Left Record as Temporary Hold…");
        adopt.ToolTipText="Use only to correct a Left record that actually represents temporary absence. Review financial effects before saving.";
        adopt.Click+=async(_,_)=>await OpenPostingHoldAsync(false,true);
        enrollmentActionsMenu.Opening+=(_,_)=>{
            var state=AdmissionWorkflowActions.For(currentAdmissionId,_workspaceStudentStatus,IsPendingRegistration,legacySetupPending,
                pendingPlacementId,studentPostingHeld,IsBusy || unconfirmedAdmission is not null,
                CurrentUser.HasPermission("Students.Administer"),CurrentUser.HasPermission("Enrollment.Manage"),HasCurrentPlacement,_hasOfficialAdmissionNumber);
            void Show(ToolStripItem item,bool visible) {item.Visible=visible;item.Enabled=visible && state.Enabled;}
            Show(setup,state.Setup);Show(complete,state.CompleteDraft);Show(history,state.History);
            Show(hold,state.Hold);Show(resume,state.Resume);Show(departure,state.Departure);Show(readmit,state.Readmit);
            Show(transfer,state.Transfer);foreach(ToolStripItem item in transfer.DropDownItems)item.Enabled=transfer.Enabled;
            transfer.ToolTipText=HasCurrentPlacement?"Transfer within this academic year.":"Record a historical transfer within the recorded academic year, with a reason. Existing fees are preserved.";
            departure.ToolTipText=HasCurrentPlacement?"Record the student's departure.":"Record the actual historical leaving date within this placement's academic year.";
            Show(advanced,state.ReviewLeft);Show(adopt,state.ReviewLeft);
            section.Visible=state.History && (state.Hold || state.Resume || state.Departure || state.Readmit || state.Transfer || state.ReviewLeft);
            hint.Visible=!(state.Setup || state.CompleteDraft || state.History || state.Hold || state.Resume || state.Departure || state.Readmit || state.Transfer || state.ReviewLeft);
            hint.Text=currentAdmissionId<=0 ? "Select a student first." : IsPendingRegistration
                ? "Complete registration first." : "No permitted actions.";
            hint.ToolTipText=currentAdmissionId<=0 ? "Save or select a student first." : IsPendingRegistration
                ? "Complete this registration before using placement actions." : "No placement actions are available with your permissions.";
            enrollmentActionsMenu.PerformLayout();
        };
    }

    private async Task OpenPlacementHistoryAsync()
    {
        if (currentAdmissionId<=0 || IsBusy || IsPendingRegistration || !CurrentUser.HasPermission("Students.Administer")) return;
        if (HasUnsavedChanges) { SetStatus("Save personal-detail changes before opening placement history.");return; }
        int admission=currentAdmissionId;
        using var dialog=new StudentPlacementCorrectionDialog(admission,name.Text);
        dialog.ShowDialog(FindForm());
        if (dialog.Changed && !IsDisposed && currentAdmissionId==admission) await LoadStudentForEditAsync(admission);
    }

    private async Task OpenDraftPlacementAsync()
    {
        if (currentAdmissionId<=0 || pendingPlacementId<=0 || IsBusy || IsPendingRegistration || legacySetupPending
            || !CurrentUser.HasPermission("Students.Administer")) return;
        if (HasUnsavedChanges) { SetStatus("Save personal-detail changes before completing placement.");return; }
        int admission=currentAdmissionId;
        using var dialog=StudentPlacementCorrectionDialog.ForDraft(admission,name.Text,pendingPlacementId);
        dialog.ShowDialog(FindForm());
        if (dialog.Changed && !IsDisposed && currentAdmissionId==admission) await LoadStudentForEditAsync(admission);
    }
}
