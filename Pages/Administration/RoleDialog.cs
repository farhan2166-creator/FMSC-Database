using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Administration;
public sealed class RoleDialog : ApplicationWindowForm
{
    readonly ModernTextBox roleName = HumanUi.TextBox(false, 50);
    readonly ModernTextBox description = HumanUi.TextBox(false, 250);
    readonly ModernButton save;
    readonly bool isNewRole;
    /* The values this dialog was loaded with, so Cancel on an untouched form closes without asking about changes that are not there. */
    readonly string initialRoleName;
    readonly string initialDescription;
    /* Set only once Save has succeeded, so that save path can close without the unsaved-changes prompt. */
    bool savedSuccessfully;
    /* One confirmation per close gesture. Assigning DialogResult on a modal form raises FormClosing too, so a single Cancel click would otherwise ask twice. */
    bool discardConfirmed;
    /* UX - a missing role name is marked on the field, not shown in a message box. */
    readonly FormValidator validator = new();
    public bool IsNewRole => isNewRole;
    public string ResultRoleName => roleName.Text.Trim();
    public string ResultDescription => description.Text.Trim();
    public RoleDialog(bool isNewRole, string existingRoleName = "", string existingDescription = "")
    {
        this.isNewRole = isNewRole;
        Text = isNewRole
            ? "FMSC - New Role"
            : "FMSC - Edit Role";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(AppScale.Scale(520), AppScale.Scale(400));
        MinimumSize = new Size(AppScale.Scale(480), AppScale.Scale(380));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        roleName.Text = existingRoleName;
        roleName.PlaceholderText = "Enter role name";
        description.PlaceholderText =
            "Describe what this role is responsible for";
        initialRoleName = roleName.Text;
        initialDescription = description.Text;
        save = new ModernButton {
            Text = isNewRole ? "Create Role" : "Save Role",
            Dock = DockStyle.Fill,
            ThemeRole = "Success"
        };
        save.Click += (_, _) => Save();
        var cancel = new ModernButton {
            Text = UiText.Actions.Cancel,
            Dock = DockStyle.Fill,
            ThemeRole = "Secondary"
        };
        cancel.Click += (_, _) => {
            if (!ConfirmDiscard())
                return;
            DialogResult = DialogResult.Cancel;
            Close();
        };
        var card = new RoundedPanel {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            BorderColor = AppTheme.Border,
            ShowBorder = true,
            CornerRadius = AppTheme.CardRadius,
            Padding = new Padding(AppScale.Scale(32, AppScale.SpacingScale))
        };
        int titleRowHeight =
            Math.Max(
                AppMetrics.AuthenticationTitleRowHeight,
                AppMetrics.TextHeight(AppTheme.PageTitleFont) + 16);
        int fieldRowHeight =
            Math.Max(
                AppMetrics.PasswordFieldRowHeight,
                AppMetrics.FieldRowHeight);
        int spacerRowHeight =
            Math.Max(
                40,
                AppMetrics.SpacingXxl + AppMetrics.SpacingLg);
        int buttonRowHeight =
            Math.Max(
                AppMetrics.ButtonRowHeight,
                AppMetrics.ButtonHeight +
                UiDesignSystem.Spacing.Md +
                UiDesignSystem.Spacing.Xs);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, titleRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, spacerRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, buttonRowHeight));
        var title = new Label {
            Text = isNewRole ? "New Role" : "Edit Role",
            Dock = DockStyle.Fill,
            Font = AppTheme.PageTitleFont,
            ForeColor = AppTheme.Primary,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0)
        };
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(HumanUi.Field("Role Name", roleName, true), 0, 1);
        root.Controls.Add(HumanUi.Field("Description", description), 0, 2);
        var buttons =
            HumanUi.ButtonRow(save, cancel);
        root.Controls.Add(buttons, 0, 4);
        card.Controls.Add(root);
        var outer = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(38, 34, 38, 24),
            Margin = new Padding(0)
        };
        outer.Controls.Add(card, 0, 0);
        Controls.Add(outer);
        int requiredClientHeight =
            outer.Padding.Vertical +
            card.Padding.Vertical +
            titleRowHeight +
            (2 * fieldRowHeight) +
            spacerRowHeight +
            buttonRowHeight +
            AppMetrics.SpacingSm;
        ClientSize =
            new Size(
                520,
                Math.Max(400, requiredClientHeight));
        AcceptButton = save;
        SetSaveShortcut(save);
        CancelButton = cancel;
        FormFlow.EnterMovesToNext(this);
        /* The window's own close (X, Alt+F4, Esc) is guarded the same way; programmatic closes are left alone. */
        FormClosing += (_, e) => {
            if (e.CloseReason == CloseReason.UserClosing)
                e.Cancel = !ConfirmDiscard();
        };
        Shown += (_, _) => roleName.Focus();
        Disposed += (_, _) => validator.Dispose();
    }
    /// <summary>every field this dialog edits, compared with the value it was loaded with. Raw text, not trimmed, so typing then removing a character leaves the form clean again.</summary>
    bool IsDirty() =>
        !savedSuccessfully &&
        (roleName.Text != initialRoleName ||
         description.Text != initialDescription);
    /// <summary>asks at most once for a close gesture. True when the caller may proceed with the close.</summary>
    bool ConfirmDiscard()
    {
        return ModernMessageDialog.ConfirmDiscardOnce(this, IsDirty, ref discardConfirmed);
    }
    void Save()
    {
        if (savedSuccessfully)
            return;
        validator.Begin();
        validator.Required(roleName, UiText.Validation.Required);
        if (!validator.Finish())
            return;
        savedSuccessfully = true;
        DialogResult = DialogResult.OK;
        Close();
    }
}

