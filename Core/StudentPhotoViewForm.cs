namespace FMSC_SMS.Core;

/// <summary>A read-only enlargement of the photo already displayed by the page.</summary>
internal sealed class StudentPhotoViewForm : ApplicationWindowForm
{
    internal StudentPhotoViewForm(Image source, string student)
    {
        Text = "Student Photo";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(AppScale.S(440), AppScale.S(520));
        Font = AppTheme.NormalFont;
        BackColor = AppTheme.PageBackground;

        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(UiDesignSystem.Spacing.Md)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.S(44)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight + UiDesignSystem.Spacing.Md));
        root.Controls.Add(new Label {
            Text = student, Dock = DockStyle.Fill, AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false,
            ForeColor = AppTheme.Text, Margin = Padding.Empty
        }, 0, 0);
        var photo = new PictureBox {
            Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = AppTheme.SurfaceSecondary, BorderStyle = BorderStyle.FixedSingle,
            Margin = Padding.Empty, TabStop = false, AccessibleName = "Enlarged student photo"
        };
        root.Controls.Add(photo, 0, 1);
        UiHelper.DisposePictureImageOnDispose(photo);
        var actions = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Margin = Padding.Empty,
            Padding = new Padding(0, UiDesignSystem.Spacing.Md, 0, 0)
        };
        var close = new ModernButton {
            Text = UiText.Actions.Close, ThemeRole = "Primary", DialogResult = DialogResult.OK,
            Width = AppScale.S(100), Height = AppMetrics.ButtonHeight, Margin = Padding.Empty
        };
        actions.Controls.Add(close);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;
        // Own a snapshot: an asynchronous page refresh may replace/dispose its image
        // while this dialog is open, and closing this dialog must leave it untouched.
        UiHelper.ReplacePictureImage(photo, new Bitmap(source));
    }

    internal static void EnableZoom(PictureBox preview, Func<string> student)
    {
        var tip = new ToolTip();
        preview.Disposed += (_, _) => tip.Dispose();
        preview.MouseEnter += (_, _) => tip.SetToolTip(preview,
            preview.Image is null ? null : "Click the photo to view it larger.");
        preview.MouseClick += (_, e) => {
            if (e.Button != MouseButtons.Left || preview.Image is not { } source) return;
            using var dialog = new StudentPhotoViewForm(source, student());
            dialog.ShowDialog(preview.FindForm());
        };
        UpdatePreviewHint(preview);
    }

    internal static void SetPreviewImage(PictureBox preview, Image? image)
    {
        UiHelper.ReplacePictureImage(preview, image);
        UpdatePreviewHint(preview);
    }

    private static void UpdatePreviewHint(PictureBox preview)
    {
        preview.Cursor = preview.Image is null ? Cursors.Default : Cursors.Hand;
        preview.AccessibleName = "Student photo";
        preview.AccessibleDescription = preview.Image is null
            ? "No photo selected." : "Click the photo to view it larger.";
    }
}
