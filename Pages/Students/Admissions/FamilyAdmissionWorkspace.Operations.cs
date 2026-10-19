using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.StudentAccounts;
using FMSC_SMS.Pages.Students.Enrollment;

namespace FMSC_SMS.Pages.Students.Admissions;

public sealed partial class FamilyAdmissionWorkspace
{
    private async Task OpenPendingRegistrationsAsync()
    {
        if(IsBusy || !QuickRegistrationAccess.CanUse(CurrentUser.HasPermission) ||
            members.Any(c=>c.Registration?.HasUnresolvedSave==true))return;
        int selected=0;
        busy=true;UpdateWorkspace();
        try
        {
            using var dialog=new ApplicationWindowForm{Text="Pending Registrations",StartPosition=FormStartPosition.CenterParent,
                ClientSize=new Size(AppScale.Scale(1200),AppScale.Scale(800)),MinimumSize=new Size(850,600)};
            using var list=new FMSC_SMS.Pages.Students.Maintenance.AdmissionReviewListPage(navigate,true,id=>{
                selected=id;dialog.DialogResult=DialogResult.OK;dialog.Close();return Task.CompletedTask;
            }){Dock=DockStyle.Fill};
            dialog.Controls.Add(list);dialog.ShowDialog(FindForm());
            // Confirming a registration in the list can update the loaded family.
            if(selected==0 && anchor>0)await RefreshSavedChildrenAsync();
        }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
        finally{busy=false;if(!IsDisposed)UpdateWorkspace();}
        if(selected>0 && !IsDisposed)await LoadFamilyAsync(selected);
        else if(!IsDisposed && current is {Id:>0} child && !child.Dirty)await ReopenChildAsync(child);
    }

