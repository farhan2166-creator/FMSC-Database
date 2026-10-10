using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Administration;
public sealed class UserAccountDialog : ApplicationWindowForm
{
    readonly ModernTextBox username = HumanUi.TextBox(false, 100);
    readonly ModernTextBox password = HumanUi.TextBox(false, 100);
    readonly ModernTextBox confirmPassword = HumanUi.TextBox(false, 100);
    readonly CheckBox mustChangePassword = new ModernCheckBox {
        Text = "Force Password Change",
        AutoSize = true
    };
    readonly ModernButton save;
    readonly bool isNewUser;
    /* The values this dialog was loaded with, so Cancel on an untouched form closes without asking about changes that are not there. */
    readonly string initialUsername = "";
    readonly bool initialMustChangePassword = true;
    /* Set only once Save has succeeded, so that save path can close without the unsaved-changes prompt. */
    bool savedSuccessfully;
    /* One confirmation per close gesture. Assigning DialogResult on a modal form raises FormClosing too, so a single Cancel click would otherwise ask twice. */
    bool discardConfirmed;
    /* UX - field problems are marked on the fields, all at once, not shown in message boxes. */
    readonly FormValidator validator = new();
    public bool IsNewUser => isNewUser;
    public string ResultUsername => username.Text.Trim();
    public string ResultPassword => password.Text;
    public bool ResultMustChangePassword => mustChangePassword.Checked;
    public UserAccountDialog(
        bool isNewUser,
        string existingUsername = "",
        bool existingMustChangePassword = true)
    {
        this.isNewUser = isNewUser;
        Text = isNewUser
            ? "FMSC - New User"
            : "FMSC - Edit User Account";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(AppScale.Scale(560), AppScale.Scale(500));
        MinimumSize = new Size(AppScale.Scale(520), AppScale.Scale(480));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        username.Text = existingUsername;
        username.PlaceholderText = "Enter username";
        password.UseSystemPasswordChar = true;
        password.PlaceholderText = isNewUser
            ? "Enter password"
            : "Leave blank to keep current password";
        confirmPassword.UseSystemPasswordChar = true;
        confirmPassword.PlaceholderText =
            "Re-enter password to confirm";
        mustChangePassword.Checked = existingMustChangePassword;
        initialUsername = username.Text;
        initialMustChangePassword = mustChangePassword.Checked;
        save = new ModernButton {
            Text = isNewUser ? "Create User" : "Save Account",
            Dock = DockStyle.Fill,
            ThemeRole = "Success"
        };
        save.Click += (_, _) => Save();
        var cancel = new ModernButton {
            Text = "Cancel",
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
        int checkRowHeight =
            Math.Max(
                40,
                AppMetrics.TextHeight(AppTheme.NormalFont) + 16);
        int buttonRowHeight =
            Math.Max(
                AppMetrics.ButtonRowHeight,
                AppMetrics.ButtonHeight +
                UiDesignSystem.Spacing.Md +
                UiDesignSystem.Spacing.Xs);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, titleRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, checkRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, buttonRowHeight));
        var title = new Label {
            Text = isNewUser ? "New User" : "Edit User Account",
            Dock = DockStyle.Fill,
            Font = AppTheme.PageTitleFont,
            ForeColor = AppTheme.Primary,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0)
        };
        root.Controls.Add(title, 0, 0);
        root.SetColumnSpan(title, 2);
        var usernameField =
            HumanUi.Field("Username", username, true);
        root.Controls.Add(usernameField, 0, 1);
        root.SetColumnSpan(usernameField, 2);
        var passwordField =
            HumanUi.Field("Password", password, isNewUser);
        root.Controls.Add(passwordField, 0, 2);
        root.SetColumnSpan(passwordField, 2);
        var confirmField =
            HumanUi.Field("Confirm Password", confirmPassword);
        root.Controls.Add(confirmField, 0, 3);
        root.SetColumnSpan(confirmField, 2);
        root.Controls.Add(mustChangePassword, 0, 4);
        var buttons =
            HumanUi.ButtonRow(save, cancel);
        root.Controls.Add(buttons, 0, 5);
        root.SetColumnSpan(buttons, 2);
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
            (3 * fieldRowHeight) +
            checkRowHeight +
            buttonRowHeight +
            AppMetrics.SpacingSm;
        ClientSize =
            new Size(
                560,
                Math.Max(500, requiredClientHeight));
        AcceptButton = save;
        SetSaveShortcut(save);
        CancelButton = cancel;
        FormFlow.EnterMovesToNext(this);
        /* The window's own close (X, Alt+F4, Esc) is guarded the same way; programmatic closes are left alone. */
        FormClosing += (_, e) => {
            if (e.CloseReason == CloseReason.UserClosing)
                e.Cancel = !ConfirmDiscard();
        };
        Shown += (_, _) => username.Focus();
        Disposed += (_, _) => validator.Dispose();
    }
    /// <summary>every field this dialog edits, compared with the value it was loaded with. A password or confirmation left empty is unchanged; anything typed into either counts as unsaved work.</summary>
    bool IsDirty() =>
        !savedSuccessfully &&
        (username.Text != initialUsername ||
         password.Text.Length > 0 ||
         confirmPassword.Text.Length > 0 ||
         mustChangePassword.Checked != initialMustChangePassword);
    /// <summary>asks at most once for a close gesture. True when the caller may proceed with the close.</summary>
    bool ConfirmDiscard()
    {
        return ModernMessageDialog.ConfirmDiscardOnce(this, IsDirty, ref discardConfirmed);
    }
    void Save()
    {
        /* UX - a second Enter/click that was already queued must not save (or confirm) twice. */
        if (savedSuccessfully)
            return;
        var cleanUsername =
            username.Text.Trim();
        validator.Begin();
        validator.Required(username, UiText.Validation.Required);
        bool passwordBlank = string.IsNullOrWhiteSpace(password.Text);
        if (isNewUser)
            validator.Required(password, UiText.Validation.Required);
        if (passwordBlank)
        {
            /* A confirmation typed with no password to confirm. */
            if (!isNewUser && confirmPassword.Text.Length > 0)
                validator.Fail(password, "Enter the password first");
        }
        else
        {
            validator.Check(
                password,
                PasswordPolicy.IsValid(password.Text),
                PasswordPolicy.Requirement,
                () => PasswordPolicy.IsValid(password.Text));
            validator.Check(
                confirmPassword,
                password.Text == confirmPassword.Text,
                "Does not match",
                () => password.Text == confirmPassword.Text);
        }
        if (!validator.Finish())
            return;
        if (!isNewUser)
        {
            bool isChangingPassword = !string.IsNullOrWhiteSpace(password.Text);
            if (isChangingPassword)
            {
                if (!ModernMessageDialog.Confirm(
                        this,
                        $"Save account changes for {cleanUsername}?{Environment.NewLine}{Environment.NewLine}Password: Will be changed",
                        "Save Account"))
                    return;
            }
        }
        savedSuccessfully = true;
        DialogResult = DialogResult.OK;
        Close();
    }
}

