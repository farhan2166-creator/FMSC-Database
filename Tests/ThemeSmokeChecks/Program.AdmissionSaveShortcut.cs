using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Admissions;

internal static partial class Program
{
    private static void CheckAdmissionSaveShortcut()
    {
        var previous = (CurrentUser.UserId, CurrentUser.Username, CurrentUser.RoleName,
            CurrentUser.MustChangePassword, Permissions: CurrentUser.GetPermissions().ToArray());
        try
        {
            CurrentUser.SignIn(7779, "Shortcut fixture", "Operator", false,
                new[] { "Students.Admit", "Students.View", "Students.Administer" });
            CheckAdmissionSaveShortcutDispatch();
        }
        finally
        {
            CurrentUser.SignIn(previous.UserId, previous.Username, previous.RoleName,
                previous.MustChangePassword, previous.Permissions);
        }
    }

    private static void CheckAdmissionSaveShortcutDispatch()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var page = new StudentAdmissionPage();
        var type = typeof(StudentAdmissionPage);
        var save = (ModernButton)type.GetField("saveButton", flags)!.GetValue(page)!;
        var photo = (ModernButton)type.GetField("savePhotoButton", flags)!.GetValue(page)!;
        // Prevent database submission while observing the real button dispatch.
        type.GetField("saveBusy", flags)!.SetValue(page, true);
        save.Enabled = true;
        int saves = 0, photos = 0;
        save.Click += (_, _) => saves++;
        photo.Click += (_, _) => photos++;
        Check(page.ShortcutController.TryHandle(Keys.Control | Keys.S) && saves == 1,
            "Ctrl+S invokes the admission Save for a new student");

        type.GetField("currentAdmissionId", flags)!.SetValue(page, 41);
        type.GetMethod("UpdatePhotoActions", flags)!.Invoke(page, null);
        // Make both competing actions visible in this unshown SQL-free fixture.
        page.Controls.Add(photo);
        photo.Visible = true;
        Check(page.ShortcutController.TryHandle(Keys.Control | Keys.S) && saves == 2 && photos == 0,
            "An existing student's disabled Save Photo cannot intercept Ctrl+S");
        photo.Enabled = true;
        save.Text = "Save Draft";
        Check(page.ShortcutController.TryHandle(Keys.Control | Keys.S) && saves == 3 && photos == 0,
            "Ctrl+S retains the admission Save target with a pending photo and Save Draft caption");
        save.Enabled = false;
        Check(!page.ShortcutController.TryHandle(Keys.Control | Keys.S) && saves == 3 && photos == 0,
            "A disabled admission Save cannot fall through to saving only the photo");
        Check(!page.IsHandleCreated, "Shortcut checks never start the admission SQL loader");
    }
}
