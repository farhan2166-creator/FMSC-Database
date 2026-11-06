using System.Data;
using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Students.Certificates;

/// <summary>
/// A focused modal over the actual existing certificate detail controls.
/// The original editor instance still owns all field rules, saves and recovery.
/// Its history grid and full-page navigation are NEVER presented here.
/// </summary>
internal static class CertificateWorkspaceDetailsDialog
{
    private static void OrderTableTabs(TableLayoutPanel table)
    {
        int index = 0;
        foreach (var control in table.Controls.Cast<Control>()
            .Select(control => (Control: control, Cell: table.GetPositionFromControl(control)))
            .OrderBy(item => item.Cell.Row).ThenBy(item => item.Cell.Column))
            control.Control.TabIndex = index++;
    }

    private static void WireFieldTabOrder(Control parent)
    {
        if (parent is TableLayoutPanel table)
        {
            OrderTableTabs(table);
            table.Layout += (_, _) => OrderTableTabs(table);
        }
        foreach (Control child in parent.Controls) WireFieldTabOrder(child);
    }

    private static void ExcludeFromTabOrder(Control control)
    {
        control.TabStop = false;
        foreach (Control child in control.Controls) ExcludeFromTabOrder(child);
    }

    internal static void LockStudent(StudentPicker picker, DataRow student)
    {
        picker.SetStudent(student);
        picker.Enabled = false;
        picker.EnabledChanged += (_, _) => { if (picker.Enabled) picker.Enabled = false; };
        if (picker.Parent is not TableLayoutPanel field) return;
        var position = field.GetCellPosition(picker);
        picker.Visible = false;
        field.Controls.Remove(picker);
        var display = new Label {
            Text = CertificateUi.GetString(student, "AdmissionNumber") + " — " + CertificateUi.GetString(student, "StudentName"),
            Dock = DockStyle.Top, Height = AppMetrics.InputControlHeight,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = AppTheme.Text, BackColor = AppTheme.SurfaceElevated,
            AutoEllipsis = true, BorderStyle = BorderStyle.FixedSingle
        };
        // Keep the original picker owned and disposed with its field component.
        display.Controls.Add(picker);
        field.Controls.Add(display, position.Column, position.Row);
    }
    /// <summary>Renders the real compact modal without touching SQL during CI previews.</summary>
    internal static Form BuildForScreenshot(string title, DataRow student,
        Func<Control> editorFactory) =>
        Build(title, student, 0, 0, editorFactory, loadSelection: false);

    internal static Form Build(string title, DataRow student,
        int recordId, int issueId, Func<Control> editorFactory, bool loadSelection)
    {
        var dialog = new Form
        {
            Text = recordId > 0 ? "Certificate Details — " + title : "Prepare Certificate — " + title,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(AppScale.Scale(title == "Provisional" ? 1120 : 930),
                AppScale.Scale(title is "Leave" or "College Leaving" or "Provisional" ? 720 : 580)),
            MinimumSize = new Size(AppScale.Scale(690), AppScale.Scale(430)),
            BackColor = AppTheme.PageBackground,
            Font = AppTheme.NormalFont,
            AutoScaleMode = AutoScaleMode.None,
            ShowInTaskbar = false
        };

        Control editor = editorFactory();
        if (editor is not IStudentCertificateWorkspaceEditor bridge)
        {
            editor.Dispose();
            throw new InvalidOperationException("The existing certificate editor does not support workspace context.");
        }

        // A real WinForms instance of the existing editor is retained as the
        // operation owner; only its original details card appears in this dialog.
        editor.Dock = DockStyle.None;
        editor.Location = new Point(-10000, -10000);
        editor.Size = new Size(2, 2);
        dialog.Controls.Add(editor);

        HumanCard details = bridge.DetailsCard;
        bridge.ConfigureWorkspaceDetails(student, editing: recordId > 0);
        details.Parent?.Controls.Remove(details);
        details.Dock = DockStyle.None;
        details.AutoSize = false;
        details.MinimumSize = Size.Empty;
        details.MaximumSize = Size.Empty;
        // The editor exposes its original field component explicitly; no card or
        // button discovery by caption or control-tree shape is required.

        // A simple fixed-band panel avoids WinForms TableLayoutPanel stealing
        // hundreds of pixels for an inherited legacy field-card preferred size.
        // The details card is explicitly allocated ALL remaining vertical space.
        var layout = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty, Padding = Padding.Empty
        };

