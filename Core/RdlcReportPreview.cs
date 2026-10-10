namespace FMSC_SMS.Core;

/// <summary>
/// Provides one consistent, user-friendly preview window for every RDLC report.
/// Reports open fitted to the page; users can still resize or maximize the window.
/// </summary>
internal static class RdlcReportPreview
{
    public static void Show(
        IWin32Window? owner,
        string title,
        string reportPath,
        string dataSourceName,
        DataTable data,
        bool fitPageWidth = false,
        string? reportDefinition = null,
        bool enableExternalImages = false)
    {
        Rectangle workingArea = owner is Control ownerControl
            ? Screen.FromControl(ownerControl).WorkingArea
            : Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1366, 768);

        int width = Math.Min(AppScale.Scale(1180), workingArea.Width * 9 / 10);
        int height = Math.Min(AppScale.Scale(820), workingArea.Height * 9 / 10);

        using var form = new ApplicationWindowForm {
            Text = title,
            StartPosition = owner is null
                ? FormStartPosition.CenterScreen
                : FormStartPosition.CenterParent,
            Size = new Size(width, height),
            MinimumSize = new Size(
                Math.Min(AppScale.Scale(850), width),
                Math.Min(AppScale.Scale(600), height)),
            FormBorderStyle = FormBorderStyle.Sizable,
            MaximizeBox = true,
            MinimizeBox = true,
            WindowState = FormWindowState.Maximized,
            KeyPreview = true
        };

        using var viewer = new Microsoft.Reporting.WinForms.ReportViewer {
            Dock = DockStyle.Fill,
            ProcessingMode = Microsoft.Reporting.WinForms.ProcessingMode.Local,
            ShowToolBar = false
        };
        viewer.LocalReport.EnableExternalImages = enableExternalImages;
        if (reportDefinition is null) viewer.LocalReport.ReportPath = reportPath;
        else viewer.LocalReport.LoadReportDefinition(new StringReader(reportDefinition));
        viewer.LocalReport.DataSources.Clear();
        viewer.LocalReport.DataSources.Add(
            new Microsoft.Reporting.WinForms.ReportDataSource(dataSourceName, data));

        Control toolbar = CreateToolbar(form, viewer, fitPageWidth);
        form.Controls.Add(viewer);
        form.Controls.Add(toolbar);
        using var shortcuts = new ReportShortcutMessageFilter(form);
        form.KeyDown += (_, e) => {
            if (e.KeyCode != Keys.Escape)
                return;
            e.SuppressKeyPress = true;
            form.Close();
        };
        form.Shown += (_, _) => {
            viewer.SetDisplayMode(Microsoft.Reporting.WinForms.DisplayMode.PrintLayout);
            viewer.ZoomMode = fitPageWidth
                ? Microsoft.Reporting.WinForms.ZoomMode.PageWidth
                : Microsoft.Reporting.WinForms.ZoomMode.FullPage;
        };

