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
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child)) yield return nested;
        }
    }

    /// <summary>
    /// Move the original editor's details card to a compact modal. No fields are
    /// reconstructed and no second save, edit, SQL, print or document logic exists.
    /// For synthetic native layout tests only, initialize=false is supported by
    /// the page factory passed here.
    /// </summary>
    internal static void Show(IWin32Window? owner, string title, DataRow student,
        int recordId, int issueId, Func<Control> editorFactory)
    {
        using var dialog = Build(title, student, recordId, issueId, editorFactory, loadSelection: true);
        dialog.ShowDialog(owner);
    }

    /// <summary>Renders the real compact modal without touching SQL during CI previews.</summary>
    internal static Form BuildForScreenshot(string title, DataRow student,
        Func<Control> editorFactory) =>
        Build(title, student, 0, 0, editorFactory, loadSelection: false);

    private static Form Build(string title, DataRow student,
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

        HumanCard details = Descendants(editor).OfType<HumanCard>()
            .FirstOrDefault(card => Descendants(card).OfType<StudentPicker>().Any())
            ?? throw new InvalidOperationException("The existing certificate details card could not be found.");
        details.Parent?.Controls.Remove(details);
        details.Dock = DockStyle.None;  // The dialog sizes it explicitly; no old table-row preferred-height feedback.
        details.AutoSize = false;
        details.MinimumSize = Size.Empty;
        details.MaximumSize = Size.Empty;

        // The workspace already chose the student; the dialog must not open a
        // second search or allow an unrelated student to be selected.
        foreach (StudentPicker picker in Descendants(details).OfType<StudentPicker>())
        {
            picker.SetStudent(student);
            picker.Enabled = false;
        }

        // Avoid an old New/Clear button clearing the locked student context.
        // Original Create/Save Changes and recovery controls remain attached,
        // visible and wired to their existing handlers.
        foreach (ModernButton button in Descendants(details).OfType<ModernButton>())
        {
            if (button.Text.Trim().Equals("New", StringComparison.OrdinalIgnoreCase) ||
                button.Text.Trim().Equals("Clear", StringComparison.OrdinalIgnoreCase))
                button.Visible = false;
        }

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
            Text = (recordId > 0 ? "Edit saved certificate" : "Issue a new certificate") +
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
            Text = "Use the existing Save/Create action above. Return to the workspace to print, collect or issue again.",
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
        bottom.Controls.Add(guidance, 0, 0);
        bottom.Controls.Add(close, 1, 0);
        layout.Controls.Add(bottom);

        void ArrangeDialog()
        {
            int pad = AppMetrics.SpacingMd;
            int headerHeight = AppScale.Scale(75);
            int footerHeight = AppScale.Scale(58);
            int usableWidth = Math.Max(1, layout.ClientSize.Width - 2 * pad);
            int usableHeight = Math.Max(1, layout.ClientSize.Height - 2 * pad);
            layout.SuspendLayout();
            try
            {
                context.Bounds = new Rectangle(pad, pad, usableWidth, headerHeight);
                bottom.Bounds = new Rectangle(pad,
                    Math.Max(pad, layout.ClientSize.Height - pad - footerHeight),
                    usableWidth, footerHeight);
                details.Bounds = new Rectangle(pad, pad + headerHeight, usableWidth,
                    Math.Max(1, usableHeight - headerHeight - footerHeight));
            }
            finally { layout.ResumeLayout(true); }
        }
        layout.SizeChanged += (_, _) => ArrangeDialog();

        dialog.Controls.Add(layout);
        layout.BringToFront();
        ArrangeDialog();
        ModalPageCloseGuard.Attach(dialog, editor);

        // Run after existing page handle creation/deferred initialization, then
        // use the existing StudentChanged + LoadCertificates + SelectSavedCopy.
        if (loadSelection) dialog.Shown += (_, _) => dialog.BeginInvoke(new Action(() =>
        {
            if (dialog.IsDisposed || editor.IsDisposed) return;
            try
            {
                bridge.LoadWorkspaceSelection(student, recordId, issueId);
                close.Focus();
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
