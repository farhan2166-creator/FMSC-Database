using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports.Finance;
using FMSC_SMS.Pages.Students.Certificates;
using FMSC_SMS.Pages.Students.Profile;
using FMSC_SMS.Pages.Security;

internal static partial class Program
{
    // Offline captures for the audit's remaining layout questions. No student
    // lookup, report creation, certificate issuance or database access is started.
    private static void CaptureUiGuidanceReview()
    {
        string output=Path.GetFullPath("artifacts/ui-guidance-review");
        Directory.CreateDirectory(output);
        var originalTheme=AppTheme.CurrentTheme;
        var previous=(CurrentUser.UserId,CurrentUser.Username,CurrentUser.RoleName,CurrentUser.MustChangePassword,CurrentUser.GetPermissions().ToArray());
        try
        {
            CurrentUser.SignIn(919,"Guidance rendering fixture","Administrator",false,Array.Empty<string>());
            foreach(var theme in new[]{AppThemeType.OceanBlue,AppThemeType.ProfessionalSimple})
            foreach(var size in new[]{new Size(1000,620),new Size(1280,720)})
            {
                AppTheme.SetTheme(theme);
                Draw("annual-receipts",()=>new AnnualFeeReceiptsPage(false));
                Draw("certificates",()=>new StudentCertificatesPage());
                Draw("student-profile",()=>new StudentProfilePage(null,false));

                void Draw(string name,Func<Control> create)
                {
                    using var page=create();
                    using var form=new Form {ClientSize=size,ShowInTaskbar=false,
                        StartPosition=FormStartPosition.Manual,Location=new Point(-32000,-32000)};
                    page.Dock=DockStyle.Fill;
                    form.Controls.Add(page);
                    form.Show();Application.DoEvents();form.PerformLayout();
                    Check(page.Width>0&&page.Height>0,name+" has a rendered client area.");
                    using var bitmap=new Bitmap(form.Width,form.Height);
                    form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                    bitmap.Save(Path.Combine(output,$"{name}-{theme}-{size.Width}.png"));
                    if(name=="annual-receipts")
                    {
                        var tip=Descendants(page).OfType<HumanCard>().Single(card=>
                            Descendants(card.Header).Any(c=>c.Text=="Tip"));
                        Console.WriteLine($"Annual receipts {theme} {size.Width}x{size.Height}, actual DPI {form.DeviceDpi}: Tip card {tip.Width}x{tip.Height}; empty content {tip.Content.Width}x{tip.Content.Height}.");
                    }
                    form.Close();
                }
            }
        }
        finally
        {
            AppTheme.SetTheme(originalTheme);
            CurrentUser.SignIn(previous.UserId,previous.Username,previous.RoleName,previous.MustChangePassword,previous.Item5);
        }
    }
}
