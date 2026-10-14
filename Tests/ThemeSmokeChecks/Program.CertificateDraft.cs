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

}
