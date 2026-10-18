using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Admissions;
using FMSC_SMS.Pages.Finance.Collection;

internal static partial class Program
{
    private static void CheckSubmittedInputHolds()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        // Exercise the same scope used by both submitted workflows. Holding the
        // parent prevents nested field-state events from unlocking other inputs.
        foreach (Control page in new Control[] { new StudentAdmissionPage(), new FeeCollectionPage() })
        using (page)
        {
            var hold = (BusyScope)page.GetType().GetMethod("HoldSubmittedInputs", flags)!.Invoke(page, null)!;
            Check(!page.Enabled, "Submitted admission/collection holds the whole editor, including grids and notes");
            page.Controls[0].Enabled = true;
            Check(!page.Controls[0].Enabled, "A nested enabled-state event cannot reopen the submitted editor");
            hold.Dispose();
            Check(page.Enabled, "Submission scope restores the editor on completion/failure");
        }
        var previous = (CurrentUser.UserId, CurrentUser.Username, CurrentUser.RoleName,
            CurrentUser.MustChangePassword, CurrentUser.GetPermissions().ToArray());
        CurrentUser.SignIn(901, "Fixture", "Administrator", false, Array.Empty<string>());
        try
        {
            var reply = new TaskCompletionSource(); int writes = 0;
            using var page = new StudentAdmissionPage(null, 0, false, false,
                (_, _, _, _) => { writes++; return reply.Task; }, (_, _) => Task.FromResult(new DataTable()));
            var type = page.GetType();
            type.GetField("currentAdmissionId", flags)!.SetValue(page, 41);
            type.GetMethod("CaptureAdmissionBaseline", flags)!.Invoke(page, null);
            using var bitmap = new Bitmap(16, 16); using var bytes = new MemoryStream(); bitmap.Save(bytes, System.Drawing.Imaging.ImageFormat.Jpeg);
            type.GetMethod("StagePreparedPhoto", flags)!.Invoke(page, new object[] { bytes.ToArray(), "synthetic.jpg" });
            var pending = (Task)type.GetMethod("Save", flags)!.Invoke(page, null)!;
            Check(writes == 1 && !pending.IsCompleted && page.IsBusy && !page.Enabled,
                "Actual photo-only admission Save holds the complete editor during its delayed write");
            reply.SetResult(); PumpUntil(() => pending.IsCompleted, "Photo-only submitted Save settles"); pending.GetAwaiter().GetResult();
            Check(page.Enabled && !page.IsBusy && writes == 1, "Photo-only submission restores editing without an unrelated student update");
        }
        finally { CurrentUser.SignIn(previous.UserId, previous.Username, previous.RoleName, previous.MustChangePassword, previous.Item5); }
    }
}
