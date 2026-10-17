using System.Security.Cryptography;
using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Security;
public sealed class ChangePasswordForm : ApplicationWindowForm
{
    readonly ModernTextBox p1 = HumanUi.TextBox(false, 100);
    readonly ModernTextBox p2 = HumanUi.TextBox(false, 100);
    readonly ModernButton save = new() { Text = "Change Password", Dock = DockStyle.Fill };
    /* this form had no close guard at all, while its sibling ChangeUsernameForm has had one since: a typed password was thrown away by the window's close button with no prompt. */
    bool savedSuccessfully;
    bool discardConfirmed;
    /* (, same rule) - the hash and the write cannot be cancelled once they have started. */
    bool savingPassword;
    /* UX - a missing or mismatched password is marked on the field, not shown in a message box. */
    readonly FormValidator validator = new();
    public ChangePasswordForm()
    {
        Text = "FMSC - Change Password";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(AppScale.Scale(560), AppScale.Scale(430));
        MinimumSize = SizeFromClientSize(
            new Size(AppScale.Scale(520), AppScale.Scale(400)));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        p1.UseSystemPasswordChar = true;
        p2.UseSystemPasswordChar = true;
        p1.PlaceholderText = "Enter new password";
        p2.PlaceholderText = "Confirm new password";
        save.SetColors(AppTheme.Success);
        /* this writes dbo.ChangeOwnPassword after a 100,000-iteration PBKDF2 hash, so a double click would run the whole thing twice. */
        save.Click += async (_, _) => {
            if (!save.Enabled)
                return;
            save.Enabled = false;
            /* UX - the button itself says the write is under way (the hash alone takes a moment). */
            string idleText = save.Text;
            save.Text = "Saving…";
            /* FormClosing reads this. */
            savingPassword = true;
            try
            {
                await SaveAsync();
            }
            finally
            {
                savingPassword = false;
                if (!save.IsDisposed)
                {
                    save.Text = idleText;
                    save.Enabled = true;
                }
            }
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
            Math.Max(14, AppMetrics.SpacingMd);
        int buttonRowHeight =
            Math.Max(
                48,
                AppMetrics.ButtonHeight + AppMetrics.SpacingSm);
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
        root.Controls.Add(new Label {
            Text = "Change Password",
            Dock = DockStyle.Fill,
            Font = AppTheme.PageTitleFont,
            ForeColor = AppTheme.Primary,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0)
        }, 0, 0);
        root.Controls.Add(HumanUi.Field("New Password", p1, true), 0, 1);
        root.Controls.Add(HumanUi.Field("Confirm Password", p2, true), 0, 2);
        root.Controls.Add(save, 0, 4);
        card.Controls.Add(root);
        var outer = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(
                AppScale.Scale(38, AppScale.SpacingScale),
                AppScale.Scale(34, AppScale.SpacingScale),
                AppScale.Scale(38, AppScale.SpacingScale),
                AppMetrics.SpacingXxl),
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
            AppMetrics.SpacingLg;
        ClientSize =
            new Size(
                AppScale.Scale(560),
                Math.Max(AppScale.Scale(430), requiredClientHeight));
        AcceptButton = save;
        SetSaveShortcut(save);
        FormFlow.EnterMovesToNext(this);
        Shown += (_, _) => p1.Focus();
        Disposed += (_, _) => validator.Dispose();
        /* The window's close is the only discard gesture on this form, so it is the one that is guarded; programmatic closes are left alone. */
        FormClosing += (_, e) => {
            if (e.CloseReason != CloseReason.UserClosing)
                return;
            /* A password change that has already been sent cannot be discarded, so the close is refused until it lands. */
            if (savingPassword)
            {
                e.Cancel = true;
                ModernMessageDialog.ShowWarning(
                    this,
                    "The password change is being saved. Wait for it to finish - a change that has already been sent cannot be discarded.",
                    "Password");
                return;
            }
            e.Cancel = !ConfirmDiscard();
        };
    }
    /// <summary>dirty means "something typed and not saved yet".</summary>
    bool IsDirty() =>
        !savedSuccessfully &&
        !savingPassword &&
        DialogResult == DialogResult.None &&
        (p1.Text.Length > 0 || p2.Text.Length > 0);
    /// <summary>asks at most once per close gesture, and never about a write that is already running.</summary>
    bool ConfirmDiscard()
    {
        return ModernMessageDialog.ConfirmDiscardOnce(this, IsDirty, ref discardConfirmed);
    }
    async Task SaveAsync()
    {
        validator.Begin();
        validator.Required(p1, UiText.Validation.Required);
        if (p1.Text.Length > 0)
        {
            validator.Check(
                p1,
                PasswordPolicy.IsValid(p1.Text),
                PasswordPolicy.Requirement,
                () => PasswordPolicy.IsValid(p1.Text));
            validator.Check(
                p2,
                p1.Text == p2.Text,
                "Does not match",
                () => p1.Text == p2.Text);
        }
        if (!validator.Finish())
            return;
        try
        {
            using var rng = RandomNumberGenerator.Create();
            var salt = new byte[32];
            rng.GetBytes(salt);
            var hash = Rfc2898DeriveBytes.Pbkdf2(p1.Text, salt, 100_000, HashAlgorithmName.SHA512, 64);
            await DatabaseHelper.ExecuteUserAsync(
                "ChangeOwnPassword",
                DatabaseHelper.P("@UserId", CurrentUser.UserId),
                DatabaseHelper.P("@PasswordHash", hash),
                DatabaseHelper.P("@PasswordSalt", salt));
            CurrentUser.MustChangePassword = false;
            /* The change committed, so this close is not a discard. */
            savedSuccessfully = true;
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            ModernMessageDialog.ShowWarning(this, DbMessage.UserMessage(ex), "Password");
        }
    }
}

