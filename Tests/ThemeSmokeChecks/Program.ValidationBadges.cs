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
    }
}
