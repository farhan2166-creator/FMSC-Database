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
}
