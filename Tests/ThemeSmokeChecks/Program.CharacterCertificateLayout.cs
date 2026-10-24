using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Certificates;

internal static partial class Program
{
    private static void CheckCharacterCertificateLayout()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var page=new CharacterCertificatePage(false);
        T Field<T>(string name)=>(T)typeof(CharacterCertificatePage).GetField(name,flags)!.GetValue(page)!;
        var student=Field<StudentPicker>("student");
        var exam=Field<ModernComboBox>("examType");
        var date=Field<ModernDatePicker>("certificateDate");
        var roll=Field<ModernTextBox>("rollNumber");
        var remarks=Field<ModernTextBox>("remarks");
        var create=Field<ModernButton>("createCertificateButton");
        var next=Field<ModernButton>("clearButton");
        Control details=student;
        while(details is not HumanCard)details=details.Parent!;
        details.Parent!.Controls.Remove(details);
        // Render just the card: the page's deferred SQL loaders never receive a handle.
        using var form=new Form { ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,
            Location=new Point(-20000,-20000),ClientSize=new Size(1000,430),AutoScaleMode=AutoScaleMode.None };
        details.Dock=DockStyle.Fill;form.Controls.Add(details);
        exam.SelectedIndex=1;
        var sequence=new Control[]{student,date,exam,roll,remarks,create,next};
        foreach(var control in sequence)control.Enabled=true;
        form.Show();Application.DoEvents();
        Directory.CreateDirectory("artifacts/character-certificate");
        foreach(int width in new[]{850,1000,1600})
        {
            form.ClientSize=new Size(width,430);form.PerformLayout();Application.DoEvents();
            var combo=exam.RectangleToScreen(exam.ClientRectangle);
            Check(exam.Parent!.ClientRectangle.Contains(exam.Bounds),
                $"Exam choice fits without clipping at {width}px.");
            Check(!Descendants(form).OfType<CheckBox>().Any(),"Certificate details have no pass acknowledgement.");
            using var bitmap=new Bitmap(form.Width,form.Height);
            form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
            bitmap.Save(Path.GetFullPath($"artifacts/character-certificate/details-{width}.png"));
        }
        student.Focus();
        Check(student.ContainsFocus,"The student picker accepts initial focus.");
        for(int index=1;index<sequence.Length;index++)
        {
            var focused=Descendants(form).Single(control=>control.Focused);
            Check(form.SelectNextControl(focused,true,true,true,true)&&sequence[index].ContainsFocus,
                $"Forward Tab reaches {sequence[index].GetType().Name} at position {index+1}.");
        }
        for(int index=sequence.Length-2;index>=0;index--)
        {
            var focused=Descendants(form).Single(control=>control.Focused);
            Check(form.SelectNextControl(focused,false,true,true,true)&&sequence[index].ContainsFocus,
                $"Shift+Tab returns to position {index+1}.");
        }
        typeof(StudentPicker).GetField("_selectedId",flags)!.SetValue(student,41);
        void Refresh()=>typeof(CharacterCertificatePage).GetMethod("ApplyWorkflowState",flags)!.Invoke(page,null);
        using var students=new System.Data.DataTable();
        students.Columns.Add("AdmissionId",typeof(int));students.Columns.Add("ClassName");
        var selected=students.Rows.Add(41,"Nine");student.SetSource(students);
        foreach(string className in new[]{"Nine","Ten","Eleven","Twelve","Custom class",""})
        foreach(int wording in new[]{0,1})
        {
            selected["ClassName"]=className;exam.SelectedIndex=wording;Refresh();
            Check(create.Enabled&&date.Enabled&&roll.Enabled&&remarks.Enabled,
                "The operator can choose either certificate wording for class '"+className+"' without placement restrictions.");
        }
        typeof(CharacterCertificatePage).GetMethod("ClearCertificateFields",flags)!.Invoke(page,null);
        exam.SelectedIndex=0;Refresh();
        Check(create.Enabled,"Clearing certificate inputs still lets the operator choose fresh wording.");
        exam.SelectedIndex=-1;Refresh();
        Check(!create.Enabled,"The operator still chooses the exam wording.");
        exam.SelectedIndex=0;
        var records=Field<System.Data.DataTable>("certificateResults");
        records.Columns.Add("CertificateRecordId",typeof(int));records.Columns.Add("ExamType");records.Rows.Add(8,"SSC");Refresh();
        Check(!create.Enabled,"An existing SSC certificate prevents duplicate SSC creation.");
        exam.SelectedIndex=1;Refresh();Check(create.Enabled,"A saved SSC certificate still permits a separate HSSC certificate.");
        records.Columns.Add("CollectionStatus");records.Columns.Add("CharacterCertificateIssueId",typeof(int));
        records.Rows[0]["CollectionStatus"]="Collected";records.Rows[0]["CharacterCertificateIssueId"]=12;
        typeof(CharacterCertificatePage).GetField("selectedCertificateRecordId",flags)!.SetValue(page,8);
        typeof(CharacterCertificatePage).GetField("selectedIssueId",flags)!.SetValue(page,12);
        typeof(CharacterCertificatePage).GetField("selectedIssueCollected",flags)!.SetValue(page,true);
        typeof(CharacterCertificatePage).GetField("selectedStoredExamType",flags)!.SetValue(page,"SSC");
        selected["ClassName"]="Nine";
        foreach(int wording in new[]{0,1})
        {
            exam.SelectedIndex=wording;Refresh();
            Check(Field<ModernButton>("issueAgainButton").Enabled&&!exam.Enabled,"A saved SSC copy may be reissued without changing its exam type.");
        }
        records.Rows[0]["CollectionStatus"]="Not Collected";Refresh();
        Check(!Field<ModernButton>("issueAgainButton").Enabled,"The latest copy must still be collected before issuing again.");
        typeof(CharacterCertificatePage).GetField("selectedCertificateRecordId",flags)!.SetValue(page,0);
        typeof(CharacterCertificatePage).GetField("selectedIssueId",flags)!.SetValue(page,0);
        records.Clear();
        typeof(StudentPicker).GetField("_selectedId",flags)!.SetValue(student,0);Refresh();
        Check(!create.Enabled,"Certificate creation still requires a selected student.");
        Check(!page.IsHandleCreated,"Layout and keyboard checks never start the certificate page's SQL loaders.");
    }
}
