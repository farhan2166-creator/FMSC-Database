using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FMSC_SMS.Core;

namespace FMSC_SMS.Core.Updates;

/// <summary>
/// School-facing manual private Git update checking and isolated download.
/// No installation or SQL operation occurs here, or background check at startup.
/// A school receives authorization only after the repository owner adds this
/// computer's public key as a READ-ONLY deploy key in FMSC-Updates.
/// </summary>
internal sealed class UpdateConnectionForm : ApplicationWindowForm
{
    private readonly TextBox _publicKey;
    private readonly Label _status;
    private readonly ModernButton _generate;
    private readonly ModernButton _copy;
    private readonly ModernButton _check;
    private readonly ModernButton _download;
    private readonly ModernButton _install;
    internal event Func<PreparedUpdate, bool>? InstallRequested;
    private bool _installRequested;
    private readonly Label _latestVersion;
    private readonly Label _downloadDetails;
    private readonly Label _accessStatus;
    private readonly Panel _authorizationPanel;
    private readonly ProgressBar _progress;
    private readonly Func<(bool Enrolled, string PublicKey)> _authorization;
    private bool _hasAuthorization;
    private PreparedUpdate? _prepared;
    private VerifiedUpdate? _release;
    private UpdateDownloadChoice _method;
    private static string InstalledVersion => typeof(UpdateConnectionForm).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().First().InformationalVersion.Split('+')[0];
    private readonly CancellationTokenSource _lifetime = new();

