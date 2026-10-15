
namespace FMSC_SMS.Core;

/// <summary>Also runs before login, so recovery never depends on a working database.</summary>
internal sealed class DatabaseConnectionForm : ApplicationWindowForm
{
    internal static bool IsConnectionProblem(Exception ex) =>
        ex is DatabaseRecoveryException or DatabaseCompatibilityException || ex is Microsoft.Data.SqlClient.SqlException sql &&
        (DatabaseHelper.IsConnectionFailure(sql) || sql.Number is 4060 or 911) ||
        ex.InnerException is not null && IsConnectionProblem(ex.InnerException);
    private readonly bool _startup;
    private readonly Label _message = new() { Dock = DockStyle.Top, AutoSize = true };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = false };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee };
    private readonly ModernButton _reconnect = new() { Text = "Reconnect", ThemeRole = "Primary" };
    private readonly ModernButton _restart = new() { Text = "Restart Connection", ThemeRole = "Secondary" };
    private readonly ModernButton _repair = new() { Text = "Repair Connection", ThemeRole = "Secondary" };
    private readonly ModernButton _close = new() { Text = UiText.Actions.Close, ThemeRole = "Secondary" };
    private readonly ModernTextBox _details = new() {
        Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Vertical
    };
    private bool _busy;
    private bool _canRestart = true;
    private bool _canRepair;

    internal DatabaseConnectionForm(bool startup)
    {
        _startup = startup;
        Text = "FMSC — School Records Connection";
        StartPosition = startup ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(AppScale.S(800), AppScale.S(440));
        Font = AppTheme.NormalFont;
        BackColor = AppTheme.PageBackground;
        AutoScaleMode = AutoScaleMode.None;
        int gap = UiDesignSystem.Spacing.Md;
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(UiDesignSystem.Spacing.Lg),
            ColumnCount = 1, RowCount = 5, BackColor = AppTheme.PageBackground
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.TextHeight(AppTheme.PageTitleFont) + gap));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight + gap * 2));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight + gap));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        var title = new Label {
            Text = "School records connection", Dock = DockStyle.Fill,
            Font = AppTheme.PageTitleFont, ForeColor = AppTheme.HeadingText,
            TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty
        };
        var connectionCard = HumanUi.Card("Connection status", accent: AppTheme.Primary);
        var state = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = AppTheme.Surface, Margin = Padding.Empty
        };
        state.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        state.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.TextHeight(AppTheme.ButtonFont) + gap));
        state.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        state.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.S(12)));
        _message.Text = "Please wait while FMSC connects to your saved school records.";
        _message.ForeColor = AppTheme.Text;
        _message.UseMnemonic = false;
        _message.Margin = new Padding(0, 0, 0, gap);
        _status.Text = "Preparing connection…";
        _status.Font = AppTheme.ButtonFont;
        _status.ForeColor = AppTheme.Primary;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Margin = Padding.Empty;
        _progress.Margin = Padding.Empty;
        state.Controls.Add(_status, 0, 0);
        // Keep the explanation readable when DPI or the working area leaves less room.
        var explanation = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        explanation.Controls.Add(_message);
        explanation.SizeChanged += (_, _) => _message.MaximumSize = new Size(
            Math.Max(1, explanation.ClientSize.Width - SystemInformation.VerticalScrollBarWidth), 0);
        state.Controls.Add(explanation, 0, 1);
        state.Controls.Add(_progress, 0, 2);
        connectionCard.Content.Controls.Add(state);
        var actionHost = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1,
            Padding = new Padding(0, gap, 0, 0), Margin = Padding.Empty
        };
        actionHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.None, WrapContents = false, Padding = Padding.Empty, Margin = Padding.Empty
        };
        foreach (var button in new[] { _reconnect, _restart, _repair })
        {
            button.Height = AppMetrics.ButtonHeight;
            button.Width = TextRenderer.MeasureText(button.Text, button.Font).Width + AppScale.S(36);
            button.Margin = new Padding(UiDesignSystem.Spacing.Sm / 2, 0, UiDesignSystem.Spacing.Sm / 2, 0);
            actions.Controls.Add(button);
        }
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AppScale.S(100)));
        var detailsLink = new LinkLabel {
            Text = "Show connection details", Dock = DockStyle.Fill,
            LinkColor = AppTheme.Primary, ActiveLinkColor = AppTheme.PrimaryHover, VisitedLinkColor = AppTheme.Primary,
            TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty
        };
        _close.Dock = DockStyle.Top;
        _close.Height = AppMetrics.ButtonHeight;
        _close.Margin = Padding.Empty;
        footer.Controls.Add(detailsLink, 0, 0);
        footer.Controls.Add(_close, 1, 0);
        var detailsCard = HumanUi.Card("Connection details", accent: AppTheme.Primary);
        detailsCard.Visible = false;
        _details.Font = AppTheme.NormalFont;
        detailsCard.Content.Controls.Add(_details);
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(connectionCard, 0, 1);
        actionHost.Controls.Add(actions, 0, 0);
        root.Controls.Add(actionHost, 0, 2);
        root.Controls.Add(footer, 0, 3);
        root.Controls.Add(detailsCard, 0, 4);
        Controls.Add(root);
        AcceptButton = _reconnect;
        CancelButton = _close;
        _reconnect.Click += async (_, _) => await ConnectAsync(false);
        _restart.Click += async (_, _) => await ConnectAsync(true);
