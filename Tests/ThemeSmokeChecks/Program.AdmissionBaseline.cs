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
        var dob = Field<ModernDatePicker>("dob");
        dob.Checked = false; Capture();
        dob.Checked = true;
        dob.Value = new DateTime(2015, 4, 12);
        Check(page.HasUnsavedChanges, "Entering an optional birth date protects the admission draft");
        dob.Checked = false;
        Check(!page.HasUnsavedChanges, "Checking, changing and unchecking an initially empty birth date returns to a clean draft");
        dob.Checked = true; Capture();
        dob.Checked = false;
        Check(page.HasUnsavedChanges, "Clearing a previously saved birth date still counts as a real edit");
        dob.Checked = true;
        Check(!page.HasUnsavedChanges, "Restoring the saved birth date returns to a clean draft");
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
        var number = Field<ModernTextBox>("admissionIdValue");
        number.Text = "6590";
        number.Enabled = false;
        Capture();
        number.Enabled = true;
        Check(!page.HasUnsavedChanges, "Saved admission 6590 stays clean when its editor reopens");
        number.Text = "6590a";
        Check(page.HasUnsavedChanges, "A genuine correction to the saved admission number remains protected");
        number.Text = "6590";
        Check(!page.HasUnsavedChanges, "Restoring the saved admission number clears its real edit");
        Capture();
        using var image = new Bitmap(16, 16);
        using var bytes = new MemoryStream(); image.Save(bytes, System.Drawing.Imaging.ImageFormat.Jpeg);
        type.GetMethod("StagePreparedPhoto", flags)!.Invoke(page, new object[] { bytes.ToArray(), "synthetic.jpg" });
        using (Hold()) Capture();
        Check(page.HasUnsavedChanges, "A refreshed details baseline never clears an unsaved photo");
        Check(!page.IsHandleCreated, "Baseline regression fixtures never start student loaders or connect to SQL");
        foreach (Control certificate in new Control[] {
            new FMSC_SMS.Pages.Students.Certificates.LeaveCertificatePage(),
            new FMSC_SMS.Pages.Students.Certificates.CollegeLeaveCertificatePage() })
        using (certificate)
        {
            var certificateType = certificate.GetType();
            var date = (ModernDatePicker)certificateType.GetField("leave", flags)!.GetValue(certificate)!;
            var snapshot = certificateType.GetMethod("DraftSnapshot", flags)!;
            date.ShowCheckBox = true; date.Checked = false;
            string baseline = (string)snapshot.Invoke(certificate, null)!;
            date.Checked = true; date.Value = new DateTime(2026, 9, 1);
            Check((string)snapshot.Invoke(certificate, null)! != baseline,
                certificateType.Name + " counts an entered leave date as an edit");
            date.Checked = false;
            Check((string)snapshot.Invoke(certificate, null)! == baseline,
                certificateType.Name + " ignores the hidden value of an unchecked date");
            Check(!certificate.IsHandleCreated, "Certificate date fixtures never start database loaders");
        }
    }

    private static void CheckAdmissionSiblingBaseline()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var page = new StudentAdmissionPage();
        var type = page.GetType();
        T Field<T>(string field) => (T)type.GetField(field, flags)!.GetValue(page)!;
        void Set(string field, object value) => type.GetField(field, flags)!.SetValue(page, value);
        void Capture() => type.GetMethod("CaptureAdmissionBaseline", flags)!.Invoke(page, null);
        Set("currentAdmissionId", 41); Set("_editEnrollmentId", 71);
        var name = Field<ModernTextBox>("name"); name.Text = "Saved student";
        Capture();
        type.GetMethod("EnsureDetailsTabBuilt", flags)!.Invoke(page, null);
        Check(!page.HasUnsavedChanges, "Opening Fees & Siblings does not invent an admission edit");
        var editorParent = name.Parent!;
        int editorIndex = editorParent.Controls.GetChildIndex(name);
        editorParent.Controls.SetChildIndex(name, 0);
        Check(!page.HasUnsavedChanges, "Reordering displayed controls does not change the saved admission draft");
        editorParent.Controls.SetChildIndex(name, editorIndex);
        var siblings = Field<DataGridView>("siblingsGrid");
        using var data = new System.Data.DataTable();
        data.Columns.Add("Student Name"); data.Rows.Add("Saved sibling");
        siblings.DataSource = data;
        // WinForms creates child editors as grids are first used. Their lifetime
        // and displayed text must not change the personal-details draft.
        using var editor = new TextBox { Text = "Saved sibling" };
        siblings.Controls.Add(editor);
        Check(!page.HasUnsavedChanges, "Saved sibling rows and newly created grid editors leave admission clean");
        name.Text = "Unsaved personal correction";
        data.Rows.Add("Another saved sibling"); editor.Text = "Another saved sibling";
        Check(page.HasUnsavedChanges, "Refreshing saved siblings preserves genuine unsaved personal changes");
        name.Text = "Saved student";
        Check(!page.HasUnsavedChanges, "Restoring personal details stays clean after sibling refresh without resetting the baseline");
        var feeGrid = Field<DataGridView>("feeGrid");
        using var feeEditor = new TextBox { Text = "100" }; feeGrid.Controls.Add(feeEditor);
        Check(!page.HasUnsavedChanges, "A saved fee grid's native editor is not an admission draft field");
        Set("currentAdmissionId", 0); Set("_editEnrollmentId", 0);
        var fees = Field<System.Collections.IList>("_selectedFees");
        var feeType = fees.GetType().GetGenericArguments()[0];
        var fee = Activator.CreateInstance(feeType, true)!; fees.Add(fee); Capture();
        feeType.GetProperty("DiscountAmount")!.SetValue(fee, 100m);
        Check(page.HasUnsavedChanges, "A new admission's actual fee discount remains protected independently of native editors");
        Check(!page.IsHandleCreated, "Sibling draft checks do not start database loaders or change school records");
    }
}
