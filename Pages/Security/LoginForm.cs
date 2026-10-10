using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Security;
public sealed class LoginForm : ApplicationWindowForm
{
    readonly ModernTextBox user = HumanUi.TextBox(false, 100);
    readonly ModernTextBox pass = HumanUi.TextBox(false, 100);
    readonly ModernButton login = new() { Text = "Sign In", Dock = DockStyle.Fill };
    /* UX - a missing username or password is marked on the field. A WRONG password is a real refusal and still says so in a dialog. */
    readonly FormValidator validator = new();
    public LoginForm()
    {
        Text = "FMSC - Sign In";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(AppScale.Scale(760), AppScale.Scale(500));
        MinimumSize = new Size(AppScale.Scale(680), AppScale.Scale(460));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        pass.UseSystemPasswordChar = true;
        user.PlaceholderText = "Enter username";
        pass.PlaceholderText = "Enter password";
        login.SetColors(AppTheme.Primary);
        login.Click += Login_Click;
        var shell = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(AppScale.Scale(28, AppScale.SpacingScale)),
            Margin = Padding.Empty
        };
        shell.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 48F));
        shell.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 52F));
        var brand = new RoundedPanel {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Primary,
            BorderColor = AppTheme.Primary,
            ShowBorder = false,
            CornerRadius = AppTheme.CardRadius,
            Padding = new Padding(AppScale.Scale(28, AppScale.SpacingScale)),
            Margin = new Padding(0, 0, AppMetrics.SpacingMd, 0)
        };
        var logo = new PictureBox {
            Image = File.Exists(
                Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png"))
                    ? UiHelper.LoadImageUnlocked(
                        Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png"))
                    : null,
            SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, AppMetrics.SpacingXl)
        };
        UiHelper.DisposePictureImageOnDispose(logo);
        var brandLayout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        brandLayout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 26F));
        brandLayout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 25F));
        brandLayout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 17F));
        brandLayout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 32F));
        var schoolTitle = new Label {
            Text = "Fahim Model \nSchool && College",
            Dock = DockStyle.Fill,
            Font = AppTheme.PageTitleFont,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = false,
            AutoEllipsis = false,
            Tag = "ThemeOnPrimaryText",
            Padding = new Padding(0, 2, 0, 2)
        };
        var systemTitle = new Label {
            Text = "School Management System",
            Dock = DockStyle.Fill,
            Font = AppTheme.HeadingFont,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            AutoEllipsis = false,
            Tag = "ThemeOnPrimaryText",
            Padding = new Padding(0, 2, 0, 2)
        };
        var description = new Label {
            Text = "A clean, secure workspace for admissions, students, fees and school operations.",
            Dock = DockStyle.Fill,
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.Tint(Color.White, 0.18f),
            TextAlign = ContentAlignment.TopLeft,
            AutoSize = false,
            AutoEllipsis = false,
            Tag = "ThemeOnPrimaryText",
            Padding = new Padding(0, 4, 0, 0)
        };
        brandLayout.Controls.Add(logo, 0, 0);
        brandLayout.Controls.Add(schoolTitle, 0, 1);
        brandLayout.Controls.Add(systemTitle, 0, 2);
        brandLayout.Controls.Add(description, 0, 3);
        brand.Controls.Add(brandLayout);
        var card = new RoundedPanel {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            BorderColor = AppTheme.Border,
            ShowBorder = true,
            CornerRadius = AppTheme.CardRadius,
            Padding = new Padding(AppScale.Scale(34, AppScale.SpacingScale)),
            Margin = new Padding(AppMetrics.SpacingMd, 0, 0, 0)
        };
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppScale.Scale(62, AppScale.ControlSizeScale)));
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.SpacingXl));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                AppMetrics.ButtonHeight + AppMetrics.SpacingSm));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(
            new Label {
                Text = "Welcome back",
                Dock = DockStyle.Fill,
                Font = AppTheme.PageTitleFont,
                ForeColor = AppTheme.HeadingText,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true
            },
            0,
            0);
        root.Controls.Add(
            HumanUi.Field("Username", user, true),
            0,
            1);
        root.Controls.Add(
            HumanUi.Field("Password", pass, true),
            0,
            2);
        root.Controls.Add(
            login,
            0,
            4);
        root.Controls.Add(
            new JustifiedNote {
                Text = "Use your account credentials to continue.",
                Dock = DockStyle.Fill,
                Font = AppTheme.NormalFont,
                ForeColor = AppTheme.MutedText,
                Padding = new Padding(0, AppMetrics.SpacingXs, 0, 0)
            },
            0,
            5);
        card.Controls.Add(root);
        shell.Controls.Add(brand, 0, 0);
        shell.Controls.Add(card, 1, 0);
        Controls.Add(shell);
        FormClosed += (_, _) => logo.Image?.Dispose();
        AcceptButton = login;
        // Login must retain username -> Tab -> password -> Enter to sign in.
        // Do not opt this form into generic Enter-to-next-field navigation:
        // it consumes Password's Enter before the form's AcceptButton can submit.
        FormFlow.OrderTableFields(root);
        Shown += (_, _) => user.Focus();
        Disposed += (_, _) => validator.Dispose();
    }
    private sealed class JustifiedNote : Label
    {
        public JustifiedNote()
        {
            AutoSize = false;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            int available = ClientSize.Width - Padding.Horizontal;
            if (available <= 0) return;
            int lineHeight = TextRenderer.MeasureText(e.Graphics, "Ag", Font, Size.Empty, flags).Height;
            int space = TextRenderer.MeasureText(e.Graphics, " ", Font, Size.Empty, flags).Width;
            string[] words = Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int index = 0;
            int y = Padding.Top;
            while (index < words.Length)
            {
                int start = index;
                int width = 0;
                var widths = new List<int>();
                while (index < words.Length)
                {
                    int wordWidth = TextRenderer.MeasureText(e.Graphics, words[index], Font, Size.Empty, flags).Width;
                    int nextWidth = width + (index > start ? space : 0) + wordWidth;
                    if (index > start && nextWidth > available) break;
                    width = nextWidth;
                    widths.Add(wordWidth);
                    index++;
                }
                // Justify wrapped lines; keep the final line naturally left aligned.
                float gap = index < words.Length && widths.Count > 1
                    ? (available - widths.Sum()) / (float)(widths.Count - 1)
                    : space;
                float x = Padding.Left;
                for (int word = start; word < index; word++)
                {
                    TextRenderer.DrawText(e.Graphics, words[word], Font, new Point((int)Math.Round(x), y), ForeColor, flags);
                    x += widths[word - start] + gap;
                }
                y += lineHeight;
            }
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter && user.ContainsFocus)
        {
            pass.Focus();
            pass.SelectAll();
            return true;
        }
        if (keyData == Keys.Enter && pass.ContainsFocus)
        {
            if (login.Enabled) login.PerformClick();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    void Login_Click(object? s, EventArgs e)
    {
        /* UX - say what is missing, in the field that is missing it, before spending a database round trip on an attempt that cannot succeed. */
        if (!login.Enabled)
            return;
        validator.Begin();
        validator.Required(user, "Enter your username");
        validator.Check(pass, pass.Text.Length > 0, "Enter your password", () => pass.Text.Length > 0);
        if (!validator.Finish())
            return;
        login.Enabled = false;
        /* UX - the check below runs on this thread, so the button says what is happening and is repainted before it starts. */
        string idleText = login.Text;
        login.Text = "Signing in…";
        login.Refresh();
        try
        {
            if (!DatabaseHelper.TestConnection(out var dbMsg))
            {
                ModernMessageDialog.ShowError(this, dbMsg, "Database");
                return;
            }
            if (AuthenticationService.Authenticate(user.Text, pass.Text, out var msg))
            {
                DialogResult = DialogResult.OK;
                return;
            }
            ModernMessageDialog.ShowWarning(this, msg, "Login");
            pass.Focus();
            pass.SelectAll();
        }
        finally
        {
            if (!login.IsDisposed)
            {
                login.Text = idleText;
                login.Enabled = true;
            }
        }
    }
}