#if FMSC_LEVEL2
        // This fixture may reconnect, but must never launch the school installer.
        _repair.Visible = false;
#endif
        _repair.Click += async (_, _) => await RepairDependenciesAsync();
        _repair.Visible = false;
        _close.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        bool detailsExpanded = false;
        detailsLink.LinkClicked += (_, _) => {
            detailsExpanded = !detailsExpanded;
            root.SuspendLayout();
            detailsCard.Visible = detailsExpanded;
            root.RowStyles[4].Height = detailsExpanded ? AppScale.S(200) : 0;
            ClientSize = new Size(ClientSize.Width, AppScale.S(detailsExpanded ? 640 : 440));
            detailsLink.Text = detailsExpanded ? "Hide connection details" : "Show connection details";
            root.ResumeLayout(true);
            var fitted = WorkingAreaLayout.Fit(Bounds, MinimumSize, Screen.FromControl(this).WorkingArea);
            MinimumSize = fitted.MinimumSize;
            Bounds = fitted.Bounds;
        };
        FormClosing += (_, e) => { if (_busy) e.Cancel = true; };
        Shown += async (_, _) => await ConnectAsync(false);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (var button in new[] { _reconnect, _restart, _repair, _close }) button.Enabled = !busy;
        _restart.Enabled = !busy && _canRestart;
        _repair.Enabled = !busy && _canRepair;
        _progress.Visible = busy;
        UseWaitCursor = busy;
    }

    private async Task ConnectAsync(bool restart)
    {
        if (_busy) return;
        SetBusy(true);
        _message.Text = restart ? "Restarting the connection. Your saved school records will be kept." : "Reconnecting to your saved school records…";
        _status.Text = restart ? "Restarting connection…" : "Checking connection…";
        _details.Text = ConnectionDetails(_status.Text);
        try
        {
            var result = await Task.Run(() => {
                DatabaseHelper.RefreshDatabaseConnection(restart);
                bool connected = DatabaseHelper.TestConnection(out string message, out var failure);
                return (connected, message, failure);
            });
            if (IsDisposed) return;
            if (result.connected)
            {
                _canRestart = true;
                _canRepair = false;
                _repair.Visible = false;
                _message.Text = "Your school records are connected and ready.";
                _status.Text = "Connection successful";
                _details.Text = ConnectionDetails(string.Empty);
                if (_startup) { SetBusy(false); DialogResult = DialogResult.OK; Close(); }
            }
            else ShowFailure(result.message, result.failure);
        }
        catch (Exception ex)
        {
            AppExceptionHandler.LogNonFatal(ex, "School records connection recovery");
            if (!IsDisposed) ShowFailure(ex is DatabaseRecoveryException recovery ? recovery.Message : DbMessage.UserMessage(ex),
                ex);
        }
        finally { if (!IsDisposed) SetBusy(false); }
    }

    private void ShowFailure(string message, Exception? failure = null)
    {
        var guidance = DatabaseConnectionGuidance.Create(message, failure,
#if FMSC_LEVEL2
            File.Exists(Path.Combine(Level2Target.Root,"database","fixture.mdf")));
#else
            File.Exists(DatabaseHelper.DatabaseFile));
#endif
        _canRestart = guidance.CanRestart;
        _canRepair =
#if FMSC_LEVEL2
            false;
#else
            guidance.CanRepair;
#endif
        _repair.Visible = _canRepair;
        _status.Text = guidance.Status;
        _message.Text = guidance.Message;
        _details.Text = ConnectionDetails(message + (failure is null ? "" : "\r\n\r\n" + failure));
    }

    private async Task RepairDependenciesAsync()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var progress = new Progress<string>(message => { if (!IsDisposed) { _status.Text = "Repairing SQL dependencies..."; _message.Text = message; } });
            int result = await SqlDependencyRepair.RepairAsync(progress);
            if (IsDisposed) return;
            _details.Text = ConnectionDetails($"SQL dependency repair returned code {result}.");
            if (result == 3010)
            {
                _canRestart = false;
                _canRepair = false;
                _repair.Visible = false;
                _status.Text = "Windows restart required";
                _message.Text = "SQL Server LocalDB was installed. Restart Windows, then open FMSC again.";
            }
            else if (result == 0)
            { SetBusy(false); await ConnectAsync(false); }
            else
            { _status.Text = "SQL repair did not complete"; _message.Text = $"SQL dependency repair returned code {result}. Your school records have not been replaced. Try again or contact your administrator."; }
        }
        catch (Exception ex)
        {
            AppExceptionHandler.LogNonFatal(ex, "SQL dependency download and repair");
            if (!IsDisposed)
            {
                _status.Text = "SQL repair did not complete";
                _message.Text = ex.Message;
                _details.Text = ConnectionDetails(ex.ToString());
            }
        }
        finally { if (!IsDisposed) SetBusy(false); }
    }

    private static string ConnectionDetails(string problem)
    {
        string endpoint = LocalDatabaseEngine.CurrentEndpoint ?? "Unavailable";
        return $"{problem}\r\n\r\nRequired database schema version: {DatabaseHelper.RequiredDatabaseSchemaVersion}\r\nSchool data: {DatabaseHelper.DatabaseFile}\r\nConnection: {endpoint}\r\nDiagnostic log: {AppExceptionHandler.LogPath}";
    }

}


