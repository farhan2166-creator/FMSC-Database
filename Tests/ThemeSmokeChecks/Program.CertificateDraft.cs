using System.Reflection;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckStaffCertificateSubjects()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var page = new FMSC_SMS.Pages.Staff.Certificates.RegularLecturerCertificatePage();
        var type = page.GetType();
        T Field<T>(string name) => (T)type.GetField(name, flags)!.GetValue(page)!;
        void Invoke(string method) => type.GetMethod(method, flags)!.Invoke(page, null);
        using var staffRows = new System.Data.DataTable();
        staffRows.Columns.Add("StaffId", typeof(int));
        foreach (string column in new[] { "StaffNumber", "StaffName", "Designation", "Specialization" }) staffRows.Columns.Add(column);
        staffRows.Rows.Add(1, "T-1", "Fictional Teacher", "Teacher", "Chemistry, Biology");
        staffRows.Rows.Add(2, "T-2", "Fictional Lecturer", "Lecturer", "Mathematics");
        staffRows.Rows.Add(3, "T-3", "Fictional New Teacher", "Teacher", "");
        using var certificates = new System.Data.DataTable();
        certificates.Columns.Add("RegularLecturerCertificateId", typeof(int)); certificates.Columns.Add("StaffId", typeof(int));
        certificates.Columns.Add("RoleTitle"); certificates.Columns.Add("SubjectTaught"); certificates.Columns.Add("CertificateDate", typeof(DateTime));
        certificates.Rows.Add(10, 1, "Teacher", "Previously certified subject", DateTime.Today.AddDays(-1));
        type.GetField("certificateResults", flags)!.SetValue(page, certificates);
        var picker = Field<StaffPicker>("staff"); var subject = Field<ModernTextBox>("subjectTaught");
        picker.SetStaff(staffRows.Rows[0]); Invoke("ClearCertificateFields");
        Check(subject.Text == "Chemistry, Biology", "New staff certificate uses Specialization from the selected staff record");
        Check(Field<ModernTextBox>("roleTitle").Text == "Teacher" && !page.HasUnsavedChanges,
            "Database designation and subjects form a clean new-certificate baseline");
        subject.Text = "Reviewed wording";
        Check(page.HasUnsavedChanges, "Reviewing the supplied subject wording remains an editable certificate draft");
        type.GetMethod("ApplyCertificateRow", flags)!.Invoke(page, new object[] { certificates.Rows[0] });
        Check(subject.Text == "Previously certified subject" && !page.HasUnsavedChanges,
            "Opening an existing certificate preserves its issued wording instead of replacing it with current specialization");
        Invoke("ClearForm");
        Check(picker.SelectedId==0 && subject.Text=="", "New clears the staff selection and saved-certificate details");
        picker.SetStaff(staffRows.Rows[1]); Invoke("ClearCertificateFields");
        Check(subject.Text == "Mathematics", "Changing staff uses the new staff member's specialization");
        picker.SetStaff(staffRows.Rows[2]); Invoke("ClearCertificateFields");
        Check(subject.Text == "", "A blank specialization never reuses another staff member's subject");
        Check(!page.IsHandleCreated, "Staff subject checks create no certificate or database connection");
    }
    private static void CheckNewCertificateDrafts()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        foreach(string name in new[]{"CharacterCertificatePage","LeaveCertificatePage","CollegeLeaveCertificatePage"})
        {
            var type=typeof(ApplicationWindowForm).Assembly.GetType("FMSC_SMS.Pages.Students.Certificates."+name,true)!;
            object?[] args=name=="LeaveCertificatePage"?new object?[]{null,false}:new object?[]{false};
            using var page=(Control)Activator.CreateInstance(type,flags,null,args,null)!;
            var student=(StudentPicker)type.GetField("student",flags)!.GetValue(page)!;student.SetStudent(41,"Student");
            type.GetMethod("CaptureDraftBaseline",flags)!.Invoke(page,null);var dirty=(IUnsavedChanges)page;
            Check(!dirty.HasUnsavedChanges,"Loaded certificate defaults do not count as an unsaved new draft");
            ((ModernTextBox)type.GetField("remarks",flags)!.GetValue(page)!).Text="Reviewed certificate wording";
            Check(dirty.HasUnsavedChanges&&student.SelectionChangeGuard is not null,"New certificate wording participates in shell leave and student-change protection");
            using var owner=new Form();int asks=0;Func<string,bool> stay=_=>{asks++;return false;};
            var guard=typeof(ApplicationWindowForm).Assembly.GetType("FMSC_SMS.Core.ModalPageCloseGuard",true)!;
            guard.GetMethod("Attach",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object?[]{owner,page,null,stay});
            var close=new FormClosingEventArgs(CloseReason.UserClosing,false);typeof(Form).GetMethod("OnFormClosing",flags)!.Invoke(owner,new object[]{close});
            Check(close.Cancel&&asks==1,"A certificate modal Stay preserves the new editable draft");
            type.GetField(name=="CharacterCertificatePage"?"selectedCertificateRecordId":"selectedLeaveRecordId",flags)!.SetValue(page,1);
            Check(!dirty.HasUnsavedChanges,"Read-only existing certificate issuance is not falsely presented as a new unsaved draft");
            student.SetStudent(0,"");Check(!dirty.HasUnsavedChanges,"Blank certificate defaults without a student do not prompt on exit");
            Check(!page.IsHandleCreated,"Certificate dirty fixtures start no SQL read or certificate creation");
        }
    }
    private static void CheckReturnAdmissionDraft()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(FMSC_SMS.Pages.Students.Enrollment.ConfirmReturnAdmissionPage);
        using var page=(Control)Activator.CreateInstance(type,flags,null,new object?[]{null,0,true,false},null)!;page.BindingContext=new BindingContext();
        ((StudentPicker)type.GetField("pendingStudent",flags)!.GetValue(page)!).SetStudent(1,"Student");
        var rows=new System.Data.DataTable();rows.Columns.Add("FeeTypeId",typeof(int));rows.Columns.Add("FeeTypeName");rows.Columns.Add("Frequency");rows.Columns.Add("Amount",typeof(decimal));rows.Columns.Add("DiscountAmount",typeof(decimal));rows.Rows.Add(1,"Tuition","Monthly",100m,10m);
        type.GetField("feeRows",flags)!.SetValue(page,rows);((DataGridView)type.GetField("fees",flags)!.GetValue(page)!).DataSource=rows;
        var capture=type.GetMethod("CaptureReturnDraftBaseline",flags)!;capture.Invoke(page,null);var dirty=(IUnsavedChanges)page;
        Check(!dirty.HasUnsavedChanges,"Loaded return fees/date/transport are a clean cancellable context");
        rows.Rows[0]["Amount"]=120m;Check(dirty.HasUnsavedChanges,"Changed opening fee amount is a protected return draft");capture.Invoke(page,null);
        rows.Rows[0]["DiscountAmount"]=20m;Check(dirty.HasUnsavedChanges,"Changed opening discount is tracked before confirmation");capture.Invoke(page,null);
        ((CheckBox)type.GetField("transport",flags)!.GetValue(page)!).Checked=true;Check(dirty.HasUnsavedChanges,"Transport intent is part of the return draft");capture.Invoke(page,null);
        ((ModernDatePicker)type.GetField("returnDate",flags)!.GetValue(page)!).Value=DateTime.Today.AddDays(-1);Check(dirty.HasUnsavedChanges,"Changed billing/return date is a protected draft");
        type.GetField("confirmingReturn",flags)!.SetValue(page,true);Check(((IBusyPage)page).IsBusy,"Submitted return confirmation has a strict busy contract separate from its draft");
        type.GetField("confirmingReturn",flags)!.SetValue(page,false);type.GetField("returnDraftBaseline",flags)!.SetValue(page,null);Check(!dirty.HasUnsavedChanges,"Confirmed return clears draft intent before its success close");
    }

    private static void CheckRegularNewCertificateDraft()
    {
        void Trace(string stage)=>Console.WriteLine("REGULAR_CERT: " + stage);
        Trace("begin");
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var type=typeof(FMSC_SMS.Pages.Students.Certificates.RegularStudentCertificatePage);
        int prompts=0,reads=0,studentRefreshes=0;
        using var page=new FMSC_SMS.Pages.Students.Certificates.RegularStudentCertificatePage(false,
            _=>{prompts++;return false;},_=>{reads++;return new System.Data.DataTable();},
            _=>studentRefreshes++);
        var student=(StudentPicker)type.GetField("student",flags)!.GetValue(page)!;
        var date=(ModernDatePicker)type.GetField("certificateDate",flags)!.GetValue(page)!;
        using var students=new System.Data.DataTable();
        students.Columns.Add("AdmissionId",typeof(int));
        students.Columns.Add("ClassName");
        students.Columns.Add("DateOfBirth",typeof(DateTime));
        students.Columns.Add("Status");
        students.Rows.Add(41,"Class 5",new DateTime(2010,1,1),"Active");
        Trace("fixture created");
        student.SetStudent(students.Rows[0]);
        Trace("student selected");
        type.GetMethod("StudentChanged",flags)!.Invoke(page,null);
        Trace("initial record loaded");
        Check(!page.HasUnsavedChanges&&prompts==0,"Regular certificate defaults start clean.");
        date.Value=DateTime.Today.AddDays(-1);
        Trace("first date changed");
        Check(!page.HasUnsavedChanges&&student.SelectionChangeGuard!(42)&&prompts==0,
            "A short new-date edit can be retyped without an unsaved-draft prompt.");
        using(var selection=new DataGridView{AllowUserToAddRows=false})
        {
            selection.Columns.Add("RegularStudentCertificateId","Certificate");
            selection.Rows.Add(7);
            selection.CurrentCell=selection.Rows[0].Cells[0];selection.Rows[0].Selected=true;
            Check(!CertificateUi.ConfirmCertificateSelection(null,selection,"RegularStudentCertificateId",0,7,true,()=>false)&&
                selection.SelectedRows.Count==0&&selection.CurrentCell is null,
                "Explicit certificate-selection refusal clears the unaccepted saved-row highlight.");
        }
        Trace("selection refusal confirmed");
        int before=reads;
        type.GetMethod("RefreshCertificates",flags)!.Invoke(page,null);
        Trace("refresh returned");
        Check(student.SelectedId==41&&date.Value.Date==DateTime.Today&&reads==before+1&&studentRefreshes==1&&prompts==0,
            "Refreshing a short edit reloads synthetic student context and defaults without SQL or a discard dialog.");
        date.Value=DateTime.Today.AddDays(-1);
        Trace("second date changed");
        type.GetMethod("ClearForm",flags)!.Invoke(page,new object[]{true});
        Trace("first clear returned");
        Check(student.SelectedId==0&&!page.HasUnsavedChanges&&prompts==0,
            "New clears short unsaved date wording without treating it as a pending reissue.");
        type.GetField("creatingCertificate",flags)!.SetValue(page,true);
        Trace("test busy set");
        student.SetStudent(41,"Student");
        Trace("busy student selected");
        type.GetMethod("ClearForm",flags)!.Invoke(page,new object[]{true});
        Check(student.SelectedId==41,"New cannot clear student context while a certificate is saving.");
        type.GetField("creatingCertificate",flags)!.SetValue(page,false);
        Trace("test busy clear");
        type.GetMethod("ClearForm",flags)!.Invoke(page,new object[]{true});
        Trace("final clear returned");
        Check(student.SelectedId==0&&!page.IsHandleCreated,"The fixture never queries a school database or opens the native page.");
    }

    private static void CheckCertificateNewDiscard()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var regular=new FMSC_SMS.Pages.Students.Certificates.RegularStudentCertificatePage(false,readCertificates:_=>new System.Data.DataTable());
        using var bank=new FMSC_SMS.Pages.Staff.Certificates.BankAccountOpeningCertificatePage(false);
        var regularType=regular.GetType();var bankType=bank.GetType();
        var student=(StudentPicker)regularType.GetField("student",flags)!.GetValue(regular)!;
        var staff=(StaffPicker)bankType.GetField("staff",flags)!.GetValue(bank)!;
        student.SetStudent(41,"Student");staff.SetStaff(101,"Staff");
        void ClearRegular()=>regularType.GetMethod("ClearForm",flags)!.Invoke(regular,new object[]{true});
        void ClearBank()=>bankType.GetMethod("ClearForm",flags)!.Invoke(bank,null);
        bankType.GetField("recoveryBlocked",flags)!.SetValue(bank,true);ClearBank();
        Check(staff.SelectedId==101,"New cannot clear a staff certificate while recovery is unresolved");
        bankType.GetField("recoveryBlocked",flags)!.SetValue(bank,false);
        regularType.GetField("creatingCertificate",flags)!.SetValue(regular,true);ClearRegular();
        Check(student.SelectedId==41,"New cannot clear a student certificate while saving");
        regularType.GetField("creatingCertificate",flags)!.SetValue(regular,false);
        ClearRegular();ClearBank();
        Check(student.SelectedId==0&&staff.SelectedId==0,"New clears the selected person and certificate details");
        ClearRegular();ClearBank();
        Check(!regular.IsHandleCreated&&!bank.IsHandleCreated,"Repeated New actions need no database connection");
    }

}
