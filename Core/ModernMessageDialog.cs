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
        "This form has unsaved changes.\r\n\r\nIf you leave now, those changes will be lost.";
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
    private static DialogResult ShowCore(IWin32Window? owner, string message, string title, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
    {
        int baseHeight = buttons == MessageBoxButtons.YesNo ? 230 : 210;
        int textWidth = AppScale.Scale(360);
        int textHeight = TextRenderer.MeasureText(
            message ?? string.Empty,
            AppTheme.NormalFont,
            new Size(textWidth, int.MaxValue),
            TextFormatFlags.WordBreak).Height;
        int dialogHeight = Math.Max(AppScale.Scale(baseHeight), textHeight + AppScale.Scale(110));
        using var form = new ApplicationWindowForm {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            KeyPreview = true,
            BackColor = AppTheme.Surface,
            ClientSize = new Size(AppScale.Scale(480), dialogHeight),
            Font = AppTheme.NormalFont
        };
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            Padding = new Padding(AppScale.Scale(22)),
            ColumnCount = 2,
            RowCount = 2,
            BackColor = AppTheme.Surface
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AppScale.Scale(52)));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.Scale(52)));
        var accent = icon switch {
            MessageBoxIcon.Warning => AppTheme.Warning,
            MessageBoxIcon.Error or MessageBoxIcon.Stop => AppTheme.Danger,
            MessageBoxIcon.Question => AppTheme.Primary,
            _ => AppTheme.Primary
        };
        var iconLabel = new Label {
            Dock = DockStyle.Fill,
            Text = icon switch {
                MessageBoxIcon.Warning => "!",
                MessageBoxIcon.Error or MessageBoxIcon.Stop => "×",
                MessageBoxIcon.Question => "?",
                _ => "i"
            },
            ForeColor = accent,
            Font = AppTheme.SharedFont("Segoe UI", 20F, FontStyle.Bold),
            TextAlign = ContentAlignment.TopCenter
        };
        var messageLabel = new Label {
            Dock = DockStyle.Fill,
            Text = message,
            ForeColor = AppTheme.Text,
            Font = AppTheme.NormalFont,
            AutoEllipsis = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0)
        };
        root.Controls.Add(iconLabel, 0, 0);
        root.Controls.Add(messageLabel, 1, 0);
        var buttonsPanel = new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 8, 0, 0)
        };
        Button? initialFocus = null;
        Button MakeButton(string text, DialogResult result, bool primary)
        {
            var button = new ModernButton {
                Text = text,
                DialogResult = result,
                Width = AppScale.Scale(108),
                Height = AppMetrics.ButtonHeight,
                Font = AppTheme.ButtonFont,
                ThemeRole = primary ? "Primary" : "Secondary"
            };
            return button;
        }
        if (buttons == MessageBoxButtons.YesNo)
        {
            var no = MakeButton("No", DialogResult.No, false);
            var yes = MakeButton("Yes", DialogResult.Yes, true);
            buttonsPanel.Controls.Add(no);
            buttonsPanel.Controls.Add(yes);
            form.AcceptButton = defaultButton == MessageBoxDefaultButton.Button2 ? no : yes;
            form.CancelButton = no;
            /* Tab order is No, Yes (added right-to-left), so without this the first tab stop. */
            initialFocus = defaultButton == MessageBoxDefaultButton.Button2 ? no : yes;
        }
        else
        {
            var ok = MakeButton("OK", DialogResult.OK, true);
            buttonsPanel.Controls.Add(ok);
            form.AcceptButton = ok;
            initialFocus = ok;
        }
        root.Controls.Add(buttonsPanel, 1, 1);
        form.Controls.Add(root);
        form.Shown += (_, _) => {
            if (initialFocus != null)
                form.ActiveControl = initialFocus;
        };
        form.KeyDown += (_, e) => {
            if (e.KeyCode == Keys.Escape)
            {
                form.DialogResult = buttons == MessageBoxButtons.YesNo ? DialogResult.No : DialogResult.Cancel;
                e.Handled = true;
            }
            else if (buttons == MessageBoxButtons.YesNo && e.KeyCode == Keys.Y && !e.Control && !e.Alt)
            {
                form.DialogResult = DialogResult.Yes;
                e.Handled = true;
            }
            else if (buttons == MessageBoxButtons.YesNo && e.KeyCode == Keys.N && !e.Control && !e.Alt)
            {
                form.DialogResult = DialogResult.No;
                e.Handled = true;
            }
        };
        return owner == null ? form.ShowDialog() : form.ShowDialog(owner);
    }
}

