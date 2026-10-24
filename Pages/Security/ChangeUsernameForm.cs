using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Security;
public sealed class ChangeUsernameForm : ApplicationWindowForm
{
    readonly ModernTextBox newUsername = HumanUi.TextBox(false, 100);
    readonly ModernButton save = new() { Text = "Change Username", Dock = DockStyle.Fill };
    /* True from the moment the rename is sent until it returns. */
    bool savingUsername;
    /* UX - a missing username is marked on the field, not shown in a message box. */
    readonly FormValidator validator = new();
    public ChangeUsernameForm()
    {
        Text = "FMSC - Change Username";
        StartPosition = FormStartPosition.CenterScreen;
        /* The content needs 324 px (outer padding 58 + card padding 64 + rows 202); the form was 320, so the Change Username button was cut off at the bottom. */
        ClientSize = new Size(AppScale.Scale(560), AppScale.Scale(360));
        MinimumSize = SizeFromClientSize(
            new Size(AppScale.Scale(520), AppScale.Scale(330)));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        newUsername.Text = CurrentUser.Username;
        newUsername.PlaceholderText = "Enter new username";
        save.SetColors(AppTheme.Success);
        Disposed += (_, _) => validator.Dispose();
        /* this writes dbo.RenameOwnUsername, so a double click could rename twice (and the second attempt would race the first). */
        save.Click += async (_, _) => {
            if (!save.Enabled)
                return;
            save.Enabled = false;
            /* UX - the button itself says the write is under way. */
            string idleText = save.Text;
            save.Text = "Saving…";
            /* FormClosing reads this. */
            savingUsername = true;
            try
            {
                await SaveAsync();
            }
            finally
            {
                savingUsername = false;
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
            Padding = new Padding(32)
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
            RowCount = 4,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, titleRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, spacerRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, buttonRowHeight));
        root.Controls.Add(new Label {
            Text = "Change Username",
            Dock = DockStyle.Fill,
            Font = AppTheme.PageTitleFont,
            ForeColor = AppTheme.Primary,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0)
        }, 0, 0);
        root.Controls.Add(HumanUi.Field("New Username", newUsername, true), 0, 1);
        root.Controls.Add(save, 0, 3);
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
            fieldRowHeight +
            spacerRowHeight +
            buttonRowHeight +
            AppMetrics.SpacingLg;
        ClientSize =
            new Size(
                560,
                Math.Max(360, requiredClientHeight));
        AcceptButton = save;
        SetSaveShortcut(save);
        FormFlow.EnterMovesToNext(this);
        /* Block closing only while a write is running. */
        FormClosing += (_, e) => {
            if (e.CloseReason != CloseReason.UserClosing)
                return;
            /* A rename that has already been sent cannot be discarded. The close is refused until the write lands. */
            if (savingUsername)
            {
                e.Cancel = true;
                ModernMessageDialog.ShowWarning(
                    this,
                    "The username change is being saved. Wait for it to finish - a change that has already been sent cannot be discarded.",
                    "Username");
                return;
            }
        };
        Shown += (_, _) => {
            newUsername.Focus();
            newUsername.SelectAll();
        };
    }
    async Task SaveAsync()
    {
        var cleanUsername = newUsername.Text.Trim();
        validator.Begin();
        validator.Required(newUsername, UiText.Validation.Required);
        if (!validator.Finish())
            return;
        if (string.Equals(cleanUsername, CurrentUser.Username, StringComparison.Ordinal))
        {
            DialogResult = DialogResult.Cancel;
            return;
        }
        try
        {
            await DatabaseHelper.ExecuteUserAsync(
                "RenameOwnUsername",
                DatabaseHelper.P("@UserId", CurrentUser.UserId),
                DatabaseHelper.P("@NewUsername", cleanUsername));
            CurrentUser.Username = cleanUsername;
            /* The rename committed, so this close is not a discard. */
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            ModernMessageDialog.ShowWarning(this, DbMessage.UserMessage(ex), "Username");
        }
    }
}