        if (owner is null)
            form.ShowDialog();
        else
            form.ShowDialog(owner);
    }

    internal static void BindToolbarAction(ApplicationWindowForm form, ModernButton button, Keys keys, string hint, Action execute)
    {
        button.Click += (_, _) => execute();
        form.ShortcutController.RegisterShortcut(button, keys, hint, execute);
    }

    private static Control CreateToolbar(
        ApplicationWindowForm form,
        Microsoft.Reporting.WinForms.ReportViewer viewer,
        bool fitPageWidth)
    {
        var toolbar = new FlowLayoutPanel {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(
                AppScale.Scale(12),
                AppScale.Scale(9),
                AppScale.Scale(12),
                AppScale.Scale(9)),
            BackColor = AppTheme.Surface
        };

        ModernButton Button(string text, int width, string role = "Secondary") =>
            new() {
                Text = text,
                Width = AppScale.Scale(width),
                Height = AppMetrics.ButtonHeight,
                Margin = new Padding(0, 0, AppScale.Scale(8), 0),
                ThemeRole = role,
                TakesFocus = true
            };

        var print = Button("Print", 88, "Primary");
        var pageSetup = Button("Page setup", 108);
        var exportPdf = Button("Export PDF", 112);
        var previous = Button("Previous", 94);
        var next = Button("Next", 78);
        var close = Button("Close", 76);

        var page = new Label {
            AutoSize = false,
            Width = AppScale.Scale(104),
            Height = AppMetrics.ButtonHeight,
            Margin = new Padding(0, 0, AppScale.Scale(8), 0),
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.Text,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "Page 1"
        };
        var zoom = new ModernComboBox {
            Width = AppScale.Scale(132),
            Height = AppMetrics.InputControlHeight,
            Margin = new Padding(0, 0, AppScale.Scale(8), 0)
        };
        zoom.Items.AddRange(new object[] {
            "Fit page",
            "Page width",
            "100%",
            "125%",
            "150%"
        });
        zoom.SelectedIndex = fitPageWidth ? 1 : 0;

        var actions = new FlowLayoutPanel {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        actions.Controls.Add(print);
        actions.Controls.Add(pageSetup);
        actions.Controls.Add(exportPdf);

        var navigation = new FlowLayoutPanel {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        navigation.Controls.Add(previous);
        navigation.Controls.Add(page);
        navigation.Controls.Add(next);
        navigation.Controls.Add(zoom);
        navigation.Controls.Add(close);

        toolbar.Controls.Add(actions);
        toolbar.Controls.Add(navigation);

        void RefreshPageState()
        {
            int totalPages;
            try
            {
                totalPages = Math.Max(1, viewer.GetTotalPages());
            }
            catch (Exception nonFatalException18)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException18, "RdlcReportPreview: CreateToolbar fallback");
                totalPages = 1;
            }
            int currentPage = Math.Clamp(viewer.CurrentPage, 1, totalPages);
            page.Text = $"Page {currentPage} of {totalPages}";
            previous.Enabled = currentPage > 1;
            next.Enabled = currentPage < totalPages;
        }

        void Execute(string action, Action execute)
        {
            try { execute(); }
            catch (Exception ex)
            {
                AppExceptionHandler.LogNonFatal(ex, $"Report {action}");
                MessageBox.Show(form, $"Unable to {action} the report.\n\n{ex.GetBaseException().Message}",
                    form.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        BindToolbarAction(form, print, Keys.Control | Keys.P, "Ctrl+P", () => Execute("print", () =>
            RdlcReportPrinter.Print(form, viewer)));
        BindToolbarAction(form, pageSetup, Keys.Control | Keys.Shift | Keys.P, "Ctrl+Shift+P", () => Execute("set up", () => viewer.PageSetupDialog()));
        BindToolbarAction(form, exportPdf, Keys.Control | Keys.E, "Ctrl+E", () => {
            Execute("export", () => {
                using var dialog = new SaveFileDialog {
                    Filter = "PDF document (*.pdf)|*.pdf", DefaultExt = "pdf", AddExtension = true,
                    FileName = string.Concat(form.Text.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)),
                    Title = "Export report"
                };
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                byte[] bytes = viewer.LocalReport.Render("PDF");
                File.WriteAllBytes(dialog.FileName, bytes);
            });
        });
        BindToolbarAction(form, previous, Keys.Control | Keys.Left, "Ctrl+Left", () => {
            viewer.CurrentPage = Math.Max(1, viewer.CurrentPage - 1);
            RefreshPageState();
        });
        BindToolbarAction(form, next, Keys.Control | Keys.Right, "Ctrl+Right", () => {
            int totalPages = Math.Max(1, viewer.GetTotalPages());
            viewer.CurrentPage = Math.Min(totalPages, viewer.CurrentPage + 1);
            RefreshPageState();
        });
        zoom.SelectionChangeCommitted += (_, _) => {
            switch (zoom.SelectedItem?.ToString())
            {
                case "Page width":
                    viewer.ZoomMode = Microsoft.Reporting.WinForms.ZoomMode.PageWidth;
                    break;
                case "100%":
                case "125%":
                case "150%":
                    viewer.ZoomMode = Microsoft.Reporting.WinForms.ZoomMode.Percent;
                    viewer.ZoomPercent = int.Parse(
                        zoom.SelectedItem!.ToString()!.TrimEnd('%'));
                    break;
                default:
                    viewer.ZoomMode = Microsoft.Reporting.WinForms.ZoomMode.FullPage;
                    break;
            }
        };
        BindToolbarAction(form, close, Keys.Escape, "Esc", () => form.Close());
        form.ShortcutController.RegisterShortcut(zoom, Keys.Control | Keys.D0, "Ctrl+0", () => {
            zoom.SelectedIndex = 0;
            viewer.ZoomMode = Microsoft.Reporting.WinForms.ZoomMode.FullPage;
        });
        form.ShortcutController.RegisterShortcut(zoom, Keys.Control | Keys.D1, "Ctrl+1", () => {
            zoom.SelectedIndex = 1;
            viewer.ZoomMode = Microsoft.Reporting.WinForms.ZoomMode.PageWidth;
        });
        form.ShortcutController.RefreshHints();
        void SetReady(bool ready)
        {
            print.Enabled = pageSetup.Enabled = exportPdf.Enabled = zoom.Enabled = ready;
            if (!ready) { previous.Enabled = next.Enabled = false; page.Text = "Loading..."; }
        }
        SetReady(false);
        viewer.RenderingBegin += (_, _) => SetReady(false);
        viewer.PageNavigation += (_, _) => {
            if (!form.IsDisposed && form.IsHandleCreated)
                form.BeginInvoke(RefreshPageState);
        };
        form.Shown += (_, _) => RefreshPageState();
        viewer.RenderingComplete += (_, e) => {
            // Navigation or closing can cancel an obsolete render without failing the report.
            if (e.Exception is OperationCanceledException) return;
            if (!form.IsDisposed && form.IsHandleCreated)
                form.BeginInvoke((Action)(() => {
                    if (form.IsDisposed || viewer.IsDisposed) return;
                    SetReady(e.Exception is null);
                    if (e.Exception is not null)
                    {
                        page.Text = "Preview failed";
                        AppExceptionHandler.LogNonFatal(e.Exception, "Report preview rendering");
                        MessageBox.Show(form, $"Unable to preview the report.\n\n{e.Exception.GetBaseException().Message}",
                            form.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    // Rendering print layout can replace the internal page control.
                    // Reapply the selected zoom after it exists, including the first render.
                    if (zoom.SelectedIndex == 0 && viewer.ZoomMode != Microsoft.Reporting.WinForms.ZoomMode.FullPage)
                        viewer.ZoomMode = Microsoft.Reporting.WinForms.ZoomMode.FullPage;
                    else if (zoom.SelectedIndex == 1 && viewer.ZoomMode != Microsoft.Reporting.WinForms.ZoomMode.PageWidth)
                        viewer.ZoomMode = Microsoft.Reporting.WinForms.ZoomMode.PageWidth;
                    RefreshPageState();
                }));
        };

        return toolbar;
    }
}

