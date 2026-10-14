using System.Reflection;
using FMSC_SMS.Core;

internal static partial class Program
{
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
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var type=typeof(FMSC_SMS.Pages.Students.Certificates.RegularStudentCertificatePage);
        bool discard=false;int asks=0,reads=0;
        using var page=new FMSC_SMS.Pages.Students.Certificates.RegularStudentCertificatePage(false,_=>{asks++;return discard;},_=>{reads++;return new System.Data.DataTable();});
        var student=(StudentPicker)type.GetField("student",flags)!.GetValue(page)!;
        var date=(ModernDatePicker)type.GetField("certificateDate",flags)!.GetValue(page)!;
        using var students=new System.Data.DataTable();students.Columns.Add("AdmissionId",typeof(int));students.Columns.Add("ClassName");students.Columns.Add("DateOfBirth",typeof(DateTime));students.Columns.Add("Status");students.Rows.Add(41,"Class 5",new DateTime(2010,1,1),"Active");
        student.SetStudent(students.Rows[0]);type.GetMethod("StudentChanged",flags)!.Invoke(page,null);
        Check(!page.HasUnsavedChanges&&asks==0,"A regular certificate's loaded default date is clean");
        date.Value=DateTime.Today.AddDays(-1);Check(page.HasUnsavedChanges&&!student.SelectionChangeGuard!(42),"An edited new date protects both shell leave and student changes");
        using(var selection=new DataGridView{AllowUserToAddRows=false})
        {
            selection.Columns.Add("RegularStudentCertificateId","Certificate");selection.Rows.Add(7);selection.CurrentCell=selection.Rows[0].Cells[0];selection.Rows[0].Selected=true;
            Check(!CertificateUi.ConfirmCertificateSelection(null,selection,"RegularStudentCertificateId",0,7,true,()=>false)&&selection.SelectedRows.Count==0&&selection.CurrentCell is null,"Stay in a new draft removes the unaccepted saved-row highlight");
        }
        int before=reads;type.GetMethod("RefreshCertificates",flags)!.Invoke(page,null);
        type.GetMethod("ClearForm",flags)!.Invoke(page,new object[]{true});
        Check(student.SelectedId==41&&date.Value.Date==DateTime.Today.AddDays(-1)&&reads==before,"Stay on Refresh/Next retains student, draft date and loaded context without another read");
        date.Value=DateTime.Today;Check(!page.HasUnsavedChanges,"Reverting the new date to its loaded baseline removes the discard prompt");
        date.Value=DateTime.Today.AddDays(-1);discard=true;type.GetMethod("RefreshCertificates",flags)!.Invoke(page,null);
        Check(!page.HasUnsavedChanges&&student.SelectedId==41&&date.Value.Date==DateTime.Today&&reads==before+1,"Accepted refresh discard reloads defaults and starts a clean new-date baseline");
        type.GetField("selectedRegularStudentCertificateId",flags)!.SetValue(page,7);type.GetField("savedClassName",flags)!.SetValue(page,"Class 5");type.GetField("savedCertificateDate",flags)!.SetValue(page,DateTime.Today);
        date.Value=DateTime.Today.AddDays(-1);Check(page.HasUnsavedChanges,"Existing saved-certificate date edits retain their original dirty protection");
        type.GetMethod("ClearCertificateFields",flags)!.Invoke(page,null);type.GetMethod("ClearCertificateSelection",flags)!.Invoke(page,null);
        Check(!page.HasUnsavedChanges,"Committed-save field/selection clearing leaves no stale new-certificate draft");
        type.GetMethod("StudentChanged",flags)!.Invoke(page,null);date.Value=DateTime.Today.AddDays(-1);type.GetMethod("ClearForm",flags)!.Invoke(page,new object[]{true});
        Check(student.SelectedId==0&&!page.HasUnsavedChanges&&!page.IsHandleCreated,"Accepted Next clears the old draft without SQL or a native page handle");
        date.Value=DateTime.Today.AddDays(-1);Check(!page.HasUnsavedChanges,"Changing a default date on a blank certificate page does not invent a student draft");
    }

}
