namespace FMSC_SMS.Core;
public static class ModernMessageDialog
{
    public static void ShowInfo(IWin32Window? owner, string message, string title = "FMSC") =>
        ShowCore(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
    public static void ShowWarning(IWin32Window? owner, string message, string title = "FMSC") =>
        ShowCore(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
    public static void ShowError(IWin32Window? owner, string message, string title = "FMSC") =>
        ShowCore(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
    /* A question about throwing something away (discard unsaved changes, delete, remove, reverse, overwrite, "cannot be undone") defaults to "No". */
    public static bool Confirm(IWin32Window? owner, string message, string title = "Confirm", bool? defaultYes = null) =>
        ShowCore(owner, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            (defaultYes ?? !LooksDestructive(title, message)) ? MessageBoxDefaultButton.Button1 : MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    private static readonly System.Text.RegularExpressions.Regex DestructiveWording = new(
        @"\b(delete|remove|discard|unsaved|reverse|reversal|overwrit|replace|cancel(?:led)?|cannot be undone|will be lost|permanent)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static bool LooksDestructive(string? title, string? message) =>
        DestructiveWording.IsMatch(title ?? string.Empty) || DestructiveWording.IsMatch(message ?? string.Empty);
    /* The standard wording for the Cancel guard on add/edit dialogs. */
    private const string UnsavedChangesMessage =
        "Leave without saving?\r\n\r\nYour changes will be lost.";
    /// <summary>The one shared Cancel guard for add/edit dialogs.</summary>
    public static bool ConfirmDiscardIfDirty(IWin32Window? owner, Func<bool> isDirty, string? message = null)
    {
        if (!isDirty())
            return true;
        return Confirm(owner, string.IsNullOrWhiteSpace(message) ? UnsavedChangesMessage : message, UiText.Dialogs.UnsavedChanges);
    }
    /// <summary>Runs the dirty-form discard prompt at most once for the caller's current close/cancel sequence.</summary>
    public static bool ConfirmDiscardOnce(
        IWin32Window? owner,
        Func<bool> isDirty,
        ref bool discardConfirmed,
        string? message = null)
    {
        if (discardConfirmed)
            return true;
        if (!ConfirmDiscardIfDirty(owner, isDirty, message))
            return false;
        discardConfirmed = true;
        return true;
    }
    public static DialogResult Show(string message, string title = "FMSC", MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.Information) =>
        Show(null, message, title, buttons, icon);
    public static DialogResult Show(IWin32Window? owner, string message, string title = "FMSC", MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.Information) =>
        ShowCore(owner, message, title, buttons, icon, MessageBoxDefaultButton.Button1);
    public static DialogResult Show(IWin32Window? owner, string message, string title, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton) =>
        ShowCore(owner, message, title, buttons, icon, defaultButton);
    public static DialogResult Show(IWin32Window? owner, MessageDialogContent content, string title,
        MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.Information,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        using var form = CreateDialog(owner, content, title, buttons, icon, defaultButton);
        return owner is null ? form.ShowDialog() : form.ShowDialog(owner);
    }
    private static DialogResult ShowCore(IWin32Window? owner, string message, string title, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton) =>
        Show(owner, MessageDialogContent.FromText(message), title, buttons, icon, defaultButton);

    internal static ApplicationWindowForm CreateDialog(IWin32Window? owner, MessageDialogContent content, string title,
        MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
    {
        Rectangle work = (owner is Control control ? Screen.FromControl(control) : Screen.PrimaryScreen)?.WorkingArea
            ?? new Rectangle(0, 0, 1280, 720);
        int width = Math.Min(AppScale.Scale(content.Records is null ? 560 : 800), Math.Max(1, work.Width - AppScale.Scale(32)));
        var form = new ApplicationWindowForm {
            Text = title, StartPosition = owner is null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
            ShowInTaskbar = false, KeyPreview = true, BackColor = AppTheme.Surface, Font = AppTheme.NormalFont
        };
        var root = new TableLayoutPanel {
            Name = "MessageRoot", Dock = DockStyle.Fill, AutoScroll = true,
            Padding = new Padding(AppScale.Scale(22)), ColumnCount = 2, RowCount = 2,
            Margin = Padding.Empty, BackColor = AppTheme.Surface
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AppScale.Scale(48)));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight + AppScale.Scale(20)));
        var iconLabel = new Label {
            Dock = DockStyle.Fill, UseMnemonic = false,
            Text = icon switch { MessageBoxIcon.Warning => "!", MessageBoxIcon.Error => "×", MessageBoxIcon.Question => "?", _ => "i" },
            ForeColor = icon switch { MessageBoxIcon.Warning => AppTheme.Warning, MessageBoxIcon.Error => AppTheme.Danger, _ => AppTheme.Primary },
            Font = AppTheme.SharedFont("Segoe UI", 20F, FontStyle.Bold), TextAlign = ContentAlignment.TopCenter
        };
        var viewport = new Panel {
            Name = "MessageViewport", Dock = DockStyle.Fill, AutoScroll = true,
            Margin = Padding.Empty, Padding = Padding.Empty, TabStop = false
        };
        var body = new TableLayoutPanel {
            Name = "MessageBody", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 0, Margin = Padding.Empty, Padding = Padding.Empty
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        viewport.Controls.Add(body);
        void Add(Control item)
        {
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.Controls.Add(item, 0, body.RowCount++);
        }
        Label TextBlock(string text, bool strong, string name) => new() {
            Name = name, Text = text, AutoSize = true, Dock = DockStyle.Fill, UseMnemonic = false,
            Font = strong ? AppTheme.BodyBoldFont : AppTheme.NormalFont, ForeColor = AppTheme.Text,
            Margin = new Padding(0, 0, 0, AppScale.Scale(12)), TextAlign = ContentAlignment.TopLeft
        };
        Add(TextBlock(content.Summary, content.Summary.Length <= 180, "MessageSummary"));
        if (!string.IsNullOrWhiteSpace(content.Notice))
        {
            var notice = TextBlock(content.Notice, true, "MessageNotice");
            notice.Padding = new Padding(AppScale.Scale(10));
            notice.BackColor = AppTheme.SurfaceSecondary;
            Add(notice);
        }
        if (!string.IsNullOrWhiteSpace(content.Explanation))
            Add(TextBlock(content.Explanation, false, "MessageExplanation"));
        TableLayoutPanel? facts = null;
        if (content.Facts is { Count: > 0 })
        {
            facts = new TableLayoutPanel {
                Name = "MessageFacts", Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 0,
                Margin = new Padding(0, 0, 0, AppScale.Scale(12)), Padding = Padding.Empty
            };
            facts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AppScale.Scale(140)));
            facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var fact in content.Facts)
            {
                int row = facts.RowCount++;
                facts.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var label = TextBlock(fact.Label, false, "DetailLabel");
                label.ForeColor = AppTheme.MutedText;
                label.Margin = new Padding(0, 0, AppScale.Scale(12), AppScale.Scale(8));
                var value = TextBlock(fact.Value, true, "DetailValue");
                value.Margin = new Padding(0, 0, 0, AppScale.Scale(8));
                facts.Controls.Add(label, 0, row); facts.Controls.Add(value, 1, row);
            }
            Add(facts);
        }
        DataGridView? recordsGrid = null;
        int recordsRow = -1;
        if (content.Records is { Columns.Count: > 0 } records)
        {
            var grid = HumanUi.Grid(false);
            grid.Name = "MessageRecords"; grid.Dock = DockStyle.Fill; grid.Margin = new Padding(0, 0, 0, AppScale.Scale(12));
            void FormatColumn(DataGridViewColumn column)
            {
                column.MinimumWidth = AppScale.Scale(90);
                if (column.ValueType == typeof(decimal))
                {
                    column.DefaultCellStyle.Format = "#,##0.##";
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
            }
            grid.ColumnAdded += (_, e) => FormatColumn(e.Column);
            grid.DataBindingComplete += (_, _) => {
                foreach (DataGridViewColumn column in grid.Columns) FormatColumn(column);
            };
            grid.DataSource = records; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.Height = grid.ColumnHeadersHeight + Math.Min(8, Math.Max(1, records.Rows.Count)) * AppMetrics.GridRowHeight + AppScale.Scale(8);
            recordsGrid = grid;
            recordsRow = body.RowCount;
            Add(grid);
            body.RowStyles[recordsRow].SizeType = SizeType.Absolute;
            body.RowStyles[recordsRow].Height = grid.Height + grid.Margin.Vertical;
        }
        bool fittingBody = false;
        void FitBody()
        {
            if (fittingBody) return;
            fittingBody = true;
            try
            {
            int available = Math.Max(1, viewport.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            body.Width = available;
            foreach (Label label in body.Controls.OfType<Label>())
                label.MaximumSize = new Size(Math.Max(1, available - label.Margin.Horizontal), 0);
            if (facts is not null)
            {
                int caption = Math.Min(AppScale.Scale(140), available / 3);
                facts.ColumnStyles[0].Width = caption;
                foreach (Label label in facts.Controls)
                    label.MaximumSize = new Size(Math.Max(1, (facts.GetColumn(label) == 0 ? caption : available - caption) - label.Margin.Horizontal), 0);
            }
            if (recordsGrid is not null)
            {
                recordsGrid.Width = available;
                recordsGrid.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders);
                int rowsHeight = recordsGrid.Rows.Cast<DataGridViewRow>().Take(8).Sum(row => row.Height);
                int height = recordsGrid.ColumnHeadersHeight + Math.Max(AppMetrics.GridRowHeight, rowsHeight) + AppScale.Scale(8);
                recordsGrid.Height = height;
                body.RowStyles[recordsRow].Height = height + recordsGrid.Margin.Vertical;
            }
            }
            finally { fittingBody = false; }
        }
        viewport.SizeChanged += (_, _) => FitBody();
        root.Controls.Add(iconLabel, 0, 0); root.Controls.Add(viewport, 1, 0);
        var buttonsPanel = new FlowLayoutPanel {
            Name = "MessageButtons", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.None, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        Button? initialFocus = null;
        Button MakeButton(string text, DialogResult result, bool primary)
        {
            var button = new ModernButton {
                Text = text, Name = "Message" + text, DialogResult = result,
                Width = AppScale.Scale(108), Height = AppMetrics.ButtonHeight, Font = AppTheme.ButtonFont,
                Margin = new Padding(AppScale.Scale(8), 0, AppScale.Scale(8), 0),
                ThemeRole = primary ? "Primary" : "Secondary"
            };
            AppMetrics.FitButtonWidth(button, minWidth: 108);
            return button;
        }
        if (buttons == MessageBoxButtons.YesNo)
        {
            var yes = MakeButton("Yes", DialogResult.Yes, true);
            var no = MakeButton("No", DialogResult.No, false);
            buttonsPanel.Controls.Add(yes); buttonsPanel.Controls.Add(no);
            form.AcceptButton = defaultButton == MessageBoxDefaultButton.Button2 ? no : yes;
            form.CancelButton = no;
            initialFocus = defaultButton == MessageBoxDefaultButton.Button2 ? no : yes;
        }
        else
        {
            var ok = MakeButton("OK", DialogResult.OK, true);
            buttonsPanel.Controls.Add(ok); form.AcceptButton = ok; initialFocus = ok;
        }
        root.Controls.Add(buttonsPanel, 0, 1); root.SetColumnSpan(buttonsPanel, 2);
        form.Controls.Add(root);
        int bodyWidth = Math.Max(1, width - root.Padding.Horizontal - AppScale.Scale(48) - SystemInformation.VerticalScrollBarWidth);
        form.ClientSize = new Size(width, AppScale.Scale(300));
        form.PerformLayout();
        viewport.Size = new Size(bodyWidth, AppScale.Scale(300)); FitBody();
        int preferred = body.GetPreferredSize(new Size(bodyWidth, 0)).Height + root.Padding.Vertical + AppMetrics.ButtonHeight + AppScale.Scale(32);
        form.ClientSize = new Size(width, Math.Min(Math.Max(AppScale.Scale(230), preferred), Math.Max(1, work.Height - AppScale.Scale(80))));
        form.Shown += (_, _) => {
            FitBody(); body.PerformLayout();
            int fitted = body.GetPreferredSize(new Size(body.Width, 0)).Height + root.Padding.Vertical + AppMetrics.ButtonHeight + AppScale.Scale(32);
            form.ClientSize = new Size(width, Math.Min(Math.Max(AppScale.Scale(230), fitted), Math.Max(1, work.Height - AppScale.Scale(80))));
            form.ActiveControl = initialFocus;
        };
        form.KeyDown += (_, e) => {
            if (e.KeyCode == Keys.Escape)
                form.DialogResult = buttons == MessageBoxButtons.YesNo ? DialogResult.No : DialogResult.Cancel;
            else if (buttons == MessageBoxButtons.YesNo && e.KeyCode == Keys.Y && !e.Control && !e.Alt)
                form.DialogResult = DialogResult.Yes;
            else if (buttons == MessageBoxButtons.YesNo && e.KeyCode == Keys.N && !e.Control && !e.Alt)
                form.DialogResult = DialogResult.No;
            else return;
            e.Handled = true; e.SuppressKeyPress = true;
        };
        return form;
    }
}
