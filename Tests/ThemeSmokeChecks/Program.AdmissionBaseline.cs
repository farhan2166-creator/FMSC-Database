using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Admissions;

internal static partial class Program
{
    private static void CheckAdmissionPostSaveBaseline()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var page = new StudentAdmissionPage();
        var type = typeof(StudentAdmissionPage);
        T Field<T>(string name) => (T)type.GetField(name, flags)!.GetValue(page)!;
        void Set(string name, object value) => type.GetField(name, flags)!.SetValue(page, value);
        void Capture() => type.GetMethod("CaptureAdmissionBaseline", flags)!.Invoke(page, null);
        BusyScope Hold() => (BusyScope)type.GetMethod("HoldSubmittedInputs", flags)!.Invoke(page, null)!;
        var name = Field<ModernTextBox>("name");
        Set("currentAdmissionId", 41); Set("_editEnrollmentId", 71);
        name.Text = "Synthetic Student"; Capture();
        Check(!page.HasUnsavedChanges, "A freshly loaded admission is clean");
        name.Text = "Synthetic Corrected Student";
        Check(page.HasUnsavedChanges, "Actual personal-detail edits remain protected");
        // Match the real Save -> held inputs -> successful reload -> baseline -> release order.
        using (Hold())
        {
            name.Text = "Synthetic Saved Student";
            Set("admissionLoadVersion", 2); Capture();
            Check(!page.Enabled && Field<(int, int)?>("deferredAdmissionBaseline") == (41, 2),
                "A successful reload defers its clean baseline while submitted inputs remain held");
        }
        Check(page.Enabled && !page.HasUnsavedChanges,
            "After saving, Mark Left and Correct Placement see a clean record without another reload");
        name.Text = "Another unsaved correction";
        Check(page.HasUnsavedChanges, "A subsequent genuine edit still blocks lifecycle actions until saved");
        using (Hold()) { /* A failed write/reload never captures a new baseline. */ }
        Check(page.HasUnsavedChanges, "Failed save/reload input restoration cannot discard unsaved edits");
        Capture();
        using (Hold())
        using (Hold())
        {
            name.Text = "Nested saved fixture"; Capture();
            Check(!page.Enabled, "Nested busy scopes keep the successful reload held");
        }
        Check(!page.HasUnsavedChanges, "Nested holds finalize the baseline only after the outer input hold releases");
        using (Hold())
        {
            name.Text = "Stale loaded fixture"; Capture();
            Set("admissionLoadVersion", 3);
        }
        Check(page.HasUnsavedChanges && Field<(int, int)?>("deferredAdmissionBaseline") is null,
            "A superseded load cannot mark a different editor generation clean");
        Capture();
        using (Hold())
        {
            name.Text = "Different student fixture"; Capture(); Set("currentAdmissionId", 42);
        }
        Check(page.HasUnsavedChanges, "A deferred baseline cannot clear edits after student identity changes");
        Capture();
        name.Text = "Unsaved without a successful reload";
        page.Enabled = false; page.Enabled = true;
        Check(page.HasUnsavedChanges, "Ordinary disabling and enabling never authorizes a fresh clean baseline");
        Capture();
        using var image = new Bitmap(16, 16);
        using var bytes = new MemoryStream(); image.Save(bytes, System.Drawing.Imaging.ImageFormat.Jpeg);
        type.GetMethod("StagePreparedPhoto", flags)!.Invoke(page, new object[] { bytes.ToArray(), "synthetic.jpg" });
        using (Hold()) Capture();
        Check(page.HasUnsavedChanges, "A refreshed details baseline never clears an unsaved photo");
        Check(!page.IsHandleCreated, "Baseline regression fixtures never start student loaders or connect to SQL");
    }
}
