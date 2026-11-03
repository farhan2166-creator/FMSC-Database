using System.Data;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;

namespace FMSC_SMS.Pages.Students.Certificates;

/// <summary>
/// The new, opt-in certificate navigation surface. Existing pages continue to own
/// creation, editing, printing, physical collection and reissues.
/// </summary>
public sealed class StudentCertificatesWorkspacePage : V2Page, IBusyPage
{
    private readonly StudentPicker student = new();
    private readonly Label identity = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = AppTheme.Text,
        Font = AppTheme.NormalFont,
        AutoEllipsis = true,
        Text = "Select a student to see saved certificates."
    };
    private readonly Label details = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.TopLeft,
        ForeColor = AppTheme.MutedText,
        Font = AppTheme.NormalFont,
        Text = "Select a saved copy to review its recorded details and open the original editor."
    };
    private readonly ModernButton open;
    private readonly ModernButton refresh;
    private readonly ModernButton printAction;
    private readonly ModernButton collectAction;
    private readonly ModernButton reissueAction;
    private DataTable records = StudentCertificateRegisterReader.CreateTable();
    private bool reading;
    private bool editorOpen;
    private int generation;

    public bool IsBusy => reading || editorOpen;
    public string BusyMessage => editorOpen
        ? "Finish the certificate editor before leaving the workspace."
        : "Wait for the certificate register to finish loading.";

    public StudentCertificatesWorkspacePage() : this(true) { }

    internal StudentCertificatesWorkspacePage(bool initialize) : base(
        "Student Certificates Workspace",
        "Select one student, review saved certificates and open the existing certificate workflow.")
    {
        if (!CertificateWorkspaceAccess.CanOpenHub(CurrentUser.HasPermission))
            throw new UnauthorizedAccessException("Certificate access is required.");

        UseWrappingToolbar();
        open = AddAction("View / Edit Details…", (_, _) => OpenSelected(), "Primary");
        printAction = AddBusyAction("Print / Reprint", () => RunWorkspaceActionAsync("print"), "Secondary");
        collectAction = AddBusyAction("Mark Collected", () => RunWorkspaceActionAsync("collect"), "Secondary");
        reissueAction = AddBusyAction("Issue Again", () => RunWorkspaceActionAsync("reissue"), "Secondary");
        refresh = AddBusyAction("Refresh Certificates", ReloadAsync, "Secondary");
        var find = AddAction("Change Student", (_, _) => { if (!reading) student.ShowMatches(); }, "Secondary");
        open.Enabled = printAction.Enabled = collectAction.Enabled = reissueAction.Enabled = false;
        refresh.Enabled = false;

        student.PlaceholderText = StudentPicker.SearchPlaceholder;
        student.SearchFailed += Fail;
        student.UserSelectionChanged += async (_, _) => await ReloadAsync();
        student.SelectionCleared += (_, _) => ClearSelection();
        student.SelectionChangeGuard = _ => !editorOpen;
        if (initialize) StudentSearchCache.Warm();

        Grid.AutoGenerateColumns = false;
        Grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        Grid.ReadOnly = true;
        Grid.MultiSelect = false;
        Grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Type", HeaderText = "Certificate Type", DataPropertyName = "Certificate Type",
            FillWeight = 135, MinimumWidth = 145
        });
        Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Date", HeaderText = "Certificate Date", DataPropertyName = "Certificate Date",
            FillWeight = 110, MinimumWidth = 105
        });
        Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Reference", HeaderText = "Reference", DataPropertyName = "Reference",
            FillWeight = 115, MinimumWidth = 95
        });
        Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Entry", HeaderText = "Entry", DataPropertyName = "Entry",
            FillWeight = 105, MinimumWidth = 105
        });
        Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Collection", HeaderText = "Collection", DataPropertyName = "Collection",
            FillWeight = 100, MinimumWidth = 95
        });
        Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Recorded", HeaderText = "Recorded On", DataPropertyName = "Recorded On",
            FillWeight = 140, MinimumWidth = 145
        });
        Grid.SelectionChanged += (_, _) => UpdateSelected();
        Grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenSelected(); };
        Grid.DataSource = records.DefaultView;

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var searchCard = HumanUi.Card("Student", "Search by admission number, student or father name.");
        searchCard.Content.Controls.Add(HumanUi.Field("Student / Admission No.", student, true));
        var identityCard = HumanUi.Card("Selected Student", "Current recorded student details.");
        identityCard.Content.Controls.Add(identity);
        identityCard.Margin = new Padding(AppMetrics.SpacingSm, 0, 0, 0);
        header.Controls.Add(searchCard, 0, 0);
        header.Controls.Add(identityCard, 1, 0);

        var types = HumanUi.Card("Prepare New Certificate", "The existing editor opens with this student selected.");
        var typeButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Margin = Padding.Empty, Padding = new Padding(AppMetrics.SpacingXs)
        };
        foreach (var kind in StudentCertificateRegisterReader.Kinds)
        {
            if (!StudentCertificateRegisterReader.Allowed(kind, CurrentUser.HasPermission)) continue;
            var button = new ModernButton
            {
                Text = kind.Caption,
                Height = AppMetrics.ButtonHeight,
                Width = AppScale.Scale(190),
                Margin = new Padding(0, 0, 0, AppMetrics.SpacingSm),
                Tag = UiDesignSystem.Actions.Secondary
            };
            button.Click += (_, _) => OpenEditor(kind, 0, 0);
            typeButtons.Controls.Add(button);
        }
        typeButtons.SizeChanged += (_, _) =>
        {
            int width = Math.Max(AppScale.Scale(135), typeButtons.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - AppMetrics.SpacingSm * 2);
            foreach (Control button in typeButtons.Controls)
                if (button.Width != width) button.Width = width;
        };
        types.Content.Controls.Add(typeButtons);

        var registerCard = HumanUi.Card("Issued Certificates", "Select an actual saved record or issue; double-click to open.");
        registerCard.Content.Controls.Add(Grid);

        var selectedCard = HumanUi.Card("Selected Certificate", "Actions remain in the original certificate editor.");
        selectedCard.Content.Controls.Add(details);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 73));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 27));
        selectedCard.Margin = new Padding(0, AppMetrics.SpacingSm, 0, 0);
        right.Controls.Add(registerCard, 0, 0);
        right.Controls.Add(selectedCard, 0, 1);

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 77));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        types.Margin = new Padding(0, 0, AppMetrics.SpacingSm, 0);
        main.Controls.Add(types, 0, 0);
        main.Controls.Add(right, 1, 0);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.Scale(148)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(main, 0, 1);
        SetContent(root);
        SetStatus("Select a student to view their certificates. Printing and collection remain separate actions.");
    }

    private static string Field(DataRow row, string name)
    {
        if (!row.Table.Columns.Contains(name) || row.IsNull(name)) return "—";
        string value = Convert.ToString(row[name])?.Trim() ?? "";
        return value.Length > 0 ? value : "—";
    }

    private DataRowView? CurrentRecord => Grid.CurrentRow?.DataBoundItem as DataRowView;

    private void ClearSelection()
    {
        generation++;
        reading = false; // Invalidate a stale in-flight read without leaving the page busy.
        records = StudentCertificateRegisterReader.CreateTable();
        Grid.DataSource = records.DefaultView;
        identity.Text = "Select a student to see saved certificates.";
        details.Text = "Select a saved certificate to review its recorded details.";
        open.Enabled = printAction.Enabled = collectAction.Enabled = reissueAction.Enabled = refresh.Enabled = false;
    }

    private async Task ReloadAsync()
    {
        int requested = student.SelectedId;
        DataRow? selected = student.SelectedRow;
        if (requested <= 0 || selected is null)
        {
            ClearSelection();
            return;
        }

        int request = ++generation;
        int actor = CurrentUser.UserId;
        string endpoint = DatabaseHelper.DatabaseEndpoint;
        bool general = CertificateWorkspaceAccess.CanOpenGeneral(CurrentUser.HasPermission);
        bool leaving = CertificateWorkspaceAccess.CanOpenLeaving(CurrentUser.HasPermission);
        records = StudentCertificateRegisterReader.CreateTable();
        Grid.DataSource = records.DefaultView;
        open.Enabled = printAction.Enabled = collectAction.Enabled = reissueAction.Enabled = refresh.Enabled = false;
        identity.Text = Field(selected, "StudentName") + "  ·  " + Field(selected, "AdmissionNumber") +
            Environment.NewLine + "Father: " + Field(selected, "FatherName") +
            "  ·  " + Field(selected, "CampusName") + " / " + Field(selected, "ClassName") +
            " / " + Field(selected, "SectionName") + "  ·  " + Field(selected, "Status");
        details.Text = "Loading saved certificates…";
        reading = true;
        try
        {
            var loaded = await Task.Run(() =>
                StudentCertificateRegisterReader.Read(requested, general, leaving, actor));
            if (IsDisposed || request != generation || student.SelectedId != requested ||
                CurrentUser.UserId != actor || DatabaseHelper.DatabaseEndpoint != endpoint ||
                general != CertificateWorkspaceAccess.CanOpenGeneral(CurrentUser.HasPermission) ||
                leaving != CertificateWorkspaceAccess.CanOpenLeaving(CurrentUser.HasPermission)) return;

            records = loaded;
            Grid.DataSource = records.DefaultView;
            Grid.ClearSelection();
            try { Grid.CurrentCell = null; } catch (InvalidOperationException) { }
            details.Text = records.Rows.Count == 0
                ? "No saved certificate records were returned for this student."
                : "Select an issued copy to review its details and open the existing certificate editor.";
            SetStatus(records.Rows.Count + " saved certificate row(s) for the selected student.");
        }
        catch (Exception ex)
        {
            if (IsDisposed || request != generation) return;
            records = StudentCertificateRegisterReader.CreateTable();
            Grid.DataSource = records.DefaultView;
            details.Text = "Certificate records could not be loaded. Refresh and try again.";
            Fail(ex);
        }
        finally
        {
            if (!IsDisposed && request == generation)
            {
                reading = false;
                refresh.Enabled = student.SelectedId > 0;
                UpdateSelected();
            }
        }
    }

    private void UpdateSelected()
    {
        var selected = CurrentRecord;
        bool available = !reading && !editorOpen && selected is not null;
        var kind = selected is null ? null : StudentCertificateRegisterReader.Kinds
            .FirstOrDefault(item => item.Key == Convert.ToString(selected["Kind"]));
        bool permitted = available && kind is not null &&
            StudentCertificateRegisterReader.Allowed(kind, CurrentUser.HasPermission);
        int record = selected is null ? 0 : Convert.ToInt32(selected["RecordId"]);
        int issue = selected is null ? 0 : Convert.ToInt32(selected["IssueId"]);
        bool hasCopy = record > 0 && (kind?.IssueKey is null || issue > 0);
        bool collected = selected is not null &&
            string.Equals(Convert.ToString(selected["Collection"]), "Collected",
                StringComparison.OrdinalIgnoreCase);

        open.Enabled = permitted && record > 0;
        printAction.Enabled = permitted && hasCopy &&
            (kind!.Leaving || CurrentUser.HasPermission("Certificates.Print"));
        collectAction.Enabled = permitted && hasCopy && !collected;
        reissueAction.Enabled = permitted && hasCopy && collected;

        if (selected is null)
        {
            if (records.Rows.Count > 0)
                details.Text = "Select an issued copy to review its details and available actions.";
            return;
        }
        details.Text =
            "Type: " + selected["Certificate Type"] + Environment.NewLine +
            "Reference: " + selected["Reference"] + Environment.NewLine +
            "Certificate date: " + selected["Certificate Date"] + Environment.NewLine +
            "Entry: " + selected["Entry"] + Environment.NewLine +
            "Collection: " + selected["Collection"] + Environment.NewLine +
            "Collected on: " + selected["Collected On"] + Environment.NewLine +
            "Recorded on: " + selected["Recorded On"] + Environment.NewLine +
            "Edit opens the existing fields in a compact dialog. Print, collect and issue again stay here.";
    }

    private bool TrySelectedCopy(out StudentCertificateRegisterReader.Kind kind,
        out int recordId, out int issueId)
    {
        kind = null!;
        recordId = issueId = 0;
        if (CurrentRecord is not DataRowView row) return false;
        string key = Convert.ToString(row["Kind"]) ?? "";
        var selected = StudentCertificateRegisterReader.Kinds.FirstOrDefault(item => item.Key == key);
        if (selected is null ||
            !StudentCertificateRegisterReader.Allowed(selected, CurrentUser.HasPermission))
            return false;
        kind = selected;
        recordId = Convert.ToInt32(row["RecordId"]);
        issueId = Convert.ToInt32(row["IssueId"]);
        return recordId > 0;
    }

    private void OpenSelected()
    {
        if (reading || editorOpen || !TrySelectedCopy(out var kind, out int recordId, out int issueId))
            return;
        OpenEditor(kind, recordId, issueId);
    }

    private static Control CreateEditor(string key, bool initialize = true) => key switch
    {
        "Regular" => initialize ? new RegularStudentCertificatePage() : new RegularStudentCertificatePage(false),
        "Character" => initialize ? new CharacterCertificatePage() : new CharacterCertificatePage(false),
        "Leave" => initialize ? new LeaveCertificatePage() : new LeaveCertificatePage(null, false),
        "College" => initialize ? new CollegeLeaveCertificatePage() : new CollegeLeaveCertificatePage(false),
        "DOB" => initialize ? new DateOfBirthCertificatePage() : new DateOfBirthCertificatePage(false),
        "Sports" => initialize ? new SportsCertificatePage() : new SportsCertificatePage(false),
        "NOC" => initialize ? new NoObjectionCertificatePage() : new NoObjectionCertificatePage(false),
        "Provisional" => initialize ? new ProvisionalCertificatePage() : new ProvisionalCertificatePage(false),
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    private void OpenEditor(StudentCertificateRegisterReader.Kind kind, int recordId, int issueId)
    {
        if (reading || editorOpen || !StudentCertificateRegisterReader.Allowed(kind, CurrentUser.HasPermission))
            return;
        DataRow? studentRow = student.SelectedRow;
        if (student.SelectedId <= 0 || studentRow is null)
        {
            Warn("Select a student before preparing certificate details.");
            return;
        }
        if (kind.IssueKey is not null && recordId > 0 && issueId <= 0)
        {
            Warn("This historical record has no saved issue copy. Review the certificate type before editing.");
            return;
        }

        int actor = CurrentUser.UserId;
        string endpoint = DatabaseHelper.DatabaseEndpoint;
        editorOpen = true;
        UpdateSelected();
        try
        {
            // The modal displays ONLY the existing details card and its actual
            // Save/Create controls. It does not open the former full-page editor.
            CertificateWorkspaceDetailsDialog.Show(FindForm(), kind.Caption, studentRow,
                recordId, issueId, () => CreateEditor(kind.Key));
        }
        catch (Exception ex) { Fail(ex); }
        finally
        {
            editorOpen = false;
            if (!IsDisposed && CurrentUser.UserId == actor &&
                DatabaseHelper.DatabaseEndpoint == endpoint)
                _ = ReloadAsync(); // Return to the consolidated register after edits.
        }
    }

    private async Task RunWorkspaceActionAsync(string action)
    {
        if (reading || editorOpen || !TrySelectedCopy(out var kind, out int recordId, out int issueId))
            return;
        if (kind.IssueKey is not null && issueId <= 0)
        {
            Warn("This record has no saved issued copy. Open the certificate type to review it.");
            return;
        }
        DataRow? selected = student.SelectedRow;
        if (selected is null || student.SelectedId <= 0)
        {
            Warn("Select the student before continuing.");
            return;
        }
        int actor = CurrentUser.UserId;
        string endpoint = DatabaseHelper.DatabaseEndpoint;
        editorOpen = true;
        Grid.Enabled = false;
        student.Enabled = false;
        UpdateSelected();
        Control? editor = null;
        try
        {
            // Keep the genuine existing editor alive as a nonvisual action
            // owner. No old full-page navigation or duplicated write path.
            editor = CreateEditor(kind.Key, initialize: false);
            editor.Dock = DockStyle.None;
            editor.Location = new Point(-10000, -10000);
            editor.Size = new Size(AppScale.Scale(1100), AppScale.Scale(780));
            Controls.Add(editor);
            editor.CreateControl();
            var workflow = (IStudentCertificateWorkspaceEditor)editor;
            workflow.LoadWorkspaceSelection(selected, recordId, issueId);

            if (CurrentUser.UserId != actor ||
                DatabaseHelper.DatabaseEndpoint != endpoint ||
                !StudentCertificateRegisterReader.Allowed(kind, CurrentUser.HasPermission))
                throw new UnauthorizedAccessException("Certificate access changed. Refresh the workspace.");

            switch (action)
            {
                case "print":
                    if (!workflow.CanPrintFromWorkspace)
                        throw new InvalidOperationException("This copy cannot be printed until its existing requirements are satisfied.");
                    workflow.PrintFromWorkspace();
                    break;
                case "collect":
                    if (!workflow.CanCollectFromWorkspace)
                        throw new InvalidOperationException("This copy cannot be marked collected in its current state.");
                    workflow.CollectFromWorkspace();
                    break;
                case "reissue":
                    if (!workflow.CanIssueAgainFromWorkspace)
                        throw new InvalidOperationException("Select the eligible latest collected copy to issue again.");
                    await workflow.IssueAgainFromWorkspaceAsync();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
        catch (Exception ex) { Fail(ex); }
        finally
        {
            if (editor is not null)
            {
                Controls.Remove(editor);
                editor.Dispose();
            }
            editorOpen = false;
            if (!IsDisposed)
            {
                Grid.Enabled = true;
                student.Enabled = true;
                if (CurrentUser.UserId == actor && DatabaseHelper.DatabaseEndpoint == endpoint)
                    await ReloadAsync();
                else ClearSelection();
            }
        }
    }
}

internal interface IStudentCertificateWorkspaceEditor
{
    /// <summary>Use the existing editor's student and exact copy selection, without synthetic events.</summary>
    void LoadWorkspaceSelection(DataRow selectedStudent, int recordId, int issueId);

    // Real existing workflow state, NOT synthetic statuses from the register.
    bool CanPrintFromWorkspace { get; }
    bool CanCollectFromWorkspace { get; }
    bool CanIssueAgainFromWorkspace { get; }

    // These delegate to the very same handlers used by the original forms.
    void PrintFromWorkspace();
    void CollectFromWorkspace();
    Task IssueAgainFromWorkspaceAsync();
}
