using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;

internal static partial class Program
{
    private static void CheckRegistrationPhotoRecovery()
    {
        var previous = (CurrentUser.UserId, CurrentUser.Username, CurrentUser.RoleName,
            CurrentUser.MustChangePassword, Permissions: CurrentUser.GetPermissions().ToArray());
        try
        {
            CurrentUser.SignIn(7779, "Recovery fixture", "Operator", false, new[] {
                "Students.Admit", "Students.View", "Students.Administer", "Fees.Adjust", "Fees.View", "Fees.Collect" });
            CheckRegistrationRefresh();
            CheckPendingSelection();
            CheckExplicitPhotoSave();
        }
        finally { CurrentUser.SignIn(previous.UserId, previous.Username, previous.RoleName, previous.MustChangePassword, previous.Permissions); }
    }

    private static void CheckRegistrationRefresh()
    {
        var type = typeof(ApplicationWindowForm).Assembly.GetType("FMSC_SMS.Pages.Students.Admissions.QuickRegistrationFormPage", true)!;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var years = new DataTable(); years.Columns.Add("AcademicYearId", typeof(int)); years.Columns.Add("YearName"); years.Rows.Add(1, "Fixture year");
        using var placements = new DataTable(); placements.Columns.Add("ClassSectionId", typeof(int));
        foreach (string column in new[] { "CampusName", "ClassName", "SectionName" }) placements.Columns.Add(column);
        placements.Rows.Add(2, "Campus", "Class", "Section");
        DataTable Details(int id, string name)
        {
            var table = new DataTable();
            foreach (var (column, kind) in new[] { ("AdmissionId", typeof(int)), ("AdmissionDate", typeof(DateTime)), ("AcademicYearId", typeof(int)),
                ("ClassSectionId", typeof(int)), ("Status", typeof(string)), ("StudentName", typeof(string)), ("FatherName", typeof(string)),
                ("ContactNumber", typeof(string)), ("Address", typeof(string)), ("QuickReference", typeof(string)), ("AdmissionNumber", typeof(string)), ("RegistrationNumber", typeof(string)), ("FamilyMemberId", typeof(int)), ("FamilyMemberCount", typeof(int)) })
                table.Columns.Add(column, kind);
            table.Rows.Add(id, new DateTime(2026, 10, 13), 1, 2, "Pending", name, "Parent", "03001234567", "Address", $"QR-{id}", DBNull.Value, DBNull.Value, 0, 0);
            return table;
        }
        DataTable Fees()
        {
            var table = new DataTable();
            foreach (var (column, kind) in new[] { ("StudentFeeAssignmentId", typeof(int)), ("HasPayments", typeof(bool)), ("FeeTypeId", typeof(int)),
                ("FeeTypeName", typeof(string)), ("Frequency", typeof(string)), ("IsMandatory", typeof(bool)), ("IsTransportLinked", typeof(bool)),
                ("Include", typeof(bool)), ("Amount", typeof(decimal)), ("Discount", typeof(decimal)) }) table.Columns.Add(column, kind);
            table.Rows.Add(77, false, 1, "Tuition", "Monthly", true, false, true, 1000m, 100m); return table;
        }
        string mode = "loaded"; int reads = 0;
        TaskCompletionSource<(DataTable, DataTable, string)>? delayed = null;
        Func<int, Task<(DataTable Details, DataTable Fees, string Snapshot)>> load = id => {
            reads++;
            if (mode == "failed") return Task.FromException<(DataTable, DataTable, string)>(new IOException("Fixture refresh failure"));
            if (mode == "delayed") return delayed!.Task;
            var details = Details(id, id == 41 ? "Ali" : "Bilal");
            if(mode is "family" or "active-family" or "cancelled-family")
            {
                details.Rows[0]["FamilyMemberId"]=81;details.Rows[0]["FamilyMemberCount"]=2;
                details.Rows[0]["Status"]=mode=="active-family"?"Active":mode=="cancelled-family"?"Cancelled":"Pending";
            }
            if (mode == "missing") details.Rows.Clear();
            var fees = Fees(); if (mode == "malformed") fees.Columns.Remove("Discount");
            return Task.FromResult((details, fees, "snapshot"));
        };
        Action<Func<Control>, string, string> navigate = (_, _, _) => { };
        using var page = (UserControl)Activator.CreateInstance(type, flags, null, new object[] { navigate, 0, load }, null)!;
        page.BindingContext = new BindingContext();
        T Field<T>(string name) => (T)type.GetField(name, flags)!.GetValue(page)!;
        type.GetField("applyingRegistration", flags)!.SetValue(page, true);
        Field<LookupBox>("year").Bind(years, "AcademicYearId", "YearName");
        Field<PlacementPicker>("placement").Bind(placements); type.GetField("placements", flags)!.SetValue(page, placements);
        type.GetField("applyingRegistration", flags)!.SetValue(page, false);
        var method = type.GetMethod("LoadRegistrationAsync", flags)!;
        Task Start(int id) => (Task)method.Invoke(page, new object[] { id, false })!;
        string Outcome(Task task) { PumpUntil(() => task.IsCompleted, "Registration fixture read completes"); return task.GetType().GetProperty("Result")!.GetValue(task)!.ToString()!; }
        string Status() => ((Label)typeof(V2Page).GetField("StatusLabel", flags)!.GetValue(page)!).Text;
        Field<ModernTextBox>("name").Text="Draft child";Field<ModernTextBox>("parent").Text="Existing parent";
        Field<ModernTextBox>("contact").Text="03001234567";type.GetField("familyAnchor",flags)!.SetValue(page,81);
        type.GetMethod("UpdateActions",flags)!.Invoke(page,null);
        Check(Field<ModernButton>("linkFamily").Enabled&&Field<ModernButton>("unlinkFamily").Enabled,"A new linked draft exposes both parent actions");
        var unlinkDraft=(Task)type.GetMethod("UnlinkFamilyAsync",flags)!.Invoke(page,null)!;
        Check(unlinkDraft.IsCompletedSuccessfully&&Field<int>("familyAnchor")==0&&Field<ModernTextBox>("name").Text=="Draft child"&&
            Field<ModernTextBox>("parent").Text=="Existing parent"&&Field<ModernTextBox>("contact").Text=="03001234567"&&!Field<ModernTextBox>("parent").ReadOnly,
            "Unlinking a draft retains entered details, makes parent editable and performs no SQL write");
        Check(Outcome(Start(41)) == "Loaded" && Field<ModernTextBox>("name").Text == "Ali", "A fully validated registration is applied");
        Check(Field<ModernButton>("linkFamily").Enabled&&!Field<ModernButton>("unlinkFamily").Enabled&&Field<ModernButton>("bill").Text=="Print Bill",
            "Saved standalone QR exposes later parent linking and a clear Print Bill action");
        mode="family";Check(Outcome(Start(41))=="Loaded"&&Field<ModernButton>("unlinkFamily").Enabled,"Saved linked pending QR can unlink its parent");
        Field<ModernTextBox>("name").Text="Unsaved edit";
        Check(!Field<ModernButton>("linkFamily").Enabled&&!Field<ModernButton>("unlinkFamily").Enabled,"Unsaved edits block family writes until saved");
        Field<ModernTextBox>("name").Text="Ali";
        mode="active-family";Check(Outcome(Start(41))=="Loaded"&&Field<ModernButton>("linkFamily").Enabled&&Field<ModernButton>("unlinkFamily").Enabled,"Parent links remain manageable after admission confirmation");
        mode="cancelled-family";Check(Outcome(Start(41))=="Loaded"&&!Field<ModernButton>("linkFamily").Enabled&&!Field<ModernButton>("unlinkFamily").Enabled,"Cancelled QR cannot change parent links");
        mode="loaded";Check(Outcome(Start(41))=="Loaded","Standalone registration fixture restored");
        type.GetField("recoveryBlocked",flags)!.SetValue(page,true);type.GetMethod("UpdateActions",flags)!.Invoke(page,null);
        Check(Field<ModernButton>("checkSave").Enabled&&!Field<ModernButton>("save").Enabled&&!Field<ModernButton>("next").Enabled&&
            !Field<ModernButton>("newFamily").Enabled&&!Field<ModernButton>("linkFamily").Enabled&&!Field<ModernButton>("bill").Enabled,
            "Unconfirmed registration recovery exposes its read-only check and blocks new writes or drafts");
        Check(((IUnsavedChanges)page).HasUnsavedChanges&&((IUnsavedChanges)page).UnsavedChangesMessage.Contains("recovery record"),"Leaving unconfirmed registration explains durable recovery");
        int recoveryReads=reads;
        var recoverySave=(Task)type.GetMethod("SaveAsync",flags)!.Invoke(page,null)!;
        type.GetMethod("StartNextDraft",flags)!.Invoke(page,new object[]{true});
        var recoveryOpen=(Task<bool>)type.GetMethod("OpenRegistrationAsync",flags)!.Invoke(page,new object[]{42})!;
        Check(recoverySave.IsCompletedSuccessfully&&recoveryOpen.IsCompletedSuccessfully&&!recoveryOpen.Result&&reads==recoveryReads&&Field<int>("savedStudent")==41,
            "Save/open/new-family runtime paths cannot bypass the recovery guard");
        type.GetField("recoveryBlocked",flags)!.SetValue(page,false);type.GetMethod("UpdateActions",flags)!.Invoke(page,null);
        var permissions=CurrentUser.GetPermissions().ToArray();
        CurrentUser.SignIn(7779,"Admission-only fixture","Operator",false,new[]{"Students.Admit","Students.View"});
        type.GetMethod("UpdateActions",flags)!.Invoke(page,null);
        Check(!Field<ModernButton>("save").Enabled&&!Field<ModernButton>("next").Enabled&&!Field<ModernButton>("linkFamily").Enabled&&
            Field<ModernTextBox>("name").ReadOnly,"Admission/view access cannot edit a saved QR or create family relationships without student-maintenance access");
        CurrentUser.SignIn(7779,"Recovery fixture","Operator",false,permissions);type.GetMethod("UpdateActions",flags)!.Invoke(page,null);
        var originalRates = Field<DataTable>("rates");
        mode = "malformed";
        Check(Outcome(Start(42)) == "Failed" && Field<ModernTextBox>("name").Text == "Ali" && ReferenceEquals(Field<DataTable>("rates"), originalRates),
            "Invalid fee data cannot partly replace the current student or bill");
        mode = "loaded";
        bool throwOnce = true;
        EventHandler failBinding = (_, _) => { if (throwOnce && Field<ModernTextBox>("name").Text == "Bilal") { throwOnce = false; throw new InvalidOperationException("Fixture binding failure"); } };
        Field<ModernTextBox>("name").TextChanged += failBinding;
        try { Check(Outcome(Start(42)) == "Failed", "A binding exception is an explicit failed load"); }
        finally { Field<ModernTextBox>("name").TextChanged -= failBinding; }
        Check(Field<ModernTextBox>("name").Text == "Ali" && Field<int>("savedStudent") == 41 && ReferenceEquals(Field<DataTable>("rates"), originalRates),
            "A midway binding failure restores the prior editor and identity");
        mode = "failed";
        var open = (Task<bool>)type.GetMethod("OpenRegistrationAsync", flags)!.Invoke(page, new object[] { 41 })!;
        PumpUntil(() => open.IsCompleted, "Same-ID reopen failure completes");
        Check(!open.Result, "Reopening the same ID cannot claim success from the prior selected ID");
        Field<ModernTextBox>("name").Text = "Committed edit";
        int before = reads;
        var committed = (Task)type.GetMethod("CompleteCommittedSaveAsync", flags)!.Invoke(page, new object[] { 41, "QR-41", false })!;
        PumpUntil(() => committed.IsCompleted, "Committed-save refresh failure completes");
        Check(reads == before + 1 && Status().Contains("saved to the database") && Status().Contains("could not refresh"),
            "A committed save reports its separate refresh failure and runs one read only");
        Check(!Field<ModernButton>("save").Enabled && !Field<ModernButton>("bill").Enabled && !Field<ModernButton>("collect").Enabled,
            "A stale post-save screen blocks further writes, printing and payment");
        Check(!Field<ModernButton>("linkFamily").Enabled&&!Field<ModernButton>("unlinkFamily").Enabled,"A stale post-save screen also blocks parent-link writes");
        Check(!Field<ModernButton>("next").Enabled&&!Field<ModernButton>("newFamily").Enabled,"A stale saved registration cannot be cleared into a new draft");
        Check(Field<ModernTextBox>("name").Text == "Committed edit", "Refresh failure preserves the entered committed values");
        mode = "loaded"; Check(Outcome(Start(41)) == "Loaded" && !Field<bool>("needsRefresh"), "A deliberate reload clears the stale-state guard");
        foreach(string state in new[]{"busy","loadingFamily","loadingFees"})
        {
            type.GetField(state,flags)!.SetValue(page,true);type.GetMethod("UpdateActions",flags)!.Invoke(page,null);
            Check(!Field<ModernButton>("next").Enabled&&!Field<ModernButton>("newFamily").Enabled&&!Field<ModernButton>("bill").Enabled&&!Field<ModernButton>("collect").Enabled,
                "In-flight "+state+" prevents changing the current draft or billing context");
            type.GetField(state,flags)!.SetValue(page,false);type.GetMethod("UpdateActions",flags)!.Invoke(page,null);
        }
        type.GetMethod("StartNextDraft",flags)!.Invoke(page,new object[]{false});
        Check(Field<int>("savedStudent")==0&&Field<int>("familyAnchor")==41&&Field<ModernTextBox>("name").Text==""&&
            Field<ModernTextBox>("parent").Text=="Parent"&&Field<ModernTextBox>("contact").Text=="03001234567",
            "Add Another Child retains the first pending child as family anchor and clears only the child draft");
        Check(Outcome(Start(41))=="Loaded","First saved child can be reopened after starting a sibling draft");
        type.GetMethod("StartNextDraft",flags)!.Invoke(page,new object[]{true});
        Check(Field<int>("savedStudent")==0&&Field<int>("familyAnchor")==0&&Field<ModernTextBox>("parent").Text==""&&
            Field<ModernTextBox>("contact").Text=="","New Family clears the family anchor and shared details from a clean saved record");
        Check(Outcome(Start(41))=="Loaded","Saved child fixture restored after starting a new family");
        mode = "missing";
        Check(Outcome(Start(41)) == "NotFound" && Field<int>("savedStudent") == 41,
            "Not found is an explicit outcome and preserves the prior editor identity");
        mode = "failed";
        var newSave = (Task)type.GetMethod("CompleteCommittedSaveAsync", flags)!.Invoke(page, new object[] { 43, "QR-43", true })!;
        PumpUntil(() => newSave.IsCompleted, "A committed new registration with failed refresh completes");
        Check(Field<int>("savedStudent") == 43 && Status().Contains("QR-43 was saved to the database") && Field<bool>("needsRefresh"),
            "A confirmed new save retains its ID and directs recovery instead of enabling a duplicate save");
        mode = "delayed"; delayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Start(42);
        var rejected = (Task<bool>)type.GetMethod("OpenRegistrationAsync", flags)!.Invoke(page, new object[] { 41 })!;
        Check(rejected.IsCompletedSuccessfully && !rejected.Result, "A second open cannot race an in-flight editor load");
        page.Dispose(); delayed.SetResult((Details(42, "Late record"), Fees(), "snapshot"));
        Check(Outcome(pending) == "Superseded", "A late registration completion cannot apply to a disposed editor");
        Check(!page.IsHandleCreated, "Registration fixture never starts its real lookup SQL loader");
    }

