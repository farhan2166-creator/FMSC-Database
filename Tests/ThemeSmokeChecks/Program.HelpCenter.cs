using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Help;
using FMSC_SMS.Pages.Security;

internal static partial class Program
{
    private static void CheckHelpCenter()
    {
        var previous = (CurrentUser.UserId, CurrentUser.Username, CurrentUser.RoleName,
            CurrentUser.MustChangePassword, CurrentUser.GetPermissions().ToArray());
        var theme = AppTheme.CurrentTheme;
        try
        {
            CurrentUser.SignOut();
            using var page = new HelpCenterPage();
            using var host = new Form { ClientSize = new Size(1100, 700) };
            host.Controls.Add(page); host.Show(); Application.DoEvents();
            T Named<T>(string name) where T : Control => (T)page.Controls.Find(name, true).Single();
            var search = Named<TextBox>("HelpSearch");
            var category = Named<ComboBox>("HelpCategory");
            var rule = Named<ComboBox>("HelpRule");
            var results = Named<ListBox>("HelpResults");
            var related = Named<ListBox>("HelpRelated");
            var answer = Named<RichTextBox>("HelpAnswer");
            var count = Named<Label>("HelpCount");
            Check(results.Items.Count == HelpCatalog.Entries.Count, "Help opens offline without a signed-in user or SQL");
            Check(answer.ReadOnly && !answer.DetectUrls, "Help answer stays read-only with no external links");
            search.Text = "pending siblings"; Application.DoEvents();
            Check(results.Items.Cast<HelpEntry>().Any(e => e.Id == "FAQ-083"), "Realistic search updates UI");
            category.SelectedItem = "Collection"; Application.DoEvents();
            Check(results.Items.Count == 0 && count.Text.Contains("No matching") && answer.Text.Contains("clear"), "Empty combined filters explain recovery");
            search.Text = "payment"; rule.SelectedIndex = (int)HelpRule.Recovery + 1; Application.DoEvents();
            Check(results.Items.Count > 0 && results.Items.Cast<HelpEntry>().All(e => e.Category == "Collection" && e.Rules.Contains(HelpRule.Recovery)), "Rule/category/query apply together");
            category.SelectedIndex = rule.SelectedIndex = 0;
            search.Text = "How is Quick Registration different from Direct Admission?"; Application.DoEvents();
            Check(((HelpEntry)results.SelectedItem!).Id == "FAQ-012" && related.Items.Count > 0, "Question has stable related destinations");
            related.SelectedIndex = 0;
            string destination = ((HelpEntry)related.SelectedItem!).Id;
            typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(related, new object[] { new KeyEventArgs(Keys.Enter) });
            Application.DoEvents();
            Check(search.Text.Length == 0 && category.SelectedIndex == 0 && rule.SelectedIndex == 0, "Following a related question clears excluding filters");
            Check(((HelpEntry)results.SelectedItem!).Id == destination && answer.Focused, "Related Enter selects the destination and focuses its answer");
            search.Text = "payment refresh"; Application.DoEvents();
            string selected = ((HelpEntry)results.SelectedItem!).Id;
            Check(answer.Text.Contains("Important:") && answer.Text.Contains("Where to go:"), "Caution and next destination are present");
            foreach (var item in AppTheme.SelectableThemes)
            {
                AppTheme.SetTheme(item); Application.DoEvents();
                Check(((HelpEntry)results.SelectedItem!).Id == selected && search.Text == "payment refresh", "Theme preserves question and filters");
                Check(answer.BackColor == AppTheme.Surface && answer.ForeColor == AppTheme.Text, "Answer adopts theme colors");
            }
            var split = page.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<SplitContainer>().Single();
            foreach (var size in new[] { new Size(1100, 700), new Size(600, 500), new Size(420, 340) })
            {
                host.ClientSize = size; Application.DoEvents();
                Check(split.Orientation == (split.Width < DpiLayout.Pixels(760, page.DeviceDpi) ? Orientation.Horizontal : Orientation.Vertical), "Layout adapts to effective available width");
                Check(answer.ClientSize.Height > 0 && answer.ClientSize.Width > 0 && search.Visible && results.Visible, "Compact layout keeps answer/search/results accessible");
            }
            host.Close();
            page.Dispose();
            AppTheme.SetTheme(AppThemeType.OceanBlue); // A disposed page must have unsubscribed.
            Check(page.IsDisposed, "Help disposes without pending callbacks or theme subscriptions");
        }
        finally
        {
            AppTheme.SetTheme(theme);
            if (previous.UserId > 0) CurrentUser.SignIn(previous.UserId, previous.Username, previous.RoleName, previous.MustChangePassword, previous.Item5);
            else CurrentUser.SignOut();
        }
    }
}
