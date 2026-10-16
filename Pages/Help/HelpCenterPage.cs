using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Help;

/// <summary>Read-only, offline operator help. No database connection or write permission required.</summary>
public sealed class HelpCenterPage : UserControl
{
    private readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "Search a question, rule or keyword (e.g. duplicate payment)...", AccessibleName = "Search help topics" };
    private readonly ComboBox category = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Help topic filter" };
    private readonly ListBox results = new() { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "Matching help questions" };
    private readonly Label counter = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoSize = false };
    private readonly Label question = new() { Dock = DockStyle.Top, AutoSize = false, Height = 80, Font = AppTheme.HeadingFont };
    private readonly Label source = new() { Dock = DockStyle.Top, Height = 34, AutoSize = false };
    private readonly Label next = new() { Dock = DockStyle.Top, AutoSize = false, Height = 58 };
    private readonly RichTextBox answer = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = AppTheme.PageBackground, DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Vertical, TabStop = true, AccessibleName = "Selected question answer" };
    private readonly SplitContainer split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 6 };
    private HelpEntry[] matching = Array.Empty<HelpEntry>();
    public HelpCenterPage()
    {
        Name = "HelpCenterPage";
        Dock = DockStyle.Fill;
        BackColor = AppTheme.PageBackground;
        ForeColor = AppTheme.Text;
        Font = AppTheme.NormalFont;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(16) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label { Dock = DockStyle.Fill, Text = "Help & Frequently Asked Questions", Font = AppTheme.HeadingFont, TextAlign = ContentAlignment.MiddleLeft };
        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 4, 0, 8) };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        filters.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
        filters.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        search.Margin = new Padding(0, 0, 10, 0);
        filters.Controls.Add(search, 0, 0);
        filters.Controls.Add(category, 1, 0);
        filters.Controls.Add(counter, 0, 1);
        filters.SetColumnSpan(counter, 2);
        category.Items.Add("All topics");
        foreach (var item in HelpCatalog.Entries.Select(e => e.Category).Distinct().OrderBy(c => c)) category.Items.Add(item);
        category.SelectedIndex = 0;
        split.Panel1.Controls.Add(results);
        var detail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(16, 4, 8, 0) };
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        question.Dock = DockStyle.Fill;
        source.Dock = DockStyle.Fill;
        next.Dock = DockStyle.Fill;
        detail.Controls.Add(question, 0, 0);
        detail.Controls.Add(source, 0, 1);
        detail.Controls.Add(answer, 0, 2);
        detail.Controls.Add(next, 0, 3);
        split.Panel2.Controls.Add(detail);
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(filters, 0, 1);
        layout.Controls.Add(split, 0, 2);
        Controls.Add(layout);
        search.TextChanged += (_, _) => Filter();
        category.SelectedIndexChanged += (_, _) => Filter();
        results.SelectedIndexChanged += (_, _) => ShowAnswer();
        results.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && matching.Length > 0) { answer.Focus(); e.Handled = true; } };
        split.SizeChanged += (_, _) => FitSplit();
        AppTheme.ThemeChanged += ThemeChanged;
        Disposed += (_, _) => AppTheme.ThemeChanged -= ThemeChanged;
        ApplyTheme();
        Filter();
    }
    private void FitSplit()
    {
        if (split.Width > 440)
            split.SplitterDistance = Math.Max(180, Math.Min(split.Width / 3, split.Width - 220 - split.SplitterWidth));
    }
    private void Filter()
    {
        var selected = results.SelectedItem as HelpEntry;
        matching = HelpCatalog.Find(search.Text, category.SelectedItem?.ToString()).ToArray();
        results.BeginUpdate();
        try
        {
            results.DataSource = null;
            results.DisplayMember = nameof(HelpEntry.Question);
            results.DataSource = matching;
            var index = selected is null ? -1 : Array.FindIndex(matching, e => e == selected);
            results.SelectedIndex = index >= 0 ? index : matching.Length > 0 ? 0 : -1;
        }
        finally { results.EndUpdate(); }
        counter.Text = matching.Length == 0 ? "No matching questions. Try another keyword or select All topics." : $"{matching.Length} matching question{(matching.Length == 1 ? "" : "s")} · {HelpCatalog.Entries.Count} available";
        ShowAnswer();
    }
    private void ShowAnswer()
    {
        if (results.SelectedItem is not HelpEntry entry)
        {
            question.Text = "No question selected"; source.Text = ""; answer.Text = "Try another search term or choose a different topic."; next.Text = ""; return;
        }
        question.Text = entry.Question;
        source.Text = $"{entry.Category}   ·   Rule origin: {entry.RuleSource}";
        answer.Text = entry.Answer;
        next.Text = $"Where to go: {entry.WhereToGo}";
    }
    private void ThemeChanged(object? sender, EventArgs e) => ApplyTheme();
    private void ApplyTheme()
    {
        BackColor = AppTheme.PageBackground;
        ForeColor = AppTheme.Text;
        answer.BackColor = AppTheme.PageBackground;
        answer.ForeColor = AppTheme.Text;
        source.ForeColor = AppTheme.MutedText;
        next.ForeColor = AppTheme.Primary;
        counter.ForeColor = AppTheme.MutedText;
        question.ForeColor = AppTheme.Text;
    }
}