    private static void CheckExplicitPhotoSave()
    {
        var type = typeof(FMSC_SMS.Pages.Students.Admissions.StudentAdmissionPage);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        byte[] jpeg;
        using (var bitmap = new Bitmap(30, 60))
        { using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Jpeg); jpeg = stream.ToArray(); }
        int writes = 0, photoReads = 0, recordedId = 0; bool fail = false;
        Func<int, byte[], string?, string?, Task> write = (id, bytes, file, mime) => {
            writes++; recordedId = id;
            Check(bytes.SequenceEqual(jpeg) && file == "fixture.jpg" && mime == "image/jpeg", "Photo save retains the prepared image and metadata");
            return fail ? Task.FromException(new IOException("Fixture photo write failure")) : Task.CompletedTask;
        };
        Func<int, CancellationToken, Task<DataTable>> read = (_, _) => {
            photoReads++; var table = new DataTable(); table.Columns.Add("PhotoData", typeof(byte[])); table.Columns.Add("UpdatedAt", typeof(DateTime));
            table.Rows.Add(jpeg, new DateTime(2026, 10, 13)); return Task.FromResult(table);
        };
        using var page = (UserControl)Activator.CreateInstance(type, flags, null, new object?[] { null, 0, false, false, write, read }, null)!;
        T Field<T>(string name) => (T)type.GetField(name, flags)!.GetValue(page)!;
        Task Call(string name) => (Task)type.GetMethod(name, flags)!.Invoke(page, null)!;
        void Stage() => type.GetMethod("StagePreparedPhoto", flags)!.Invoke(page, new object[] { jpeg, "fixture.jpg" });
        bool Pending() => type.GetProperty("_pendingPhotoData", flags)!.GetValue(page) is not null;
        // An uncertain admission must never permit another write or store its pending photo.
        var submissionType = type.Assembly.GetType("FMSC_SMS.Core.AdmissionSubmission", true)!;
        var submission = Activator.CreateInstance(submissionType, new object[] { "125", "Ali", "Parent", new DateTime(2026, 10, 13), 2, false, 0 })!;
        type.GetField("unconfirmedAdmission", flags)!.SetValue(page, submission);
        Stage();
        type.GetMethod("ShowUnconfirmedAdmission", flags)!.Invoke(page, null);
        Check(Field<Label>("studentStatusBadge").Text.Contains("Check save outcome"), "The student header explains that the submitted admission needs confirmation");
        Check(!Field<ModernButton>("saveButton").Enabled && !Field<ModernButton>("saveAndNewButton").Enabled &&
            Field<ModernButton>("checkAdmissionButton").Visible, "An unconfirmed admission disables both save paths and exposes a read-only check");
        Check(((IUnsavedChanges)page).HasUnsavedChanges && ((IUnsavedChanges)page).UnsavedChangesMessage.Contains("may already have been saved"),
            "Leaving an unconfirmed admission explains the possible existing record");
        var guardedSave = Call("Save");
        Check(guardedSave.IsCompletedSuccessfully && Pending() && writes == 0 && !Field<ModernButton>("saveButton").Enabled,
            "A keyboard/main Save cannot bypass unconfirmed state or save the photo without a student ID");
        var guardedNext = Call("SaveAndStartNextAsync");
        Check(guardedNext.IsCompletedSuccessfully && ReferenceEquals(type.GetField("unconfirmedAdmission", flags)!.GetValue(page), submission),
            "Save and New cannot clear an unconfirmed admission");
        type.GetField("unconfirmedAdmission", flags)!.SetValue(page, null);
        type.GetField("currentAdmissionId", flags)!.SetValue(page, 41);
        type.GetMethod("CaptureAdmissionBaseline", flags)!.Invoke(page, null);
        Stage(); Check(writes == 0 && Pending() && Field<ModernButton>("savePhotoButton").Enabled, "Choosing a photo previews it without saving");
        Check(!(bool)type.GetProperty("HasAdmissionEdits", flags)!.GetValue(page)! && ((IUnsavedChanges)page).HasUnsavedChanges,
            "A pending photo is dirty independently of the student details");
        var discard = Call("RemovePhoto"); PumpUntil(() => discard.IsCompleted, "Discarding a preview completes");
        Check(!Pending() && writes == 0 && photoReads == 1, "Discard restores the saved preview without deleting or replacing it");
        Stage(); fail = true;
        var failed = Call("SavePhotoAsync"); PumpUntil(() => failed.IsCompleted, "Failed explicit photo save completes");
        Check(Pending() && writes == 1 && Field<ModernButton>("savePhotoButton").Text == "Retry Photo Save", "Failed photo saves retain a visible retry action");
        fail = false;
        var retry = Call("SavePhotoAsync"); PumpUntil(() => retry.IsCompleted, "Explicit photo retry completes");
        Check(!Pending() && writes == 2 && recordedId == 41, "Retry saves the same selected student and clears only the completed draft");
        Stage(); var mainSave = Call("Save"); PumpUntil(() => mainSave.IsCompleted, "Photo-only main Save completes");
        Check(writes == 3 && !Pending(), "The principal Save processes a photo-only change without student-details validation/update");
        Stage(); type.GetField("currentAdmissionId", flags)!.SetValue(page, 0);
        var tooEarly = Call("SavePhotoAsync"); Check(tooEarly.IsCompletedSuccessfully && writes == 3 && Pending(), "A new photo waits for its admission to exist");
        type.GetField("currentAdmissionId", flags)!.SetValue(page, 41);
        CurrentUser.SignIn(7780, "Photo viewer", "Operator", false, new[] { "Students.View" });
        var denied = Call("SavePhotoAsync"); Check(denied.IsCompletedSuccessfully && writes == 3 && Pending(), "View permission cannot save a photo");
        Check(!page.IsHandleCreated, "Photo fixture never starts real admission lookup SQL");
    }
}
