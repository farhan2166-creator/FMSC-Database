using System.Data;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Administration;
using FMSC_SMS.Pages.Security;

internal static partial class Program
{
    private sealed class FixtureAdministrationStore(AccessEditorKind kind) : IAdministrationStore
    {
        internal int Reads,Writes,Resets;
        internal bool FailReads,FailSaves,StaleSave,UnknownSave;
        internal TaskCompletionSource<(AccessCatalog Catalog,AccessRecord Record)>? Delayed;
        internal AccessDraft? LastDraft;
        internal string? LastPassword;
        internal readonly Dictionary<int,AccessRecord> Records=kind==AccessEditorKind.Users
            ? new(){[7]=new(7,"Ali","",false,false,new[]{2},"user-1","credential"),[8]=new(8,"Bilal","",true,false,new[]{2},"user-2","other"),
                [9]=new(9,"Administrator account","",false,true,new[]{1},"admin-user","admin-proof")}
            : new(){[1]=new(1,"Administrator","Protected",false,true,new[]{10,20},"admin-role"),
                [2]=new(2,"Accountant","Accounting duties",false,false,new[]{10},"role-1",MemberCount:2)};
        private readonly AccessPermission[] permissions={new(10,"Students.View","View students","Student records"),new(20,"Fees.View","View fees","Fee reports")};
        internal AccessCatalog Catalog()
        {
            var roles=kind==AccessEditorKind.Users?new[]{new AccessRole(1,"Administrator","Protected",new[]{10,20}),new AccessRole(2,"Accountant","Accounting duties",new[]{10})}
                :Records.Values.OrderBy(record=>record.Id).Select(record=>new AccessRole(record.Id,record.Name,record.Description,record.Choices)).ToArray();
            var items=Records.Values.Select(record=>new AccessListItem(record.Id,record.Name,record.Protected?"Protected":$"{record.Choices.Length} choice(s)",record.Protected)).ToArray();
            return new(roles,permissions,items,AdministrativeAccess.CatalogToken(roles,permissions));
        }
        public Task<AccessCatalog> LoadCatalogAsync()=>Task.FromResult(Catalog());
        public Task<(AccessCatalog Catalog,AccessRecord Record)> LoadAsync(int id)
        {
            Reads++;
            if(Delayed is not null)return Delayed.Task;
            if(FailReads)return Task.FromException<(AccessCatalog,AccessRecord)>(new IOException("Fixture refresh failed"));
            return Task.FromResult((Catalog(),Records[id]));
        }
        public Task<AccessSaveResult> SaveAsync(AccessDraft draft,string snapshot,string catalogToken,string? password=null)
        {
            Writes++;LastDraft=draft;LastPassword=password;
            var intent=new AccessWriteIntent(kind,draft,OriginalSnapshot:snapshot);
            if(UnknownSave)return Task.FromResult(new AccessSaveResult(AccessSaveOutcome.Unconfirmed,Intent:intent,Error:new IOException("Lost reply")));
            if(FailSaves)return Task.FromResult(new AccessSaveResult(AccessSaveOutcome.Rejected,Intent:intent,
                Error:StaleSave?new AdministrativeStaleStateException("Changed fixture"):new IOException("Refused fixture")));
            int id=draft.Id>0?draft.Id:Records.Keys.Max()+1;
            Records[id]=new(id,draft.Name,draft.Description,draft.ForcePasswordChange,false,draft.Choices,"saved-"+Guid.NewGuid().ToString("N"));
            return Task.FromResult(new AccessSaveResult(AccessSaveOutcome.Saved,id,intent));
        }
        public Task<AccessSaveResult> ResetPasswordAsync(AccessRecord original,string catalogToken,string password,bool forceChange)
        {Resets++;return SaveAsync(AccessDraft.From(original) with { ForcePasswordChange=forceChange },original.Snapshot,catalogToken,password);}
        public Task<AccessCheckResult> CheckAsync(AccessWriteIntent intent)=>Task.FromResult(new AccessCheckResult(AccessCheckOutcome.NotApplied,Catalog(),
            Records.GetValueOrDefault(intent.Values.Id)));
    }
    private static void CheckAdministrationEditors()
    {
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,Permissions:CurrentUser.GetPermissions().ToArray());
        var theme=AppTheme.CurrentTheme;
        try
        {
            CurrentUser.SignIn(7795,"Admin editor fixture","Operator",false,new[]{"System.Users","System.Users.Manage","System.Roles","System.Roles.Manage"});
            var full=new AccessRules(true,true,true,true);
            void Finish(Task task){PumpUntil(()=>task.IsCompleted,"Administration fixture operation completes");task.GetAwaiter().GetResult();}
            bool Open(AccessEditorPage page,int id){var task=page.SelectAsync(id);Finish(task);return task.Result;}
            void Choose(AccessEditorPage page,int id,bool selected)
            {
                var data=(DataView)page.Assignments.DataSource!;
                var row=page.Assignments.Rows.Cast<DataGridViewRow>().First(item=>item.DataBoundItem is DataRowView view&&(int)view["ChoiceId"]==id);
                row.Cells["Selected"].Value=selected;page.Assignments.EndEdit();
                Check(data.Table!.Rows.Cast<DataRow>().Single(item=>(int)item["ChoiceId"]==id).Field<bool>("Selected")==selected,"Assignment fixture updates the same bound draft");
            }
            using(var storePage=new UsersPage(new FixtureAdministrationStore(AccessEditorKind.Users),full,false))
                Check(!storePage.IsHandleCreated,"Injected unshown editor does not start production SQL");
            var createStore=new FixtureAdministrationStore(AccessEditorKind.Users);
            using(var page=new UsersPage(createStore,full,false){BindingContext=new BindingContext()})
            {
                Finish(page.InitializeAsync());page.NewRecord();
                Check(page.Creating&&!page.HasUnsavedChanges&&page.CaptureDraft().Choices.Length==0,"A fresh new-user draft has explicit empty roles and no spurious leave warning");
                page.NameInput.Text="New account";page.NewPassword.Text="short";page.ConfirmPassword.Text="short";
                Finish(page.SaveAsync());Check(createStore.Writes==0&&page.HasUnsavedChanges,"Password policy refuses an incomplete new-user draft before any write");
                page.NewPassword.Text=page.ConfirmPassword.Text="Fixture password 12345";
                Finish(page.SaveAsync());Check(createStore.LastPassword=="Fixture password 12345"&&createStore.LastDraft!.Choices.Length==0,
                    "One Save User carries the initial password and deliberate zero-role choice");
                Check(!page.Creating&&!page.HasUnsavedChanges&&page.NewPassword.Text.Length==0&&page.ConfirmPassword.Text.Length==0,
                    "Confirmed creation clears the plaintext password inputs and shows the saved account");
            }
            var userStore=new FixtureAdministrationStore(AccessEditorKind.Users);
            using(var page=new UsersPage(userStore,full,false){BindingContext=new BindingContext()})
            {
                Finish(page.InitializeAsync());Check(Open(page,7)&&page.NameInput.Text=="Ali","A complete selected account loads into the inline editor");
                Check(!page.HasUnsavedChanges&&!page.SaveAction.Enabled&&page.ResetAction.Enabled,"A clean saved user has a separate password reset and no redundant save");
                page.NameInput.Text="Ali updated";Choose(page,2,false);
                Check(page.HasUnsavedChanges&&page.SaveAction.Enabled&&!page.ResetAction.Enabled,"Account/role edits are one dirty draft and disable password-only reset");
                var refused=page.ResetForSavedUserAsync("Fixture password 12345",true);Finish(refused);
                Check(refused.Result.Outcome==AccessSaveOutcome.Rejected&&userStore.Resets==0,"Password reset cannot secretly submit live role checkbox changes");
                int reads=userStore.Reads;Check(Open(page,7)&&userStore.Reads==reads&&page.HasUnsavedChanges,"Reopening the same selected user keeps its pending account/role draft");
                page.ConfirmForTests=_=>false;
                Check(!Open(page,8)&&page.Original?.Id==7&&page.NameInput.Text=="Ali updated","Declining a record switch retains the complete current draft");
                page.ConfirmForTests=_=>true;Finish(page.SaveAsync());
                Check(userStore.LastDraft is { Name:"Ali updated" }&&userStore.LastDraft.Choices.Length==0,"One Save User deliberately submits displayed account and role changes together");
                Check(!page.HasUnsavedChanges&&!page.NeedsReload&&page.ResetAction.Enabled,"A confirmed save with successful refresh establishes a clean editor");
                var reset=page.ResetForSavedUserAsync("Fixture password 12345",true);Finish(reset);
                Check(reset.Result.Outcome==AccessSaveOutcome.Saved&&userStore.Resets==1&&userStore.LastDraft!.Choices.Length==0,"Separate reset uses saved roles after the account draft has settled");
                page.NameInput.Text="Committed but stale";userStore.FailReads=true;Finish(page.SaveAsync());
                Check(page.NeedsReload&&!page.SaveAction.Enabled&&!page.ResetAction.Enabled&&!page.HasUnsavedChanges&&page.Original?.Name=="Committed but stale",
                    "A failed post-save refresh retains confirmed identity/values and blocks another write");
                int writes=userStore.Writes;Finish(page.SaveAsync());Check(userStore.Writes==writes,"A stale committed editor cannot repeat the save");
                userStore.FailReads=false;Finish(page.ReloadAsync());Check(!page.NeedsReload&&page.NameInput.Text=="Committed but stale","Deliberate reload recovers a committed stale editor");
                page.NameInput.Text="Uncertain draft";userStore.UnknownSave=true;Finish(page.SaveAsync());
                Check(page.Unconfirmed is not null&&page.HasUnsavedChanges&&!page.SaveAction.Enabled&&page.CheckAction.Visible&&page.EditorContext.Contains("check save outcome"),
                    "Unconfirmed commits retain a guarded draft and visible read-only recovery action");
                writes=userStore.Writes;Finish(page.CheckAsync());Check(userStore.Writes==writes&&page.Unconfirmed is null&&!page.NeedsReload&&page.NameInput.Text=="Uncertain draft",
                    "A NotApplied check preserves the exact draft and never repeats its write");
                userStore.UnknownSave=false;userStore.FailSaves=true;userStore.StaleSave=true;Finish(page.SaveAsync());
                Check(page.NeedsReload&&page.HasUnsavedChanges&&page.NameInput.Text=="Uncertain draft"&&!page.SaveAction.Enabled,"Stale-state refusal retains unsaved values and requires review");
                Check(Open(page,9)&&page.CaptureDraft().Choices.SequenceEqual(new[]{1}),"Administrator shows its actual membership without falsely checking Accountant");
            }
            var roleStore=new FixtureAdministrationStore(AccessEditorKind.Roles);
            using(var page=new RolesPage(roleStore,full,false){BindingContext=new BindingContext()})
            {
                Finish(page.InitializeAsync());Check(!page.NewAction.Visible&&!page.NewAction.Enabled&&page.NameInput.ReadOnly,"Fixed roles cannot be created or renamed");page.NewRecord();Check(!page.Creating,"Direct New Role calls cannot introduce another role");Check(Open(page,2)&&page.DescriptionInput.Text=="Accounting duties","Role descriptions are bound directly rather than lost in a separate dialog");
                Choose(page,20,true);page.DescriptionInput.Text="Pending role description";
                page.ConfirmForTests=_=>false;Check(!Open(page,1)&&page.Original?.Id==2&&page.HasUnsavedChanges,"Unsaved role permissions/descriptions are guarded during selection changes");
                page.ConfirmForTests=_=>true;Finish(page.SaveAsync());
                Check(roleStore.LastDraft is { Description:"Pending role description" }&&roleStore.LastDraft.Choices.ToHashSet().SetEquals(new[]{10,20}),"One Save Role carries both details and permissions");
                page.AssignmentSearch.Text="Fees";PumpUntil(()=>page.Assignments.Rows.Count==1,"Permission filter debounces against the existing rows");
                Check(page.CaptureDraft().Choices.Length==2,"Filtering permissions does not uncheck hidden selected permissions");
                page.AssignmentSearch.Clear();PumpUntil(()=>page.Assignments.Rows.Count==2,"Clearing the filter restores all permission rows");
                page.RecordSearch.Text="Administrator";PumpUntil(()=>page.RecordGrid.Rows.Count==1,"Role list filters the cached view");
                Check(page.Original?.Id==2&&page.DescriptionInput.Text=="Pending role description","Filtering the list does not implicitly replace the open editor");
                page.RecordSearch.Clear();PumpUntil(()=>page.RecordGrid.Rows.Count==3,"Clearing role search restores the list");
                bool throwOnce=true;EventHandler fail=(_,_)=>{if(throwOnce&&page.NameInput.Text=="Administrator"){throwOnce=false;throw new InvalidOperationException("Fixture binding failure");}};
                page.NameInput.TextChanged+=fail;
                try{Check(!Open(page,1),"A midway binding failure returns a failed read");}finally{page.NameInput.TextChanged-=fail;}
                Check(page.Original?.Id==2&&page.NameInput.Text=="Accountant"&&page.DescriptionInput.Text=="Pending role description"&&page.CaptureDraft().Choices.Length==2,
                    "Failed binding restores the old record, details and permission draft atomically");
                Check(Open(page,1)&&page.Original!.Protected&&!page.SaveAction.Enabled&&page.NameInput.ReadOnly&&page.Assignments.ReadOnly,"Administrator role remains fully read-only");
                int writes=roleStore.Writes;Finish(page.SaveAsync());Check(roleStore.Writes==writes,"A protected role cannot bypass the main save guard");
            }
            var rollbackStore=new FixtureAdministrationStore(AccessEditorKind.Users);
            using(var page=new UsersPage(rollbackStore,full,false){BindingContext=new BindingContext()})
            {
                Finish(page.InitializeAsync());page.NewRecord();page.NameInput.Text="Kept new user";page.NewPassword.Text=page.ConfirmPassword.Text="Fixture password 12345";
                page.ConfirmForTests=_=>true;bool failOnce=true;
                EventHandler fail=(_,_)=>{if(failOnce&&page.NameInput.Text=="Bilal"){failOnce=false;throw new InvalidOperationException("Fixture new-draft binding failure");}};
                page.NameInput.TextChanged+=fail;
                try{Check(!Open(page,8),"A failed record switch refuses to replace a new-user draft");}finally{page.NameInput.TextChanged-=fail;}
                Check(page.Creating&&page.Original is null&&page.NameInput.Text=="Kept new user"&&page.NewPassword.Text=="Fixture password 12345"&&page.HasUnsavedChanges,
                    "Atomic editor rollback includes the new-user password fields without storing or logging them");
                int prompts=0;page.ConfirmForTests=_=>{prompts++;return false;};Finish(page.ReloadAsync());
                Check(prompts==1&&page.Creating,"Reload asks at most once before discarding a fresh-account draft");
            }
            var busyStore=new FixtureAdministrationStore(AccessEditorKind.Users);
            using(var page=new UsersPage(busyStore,full,false){BindingContext=new BindingContext()})
            {
                Finish(page.InitializeAsync());Check(Open(page,7),"Busy fixture opens its initial user");
                busyStore.Delayed=new(TaskCreationOptions.RunContinuationsAsynchronously);var pending=page.SelectAsync(8);
                Check(((IBusyPage)page).IsBusy&&!page.NewAction.Enabled&&!page.SaveAction.Enabled&&!page.RecordGrid.Enabled,"The shell contract and interfering controls cover an active read");
                page.NewRecord();Check(page.Original?.Id==7&&!page.Creating,"New cannot wipe the editor during an in-flight operation");
                page.Dispose();busyStore.Delayed.SetResult((busyStore.Catalog(),busyStore.Records[8]));Finish(pending);
                Check(!pending.Result&&page.Original?.Id==7,"A disposed editor ignores a delayed read completion");
            }
            var readStore=new FixtureAdministrationStore(AccessEditorKind.Users);
            using(var page=new UsersPage(readStore,new(true,false,true,false),false){BindingContext=new BindingContext()})
            {
                Finish(page.InitializeAsync());Check(Open(page,7)&&page.NameInput.ReadOnly&&!page.NewAction.Enabled&&!page.SaveAction.Enabled&&!page.ResetAction.Enabled,
                    "Read-only viewing permission does not enable any management action");
                page.NameInput.Text="Programmatic draft";Finish(page.SaveAsync());Check(readStore.Writes==0,"Programmatic UI changes cannot bypass the permission guard");
            }
            foreach(var selectedTheme in AppTheme.SelectableThemes)
                foreach(int width in new[]{760,1100})
                {
                    AppTheme.SetTheme(selectedTheme);using var page=new RolesPage(new FixtureAdministrationStore(AccessEditorKind.Roles),full,false){Dock=DockStyle.Fill,BindingContext=new BindingContext()};
                    using var owner=new Form{ClientSize=new Size(width,600)};owner.Controls.Add(page);owner.Show();Finish(page.InitializeAsync());Check(Open(page,2),"Layout fixture opens a role without production SQL");
                    owner.PerformLayout();page.PerformLayout();Check(page.Assignments.Width>0&&page.SaveAction.Parent is not null&&page.SaveAction.Visible,"Both themes retain the assignment grid and fixed save action in compact/wide layouts");owner.Close();
                }
        }
        finally{AppTheme.SetTheme(theme);CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Permissions);}
    }
}
