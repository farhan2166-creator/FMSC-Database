using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Help;

/// <summary>Read-only offline help, available without school data or an action permission.</summary>
public sealed class HelpCenterPage : UserControl
{
    private readonly TextBox search = new() { Name = "HelpSearch", Dock = DockStyle.Fill, PlaceholderText = "Search your question — e.g. unpaid fees, pending sibling, save timeout", AccessibleName = "Search help", AccessibleDescription = "Search questions and answers. All meaningful words must match." };
    private readonly ComboBox category = new() { Name = "HelpCategory", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Topic" };
    private readonly ComboBox rule = new() { Name = "HelpRule", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Rule type" };
    private readonly ListBox results = new() { Name = "HelpResults", Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, AccessibleName = "Matching questions", AccessibleDescription = "Use arrow keys to select a question. Enter moves to the answer." };
    private readonly ListBox related = new() { Name = "HelpRelated", Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, AccessibleName = "Related questions", AccessibleDescription = "Select a question and press Enter, or double-click, to read it." };
    private readonly Label counter = new() { Name = "HelpCount", Dock = DockStyle.Fill, AutoSize = true, AccessibleName = "Question count" };
    private readonly Label title = new() { Dock = DockStyle.Fill, AutoSize = true, Text = "Help & FAQ" };
    private readonly Label introduction = new() { Dock = DockStyle.Fill, AutoSize = true, Text = "Find the reason, the supported next step and where to go. Help works offline." };
    private readonly Label relatedTitle = new() { AutoSize = true, Text = "Related questions", Dock = DockStyle.Fill };
    private readonly RichTextBox answer = new() { Name = "HelpAnswer", Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Vertical, TabStop = true, AccessibleName = "Answer and next steps" };
    private readonly SplitContainer split = new() { Dock = DockStyle.Fill, Panel1MinSize = 0, Panel2MinSize = 0 };
    private readonly TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
    private readonly TableLayoutPanel filters = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, AutoSize = true };
    private readonly TableLayoutPanel detail = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
    private HelpEntry[] matching = Array.Empty<HelpEntry>();
    private bool updating;

    public HelpCenterPage()
    {
        Name = "HelpCenterPage";
        Dock = DockStyle.Fill;
        AutoScaleMode = AutoScaleMode.None; // Shared UiDpi metrics/fonts own the DPI boundary.
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        filters.Controls.Add(search, 0, 0); filters.SetColumnSpan(search, 2);
        filters.Controls.Add(new Label { Text = "Topic", AutoSize = true }, 0, 1);
        filters.Controls.Add(new Label { Text = "Rule type", AutoSize = true }, 1, 1);
        filters.Controls.Add(category, 0, 2); filters.Controls.Add(rule, 1, 2);
        filters.Controls.Add(counter, 0, 3); filters.SetColumnSpan(counter, 2);
        category.Items.Add("All topics");
        foreach (string item in HelpCatalog.Entries.Select(e => e.Category).Distinct().OrderBy(c => c)) category.Items.Add(item);
        category.SelectedIndex = 0;
        rule.Items.Add("All rule types");
        foreach (HelpRule item in Enum.GetValues<HelpRule>()) rule.Items.Add(HelpCatalog.RuleLabel(item));
        rule.SelectedIndex = 0;
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        detail.Controls.Add(answer, 0, 0); detail.Controls.Add(relatedTitle, 0, 1); detail.Controls.Add(related, 0, 2);
        split.Panel1.Controls.Add(results); split.Panel2.Controls.Add(detail);
        layout.Controls.Add(title, 0, 0); layout.Controls.Add(introduction, 0, 1);
        layout.Controls.Add(filters, 0, 2); layout.Controls.Add(split, 0, 3);
        Controls.Add(layout);
        search.TextChanged += (_, _) => Filter();
        category.SelectedIndexChanged += (_, _) => Filter();
        rule.SelectedIndexChanged += (_, _) => Filter();
        results.SelectedIndexChanged += (_, _) => { if (!updating) ShowAnswer(); };
        results.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && results.SelectedItem != null) { answer.Focus(); e.Handled = e.SuppressKeyPress = true; } };
        related.DoubleClick += (_, _) => OpenRelated();
        related.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { OpenRelated(); e.Handled = e.SuppressKeyPress = true; } };
        split.SizeChanged += (_, _) => FitSplit();
        detail.SizeChanged += (_, _) => FitRelated();
        DpiChangedAfterParent += (_, _) => ApplyTheme();
        AppTheme.ThemeChanged += ThemeChanged;
        Disposed += (_, _) => AppTheme.ThemeChanged -= ThemeChanged;
        ApplyTheme();
        Filter();
    }

    private void FitSplit()
    {
        // Table layout can temporarily give either axis no room for the splitter.
        // Orientation reuses the old distance, so bound it for both axes first.
        int sharedAvailable = Math.Min(split.Width, split.Height) - split.SplitterWidth;
        if (sharedAvailable <= 0) return;
        bool compact = split.Width < DpiLayout.Pixels(760, DeviceDpi);
        var orientation = compact ? Orientation.Horizontal : Orientation.Vertical;
        if (split.Orientation != orientation)
        {
            split.SplitterDistance = Math.Min(split.SplitterDistance, sharedAvailable);
            split.Orientation = orientation;
        }
        int available = (compact ? split.Height : split.Width) - split.SplitterWidth;
        if (available > 0) split.SplitterDistance = Math.Clamp(available / 3, 0, available);
    }

    private void Filter(string? selectId = null)
    {
        if (updating) return;
        string? previousId = selectId ?? (results.SelectedItem as HelpEntry)?.Id;
        HelpRule? selectedRule = rule.SelectedIndex > 0 ? (HelpRule)(rule.SelectedIndex - 1) : null;
        matching = HelpCatalog.Find(search.Text, category.SelectedItem?.ToString(), selectedRule).ToArray();
        updating = true;
        results.BeginUpdate();
        try
        {
            results.DataSource = null;
            results.DisplayMember = nameof(HelpEntry.Question);
            results.DataSource = matching;
            int index = Array.FindIndex(matching, e => e.Id == previousId);
            results.SelectedIndex = index >= 0 ? index : matching.Length > 0 ? 0 : -1;
        }
        finally { results.EndUpdate(); updating = false; }
        counter.Text = matching.Length == 0 ? "No matching questions. Try fewer words, All topics or All rule types." :
            $"{matching.Length} matching question{(matching.Length == 1 ? "" : "s")} · {HelpCatalog.Entries.Count} available";
        counter.AccessibleDescription = counter.Text;
        ShowAnswer();
    }

    private void ShowAnswer()
    {
        answer.Clear();
        HelpEntry? entry = results.SelectedItem as HelpEntry;
        related.DataSource = null;
        if (entry is null)
            Append("No matching question\n\nTry another keyword or clear the topic and rule-type filters.", AppTheme.NormalFont, AppTheme.Text);
        else
        {
            Append(entry.Question + "\n\n", AppTheme.HeadingFont, AppTheme.Text);
            Append(entry.Category + " · " + string.Join(" · ", entry.Rules.Select(HelpCatalog.RuleLabel)) + "\n\n", AppTheme.SmallFont, AppTheme.MutedText);
            Append(entry.Answer + "\n\n", AppTheme.NormalFont, AppTheme.Text);
            if (entry.Warning.Length > 0) Append("Important: " + entry.Warning + "\n\n", AppTheme.ButtonFont, AppTheme.Text);
            Append("Where to go: " + entry.WhereToGo, AppTheme.ButtonFont, AppTheme.Primary);
            related.DisplayMember = nameof(HelpEntry.Question);
            related.DataSource = HelpCatalog.Related(entry);
            related.SelectedIndex = -1;
        }
        bool hasRelated = related.Items.Count > 0;
        related.Visible = relatedTitle.Visible = hasRelated;
        FitRelated();
        answer.SelectionStart = 0; answer.SelectionLength = 0; answer.ScrollToCaret();
    }

    private void FitRelated() => detail.RowStyles[2].Height = related.Items.Count > 0
        ? Math.Min(UiDpi.Metric(this, AppMetrics.FieldRowHeight * 2), Math.Max(0, detail.ClientSize.Height / 4)) : 0;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F)) { search.Focus(); search.SelectAll(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Append(string text, Font font, Color color)
    {
        answer.SelectionStart = answer.TextLength;
        answer.SelectionFont = UiDpi.ForControl(answer, font);
        answer.SelectionColor = color;
        answer.AppendText(text);
    }

    private void OpenRelated()
    {
        if (related.SelectedItem is not HelpEntry entry) return;
        // Related links explicitly leave the current filtered list; never hide the destination.
        updating = true;
        try { search.Clear(); category.SelectedIndex = 0; rule.SelectedIndex = 0; }
        finally { updating = false; }
        Filter(entry.Id);
        answer.Focus();
    }

    private void ThemeChanged(object? sender, EventArgs e) => ApplyTheme();
    private void ApplyTheme()
    {
        BackColor = AppTheme.PageBackground; ForeColor = AppTheme.Text;
        Font = UiDpi.ForControl(this, AppTheme.NormalFont);
        title.Font = UiDpi.ForControl(title, AppTheme.HeadingFont);
        foreach (Control control in new Control[] { answer, results, related, search, category, rule })
        {
            control.BackColor = AppTheme.Surface; control.ForeColor = AppTheme.Text;
            control.Font = UiDpi.ForControl(control, AppTheme.NormalFont);
        }
        introduction.ForeColor = counter.ForeColor = AppTheme.MutedText;
        layout.Padding = new Padding(UiDpi.Metric(this, AppMetrics.PagePadding));
        detail.Padding = new Padding(UiDpi.Metric(this, AppMetrics.SpacingSm));
        filters.RowStyles.Clear();
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, UiDpi.Metric(this, AppMetrics.FieldRowHeight)));
        filters.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, UiDpi.Metric(this, AppMetrics.FieldRowHeight)));
        filters.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        split.SplitterWidth = UiDpi.Metric(this, AppMetrics.SpacingSm);
        ShowAnswer(); FitSplit();
    }
}
