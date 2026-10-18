using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Maintenance;

internal static partial class Program
{
    private static void CheckCorrectionDraftTracking()
    {
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,Permissions:CurrentUser.GetPermissions().ToArray());
        try
        {
            CurrentUser.SignIn(8881,"Correction fixture","Operator",false,new[]{"Students.View","Students.Administer"});
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(StudentRecordChecksPage);
            using var page=(StudentRecordChecksPage)Activator.CreateInstance(type,flags,null,new object?[]{null,false},null)!;
            T Field<T>(string n)=>(T)type.GetField(n,flags)!.GetValue(page)!;
            Field<StudentPicker>("student").SetStudent(41,"Student");
            type.GetMethod("CaptureCorrectionBaselines",flags)!.Invoke(page,null);
            Check(!page.HasUnsavedChanges,"A loaded correction baseline is clean");
            Field<ModernTextBox>("statusReason").Text="Reviewed reason";
            Check(page.HasUnsavedChanges,"A status correction reason is an actual unsaved draft");
            using var host=new StudentDataReviewPage((_,_,_)=>{});var tabs=(TabControl)typeof(StudentDataReviewPage).GetField("tabs",flags)!.GetValue(host)!;tabs.TabPages[1].Controls.Add(page);
            Check(host.HasUnsavedChanges&&host.UnsavedChangesMessage.Contains("correction"),"The Review host includes correction drafts from its non-selected cached tab");
            type.GetMethod("CaptureCorrectionBaselines",flags)!.Invoke(page,null);
            Field<ModernDatePicker>("billingStartDate").Checked=true;Field<ModernDatePicker>("billingStartDate").Value=new DateTime(2026,10,1);
            Check(page.HasUnsavedChanges,"A checked placement billing-date edit is tracked");
            type.GetMethod("CaptureCorrectionBaselines",flags)!.Invoke(page,null);type.GetField("creatingEnrollment",flags)!.SetValue(page,true);
            Check(page.HasUnsavedChanges,"Starting a separate missing-placement draft remains visible to the leave guard");
            Field<StudentPicker>("student").SetStudent(0,"");Check(!page.HasUnsavedChanges,"Unbound default dates and reasons cannot create a correction draft without a student");
            Check(!page.IsHandleCreated,"Correction draft fixtures perform no school database operations");
        }
        finally{CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Permissions);}
    }
}
