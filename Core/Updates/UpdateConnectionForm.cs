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
    internal PreparedUpdate? InstallationRequested { get; private set; }
    private PreparedUpdate? _prepared;
    private VerifiedUpdate? _release;
    private UpdateDownloadChoice _method;
    private static string InstalledVersion => typeof(UpdateConnectionForm).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().First().InformationalVersion.Split('+')[0];
    private readonly CancellationTokenSource _lifetime = new();

    internal UpdateConnectionForm()
    {
        Text = "FMSC — Check for Updates";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(AppScale.S(760), AppScale.S(445));
        Font = AppTheme.NormalFont;
        BackColor = AppTheme.PageBackground;

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1, RowCount = 5,
            Padding = new Padding(AppScale.S(20)),
            BackColor = AppTheme.PageBackground
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.S(95)));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var intro = new Label
        {
            Text = "Installed version: " + InstalledVersion + "\nAuthorize this computer to check the private FMSC update repository. " +
                "No GitHub account is needed at the school.",
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = AppTheme.Text,
            Margin = new Padding(0, 0, 0, AppScale.S(10))
        };
        var instructions = new Label
        {
            Text = "1. Generate a key.  2. Send ONLY the public key below to the software owner.  " +
                "3. The owner approves it as a read-only GitHub deploy key.  " +
                "4. Click Check for Updates.",
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = AppTheme.MutedText,
            Margin = new Padding(0, 0, 0, AppScale.S(8))
        };
        _publicKey = new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Horizontal,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, AppScale.S(9)),
            BackColor = AppTheme.Surface,
            ForeColor = AppTheme.Text,
            Margin = new Padding(0, 0, 0, AppScale.S(10))
        };
        _status = new Label
        {
            Text = "No update authorization has been prepared.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = AppTheme.MutedText,
            Margin = new Padding(0, 0, 0, AppScale.S(8))
        };
        _generate = new ModernButton { Text = "Generate key", ThemeRole = "Secondary",
            Width = AppScale.S(140), Height = AppMetrics.ButtonHeight };
        _copy = new ModernButton { Text = "Copy public key", ThemeRole = "Secondary",
            Width = AppScale.S(155), Height = AppMetrics.ButtonHeight };
        _check = new ModernButton { Text = "Check for Updates", ThemeRole = "Primary",
            Width = AppScale.S(175), Height = AppMetrics.ButtonHeight };
        _download = new ModernButton { Text = "Download Update", ThemeRole = "Primary",
            Width = AppScale.S(175), Height = AppMetrics.ButtonHeight, Enabled = false };
        _download.Click += async (_, _) => await DownloadAsync();
        _install = new ModernButton { Text = "Install Update", ThemeRole = "Primary",
            Width = AppScale.S(150), Height = AppMetrics.ButtonHeight, Enabled = false };
        _install.Click += (_, _) => {
            if (_prepared is null) return;
            InstallationRequested = _prepared;
            DialogResult = DialogResult.Yes;
            Close();
        };
        var close = new ModernButton { Text = "Close", ThemeRole = "Secondary",
            Width = AppScale.S(100), Height = AppMetrics.ButtonHeight, DialogResult = DialogResult.OK };
        _generate.Click += (_, _) => GenerateKey();
        _copy.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_publicKey.Text)) return;
            try
            {
                Clipboard.SetText(_publicKey.Text);
                _status.Text = "Public key copied. Send this public key only, never a private key.";
            }
            catch (ExternalException)
            {
                _status.Text = "Clipboard unavailable. Select and copy the public key manually.";
            }
        };
        _check.Click += async (_, _) => await CheckNowAsync();

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty
        };
        var later = new ModernButton { Text = "Remind Me Later", ThemeRole = "Secondary",
            Width = AppScale.S(160), Height = AppMetrics.ButtonHeight, DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange(new Control[] { _generate, _copy, _check, _download, _install, later, close });
        content.Controls.Add(intro, 0, 0);
        content.Controls.Add(instructions, 0, 1);
        content.Controls.Add(_publicKey, 0, 2);
        content.Controls.Add(_status, 0, 3);
        content.Controls.Add(actions, 0, 4);
        Controls.Add(content);
        AcceptButton = close;
        CancelButton = close;
        FormClosing += (_, _) => _lifetime.Cancel();
        RefreshAuthorization();
    }

    private void RefreshAuthorization()
    {
        bool enrolled = UpdateDeviceKey.IsEnrolled;
        _generate.Enabled = !enrolled;
        _check.Enabled = enrolled;
        _copy.Enabled = enrolled;
        if (!enrolled)
        {
            _publicKey.Text = "";
            _status.Text = "Generate a new key here; the private half stays protected on this computer.";
            return;
        }
        try
        {
            _publicKey.Text = UpdateDeviceKey.GetPublicKey();
            _status.Text =
                "Authorization key prepared. Ask the software owner to approve " +
                "this public key in FMSC-Updates → Settings → Deploy keys (READ ONLY).";
        }
        catch (Exception error) when (error is CryptographicException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _publicKey.Text = "";
            _copy.Enabled = false;
            _check.Enabled = false;
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
        try
        {
            await using var session = await UpdateGitSession.OpenAsync(_lifetime.Token);
            if (!await session.HasStableFeedAsync(_lifetime.Token))
            {
                _status.Text = "Computer authorized. No approved update has been published yet.";
                return;
            }
            var release = await UpdateReleasePreparer.ReadLatestAsync(session, _lifetime.Token);
            if (IsDisposed || Disposing) return;
            if (release is null) return;
            _method = await Task.Run(() => UpdateReleasePreparer.Choose(release, InstalledVersion,
                int.Parse(DatabaseHelper.RequiredDatabaseSchemaVersion), AppContext.BaseDirectory), _lifetime.Token);
            if (IsDisposed || Disposing) return;
            _release = release;
            var archive = _method == UpdateDownloadChoice.Incremental ? release.Manifest.Incremental! : release.Manifest.Full;
            _status.Text = "Latest version: " + release.Manifest.Version + "\n" +
                (_method == UpdateDownloadChoice.NotNewer ? "FMSC is up to date." :
                 _method == UpdateDownloadChoice.RequiresDatabaseInstaller ?
                 "Update available — Full package; database migration will run through the installer after FMSC closes. " +
                    (archive.Length / 1048576d).ToString("N1") + " MB." :
                 "Update available — " + _method + ", approximately " + (archive.Length / 1048576d).ToString("N1") + " MB.");
            if (_method == UpdateDownloadChoice.RequiresDatabaseInstaller) _method = UpdateDownloadChoice.Full;
            _download.Enabled = _method is UpdateDownloadChoice.Full or UpdateDownloadChoice.Incremental;
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
                _check.Enabled = UpdateDeviceKey.IsEnrolled;
        }
    }

    private async Task DownloadAsync()
    {
        if (_release is null) return;
        var token = _lifetime.Token;
        _download.Enabled = _check.Enabled = false;
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
        finally { if (!IsDisposed && !Disposing) _check.Enabled = true; }
    }
}
