using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FMSC_SMS.Core;

namespace FMSC_SMS.Core.Updates;

/// <summary>
/// School-facing manual private Git Update Connection. No download, installation or SQL
/// operation occurs here; nothing is run in the background at startup.
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
        ClientSize = new Size(AppScale.S(680), AppScale.S(365));
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
            Text = "Authorize this computer to check the private FMSC update repository. " +
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
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty
        };
        actions.Controls.AddRange(new Control[] { _generate, _copy, _check, close });
        content.Controls.Add(intro, 0, 0);
        content.Controls.Add(instructions, 0, 1);
        content.Controls.Add(_publicKey, 0, 2);
        content.Controls.Add(_status, 0, 3);
        content.Controls.Add(actions, 0, 4);
        Controls.Add(content);
        AcceptButton = close;
        CancelButton = close;
        FormClosing += (_, _) => _lifetime.Cancel();
        Disposed += (_, _) => _lifetime.Dispose();
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
        _status.Text = "Checking the private update repository without interrupting FMSC...";
        try
        {
            var result = await UpdateGitReader.CheckAsync(_lifetime.Token);
            if (IsDisposed || Disposing) return;
            _status.Text = result.Message +
                (result.FeedPublished ? " Published version: " + result.Version : "");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Closing this window cancels the network check. No install is running.
        }
        catch (Exception error) when (error is CryptographicException or IOException or UnauthorizedAccessException
                                         or InvalidDataException or JsonException)
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
}
