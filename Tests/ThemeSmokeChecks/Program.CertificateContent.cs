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
                Check(draft.HasUnsavedChanges == provisional, provisional
                    ? name + " protects meaningful new provisional certificate content"
                    : name + " follows the prompt-free short sports-certificate policy");
            }
            Clear();
            if (provisional)
            {
                var grade=Field<ModernComboBox>("grade");
                Check(grade.Items.Cast<string>().SequenceEqual(new[]{"","A+","A","B","C","D","E","F"}),"Provisional grade choices use only the owner's standard labels");
                Field<ModernComboBox>("examType").SelectedIndex=0;
                Field<ModernTextBox>("examYear").Text="2026";
                Field<ModernTextBox>("marksOutOf").Text="100";
                Field<ModernTextBox>("marksObtained").Text="80";
                Check(grade.Text=="A+","Automatic legacy A1 grade displays as A+");
                Field<ModernTextBox>("examYear").Text="2027";
                foreach(var (marks,expected) in new[]{("96","A+"),("91","A+"),("86","A"),("81","B"),("76","B"),("61","C"),("39","F")})
                {
                    Field<ModernTextBox>("marksObtained").Text=marks;
                    Check(grade.Text==expected,"Automatic revised grade displays standard label "+expected);
                }
                Check(ProvisionalCertificatePage.StandardGrade("A++")=="A+" && ProvisionalCertificatePage.StandardGrade("B+")=="B","Loading an older grade cannot add extra variants to the choices");
                Field<CheckedListBox>("subjectsList").Items.Add("Synthetic subject", true);
                Check(draft.HasUnsavedChanges, "Passed subject choices are part of the new provisional draft");
            }
            else Field<ModernTextBox>("performance").Text = "Synthetic wording";
            using var owner = new Form(); int asks = 0;
            ModalPageCloseGuard.Attach(owner, page, confirmDiscard: _ => { asks++; return false; });
            var close = new FormClosingEventArgs(CloseReason.UserClosing, false);
            typeof(Form).GetMethod("OnFormClosing", flags)!.Invoke(owner, new object[] { close });
            Check(provisional ? close.Cancel && asks == 1 : !close.Cancel && asks == 0,
                "Modal closing follows each certificate's discard policy");
            Clear(); Check(!draft.HasUnsavedChanges, "Clearing committed/discarded certificate fields resets meaningful draft state");
            Field<ModernTextBox>(provisional ? "rollNumber" : "performance").Text = "Synthetic content";
            student.SetStudent(0, ""); Check(!draft.HasUnsavedChanges, "A blank student context does not invent a certificate draft");
        }
    }
}
