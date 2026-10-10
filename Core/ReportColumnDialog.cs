namespace FMSC_SMS.Core;

/// <summary>The column chooser: the operator ticks the columns they want and orders them. Columns appear left to right in that order, in the grid, the PDF and the CSV alike.</summary>
public sealed class ReportColumnDialog : ApplicationWindowForm
{
    private const string UngroupedHeading = "Other";
    private readonly ListView list = new();
    private readonly Label count = new();
    private readonly IReadOnlyList<ReportColumnDef> catalog;
    private readonly IReadOnlyList<string> defaultKeys;
    /// <summary>The ticked columns in the order chosen. Valid after the dialog returns OK.</summary>
    public IReadOnlyList<string> ResultKeys { get; private set; } = Array.Empty<string>();
    public ReportColumnDialog(
        string title,
        IReadOnlyList<ReportColumnDef> catalog,
        IReadOnlyList<string> currentKeys,
        IReadOnlyList<string> defaultKeys)
    {
        this.catalog = catalog;
        this.defaultKeys = defaultKeys;
        Text = "Choose columns";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(AppScale.S(560), AppScale.S(560));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        KeyPreview = true;
        Build(title);
        Fill(currentKeys);
    }
    private ModernButton MakeButton(string text, string role, int width)
    {
        return new ModernButton {
            Text = text,
            ThemeRole = role,
            Width = AppScale.S(width),
            Height = AppMetrics.ButtonHeight,
            Margin = new Padding(0, 0, AppScale.S(8), 0)
        };
    }
    private void Build(string title)
    {
        var card = new RoundedPanel {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            Padding = new Padding(AppScale.S(18))
        };
        var heading = new Label {
            Dock = DockStyle.Top,
            Height = AppScale.S(32),
            Text = title,
            Font = AppTheme.HeadingFont,
            ForeColor = AppTheme.HeadingText,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var hint = new Label {
            Dock = DockStyle.Top,
            Height = AppScale.S(46),
            Text = "Tick the columns you want, then use Move up / Move down to set their order. " +
                   "The grid, the PDF and the CSV all follow this choice.",
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.TopLeft
        };
        list.Dock = DockStyle.Fill;
        list.View = View.Details;
        list.HeaderStyle = ColumnHeaderStyle.None;
        list.CheckBoxes = true;
        list.FullRowSelect = true;
        list.GridLines = false;
        list.MultiSelect = false;
        list.HideSelection = false;
        list.BorderStyle = BorderStyle.FixedSingle;
        list.BackColor = AppTheme.Surface;
        list.ForeColor = AppTheme.Text;
        list.Font = AppTheme.NormalFont;
        list.Columns.Add(string.Empty);
        list.Resize += (_, _) => {
            if (list.Columns.Count > 0)
                list.Columns[0].Width = list.ClientSize.Width;
        };
        // ItemChecked fires after the new state is applied. During the constructor (Fill) there is no window handle yet, and BeginInvoke would throw - update directly then.
        list.ItemChecked += (_, _) => {
            if (IsHandleCreated)
                BeginInvoke(new Action(UpdateCount));
            else
                UpdateCount();
        };
        list.DoubleClick += (_, _) => Toggle();
        count.Dock = DockStyle.Bottom;
        count.Height = AppScale.S(26);
        count.Font = AppTheme.SmallFont;
        count.ForeColor = AppTheme.MutedText;
        count.TextAlign = ContentAlignment.MiddleLeft;
        var moveColumn = new FlowLayoutPanel {
            Dock = DockStyle.Right,
            Width = AppScale.S(130),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(AppScale.S(10), 0, 0, 0),
            BackColor = Color.Transparent
        };
        var up = MakeButton("Move up", "Secondary", 110);
        var down = MakeButton("Move down", "Secondary", 110);
        var all = MakeButton("Select all", "Secondary", 110);
        var none = MakeButton("Clear all", "Secondary", 110);
        var reset = MakeButton("Defaults", "Secondary", 110);
        up.Click += (_, _) => MoveSelectedColumn(-1);
        down.Click += (_, _) => MoveSelectedColumn(1);
        all.Click += (_, _) => SetAll(true);
        none.Click += (_, _) => SetAll(false);
        reset.Click += (_, _) => Fill(defaultKeys);
        moveColumn.Controls.AddRange(new Control[] { up, down, all, none, reset });
        var apply = MakeButton("Apply", "Primary", 100);
        var cancel = MakeButton("Cancel", "Secondary", 100);
        apply.Click += (_, _) => Accept();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        apply.Margin = new Padding(AppScale.S(8), 0, 0, 0);
        cancel.Margin = new Padding(AppScale.S(8), 0, 0, 0);
        var buttonRow = new FlowLayoutPanel {
            Dock = DockStyle.Bottom,
            Height = AppMetrics.ButtonHeight + AppScale.S(14),
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, AppScale.S(8), 0, 0),
            BackColor = Color.Transparent
        };
        buttonRow.Controls.Add(apply);
        buttonRow.Controls.Add(cancel);
        var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        body.Controls.Add(list);
        body.Controls.Add(moveColumn);
        card.Controls.Add(body);
        card.Controls.Add(count);
        card.Controls.Add(buttonRow);
        card.Controls.Add(hint);
        card.Controls.Add(heading);
        Controls.Add(card);
        AcceptButton = apply;
        CancelButton = cancel;
    }
    /// <summary>Rebuilds the list as one <see cref="ListViewGroup"/> per catalogue group (in the order the catalogue defines them), each holding its columns.</summary>
    private void Fill(IReadOnlyList<string> keys)
    {
        list.BeginUpdate();
        list.Items.Clear();
        list.Groups.Clear();
        var chosen = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        var groupNames = new List<string>();
        var seenGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in catalog)
        {
            string groupName = string.IsNullOrWhiteSpace(def.Group) ? UngroupedHeading : def.Group;
            if (seenGroups.Add(groupName))
                groupNames.Add(groupName);
        }
        foreach (string groupName in groupNames)
        {
            var lvGroup = new ListViewGroup(groupName);
            list.Groups.Add(lvGroup);
            var groupDefs = catalog
                .Where(c => (string.IsNullOrWhiteSpace(c.Group) ? UngroupedHeading : c.Group) == groupName)
                .ToList();
            var ordered = new List<ReportColumnDef>();
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in keys)
            {
                var match = groupDefs.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
                if (match is not null && added.Add(match.Key))
                    ordered.Add(match);
            }
            foreach (var def in groupDefs)
            {
                if (added.Add(def.Key))
                    ordered.Add(def);
            }
            foreach (var def in ordered)
            {
                var item = new ListViewItem(def.Header) {
                    Tag = def,
                    Group = lvGroup,
                    Checked = chosen.Contains(def.Key)
                };
                list.Items.Add(item);
            }
        }
        list.EndUpdate();
        if (list.Items.Count > 0)
            list.Items[0].Selected = true;
        UpdateCount();
    }
    private void Toggle()
    {
        if (list.SelectedItems.Count == 0)
            return;
        ListViewItem item = list.SelectedItems[0];
        item.Checked = !item.Checked;
    }
    private void SetAll(bool value)
    {
        foreach (ListViewItem item in list.Items)
            item.Checked = value;
        UpdateCount();
    }
    /// <summary>Moves the selected column up or down within its own group - a column's category is fixed, only its place inside it is not.</summary>
    private void MoveSelectedColumn(int delta)
    {
        if (list.SelectedItems.Count == 0)
            return;
        ListViewItem item = list.SelectedItems[0];
        int from = item.Index;
        int to = from + delta;
        if (to < 0 || to >= list.Items.Count)
            return;
        ListViewItem neighbor = list.Items[to];
        if (neighbor.Group != item.Group)
            return;
        bool wasChecked = item.Checked;
        list.BeginUpdate();
        list.Items.RemoveAt(from);
        list.Items.Insert(to, item);
        list.EndUpdate();
        item.Checked = wasChecked;
        item.Selected = true;
        item.EnsureVisible();
    }
    private void UpdateCount()
    {
        if (IsDisposed)
            return;
        int checkedCount = list.Items.Cast<ListViewItem>().Count(i => i.Checked);
        count.Text = $"{checkedCount} of {list.Items.Count} columns shown";
    }
    private void Accept()
    {
        var keys = new List<string>();
        foreach (ListViewItem item in list.Items)
        {
            if (item.Checked && item.Tag is ReportColumnDef def)
                keys.Add(def.Key);
        }
        if (keys.Count == 0)
        {
            ModernMessageDialog.ShowWarning(this, "Tick at least one column to show.", "Choose columns");
            return;
        }
        ResultKeys = keys;
        DialogResult = DialogResult.OK;
    }
}


