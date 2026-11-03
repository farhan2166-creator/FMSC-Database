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
    private DataTable records = StudentCertificateRegisterReader.CreateTable();
    private bool reading;
    private bool editorOpen;
    private int generation;

    public bool IsBusy => reading || editorOpen;
    public string BusyMessage => editorOpen
        ? "Finish the certificate editor before leaving the workspace."
        : "Wait for the certificate register to finish loading.";

    public StudentCertificatesWorkspacePage() : base(
        "Student Certificates Workspace",
        "Select one student, review saved certificates and open the existing certificate workflow.")
    {
        if (!CertificateWorkspaceAccess.CanOpenHub(CurrentUser.HasPermission))
            throw new UnauthorizedAccessException("Certificate access is required.");

        UseWrappingToolbar();
        open = AddAction("Open Selected Certificate", (_, _) => OpenSelected(), "Primary");
        refresh = AddBusyAction("Refresh Certificates", ReloadAsync, "Secondary");
        var find = AddAction("Change Student", (_, _) => { if (!reading) student.ShowMatches(); }, "Secondary");
        open.Enabled = false;
        refresh.Enabled = false;

        student.PlaceholderText = StudentPicker.SearchPlaceholder;
        student.SearchFailed += Fail;
        student.UserSelectionChanged += async (_, _) => await ReloadAsync();
        student.SelectionCleared += (_, _) => ClearSelection();
        student.SelectionChangeGuard = _ => !editorOpen;
        StudentSearchCache.Warm();

        Grid.AutoGenerateColumns = false;
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
        records = StudentCertificateRegisterReader.CreateTable();
        Grid.DataSource = records.DefaultView;
        identity.Text = "Select a student to see saved certificates.";
        details.Text = "Select a saved certificate to review its recorded details.";
        open.Enabled = refresh.Enabled = false;
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
        open.Enabled = refresh.Enabled = false;
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
        open.Enabled = !reading && !editorOpen && selected is not null;
        if (selected is null)
        {
            if (records.Rows.Count > 0)
                details.Text = "Select a saved certificate to review its details.";
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
            "Open the original editor to print, issue again or mark collected when permitted.";
    }

    private void OpenSelected()
    {
        if (reading || editorOpen || CurrentRecord is not DataRowView row) return;
        string key = Convert.ToString(row["Kind"]) ?? "";
        var kind = StudentCertificateRegisterReader.Kinds.FirstOrDefault(item => item.Key == key);
        if (kind is null) return;
        int recordId = Convert.ToInt32(row["RecordId"]);
        int issueId = Convert.ToInt32(row["IssueId"]);
        if (kind.IssueKey is not null && issueId <= 0)
        {
            Warn("This is a legacy saved record without a tracked issue. Open the certificate type to review its existing history.");
            return;
        }
        OpenEditor(kind, recordId, issueId);
    }

    private static Control CreateEditor(string key) => key switch
    {
        "Regular" => new RegularStudentCertificatePage(),
        "Character" => new CharacterCertificatePage(),
        "Leave" => new LeaveCertificatePage(),
        "College" => new CollegeLeaveCertificatePage(),
        "DOB" => new DateOfBirthCertificatePage(),
        "Sports" => new SportsCertificatePage(),
        "NOC" => new NoObjectionCertificatePage(),
        "Provisional" => new ProvisionalCertificatePage(),
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    private void OpenEditor(StudentCertificateRegisterReader.Kind kind, int recordId, int issueId)
    {
        if (reading || editorOpen || !StudentCertificateRegisterReader.Allowed(kind, CurrentUser.HasPermission)) return;
        int admissionId = student.SelectedId;
        DataRow? studentRow = student.SelectedRow;
        if (admissionId <= 0 || studentRow is null)
        {
            Warn("Select a student before opening a certificate.");
            return;
        }

        editorOpen = true;
        open.Enabled = refresh.Enabled = false;
        try
        {
            using var dialog = new ApplicationWindowForm
            {
                Text = kind.Caption + " Certificate",
                StartPosition = FormStartPosition.CenterParent,
                Width = AppScale.Scale(1240),
                Height = AppScale.Scale(800),
                MinimumSize = new Size(AppScale.Scale(980), AppScale.Scale(620)),
                BackColor = AppTheme.PageBackground
            };
            var page = CreateEditor(kind.Key);
            page.Dock = DockStyle.Fill;
            dialog.Controls.Add(page);
            ModalPageCloseGuard.Attach(dialog, page);
            dialog.Shown += (_, _) => dialog.BeginInvoke(new Action(() =>
            {
                if (dialog.IsDisposed || page.IsDisposed) return;
                try
                {
                    if (!StudentCertificateRegisterReader.Allowed(kind, CurrentUser.HasPermission) ||
                        CurrentUser.UserId <= 0)
                    {
                        dialog.Close();
                        return;
                    }
                    ((IStudentCertificateWorkspaceEditor)page).LoadWorkspaceSelection(studentRow, recordId, issueId);
                }
                catch (Exception ex)
                {
                    Fail(ex);
                    dialog.Close();
                }
            }));
            dialog.ShowDialog(FindForm());
        }
        catch (Exception ex) { Fail(ex); }
        finally
        {
            editorOpen = false;
            if (!IsDisposed)
            {
                open.Enabled = CurrentRecord is not null;
                _ = ReloadAsync(); // Read only: refresh records after any editor changes.
            }
        }
    }
}

internal interface IStudentCertificateWorkspaceEditor
{
    /// <summary>Use the normal existing selection/record-loading flow, without an event hack.</summary>
    void LoadWorkspaceSelection(DataRow selectedStudent, int recordId, int issueId);
}
