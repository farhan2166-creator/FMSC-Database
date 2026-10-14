using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Profile;

internal static partial class Program
{
    private static void CheckProfileShortcutContext()
    {
        var previous = (CurrentUser.UserId, CurrentUser.Username, CurrentUser.RoleName,
            CurrentUser.MustChangePassword, CurrentUser.GetPermissions().ToArray());
        try
        {
            CurrentUser.SignIn(903, "Profile fixture", "Administrator", false, Array.Empty<string>());
            CheckAdministratorProfileShortcuts();
        }
        finally { CurrentUser.SignIn(previous.UserId, previous.Username, previous.RoleName, previous.MustChangePassword, previous.Item5); }
    }
    private static void CheckAdministratorProfileShortcuts()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;int opens=0;
        Action<Func<Control>,string,string> navigate=(_,_,_)=>opens++;
        using var page=(StudentProfilePage)Activator.CreateInstance(typeof(StudentProfilePage),flags,null,new object[]{navigate,false},null)!;
        var buttons=(List<ModernButton>)typeof(StudentProfilePage).GetField("studentShortcuts",flags)!.GetValue(page)!;
        Check(buttons.Count>0&&buttons.All(button=>!button.Enabled),"Record-specific profile shortcuts start unavailable with no selected student");
        foreach(var button in buttons)button.PerformClick();Check(opens==0,"Disabled blank-profile shortcuts cannot dispatch an accidental default record page");
        var picker=(StudentPicker)typeof(StudentProfilePage).GetField("student",flags)!.GetValue(page)!;picker.SetStudent(41,"Student");typeof(StudentProfilePage).GetMethod("UpdateStudentShortcuts",flags)!.Invoke(page,null);
        Check(buttons.All(button=>button.Enabled),"A committed profile student makes record-specific shortcuts available");buttons[0].PerformClick();Check(opens==1,"A deliberate selected-student shortcut still navigates normally");
        picker.SetStudent(0,"");typeof(StudentProfilePage).GetMethod("UpdateStudentShortcuts",flags)!.Invoke(page,null);Check(buttons.All(button=>!button.Enabled),"Clearing the committed profile identity removes shortcut targets");
        Check(!page.IsHandleCreated,"Profile shortcut fixture starts no SQL or destination form");
    }
}
