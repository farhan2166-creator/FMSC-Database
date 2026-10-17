using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Certificates;

internal static partial class Program
{
    private static void CheckMeaningfulCertificateContent()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (bool provisional in new[] { false, true })
        {
            using Control page = provisional ? new ProvisionalCertificatePage(false, readCertificates: _ => new DataTable()) : new SportsCertificatePage(false, readCertificates: _ => new DataTable());
            var type = page.GetType();
            T Field<T>(string name) => (T)type.GetField(name, flags)!.GetValue(page)!;
            void Clear() => type.GetMethod("ClearCertificateFields", flags)!.Invoke(page, null);
            var student = Field<StudentPicker>("student"); student.SetStudent(41, "Synthetic student"); Clear();
            var draft = (IUnsavedChanges)page;
            Check(!draft.HasUnsavedChanges, "Loaded certificate defaults remain clean");
            Field<ModernDatePicker>("certificateDate").Value = DateTime.Today.AddDays(-1);
            Check(!draft.HasUnsavedChanges, "A new date alone does not force a discard prompt on these certificate dialogs");
            foreach (string name in provisional ? new[] { "examYear", "boardName", "rollNumber", "marksObtained", "marksOutOf", "otherSubjects" } : new[] { "sportName", "performance" })
            {
                Clear(); Field<ModernTextBox>(name).Text = "Synthetic edited content";
                Check(draft.HasUnsavedChanges, name + " protects meaningful new certificate content");
            }
            Clear();
            if (provisional)
            {
                Field<CheckedListBox>("subjectsList").Items.Add("Synthetic subject", true);
                Check(draft.HasUnsavedChanges, "Passed subject choices are part of the new provisional draft");
            }
            else Field<ModernTextBox>("performance").Text = "Synthetic wording";
            using var owner = new Form(); int asks = 0;
            ModalPageCloseGuard.Attach(owner, page, confirmDiscard: _ => { asks++; return false; });
            var close = new FormClosingEventArgs(CloseReason.UserClosing, false);
            typeof(Form).GetMethod("OnFormClosing", flags)!.Invoke(owner, new object[] { close });
            Check(close.Cancel && asks == 1, "Stay preserves meaningful new certificate content at its modal boundary");
            Clear(); Check(!draft.HasUnsavedChanges, "Clearing committed/discarded certificate fields resets meaningful draft state");
            Field<ModernTextBox>(provisional ? "rollNumber" : "performance").Text = "Synthetic content";
            student.SetStudent(0, ""); Check(!draft.HasUnsavedChanges, "A blank student context does not invent a certificate draft");
        }
    }
}