        var context = new Label
        {
            Text = (recordId > 0 ? "Saved certificate · " : "Prepare certificate · ") + title +
                Environment.NewLine + "Selected student: " +
                CertificateUi.GetString(student, "StudentName") +
                "  ·  " + CertificateUi.GetString(student, "AdmissionNumber"),
            Dock = DockStyle.None, ForeColor = AppTheme.HeadingText,
            BackColor = AppTheme.PageBackground, Font = AppTheme.NormalFont,
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
        };
        layout.Controls.Add(context);
        layout.Controls.Add(details);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.None, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AppScale.Scale(200)));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var guidance = new Label
        {
            Text = "Print, collect or issue again from Saved Certificates.",
            ForeColor = AppTheme.MutedText, Dock = DockStyle.Fill,
            AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
            Font = AppTheme.NormalFont
        };
        var close = new ModernButton
        {
            Text = "Return to Workspace",
            Dock = DockStyle.Fill,
            Margin = new Padding(AppMetrics.SpacingSm),
            Height = AppMetrics.ButtonHeight
        };
        close.Click += (_, _) => dialog.Close();
        dialog.CancelButton = close;
        close.DialogResult = DialogResult.None;
        bottom.Controls.Add(guidance, 0, 0);
        bottom.Controls.Add(close, 1, 0);
        layout.Controls.Add(bottom);
        var feedback = bridge.Feedback;
        feedback.Parent?.Controls.Remove(feedback);
        feedback.Dock = DockStyle.None;
        feedback.AutoEllipsis = true;
        layout.Controls.Add(feedback);
        details.TabIndex = 0;
        bottom.TabIndex = 1;
        ExcludeFromTabOrder(editor);
        WireFieldTabOrder(details);

        bool arranging = false;
        void ArrangeDialog()
        {
            if (arranging) return;
            arranging = true;
            try
            {
            int pad = AppMetrics.SpacingMd;
            int headerHeight = AppScale.Scale(75);
            int footerHeight = AppScale.Scale(105);
            int usableWidth = Math.Max(1, layout.ClientSize.Width - 2 * pad);
            int usableHeight = Math.Max(1, layout.ClientSize.Height - 2 * pad);
            layout.SuspendLayout();
            try
            {
                context.Bounds = new Rectangle(pad, pad, usableWidth, headerHeight);
                bottom.Bounds = new Rectangle(pad,
                    Math.Max(pad, layout.ClientSize.Height - pad - footerHeight),
                    usableWidth, footerHeight);
                feedback.Bounds = new Rectangle(pad, bottom.Top, usableWidth, AppScale.Scale(44));
                bottom.Top += AppScale.Scale(44);
                bottom.Height -= AppScale.Scale(44);
                details.Bounds = new Rectangle(pad, pad + headerHeight, usableWidth,
                    Math.Max(1, usableHeight - headerHeight - footerHeight));
            }
            finally { layout.ResumeLayout(true); }

            }
            finally { arranging = false; }
        }
        layout.SizeChanged += (_, _) => ArrangeDialog();

        dialog.Controls.Add(layout);
        layout.BringToFront();
        ArrangeDialog();
        ModalPageCloseGuard.Attach(dialog, editor, confirmDiscard: _ => true);

        // Run after existing page handle creation/deferred initialization, then
        // use the existing StudentChanged + LoadCertificates + SelectSavedCopy.
        if (loadSelection) dialog.Shown += (_, _) => dialog.BeginInvoke(new Action(() =>
        {
            if (dialog.IsDisposed || editor.IsDisposed) return;
            try
            {
                bridge.LoadWorkspaceSelection(student, recordId, issueId);
                if (!FormFlow.FocusFirstInput(details)) close.Focus();
            }
            catch (Exception ex)
            {
                ModernMessageDialog.Show(dialog,
                    DbMessage.UserMessage(ex),
                    "Certificate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                dialog.Close();
            }
        }));

        return dialog;
    }
}
