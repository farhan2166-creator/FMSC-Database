using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckValidationBadges()
    {
        using var form=new Form {ClientSize=new Size(640,180)};
        var input=HumanUi.TextBox(false,30);
        var field=HumanUi.Field("Primary Phone (optional)",input);
        field.Dock=DockStyle.None;field.Height=76;form.Controls.Add(field);
        form.Show();
        using var validator=new FormValidator();
        foreach(int width in new[]{180,300,520})
        {
            field.Width=width;field.PerformLayout();
            validator.Begin();validator.Check(input,false,"Enter a full Pakistan phone number");validator.Finish();
            var caption=field.Controls.OfType<Label>().Single();
            var badge=caption.Controls.OfType<Label>().Single();
            int occupied=TextRenderer.MeasureText(caption.Text,caption.Font).Width+caption.Padding.Horizontal+AppScale.Scale(8);
            Check(badge.Width==0||badge.Left>=occupied,"Validation feedback must not paint over a long field caption");
            Check(input.IsInvalid,"Layout fitting must retain the validation state");
            Check(badge.Text=="Enter a full Pakistan phone number","Elision must preserve the full accessible validation message");
        }
        form.Close();
        // Exercise the real admission header without creating its handle or SQL loaders.
        using var admission=new FMSC_SMS.Pages.Students.Admissions.StudentAdmissionPage();
        var badgeField=typeof(FMSC_SMS.Pages.Students.Admissions.StudentAdmissionPage)
            .GetField("studentStatusBadge",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        var status=(Label)badgeField.GetValue(admission)!;
        var row=(TableLayoutPanel)status.Parent!.Parent!;
        row.Dock=DockStyle.None; // exercise actual header at explicit available widths
        foreach(int width in new[]{850,1500})
        {
            row.Width=width;row.PerformLayout();
            foreach(string text in new[]{"New Admission","Active","Left"})
            {
                status.Text=text;row.PerformLayout();status.Parent.PerformLayout();
                Check(status.Width>=TextRenderer.MeasureText(text,status.Font).Width+status.Padding.Horizontal,
                    $"Admission header must show its complete ordinary status: {text}; badge={status.Width}, host={status.Parent.Width}, row={row.Width}, column={row.ColumnStyles[1].Width}");
            }
        }
        Check(!admission.IsHandleCreated,"Header regression must never start SQL loaders");
        foreach(string actionField in new[]{"findButton","newAdmissionButton","collectPaymentButton"})
        {
            var action=(Button)typeof(FMSC_SMS.Pages.Students.Admissions.StudentAdmissionPage)
                .GetField(actionField,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(admission)!;
            Check(action.AutoSize&&action.GetPreferredSize(Size.Empty).Width>=
                TextRenderer.MeasureText(action.Text,action.Font).Width+action.Padding.Horizontal,
                "Admission toolbar preferred width must fit the full caption and padding: "+action.Text);
        }
    }
}
