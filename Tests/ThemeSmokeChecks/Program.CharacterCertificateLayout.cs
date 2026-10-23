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
        var eligible=Field<HashSet<int>>("eligibleSchoolClasses");
        typeof(StudentPicker).GetField("_selectedId",flags)!.SetValue(student,41);
        void Refresh()=>typeof(CharacterCertificatePage).GetMethod("ApplyWorkflowState",flags)!.Invoke(page,null);
        eligible.Add(10);exam.SelectedIndex=0;Refresh();
        Check(create.Enabled,"A former Class 10 student can create SSC without a pass acknowledgement.");
        typeof(CharacterCertificatePage).GetMethod("ClearCertificateFields",flags)!.Invoke(page,null);
        exam.SelectedIndex=0;Refresh();
        Check(create.Enabled,"Clearing saved certificate inputs retains this student's placement eligibility.");
        exam.SelectedIndex=1;Refresh();
        Check(!create.Enabled,"Class 10 history alone does not qualify for HSSC.");
        eligible.Add(12);Refresh();
        Check(create.Enabled,"Class 12 history qualifies for HSSC without a pass acknowledgement.");
        exam.SelectedIndex=-1;Refresh();
        Check(!create.Enabled,"The operator still chooses the exam wording.");
        eligible.Clear();exam.SelectedIndex=0;Refresh();
        Check(!create.Enabled,"A student without Class 10 or 12 history cannot create a certificate.");
        Check(!page.IsHandleCreated,"Layout and keyboard checks never start the certificate page's SQL loaders.");
    }
}
