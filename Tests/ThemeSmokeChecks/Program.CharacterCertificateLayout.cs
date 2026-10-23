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
        var passed=Field<CheckBox>("passedExam");
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
        exam.SelectedIndex=1;passed.Checked=true;
        var sequence=new Control[]{student,date,exam,passed,roll,remarks,create,next};
        foreach(var control in sequence)control.Enabled=true;
        form.Show();Application.DoEvents();
        Directory.CreateDirectory("artifacts/character-certificate");
        foreach(int width in new[]{850,1000,1600})
        {
            form.ClientSize=new Size(width,430);form.PerformLayout();Application.DoEvents();
            var combo=exam.RectangleToScreen(exam.ClientRectangle);
            var check=passed.RectangleToScreen(passed.ClientRectangle);
            Check(exam.Parent==passed.Parent&&Math.Abs((combo.Top+combo.Height/2)-(check.Top+check.Height/2))<=3,
                $"Passed checkbox sits beside and aligns with the exam combo at {width}px.");
            Check(check.Left>=combo.Right&&check.Left-combo.Right<=AppScale.Scale(16),
                $"Checkbox stays immediately beside the combo at {width}px.");
            Check(exam.Parent!.ClientRectangle.Contains(exam.Bounds)&&passed.Parent!.ClientRectangle.Contains(passed.Bounds),
                $"Exam and confirmation fit without clipping at {width}px.");
            using var bitmap=new Bitmap(form.Width,form.Height);
            form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
            bitmap.Save(Path.GetFullPath($"artifacts/character-certificate/details-{width}.png"));
        }
        Check(form.SelectNextControl(null,true,true,true,true)&&student.ContainsFocus,"Tab starts in the student picker.");
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
        passed.Enabled=false;
        exam.SelectNextControl(null,true,true,true,false);
        var examFocus=Descendants(form).Single(control=>control.Focused);
        Check(form.SelectNextControl(examFocus,true,true,true,true)&&roll.ContainsFocus,"Tab skips the pass confirmation when eligibility disables it.");
        Check(!page.IsHandleCreated,"Layout and keyboard checks never start the certificate page's SQL loaders.");
    }
}