    private bool DiscardAllowed()=>!HasUnsavedChanges || Confirm(UnsavedChangesMessage,UiText.Dialogs.UnsavedChanges);
    private void ClearWorkspace()
    {
        familyVersion++;
        editorHost.Controls.Clear();foreach(var child in members)(child.Viewport??child.Editor)?.Dispose();
        members.Clear();selecting=true;try{children.Items.Clear();}finally{selecting=false;}
        current=null;anchor=0;father=phone=address="";SyncFamilyFields();ShowEmptyEditor();
    }
    private async Task StartNewFamilyAsync()
    {
        if(IsBusy || !CurrentUser.HasPermission("Students.Admit") || !DiscardAllowed())return;
        ClearWorkspace();find.Clear();await AddNewChildAsync();
    }
    private async Task AddNewChildAsync()
    {
        if(IsBusy || !CurrentUser.HasPermission("Students.Admit") || members.Any(c=>c.Registration?.HasUnresolvedSave==true))return;
        if(members.Count>0 && !CurrentUser.HasPermission("Students.Administer")){Warn("Student-administration access is required to add siblings.");return;}
        var source=members.Select(c=>c.Registration).FirstOrDefault(r=>r is not null && r.WorkspaceParent.Length>0);
        if(anchor==0 && source is not null){father=source.WorkspaceParent;phone=source.WorkspaceContact;address=source.WorkspaceAddress;}
        var child=new Child();AddMember(child);await ShowChildAsync(child);UpdateWorkspace();
    }
    private async Task LoadFamilyAsync(int id)
    {
        if(IsBusy || !FamilyAdmissionData.CanUse(CurrentUser.HasPermission) || !DiscardAllowed())return;
        busy=true;UpdateWorkspace();
        try
        {
            using var rows=await readFamily(id);
            if(IsDisposed)return;
            if(rows.Rows.Count==0){Warn("That student could not be found. The current workspace has been retained.");return;}
            if(!rows.AsEnumerable().Any(r=>Convert.ToInt32(r["AdmissionId"])==id))throw new InvalidDataException("The selected student's family could not be verified.");
            ClearWorkspace();anchor=id;
            foreach(DataRow row in rows.Rows)
            {
                var copy=rows.Clone();copy.ImportRow(row);var saved=copy.Rows[0];
                AddMember(new Child{Id=Convert.ToInt32(row["AdmissionId"]),Record=saved,Name=FieldText(row,"StudentName"),
                    Included=FieldText(row,"Status") is "Pending" or "Active",Outcome="Loaded"});
            }
            var owner=members.First(c=>c.Id==id).Record!;father=FieldText(owner,"FatherName");phone=FieldText(owner,"ContactNumber");address=FieldText(owner,"Address");
            SyncFamilyFields();
            busy=false;await ShowChildAsync(members.First(c=>c.Id==id));
            SetStatus("Family loaded. Review children, add a new child if needed, then process the selected children.");
        }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
        finally{busy=false;if(!IsDisposed)UpdateWorkspace();}
    }
    private async Task ReloadFamilyAsync()
    {
        if(IsBusy || anchor<=0)return;
        busy=true;UpdateWorkspace();
        try{if(await RefreshSavedChildrenAsync())SetStatus("Family balances refreshed. Unsaved child drafts are retained.");}
        finally{busy=false;UpdateWorkspace();}
    }
    private async Task ShowChildAsync(Child child)
    {
        if(busy || child.Busy)return;
        if(current!=child && current?.Registration?.CommitWorkspaceEdit()==false)
        {
            selecting=true;try{children.SelectedIndex=members.IndexOf(current);}finally{selecting=false;}
            Warn("Correct the current fee amount before switching children.");return;
        }
        busy=true;UpdateWorkspace();
        try
        {
            current=child;selecting=true;try{children.SelectedIndex=members.IndexOf(child);}finally{selecting=false;}
            editorHost.Controls.Clear();
            if(child.Editor is null)
            {
                if(child.Id==0 || FieldText(child.Record,"Status")=="Pending")
                {
                    var registration=new QuickRegistrationFormPage(navigate,0,initialize:false);
                    registration.ConfigureFamilyWorkspace();child.Editor=registration;
                    registration.WorkspaceChanged+=(_,_)=>{
                        if(IsDisposed)return;
                        UpdateWorkspace();
                        if(current==child && !busy && !processing && registration.WorkspaceStatus.Length>0)SetStatus(registration.WorkspaceStatus);
                    };
                }
                else if(child.AwaitsReturn)
                {
                    var returning=new ConfirmReturnAdmissionPage(navigate,child.Id,false,false);
                    returning.ConfigureFamilyWorkspace();child.Editor=returning;
                    returning.WorkspaceChanged+=(_,_)=>{if(!IsDisposed)UpdateWorkspace();};
                    returning.ReturnConfirmed+=async(_,_)=>{
                        child.ReturnCompleted=true;child.Outcome="Return confirmed";UpdateWorkspace();
                        await RefreshSavedChildrenAsync();
                    };
                }
                else child.Editor=BuildExistingEditor(child);
            }
            int minimumHeight=child.Registration is not null?530:child.Return is not null?650:450;
            child.Viewport??=new ResponsiveViewport(child.Editor,new Size(AppScale.Scale(620),AppScale.Scale(minimumHeight)));
            editorHost.Controls.Add(child.Viewport);
            if(!child.Initialized && initialize)
            {
                bool loaded=true;
                if(child.Registration is { } registration)
                {
                    loaded=await registration.InitializeWorkspaceAsync(child.Id);
                    if(child.Id==0)registration.SetWorkspaceFamily(anchor,father,phone,address);
                }
                else if(child.Return is { } returning)await returning.InitializeWorkspaceAsync(child.Id);
                child.Initialized=loaded;
                if(!loaded)child.Outcome="Reload needed";
            }
            if(child.Id==0 && child.Registration is { } draft && draft.WorkspaceParent.Length==0)
                draft.SetWorkspaceFamily(anchor,father,phone,address);
        }
        catch(Exception ex){child.Outcome="Needs attention";if(!IsDisposed)Fail(ex);}
        finally
        {
            busy=false;
            if(!IsDisposed)
            {
                UpdateWorkspace();
                if(!processing && child.Registration is { } registration && registration.WorkspaceStatus.Length>0)SetStatus(registration.WorkspaceStatus);
            }
        }
    }
    private Control BuildExistingEditor(Child child)
    {
        var card=HumanUi.Card(child.DisplayName,$"{FieldText(child.Record,"AcademicYear")} · {FieldText(child.Record,"CampusName")} · {FieldText(child.Record,"ClassName")} - {FieldText(child.Record,"SectionName")} · {child.Status}");
        var panel=new StudentFeeAssignmentsPanel();child.Assignments=panel;
        panel.Changed+=async(_,_)=>{child.Outcome="Fee changes saved";await RefreshSavedChildrenAsync();};
        card.Content.Controls.Add(panel);
        if(initialize && child.Record is not null && int.TryParse(FieldText(child.Record,"EnrollmentId"),out int enrollment))panel.SetStudent(enrollment,child.Id);
        return card;
    }
    private void RemoveDraft()
    {
        if(IsBusy || current is not {Id:0} child || child.Registration?.HasUnresolvedSave==true)return;
        if(child.Dirty && !Confirm("Discard this child's unsaved draft? Other children are retained.",UiText.Dialogs.UnsavedChanges))return;
        int index=members.IndexOf(child);editorHost.Controls.Clear();(child.Viewport??child.Editor)?.Dispose();members.RemoveAt(index);
        selecting=true;try{children.Items.RemoveAt(index);}finally{selecting=false;}
        current=null;ShowEmptyEditor();UpdateWorkspace();
    }
    private async Task ProcessSelectedAsync()
    {
        if(IsBusy || !FamilyAdmissionData.CanUse(CurrentUser.HasPermission))return;
        if(current?.Registration?.CommitWorkspaceEdit()==false)return;
        if(members.Any(c=>c.Registration?.HasUnresolvedSave==true))
        {Warn("Use Check Save Outcome on the affected child before processing more children.");return;}
        var selected=members.Where(c=>c.Included).ToArray();
        if(selected.Length==0)return;
        if(selected.Any(c=>c.Id==0) && string.IsNullOrWhiteSpace(familyParent.Text))
        {Warn("Enter the shared parent name above before processing new children.");familyParent.Focus();return;}
        if(selected.Any(c=>c.Id==0) && !PakistanPhoneNumber.IsValidOptional(familyPhone.Text))
        {Warn(PakistanPhoneNumber.ValidationMessage);familyPhone.Focus();return;}
        if(!Confirm($"Process {selected.Length} selected children?\n\n"+string.Join("\n",selected.Select(c=>$"{c.DisplayName} — {c.Status}"))+
            "\n\nEach child uses their established save or return process. Completed children remain saved if a later child needs attention. Payment is collected separately.","Process Family"))return;
        processing=true;UpdateWorkspace();
        try
        {
            foreach(var child in selected)
            {
                await ShowChildAsync(child);
                if(!child.Initialized && initialize){child.Outcome="Reload needed";Warn("The selected child could not load. Processing stopped; earlier saved children are retained.");return;}
                child.Outcome="Processing…";UpdateWorkspace();
                bool saved;
                if(child.Registration is { } registration)
                {
                    if(child.Id==0 && anchor>0)registration.SetWorkspaceFamily(anchor,father,registration.WorkspaceContact,registration.WorkspaceAddress);
                    saved=await registration.SaveWorkspaceAsync();
                    if(saved)
                    {
                        child.Id=registration.SavedRegistrationId;
                        if(anchor==0){anchor=child.Id;father=registration.WorkspaceParent;phone=registration.WorkspaceContact;address=registration.WorkspaceAddress;}
                        child.Outcome="Registration saved";
                    }
                }
                else if(child.Return is { } returning){saved=await returning.SaveWorkspaceAsync();if(saved)child.Outcome="Return confirmed";}
                else{saved=CanBill(child);if(saved)child.Outcome="Ready · no admission changes";}
                if(!saved)
                {
                    child.Outcome="Needs attention";
                    Warn(child.Registration?.WorkspaceStatus is {Length:>0} detail?detail:
                        "Processing stopped at this child. Review their inputs; earlier saved children are retained.");return;
                }
                if(!await RefreshSavedChildrenAsync())
                {Warn("The saved result could not refresh. Processing stopped; refresh the family before continuing.");return;}
            }
            SetStatus("Selected children processed. Review the family and print the selected bill.",PageStatusKind.Success);
        }
        catch(Exception ex){if(current is not null)current.Outcome="Needs attention";if(!IsDisposed)Fail(ex);}
        finally{processing=false;if(!IsDisposed)UpdateWorkspace();}
    }
    private async Task<bool> RefreshSavedChildrenAsync()
    {
        if(anchor<=0 || !initialize)return true;
        int requestedAnchor=anchor,requestedVersion=familyVersion;
        try
        {
            using var fresh=await readFamily(requestedAnchor);
            if(IsDisposed || anchor!=requestedAnchor || familyVersion!=requestedVersion)return false;
            var owner=fresh.AsEnumerable().FirstOrDefault(r=>Convert.ToInt32(r["AdmissionId"])==anchor);
            if(owner is null)throw new InvalidOperationException("The family anchor could not be verified. Load the family again.");
            father=FieldText(owner,"FatherName");phone=FieldText(owner,"ContactNumber");address=FieldText(owner,"Address");
            SyncFamilyFields();
            foreach(var child in members.Where(c=>c.Id>0))
            {
                var row=fresh.AsEnumerable().FirstOrDefault(r=>Convert.ToInt32(r["AdmissionId"])==child.Id);
                if(row is null){child.Outcome="Family changed · refresh";child.MembershipVerified=false;continue;}
                var copy=fresh.Clone();copy.ImportRow(row);child.Record=copy.Rows[0];child.Name=FieldText(row,"StudentName");child.MembershipVerified=true;
            }
            foreach(DataRow row in fresh.Rows)
                if(members.All(c=>c.Id!=Convert.ToInt32(row["AdmissionId"])))
                {
                    var copy=fresh.Clone();copy.ImportRow(row);
                    AddMember(new Child{Id=Convert.ToInt32(row["AdmissionId"]),Name=FieldText(row,"StudentName"),Record=copy.Rows[0],Included=false,Outcome="Added since last load · review"});
                }
            UpdateWorkspace();
            return true;
        }
        catch(Exception ex)
        {
            if(IsDisposed || anchor!=requestedAnchor || familyVersion!=requestedVersion)return false;
            foreach(var child in members.Where(c=>c.Id>0))child.MembershipVerified=false;
            if(!IsDisposed){Fail(ex);UpdateWorkspace();}return false;
        }
    }
    private async Task ReviewFeesAsync()
    {
        if(IsBusy || current is not {Id:>0} child || child.Dirty || !CurrentUser.HasPermission("Fees.View") ||
            !int.TryParse(FieldText(child.Record,"EnrollmentId"),out int enrollment) || enrollment<=0)return;
        busy=true;UpdateWorkspace();
        bool reload=false;
        try
        {
            using var dialog=new FeeAssignmentsDialog(enrollment,child.Id,child.DisplayName);
            dialog.ShowDialog(FindForm());
            if(dialog.ChangesMade){child.Outcome="Fee changes saved";await RefreshSavedChildrenAsync();reload=true;}
        }
        catch(Exception ex){Fail(ex);}
        finally{busy=false;UpdateWorkspace();}
        if(reload)await ReopenChildAsync(child);
    }
    private async Task ReviewAdmissionAsync()
    {
        if(IsBusy || current is not {Id:>0} child || child.Dirty || !CurrentUser.HasPermission("Students.Admit"))return;
        busy=true;UpdateWorkspace();
        try
        {
            using var page=new StudentAdmissionPage(null,child.Id,registrationReview:FieldText(child.Record,"Status")=="Pending"){Dock=DockStyle.Fill};
            using var dialog=new ApplicationWindowForm{Text="Review Admission — "+child.DisplayName,StartPosition=FormStartPosition.CenterParent,
                ClientSize=new Size(AppScale.Scale(1200),AppScale.Scale(800)),MinimumSize=new Size(850,600)};
            bool completed=false;
            ModalPageCloseGuard.Attach(dialog,page,()=>completed);
            page.RegistrationFinished+=()=>{completed=true;dialog.DialogResult=DialogResult.OK;dialog.Close();};
            dialog.Controls.Add(page);dialog.ShowDialog(FindForm());await RefreshSavedChildrenAsync();
        }
        catch(Exception ex){Fail(ex);}
        finally{busy=false;UpdateWorkspace();}
        await ReopenChildAsync(child);
    }
    private async Task ReopenChildAsync(Child child)
    {
        if(IsDisposed || child.Dirty || child.Busy)return;
        editorHost.Controls.Clear();(child.Viewport??child.Editor)?.Dispose();child.Viewport=null;child.Editor=null;child.Assignments=null;child.Initialized=false;
        await ShowChildAsync(child);
    }
    private async Task PrintSelectedAsync()
    {
        if(IsBusy || !CurrentUser.HasPermission("Fees.View"))return;
        var selected=members.Where(c=>c.Included).ToArray();
        if(selected.Length==0 || selected.Any(c=>!CanBill(c))){Warn("Save the selected child drafts and finish required returns before printing.");return;}
        busy=true;UpdateWorkspace();
        int actor=CurrentUser.UserId,requestedAnchor=anchor,requestedVersion=familyVersion;
        bool Current()=>!IsDisposed && CurrentUser.UserId==actor && anchor==requestedAnchor && familyVersion==requestedVersion && CurrentUser.HasPermission("Fees.View");
        try
        {
            int[] ids=selected.Select(c=>c.Id).Distinct().ToArray();
            using var fresh=await readFamily(anchor);
            if(!Current())return;
            foreach(int id in ids)
            {
                var row=fresh.AsEnumerable().FirstOrDefault(r=>Convert.ToInt32(r["AdmissionId"])==id);
                if(row is null || (FieldText(row,"Status")!="Pending" && FamilyBillingData.Readiness(row)!="Ready for Billing"))
                    throw new InvalidOperationException("A selected child's family or billing readiness changed. Refresh the family before printing.");
            }
            using var charges=await FamilyAdmissionData.ReadBillAsync(anchor,ids);
            if(!Current())return;
            if(charges.Rows.Count==0){Info("The selected children have no posted outstanding fees or current waived charges to print.");return;}
            var institution=await InstitutionProfile.GetPrintIdentityAsync();
            if(!Current())return;
            int[] billed=charges.AsEnumerable().Select(r=>Convert.ToInt32(r["AdmissionId"])).Distinct().ToArray();
            int primary=billed.Contains(anchor)?anchor:billed[0];
            if(billed.Length>1)await FamilyBillReport.ShowAsync(FindForm(),institution,father,charges,primary,billed);
            else await StudentBillReport.ShowChargesAsync(FindForm(),institution,father,charges,primary);
        }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
        finally{busy=false;if(!IsDisposed)UpdateWorkspace();}
    }
}
