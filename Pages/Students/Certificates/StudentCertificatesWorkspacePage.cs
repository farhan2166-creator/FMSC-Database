using System.Data;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;

namespace FMSC_SMS.Pages.Students.Certificates;

/// <summary>
/// The dashboard's student certificate workspace. Existing pages continue to own
/// creation, editing, printing, physical collection and reissues.
/// </summary>
public sealed class StudentCertificatesWorkspacePage : V2Page, IBusyPage
{
    private readonly StudentPicker student = new();
    private readonly Label details = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.TopLeft,
        ForeColor = AppTheme.MutedText,
        Font = AppTheme.NormalFont,
        Text = "Select a saved copy to review its recorded details and open the original editor."
    };
    private readonly List<ModernButton> certificateButtons = new();
    private readonly TableLayoutPanel copyFacts = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Visible = false };
    private readonly Dictionary<string, Label> copyValues = new();
    private readonly HumanCard registerCard;
    private readonly Func<int, bool, bool, int, DataTable> readRegister;
    private readonly Func<string> registerEndpoint;
    private Form? activeEditor;
    private readonly ModernComboBox typeFilter = HumanUi.Combo();
    private readonly SplitContainer workspaceSplit = new();
    private readonly Label actionHint = new() { Dock = DockStyle.Top, Height = AppScale.Scale(76), AutoEllipsis = true, ForeColor = AppTheme.MutedText };
    private readonly ModernButton open;
    private readonly ModernButton refresh;
    private readonly ModernButton printAction;
    private readonly ModernButton collectAction;
    private readonly ModernButton reissueAction;
    private DataTable records = StudentCertificateRegisterReader.CreateTable();
    private bool reading;
    private bool editorOpen;
    private bool arrangingWorkspace;
    private int generation;
    private int registerStudent;
    private CertificateWorkspaceSelection.Key? nextSelection;
    private HashSet<(int Record, int Issue)>? copiesBeforeCreate;

    private IBusyPage? BusyEditor => activeEditor?.Controls.OfType<IBusyPage>().FirstOrDefault(page => page.IsBusy);

    public bool IsBusy => reading || (editorOpen && activeEditor is null) || BusyEditor is not null;
    public string BusyMessage => BusyEditor?.BusyMessage ?? (reading
        ? "Wait for the certificate register to finish loading."
        : "Wait for the certificate operation to finish.");

    public StudentCertificatesWorkspacePage() : this(true) { }

    internal StudentCertificatesWorkspacePage(bool initialize, Func<int, bool, bool, int, DataTable>? readRegister = null,
        Func<string>? registerEndpoint = null) : base(
        "Student Certificates",
        "Select a student to review, edit, print or reissue certificates.")
    {
        if (!CertificateWorkspaceAccess.CanOpenHub(CurrentUser.HasPermission))
            throw new UnauthorizedAccessException("Certificate access is required.");
        this.readRegister = readRegister ?? StudentCertificateRegisterReader.Read;
        this.registerEndpoint = registerEndpoint ?? (() => DatabaseHelper.DatabaseEndpoint);

        UseWrappingToolbar();
        foreach (var kind in StudentCertificateRegisterReader.Kinds.Where(kind => StudentCertificateRegisterReader.Allowed(kind, CurrentUser.HasPermission)))
        {
            var button = AddAction(kind.Caption, (_, _) => OpenEditor(kind, 0, 0), "Secondary");
            button.Enabled = false;
            certificateButtons.Add(button);
        }
        open = new ModernButton { Text = "Edit Details", Height = AppMetrics.ButtonHeight, AutoSize = true };
        open.Click += (_, _) => OpenSelected();
        printAction = new ModernButton { Text = "Print", Height = AppMetrics.ButtonHeight, AutoSize = true };
        printAction.Click += async (_, _) => await RunWorkspaceActionAsync("print");
        collectAction = new ModernButton { Text = "Mark Collected", Height = AppMetrics.ButtonHeight, AutoSize = true };
        collectAction.Click += async (_, _) => await RunWorkspaceActionAsync("collect");
        reissueAction = new ModernButton { Text = "Issue Again", Height = AppMetrics.ButtonHeight, AutoSize = true };
        reissueAction.Click += async (_, _) => await RunWorkspaceActionAsync("reissue");
        refresh = AddBusyAction("Refresh", ReloadAsync, "Secondary");
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
            FillWeight = 32, MinimumWidth = 128
        });
        Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Date", HeaderText = "Certificate Date", DataPropertyName = "Certificate Date",
            FillWeight = 23, MinimumWidth = 105
        });
        Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Reference", HeaderText = "Reference", DataPropertyName = "Reference",
            FillWeight = 35, MinimumWidth = 140
        });
         Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Collection", HeaderText = "Collection", DataPropertyName = "Collection",
            FillWeight = 21, MinimumWidth = 95
        });
         Grid.SelectionChanged += (_, _) => UpdateSelected();
        Grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenSelected(); };
        Grid.DataSource = records.DefaultView;

        var searchCard = HumanUi.Card("Student");
        searchCard.Content.Controls.Add(HumanUi.Field("Student / Admission No.", student, true));

        registerCard = HumanUi.Card("Saved Certificates");
        typeFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        typeFilter.Items.Add("All certificate types");
        foreach (var kind in StudentCertificateRegisterReader.Kinds.Where(k => StudentCertificateRegisterReader.Allowed(k, CurrentUser.HasPermission)))
            typeFilter.Items.Add(kind.Caption);
        typeFilter.SelectedIndex = 0;
        typeFilter.Dock = DockStyle.Top;
        typeFilter.SelectedIndexChanged += (_, _) => ApplyTypeFilter();
        registerCard.Content.Controls.Add(Grid);
        registerCard.Content.Controls.Add(typeFilter);
        var selectedCard = HumanUi.Card("Selected Copy");
        var copyContent = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        copyContent.Controls.Add(details);
        copyContent.Controls.Add(actionHint);
        copyFacts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AppScale.Scale(91)));
        copyFacts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var (key, caption) in new[] { ("Certificate Type", "Type"), ("Reference", "Reference"),
            ("Certificate Date", "Date"), ("Collection", "Collection"),
            ("Collected On", "Collected"), ("Recorded On", "Recorded") })
        {
            int row = copyFacts.RowCount++;
            copyFacts.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.Scale(24)));
            var label = new Label { Text = caption, Dock = DockStyle.Fill, ForeColor = AppTheme.MutedText, TextAlign = ContentAlignment.MiddleLeft };
            var value = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = AppTheme.Text, TextAlign = ContentAlignment.MiddleLeft };
            copyFacts.Controls.Add(label, 0, row);
            copyFacts.Controls.Add(value, 1, row);
            copyValues.Add(key, value);
        }
        copyContent.Controls.Add(copyFacts);
        selectedCard.Content.Controls.Add(copyContent);
        var buttons = new TableLayoutPanel {
            Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 1, RowCount = 4,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var copyActions = new[] { open, printAction, collectAction, reissueAction };
        for (int i = 0; i < copyActions.Length; i++)
        {
            buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            copyActions[i].AutoSize = false;
            copyActions[i].Dock = DockStyle.Top;
            copyActions[i].Margin = new Padding(0, 0, 0, AppMetrics.SpacingSm);
            buttons.Controls.Add(copyActions[i], 0, i);
        }
        selectedCard.Content.Controls.Add(buttons);
        var sidebar = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.Scale(132)));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        searchCard.Margin = new Padding(0, 0, 0, AppMetrics.SpacingSm);
        sidebar.Controls.Add(searchCard, 0, 0);
        sidebar.Controls.Add(selectedCard, 0, 1);
        workspaceSplit.Dock = DockStyle.Fill;
        workspaceSplit.Panel1.Controls.Add(sidebar);
        workspaceSplit.Panel2.Controls.Add(registerCard);
        workspaceSplit.FixedPanel = FixedPanel.Panel1;
        workspaceSplit.SizeChanged += (_, _) => ArrangeWorkspace();
        SetContent(workspaceSplit);
        SetStatus("Select a student. Certificate actions are available in this workspace.");
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && activeEditor is { IsDisposed: false } editor)
        {
            editor.Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ArrangeWorkspace()
    {
        if (arrangingWorkspace || workspaceSplit.Width < AppScale.Scale(200) || workspaceSplit.Height < AppScale.Scale(200)) return;
        arrangingWorkspace = true;
        try
        {
            workspaceSplit.Panel1MinSize = workspaceSplit.Panel2MinSize = 0;
            workspaceSplit.Orientation = Orientation.Vertical;
            int desired = Math.Max(AppScale.Scale(250), (int)(workspaceSplit.Width * .20));
            workspaceSplit.SplitterDistance = Math.Clamp(desired, 0,
                Math.Max(0, workspaceSplit.Width - workspaceSplit.SplitterWidth));
        }
        finally { arrangingWorkspace = false; }
    }

    private void ApplyTypeFilter()
    {
        records.DefaultView.RowFilter = typeFilter.SelectedIndex <= 0 ? "" :
            "[Certificate Type] = '" + Convert.ToString(typeFilter.SelectedItem)!.Replace("'", "''") + "'";
        UpdateSelected();
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
        nextSelection = null;
        generation++;
        reading = false; // Invalidate a stale in-flight read without leaving the page busy.
        records = StudentCertificateRegisterReader.CreateTable();
        Grid.DataSource = records.DefaultView;
        details.Text = "Select a saved certificate to review its recorded details.";
        UpdateSelected();
    }

    private async Task ReloadAsync()
    {
        if (editorOpen) return;
        int requested = student.SelectedId;
        DataRow? selected = student.SelectedRow;
        if (requested <= 0 || selected is null)
        {
            ClearSelection();
            return;
        }

        var preferred = nextSelection ?? (registerStudent == requested
            ? CertificateWorkspaceSelection.FromRow(CurrentRecord) : null);
        var previousCopies = copiesBeforeCreate;
        copiesBeforeCreate = null;
        registerStudent = requested;
        nextSelection = null;
        int request = ++generation;
        int actor = CurrentUser.UserId;
        bool general = CertificateWorkspaceAccess.CanOpenGeneral(CurrentUser.HasPermission);
        bool leaving = CertificateWorkspaceAccess.CanOpenLeaving(CurrentUser.HasPermission);
        records = StudentCertificateRegisterReader.CreateTable();
        Grid.DataSource = records.DefaultView;
        open.Enabled = printAction.Enabled = collectAction.Enabled = reissueAction.Enabled = refresh.Enabled = false;
        details.Text = "Loading saved certificates…";
        reading = true;
        try
        {
            string endpoint = registerEndpoint();
            var loaded = await Task.Run(() =>
                readRegister(requested, general, leaving, actor));
            if (IsDisposed || request != generation || student.SelectedId != requested ||
                CurrentUser.UserId != actor || registerEndpoint() != endpoint ||
                general != CertificateWorkspaceAccess.CanOpenGeneral(CurrentUser.HasPermission) ||
                leaving != CertificateWorkspaceAccess.CanOpenLeaving(CurrentUser.HasPermission)) return;

            records = loaded;
            ApplyTypeFilter();
            Grid.DataSource = records.DefaultView;
            var selection = CertificateWorkspaceSelection.Resolve(records.DefaultView, preferred);
            if (previousCopies is not null && preferred is { } created)
            {
                var added = records.DefaultView.Cast<DataRowView>().Where(row =>
                    CertificateWorkspaceSelection.FromRow(row) is { } key && key.Kind == created.Kind &&
                    !previousCopies.Contains((key.RecordId, key.IssueId))).ToArray();
                if (added.Length == 1) selection = added[0];
                else if (added.Length > 1 && !added.Contains(selection)) selection = null;
            }
            SelectAfterRefresh(selection);
            details.Text = records.Rows.Count == 0
                ? "No saved certificate records were returned for this student."
                : "Select a certificate for details, print/reprint, collection or a new copy.";
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

    private void SelectAfterRefresh(DataRowView? preferred)
    {
        Grid.ClearSelection();
        try { Grid.CurrentCell = null; } catch (InvalidOperationException) { }
        if (preferred is null) return;
        for (int index = 0; index < Grid.Rows.Count; index++)
        {
            if (Grid.Rows[index].DataBoundItem is not DataRowView candidate ||
                !ReferenceEquals(candidate.Row, preferred.Row)) continue;
            DataGridViewCell? cell = Grid.Rows[index].Cells.Cast<DataGridViewCell>()
                .FirstOrDefault(item => item.OwningColumn.Visible);
            if (cell is null) return;
            Grid.CurrentCell = cell;
            Grid.Rows[index].Selected = true;
            return;
        }
    }

    private void UpdateSelected()
    {
        foreach (var button in certificateButtons)
            button.Enabled = !reading && !editorOpen && student.SelectedId > 0;
        refresh.Enabled = !reading && !editorOpen && student.SelectedId > 0;
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

        bool latest = selected is not null && !records.AsEnumerable().Any(row =>
            Convert.ToString(row["Kind"]) == kind?.Key && Convert.ToInt32(row["RecordId"]) == record &&
            Convert.ToInt32(row["IssueId"]) > issue);
        bool linkedChild = selected is not null && records.Columns.Contains("SourceCopyId") && records.AsEnumerable().Any(row =>
            Convert.ToString(row["Kind"]) == kind?.Key && !row.IsNull("SourceCopyId") && Convert.ToInt32(row["SourceCopyId"]) == record);
        bool editable = !collected && latest;
        open.Text = editable ? "Edit Details" : "View Details";
        printAction.Text = collected ? "Reprint" : "Print";
        collectAction.Visible = !collected;
        reissueAction.Visible = collected;
        actionHint.Text = !available ? "Select a saved copy." : collected
            ? (latest && !linkedChild ? "Collected copies stay locked. Issue Again prepares a separate copy." : "This historical copy stays locked. Reprint it or select an eligible latest copy to issue again.")
            : "Save any corrections before printing. Mark Collected after handing over the paper.";
        open.Enabled = permitted && record > 0;
        printAction.Enabled = permitted && hasCopy &&
            (kind!.Leaving || CurrentUser.HasPermission("Certificates.Print"));
        collectAction.Enabled = permitted && hasCopy && !collected;
        reissueAction.Enabled = permitted && hasCopy && collected && latest && !linkedChild;

        copyFacts.Visible = selected is not null;
        actionHint.Visible = selected is not null;
        details.Visible = selected is null;
        if (selected is null)
        {
            if (records.Rows.Count > 0)
                details.Text = "Select an issued copy to review its details and available actions.";
            return;
        }
        foreach (var (key, value) in copyValues)
        {
            value.Text = Field(selected.Row, key);
            SetToolTip(value, value.Text);
        }
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

    internal void OpenEditor(StudentCertificateRegisterReader.Kind kind, int recordId, int issueId, bool loadSelection = true)
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

        editorOpen = true;
        student.Enabled = false;
        UpdateSelected();
        bool saved = false;
        var priorCopies = recordId > 0 ? null : records.AsEnumerable()
            .Where(row => Convert.ToString(row["Kind"]) == kind.Key)
            .Select(row => (Convert.ToInt32(row["RecordId"]), Convert.ToInt32(row["IssueId"]))).ToHashSet();
        Form? form = null;
        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        try
        {
            form = CertificateWorkspaceDetailsDialog.Build(kind.Caption, studentRow,
                recordId, issueId, () => CreateEditor(kind.Key, initialize: loadSelection), loadSelection);
            activeEditor = form;
            form.TopLevel = false;
            form.FormBorderStyle = FormBorderStyle.None;
            form.Dock = DockStyle.Top;
            ((Control)form.CancelButton!).Text = "Back to Certificates";
            var bridge = form.Controls.OfType<IStudentCertificateWorkspaceEditor>().Single();
            bridge.Saved = row =>
            {
                if (saved || IsDisposed) return;
                saved = true;
                copiesBeforeCreate = priorCopies;
                int savedRecord = row is null ? recordId : CertificateUi.GetInt(row.Row, kind.RecordKey);
                int savedIssue = row is null || kind.IssueKey is null ? issueId : CertificateUi.GetInt(row.Row, kind.IssueKey);
                nextSelection = new CertificateWorkspaceSelection.Key(kind.Key, savedRecord, savedIssue,
                    PreferNewest: savedRecord <= 0);
                BeginInvoke(new Action(() => { if (!form.IsDisposed) form.Close(); }));
            };
            form.FormClosed += (_, _) =>
            {
                host.Controls.Remove(form);
                host.Dispose();
                activeEditor = null;
                editorOpen = false;
                if (IsDisposed || Disposing) return;
                student.Enabled = true;
                registerCard.Visible = true;
                if (saved)
                {
                    if (typeFilter.SelectedIndex > 0 && !Equals(typeFilter.SelectedItem, kind.Caption))
                        typeFilter.SelectedIndex = 0;
                    _ = ReloadAsync();
                }
                else
                {
                    UpdateSelected();
                    Grid.Focus();
                }
            };
            host.Controls.Add(form);
            workspaceSplit.Panel2.Controls.Add(host);
            registerCard.Visible = false;
            host.BringToFront();
            form.Show();
        }
        catch (Exception ex)
        {
            form?.Dispose();
            host.Dispose();
            activeEditor = null;
            editorOpen = false;
            student.Enabled = true;
            registerCard.Visible = true;
            UpdateSelected();
            Fail(ex);
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
        nextSelection = new CertificateWorkspaceSelection.Key(kind.Key, recordId, issueId,
            PreferNewest: action == "reissue");
        editorOpen = true;
        Grid.Enabled = false;
        student.Enabled = false;
        UpdateSelected();
        Control? editor = null;
        string? operationFeedback = null;
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
            // Drain deferred reset/layout work before binding the exact copy.
            var ready = new TaskCompletionSource();
            editor.BeginInvoke(new Action(() => ready.SetResult()));
            await ready.Task;
            workflow.LoadWorkspaceSelection(selected, recordId, issueId);
            workflow.Feedback.TextChanged += (_, _) => {
                operationFeedback = workflow.Feedback.Text;
                if (!IsDisposed) SetStatus(operationFeedback);
            };

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
        catch (Exception ex) { operationFeedback = DbMessage.UserMessage(ex); Fail(ex); }
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
                if (!string.IsNullOrWhiteSpace(operationFeedback)) SetStatus(operationFeedback);
            }
        }
    }
}

internal interface IStudentCertificateWorkspaceEditor
{
    HumanCard DetailsCard { get; }
    Label Feedback { get; }
    Action<DataRowView?>? Saved { set; }
    void ConfigureWorkspaceDetails(DataRow selectedStudent, bool editing);

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