    internal UpdateConnectionForm(Func<(bool Enrolled, string PublicKey)>? authorization = null)
    {
        _authorization = authorization ?? (() => (UpdateDeviceKey.IsEnrolled,
            UpdateDeviceKey.IsEnrolled ? UpdateDeviceKey.GetPublicKey() : ""));
        Text = "FMSC — Check for Updates";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(AppScale.S(780), AppScale.S(540));
        Font = AppTheme.NormalFont;
        BackColor = AppTheme.PageBackground;
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7,
            Padding = new Padding(AppScale.S(20)), BackColor = AppTheme.PageBackground };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 7; row++)
            content.RowStyles.Add(new RowStyle(row == 4 ? SizeType.Percent : SizeType.AutoSize, row == 4 ? 100 : 0));
        var heading = new Label { Text = "Check for Updates", Font = AppTheme.HeadingFont,
            AutoSize = true, ForeColor = AppTheme.Text, Margin = new Padding(0, 0, 0, AppScale.S(12)) };
        var summary = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2,
            Padding = new Padding(AppScale.S(12)), BackColor = AppTheme.Surface, Margin = new Padding(0, 0, 0, AppScale.S(12)) };
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        summary.Controls.Add(new Label { Text = "Installed version", AutoSize = true, ForeColor = AppTheme.MutedText }, 0, 0);
        summary.Controls.Add(new Label { Text = "Latest version", AutoSize = true, ForeColor = AppTheme.MutedText }, 1, 0);
        summary.Controls.Add(new Label { Text = InstalledVersion, AutoSize = true, Font = AppTheme.BodyBoldFont,
            ForeColor = AppTheme.Text, Margin = new Padding(3, 4, 3, 10) }, 0, 1);
        _latestVersion = new Label { Text = "Not checked", AutoSize = true, Font = AppTheme.BodyBoldFont,
            ForeColor = AppTheme.Text, Margin = new Padding(3, 4, 3, 10) };
        summary.Controls.Add(_latestVersion, 1, 1);
        _downloadDetails = new Label { Text = "Check for an approved update when you are ready.", AutoSize = true,
            Dock = DockStyle.Fill, ForeColor = AppTheme.MutedText };
        summary.Controls.Add(_downloadDetails, 0, 2); summary.SetColumnSpan(_downloadDetails, 2);
        var access = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        var accessButton = Button("Computer access", 150, "Secondary");
        _accessStatus = new Label { AutoSize = true, ForeColor = AppTheme.MutedText, Margin = new Padding(8, 10, 0, 0) };
        access.Controls.AddRange(new Control[] { accessButton, _accessStatus });
        _authorizationPanel = new Panel { Dock = DockStyle.Top, Height = AppScale.S(175), Margin = new Padding(0, 8, 0, 8) };
        var enrollment = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        enrollment.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.S(48)));
        enrollment.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        enrollment.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        enrollment.Controls.Add(new Label { Text = "Generate a key and send its public half to the software owner. " +
            "The owner authorizes this computer for read-only access. No school GitHub account is needed.",
            Dock = DockStyle.Fill, ForeColor = AppTheme.MutedText }, 0, 0);
        _publicKey = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true,
            ScrollBars = ScrollBars.Vertical, WordWrap = true, Font = AppTheme.SmallFont,
            BackColor = AppTheme.Surface, ForeColor = AppTheme.Text };
        enrollment.Controls.Add(_publicKey, 0, 1);
        var keyActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        _generate = Button("Generate key", 140, "Secondary");
        _copy = Button("Copy public key", 155, "Secondary");
        keyActions.Controls.AddRange(new Control[] { _generate, _copy });
        enrollment.Controls.Add(keyActions, 0, 2);
        _authorizationPanel.Controls.Add(enrollment);
        accessButton.Click += (_, _) => {
            _authorizationPanel.Visible = !_authorizationPanel.Visible;
            ClientSize = new Size(AppScale.S(780), AppScale.S(_authorizationPanel.Visible ? 540 : 365));
        };
        _status = new Label { AutoSize = false, Dock = DockStyle.Fill, ForeColor = AppTheme.Text,
            Margin = new Padding(0, 12, 0, 8), TextAlign = ContentAlignment.TopLeft };
        _progress = new ProgressBar { Dock = DockStyle.Top, Height = AppScale.S(6), Style = ProgressBarStyle.Marquee,
            Visible = false, Margin = new Padding(0, 0, 0, 12) };
        _check = Button("Check for Updates", 175, "Primary");
        _download = Button("Download Update", 170, "Primary"); _download.Enabled = false;
        _install = Button("Install Update", 150, "Primary"); _install.Enabled = false;
        _install.Click += (_, _) => {
            if (_prepared is null || _installRequested) return;
            _installRequested = true; _install.Enabled = false;
            try { InstallRequested?.Invoke(_prepared); }
            finally {
                if (!IsDisposed && !Disposing) { _installRequested = false; _install.Enabled = _prepared is not null; }
            }
        };
        var later = Button("Remind Me Later", 160, "Secondary");
        var close = Button("Close", 100, "Secondary");
        // This is a modeless window: DialogResult alone never closes it.
        later.Click += (_, _) => Close();
        close.Click += (_, _) => Close();
        _generate.Click += (_, _) => GenerateKey();
        _copy.Click += (_, _) => {
            if (string.IsNullOrWhiteSpace(_publicKey.Text)) return;
            try { Clipboard.SetText(_publicKey.Text); _status.Text = "Public key copied."; }
            catch (ExternalException) { _status.Text = "Clipboard unavailable. Select and copy the public key manually."; }
        };
        _check.Click += async (_, _) => await CheckNowAsync();
        _download.Click += async (_, _) => await DownloadAsync();
        var actions = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var updateActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        // Only the action relevant to the current state is shown in this slot.
        updateActions.Controls.AddRange(new Control[] { _check, _download, _install });
        _download.Visible = _install.Visible = false;
        var dismissActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        dismissActions.Controls.AddRange(new Control[] { later, close });
        actions.Controls.Add(updateActions, 0, 0); actions.Controls.Add(dismissActions, 1, 0);
        content.Controls.Add(heading, 0, 0); content.Controls.Add(summary, 0, 1);
        content.Controls.Add(access, 0, 2); content.Controls.Add(_authorizationPanel, 0, 3);
        content.Controls.Add(_status, 0, 4); content.Controls.Add(_progress, 0, 5); content.Controls.Add(actions, 0, 6);
        Controls.Add(content);
        AcceptButton = _check; CancelButton = close;
        FormClosing += (_, _) => _lifetime.Cancel();
        RefreshAuthorization();
    }

    private static ModernButton Button(string text, int width, string role) => new() {
        Text = text, ThemeRole = role, Width = AppScale.S(width), Height = AppMetrics.ButtonHeight,
        Margin = new Padding(0, 0, AppScale.S(8), 0) };
    private void RefreshAuthorization()
    {
        try
        {
            var state = _authorization();
            _hasAuthorization = state.Enrolled;
            _generate.Enabled = !state.Enrolled;
            _generate.Visible = !state.Enrolled;
            _check.Enabled = _copy.Enabled = state.Enrolled;
            _publicKey.Text = state.PublicKey;
            _authorizationPanel.Visible = !state.Enrolled;
            ClientSize = new Size(AppScale.S(780), AppScale.S(state.Enrolled ? 365 : 540));
            _accessStatus.Text = state.Enrolled ? "Computer key ready" : "Authorization required";
            _status.Text = state.Enrolled ? "Select Check for Updates to contact the private update repository." :
                "Authorize this computer before checking for updates.";
        }
        catch (Exception error) when (error is CryptographicException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _publicKey.Text = "";
            _copy.Enabled = false;
            _check.Enabled = false;
            _hasAuthorization = false;
            _status.Text = "This Windows user's protected authorization could not be read. " +
                "Contact the software owner; no key was replaced. " + error.GetType().Name;
        }
    }

    private void GenerateKey()
    {
        try
        {
            _ = UpdateDeviceKey.GenerateEnrollment();
            RefreshAuthorization();
        }
        catch (Exception error) when (error is CryptographicException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _status.Text = "Unable to prepare this computer's update authorization: " + error.Message;
        }
    }

    private async Task CheckNowAsync()
    {
        _check.Enabled = false;
        _generate.Enabled = false;
        _download.Enabled = false;
        _release = null;
        _prepared = null;
        _install.Enabled = false;
        _status.Text = "Checking the private update repository without interrupting FMSC...";
        _progress.Visible = true;
        try
        {
            await using var session = await UpdateGitSession.OpenAsync(_lifetime.Token);
            if (!await session.HasStableFeedAsync(_lifetime.Token))
            {
                _status.Text = "Computer authorized. No approved update has been published yet.";
                _accessStatus.Text = "Computer authorized";
                _latestVersion.Text = "No release published";
                return;
            }
            var release = await UpdateReleasePreparer.ReadLatestAsync(session, _lifetime.Token);
            if (IsDisposed || Disposing) return;
            if (release is null) return;
            _method = await Task.Run(() => UpdateReleasePreparer.Choose(release, InstalledVersion,
                int.Parse(DatabaseHelper.RequiredDatabaseSchemaVersion), AppContext.BaseDirectory), _lifetime.Token);
            if (IsDisposed || Disposing) return;
            _release = release;
            _latestVersion.Text = release.Manifest.Version;
            _accessStatus.Text = "Computer authorized";
            var archive = _method == UpdateDownloadChoice.Incremental ? release.Manifest.Incremental! : release.Manifest.Full;
            _status.Text = "Latest version: " + release.Manifest.Version + "\n" +
                (_method == UpdateDownloadChoice.NotNewer ? "FMSC is up to date." :
                 _method == UpdateDownloadChoice.RequiresDatabaseInstaller ?
                 "Update available — Full package; database migration will run through the installer after FMSC closes. " +
                    (archive.Length / 1048576d).ToString("N1") + " MB." :
                 "Update available — " + _method + ", approximately " + (archive.Length / 1048576d).ToString("N1") + " MB.");
            if (_method == UpdateDownloadChoice.RequiresDatabaseInstaller) _method = UpdateDownloadChoice.Full;
            _download.Enabled = _method is UpdateDownloadChoice.Full or UpdateDownloadChoice.Incremental;
            _download.Visible = _download.Enabled;
            _install.Visible = false;
            _downloadDetails.Text = _download.Enabled ? _method + " · approximately " +
                (archive.Length / 1048576d).ToString("N1") + " MB" : "No download required";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Closing this window cancels the network check. No install is running.
        }
        catch (Exception error) when (error is CryptographicException or IOException or UnauthorizedAccessException
                                         or InvalidDataException or JsonException or UpdateGitCommandException
                                         or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            if (!IsDisposed && !Disposing)
                _status.Text = "Unable to check updates safely: " + error.Message;
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            { _check.Enabled = _hasAuthorization; _progress.Visible = false; }
        }
    }

    private async Task DownloadAsync()
    {
        if (_release is null) return;
        var token = _lifetime.Token;
        _download.Enabled = _check.Enabled = false;
        _progress.Visible = true;
        var progress = new Progress<string>(message => { if (!IsDisposed && !Disposing) _status.Text = message; });
        try
        {
            var release = _release;
            string root = Path.Combine(UpdateDeviceKey.StateDirectory, "prepared-" + Guid.NewGuid().ToString("N"));
            var prepared = await Task.Run(async () =>
            {
                await using var session = await UpdateGitSession.OpenAsync(token);
                return await UpdateReleasePreparer.PrepareAsync(release, session, _method,
                    AppContext.BaseDirectory, root, token, progress);
            }, token);
            await Task.Run(() => UpdateInstallerHandoff.Validate(prepared), token);
            if (!IsDisposed && !Disposing)
            {
                _prepared = prepared;
                _install.Enabled = true;
                _install.Visible = true;
                _download.Visible = false;
                _downloadDetails.Text = prepared.Method + " · downloaded and verified";
                _status.Text = "Update and installer files verified. You can continue working. " +
                    "Choose Install Update when ready to close FMSC safely.";
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or UpdateGitCommandException or System.ComponentModel.Win32Exception)
        {
            if (!IsDisposed && !Disposing) { _status.Text = "Update could not be prepared: " + error.Message; _download.Enabled = true; }
        }
        finally { if (!IsDisposed && !Disposing) { _check.Enabled = true; _progress.Visible = false; } }
    }
}
