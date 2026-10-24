using System.Reflection;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckBlockingFeedback()
    {
        var themeField=typeof(AppTheme).GetField("_currentTheme",BindingFlags.Static|BindingFlags.NonPublic)!;
        var previous=AppTheme.CurrentTheme;
        try
        {
            foreach(var theme in AppTheme.SelectableThemes)
            {
                themeField.SetValue(null,theme);
                foreach(var icon in new[]{MessageBoxIcon.Warning,MessageBoxIcon.Error})
                {
                    using var dialog=ModernMessageDialog.CreateDialog(null,new MessageDialogContent("The operation cannot continue.","Configure the missing fees, then review again."),
                        "Cannot Confirm",MessageBoxButtons.OK,icon,MessageBoxDefaultButton.Button1);
                    var labels=MessageChildren(dialog).OfType<Label>().Where(label=>label.Name is "MessageSummary" or "MessageExplanation").ToArray();
                    Check(labels.Length==2 && labels.All(label=>label.ForeColor==AppTheme.Danger),"Blocking dialog text is red in "+theme);
                }
                using var confirmation=ModernMessageDialog.CreateDialog(null,new MessageDialogContent("Continue with this reviewed action?"),
                    "Confirm",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2);
                Check(MessageChildren(confirmation).OfType<Label>().Single(label=>label.Name=="MessageSummary").ForeColor==AppTheme.Text,
                    "A normal confirmation choice remains distinct from a refusal");
                using var page=new StatusMessageFixture(false,false);
                page.ShowMessage("Required fees are missing. Nothing was saved.");
                Check(page.Status.ForeColor==AppTheme.StatusText(AppTheme.Danger),"Page refusal status is red in "+theme);
                using var status=new Label();
                OperationFeedback.Set(status,"A saved fee prevents this operation.",true);
                Check(status.ForeColor==AppTheme.Danger,"Inline blockers use the same red semantic colour");
                status.Text="Ready to review.";
                Check(status.ForeColor==AppTheme.MutedText,"New guidance clears a previous inline error colour");
            }
        }
        finally{themeField.SetValue(null,previous);}
    }
}
