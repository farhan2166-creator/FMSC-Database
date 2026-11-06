using System.Reflection;

namespace FMSC_SMS.Core;

/// <summary>Application information remains available without a database connection.</summary>
internal sealed class AboutForm : ApplicationWindowForm
{
    internal AboutForm()
    {
        Text = "About FMSC";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = AppMetrics.MediumDialogClientSize;
        Font = AppTheme.NormalFont;
        BackColor = AppTheme.PageBackground;

        var assembly = typeof(AboutForm).Assembly;
        string build = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "Unavailable";
        string version = build.Split('+')[0];
        string buildMetadata = build.Length > version.Length ? build[(version.Length + 1)..] : "";
        string displayBuild = buildMetadata.Length > 12 && buildMetadata.All(Uri.IsHexDigit)
            ? $"{version}+{buildMetadata[..12]}"
            : build;
        var rows = new[] {
            ("Application version", version),
            ("Build", build),
            ("Required database schema", DatabaseHelper.RequiredDatabaseSchemaVersion),
            ("Appearance", AppTheme.CurrentThemeName),
            (".NET runtime", Environment.Version.ToString())
        };
        string supportDetails = "FMSC — School Management System\r\n" +
            string.Join("\r\n", rows.Select(row => $"{row.Item1}: {row.Item2}"));

        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(UiDesignSystem.Spacing.Lg), BackColor = AppTheme.PageBackground
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight + UiDesignSystem.Spacing.Md));
        var card = HumanUi.Card("FMSC", "School Management System", AppTheme.Primary);
        var information = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = rows.Length + 1,
            BackColor = AppTheme.Surface, Margin = Padding.Empty
        };
        int columnGap = UiDesignSystem.Spacing.Lg;
        int labelWidth = rows.Max(row => AppMetrics.TextWidth(row.Item1, Font, columnGap + AppScale.S(16)));
        information.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        information.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int rowHeight = AppMetrics.TextHeight(Font) + UiDesignSystem.Spacing.Md;
        for (int i = 0; i < rows.Length; i++)
        {
            information.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
            information.Controls.Add(new Label {
                Text = rows[i].Item1, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = AppTheme.MutedText, Margin = new Padding(0, 0, columnGap, 0)
            }, 0, i);
            var value = new Label {
                Text = rows[i].Item1 == "Build" ? displayBuild : rows[i].Item2,
                AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = AppTheme.Text, UseMnemonic = false, Margin = Padding.Empty
            };
            information.Controls.Add(value, 1, i);
        }
        information.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Content.Controls.Add(information);
        root.Controls.Add(card, 0, 0);
        var feedback = new Label {
            Text = "For database status and troubleshooting, use Account → Database - Connection Help.",
            Dock = DockStyle.Fill, AutoSize = true, ForeColor = AppTheme.MutedText,
            Margin = new Padding(0, UiDesignSystem.Spacing.Sm, 0, UiDesignSystem.Spacing.Sm)
        };
        root.SizeChanged += (_, _) => feedback.MaximumSize = new Size(Math.Max(1, root.ClientSize.Width - root.Padding.Horizontal), 0);
        root.Controls.Add(feedback, 0, 1);
        var close = new ModernButton {
            Text = UiText.Actions.Close, ThemeRole = "Primary", DialogResult = DialogResult.OK,
            Width = AppScale.S(130), Height = AppMetrics.ButtonHeight, Margin = Padding.Empty
        };
        var copy = new ModernButton {
            Text = "Copy details", ThemeRole = "Secondary", Width = AppScale.S(130), Height = AppMetrics.ButtonHeight,
            Margin = new Padding(0, 0, UiDesignSystem.Spacing.Sm, 0)
        };
        copy.Click += (_, _) => {
            try { Clipboard.SetText(supportDetails); feedback.Text = "Application details copied."; }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                AppExceptionHandler.LogNonFatal(ex, "Copy application details");
                feedback.Text = "The clipboard is busy. Try Copy details again.";
            }
        };
        var actions = HumanUi.DialogButtons(copy, close);
        actions.Padding = new Padding(0, UiDesignSystem.Spacing.Md, 0, 0);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;
    }
}
