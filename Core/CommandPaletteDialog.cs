namespace FMSC_SMS.Core;
public sealed class CommandPaletteDialog : ApplicationWindowForm
{
    private readonly TextBox search = new();
    private readonly ListBox results = new();
    private readonly IReadOnlyList<Control> source;
    public Button? SelectedButton { get; private set; }
    public CommandPaletteDialog(IReadOnlyList<Control> source, string? initialQuery = null)
    {
        this.source = source;
        Text = "Search FMSC";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(AppScale.S(620), AppScale.S(430));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        KeyPreview = true;
        Build();
        initialQuery = StripDigits(initialQuery);
        Populate(initialQuery);
        search.Text = initialQuery;
        search.SelectionStart = search.TextLength;
    }
    /// <summary>The text without any digit (feature names are text only).</summary>
    public static string StripDigits(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : new string(text.Where(c => !char.IsDigit(c)).ToArray());
    private void Build()
    {
        var card = new RoundedPanel {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            Padding = new Padding(AppScale.S(18))
        };
        var title = new Label {
            Dock = DockStyle.Top,
            Height = AppScale.S(32),
            Text = "Quick Navigation",
            Font = AppTheme.HeadingFont,
            ForeColor = AppTheme.HeadingText,
            TextAlign = ContentAlignment.MiddleLeft
        };
        search.Dock = DockStyle.Top;
        search.Height = AppScale.S(42);
        search.Font = AppTheme.NormalFont;
        search.BorderStyle = BorderStyle.FixedSingle;
        search.PlaceholderText = "Search a page or module...";
        search.Margin = new Padding(0, AppScale.S(8), 0, AppScale.S(10));
        /* Feature / page names are text: digits are refused as they are typed (with a beep) and stripped if they arrive by paste. */
        search.KeyPress += (_, e) => {
            if (!char.IsDigit(e.KeyChar))
                return;
            e.Handled = true;
            System.Media.SystemSounds.Beep.Play();
        };
        search.TextChanged += (_, _) => {
            string clean = StripDigits(search.Text);
            if (!string.Equals(clean, search.Text, StringComparison.Ordinal))
            {
                int caret = Math.Max(0, search.SelectionStart - (search.TextLength - clean.Length));
                search.Text = clean;
                search.SelectionStart = Math.Min(caret, clean.Length);
                return;
            }
            Populate(search.Text);
        };
        search.KeyDown += SearchKeyDown;
        results.Dock = DockStyle.Fill;
        results.BorderStyle = BorderStyle.None;
        results.BackColor = AppTheme.Surface;
        results.ForeColor = AppTheme.Text;
        results.Font = AppTheme.NormalFont;
        results.IntegralHeight = false;
        results.DoubleClick += (_, _) => Accept();
        results.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Accept(); } };
        var hint = new Label {
            Dock = DockStyle.Bottom,
            Height = AppScale.S(26),
            Text = "Ctrl+K to open  •  Enter to navigate  •  Esc to close",
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft
        };
        card.Controls.Add(results);
        card.Controls.Add(hint);
        card.Controls.Add(search);
        card.Controls.Add(title);
        Controls.Add(card);
        Shown += (_, _) => search.Focus();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; DialogResult = DialogResult.Cancel; } };
    }
    private void Populate(string query)
    {
        results.BeginUpdate();
        results.Items.Clear();
        /* UX - every word typed has to match, in any order ("fee coll" or "collection fee"), instead of the whole phrase having to appear exactly as typed. */
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var button in source)
        {
            if (!button.Visible || string.IsNullOrWhiteSpace(button.Text))
                continue;
            string caption=AppHotKeys.PlainCaption(button.Text);
            if (words.Length > 0 && !words.All(w => caption.Contains(w, StringComparison.OrdinalIgnoreCase)))
                continue;
            results.Items.Add(caption);
        }
        results.EndUpdate();
        if (results.Items.Count > 0)
            results.SelectedIndex = 0;
    }
    private void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down)
        {
            e.SuppressKeyPress = true;
            if (results.Items.Count > 0)
                results.SelectedIndex = Math.Min(results.SelectedIndex + 1, results.Items.Count - 1);
        }
        else if (e.KeyCode == Keys.Up)
        {
            e.SuppressKeyPress = true;
            if (results.Items.Count > 0)
                results.SelectedIndex = Math.Max(results.SelectedIndex - 1, 0);
        }
        else if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            Accept();
        }
    }
    private void Accept()
    {
        if (results.SelectedItem is not string text)
            return;
        SelectedButton = source.FirstOrDefault(b => b.Visible && string.Equals(AppHotKeys.PlainCaption(b.Text), text, StringComparison.Ordinal)) as Button;
        if (SelectedButton != null)
            DialogResult = DialogResult.OK;
    }
}

