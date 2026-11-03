using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Setup.Academics;
// BD-24: catalogs are global; creating/renaming a row does not save campus assignments.
// Refresh pending matrix changes by IDs; only Apply Changes commits assignments.
public sealed class CampusClassSectionAssignmentsPage : V2Page, IUnsavedChanges
{
    private readonly Dictionary<(int ClassId, int SectionId), bool> catalogDraft = new();
    private readonly LookupBox campus = new();
    private readonly DataGridView matrix = new() {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        MultiSelect = true,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
        EditMode = DataGridViewEditMode.EditProgrammatically,
        BackgroundColor = AppTheme.Surface,
        BorderStyle = BorderStyle.None
    };
    private readonly List<ClassInfo> classes = new();
    private readonly List<SectionInfo> sections = new();
    // Dirty state drives Apply/Reset. Campus switching remains prompt-free by owner policy.
    private string? appliedMatrixSignature;
    private int loadedCampusId;
    // Suppress selection events while initial lookup binding is in progress.
    private bool suppressCampusChanged;
    private bool updatingMatrix;
    private bool matrixReady;
    private readonly Dictionary<(int ClassId, int SectionId), bool> savedAssignments = new();
    private readonly List<ModernButton> matrixActions = new();
    private ModernButton applyButton = null!;
    private Point? assignmentAnchor;
    private Point? dragStart;
    private bool dragAssigned;
    private ModernButton resetButton = null!;
    public CampusClassSectionAssignmentsPage() : this(true) { }
    internal CampusClassSectionAssignmentsPage(bool initialize)
        : base(PageTitles.CampusAssignments, "Choose which classes and sections each campus offers.")
    {
        applyButton =
            AddAction("Apply Changes", (_, _) => Save(), "Success");
        SetSaveShortcut(applyButton);
        resetButton = AddAction("Reset Changes", (_, _) => ResetChanges(), "Secondary");
        var selectAll =
            AddAction("Assign All", (_, _) => SelectAll(), "Secondary");
        var clearAll =
            AddAction("Clear All", (_, _) => ClearAll(), "Secondary");
        matrixActions.AddRange(new[] { selectAll, clearAll, resetButton });
        RelocateFromActions(applyButton, selectAll, clearAll, resetButton);
        AddAction("Manage Classes…", (_, _) => ManageCatalog(true), "Secondary");
        AddAction("Manage Sections…", (_, _) => ManageCatalog(false), "Secondary");
        AddAction("Refresh Catalogs", (_, _) => RefreshCatalogs(), "Secondary");
        BuildMatrix();
        campus.SelectedIndexChanged +=
            (_, _) => {
                if (suppressCampusChanged)
                    return;
                catalogDraft.Clear();
                LoadCampusMatrix();
            };
        if (initialize) Defer(BindLookups);
        var matrixCard = HumanUi.Card("Class & Section Assignments", "Click and drag to check or uncheck a range. Ctrl+click toggles separate cells; Shift+click extends a range. Click a class name or section heading to toggle its group. Apply Changes saves your assignments.");
        var controlsRow = new Panel {
            Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty
        };
        var tools = new FlowLayoutPanel {
            Name = "AssignmentTools", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty
        };
        int actionHeight = applyButton.Height;
        var caption = new Label {
            Text = "Campus *", AutoSize = false, Height = actionHeight,
            Width = TextRenderer.MeasureText("Campus *", AppTheme.FieldCaptionFont).Width + 12,
            Font = AppTheme.FieldCaptionFont, TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
        campus.Width = AppScale.Scale(300);
        campus.Height = actionHeight;
        campus.Margin = new Padding(0, 0, 16, 0);
        UiAccessibility.Describe(campus, "Campus", "Required");
        tools.Controls.Add(caption);
        tools.Controls.Add(campus);
        foreach (var button in new[] { applyButton }.Concat(matrixActions))
        {
            button.AutoSize = false;
            button.Margin = new Padding(0, 0, 8, 0);
            tools.Controls.Add(button);
        }
        controlsRow.Controls.Add(tools);
        void CenterTools()
        {
            if (controlsRow.IsDisposed) return;
            tools.Location = new Point(Math.Max(0, (controlsRow.ClientSize.Width - tools.Width) / 2),
                Math.Max(0, (controlsRow.ClientSize.Height - tools.Height) / 2));
        }
        controlsRow.SizeChanged += (_, _) => CenterTools();
        tools.SizeChanged += (_, _) => CenterTools();
        var matrixLayout = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        matrixLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        matrixLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, actionHeight + AppScale.Scale(16)));
        matrixLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        matrixLayout.Controls.Add(controlsRow, 0, 0);
        matrixLayout.Controls.Add(matrix, 0, 1);
        matrixCard.Content.Controls.Add(matrixLayout);
        SetContent(matrixCard);
        UiHelper.ApplyGridPresentation(matrix);
        UiHelper.UseEqualColumnWidths(matrix);
        Defer(CenterTools);
        UpdateAssignmentSummary();

    }
    private void BuildMatrix()
    {
        matrix.Columns.Clear();
        matrix.AutoGenerateColumns = false;
        matrix.AllowUserToAddRows = false;
        matrix.AllowUserToDeleteRows = false;
        matrix.AllowUserToResizeRows = false;
        matrix.AllowUserToResizeColumns = true;
        matrix.RowHeadersVisible = false;
        matrix.MultiSelect = true;
        matrix.SelectionMode = DataGridViewSelectionMode.CellSelect;
        matrix.EditMode = DataGridViewEditMode.EditProgrammatically;
        matrix.BorderStyle = BorderStyle.None;
        matrix.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        matrix.BackgroundColor = AppTheme.Surface;
        matrix.GridColor = AppTheme.Border;
        matrix.EnableHeadersVisualStyles = false;
        matrix.ColumnHeadersHeight =
            AppMetrics.GridHeaderHeight;
        matrix.ColumnHeadersDefaultCellStyle =
            new DataGridViewCellStyle {
                BackColor = AppTheme.ModuleAccent("Setup"),
                ForeColor = Color.White,
                Font = AppTheme.ButtonFont,
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                Padding = new Padding(8, 0, 8, 0),
                SelectionBackColor = AppTheme.ModuleAccent("Setup"),
                SelectionForeColor = Color.White
            };
        matrix.RowTemplate.Height = AppMetrics.GridRowHeight;
        matrix.DefaultCellStyle =
            new DataGridViewCellStyle {
                BackColor = AppTheme.Surface,
                ForeColor = AppTheme.Text,
                Font = AppTheme.NormalFont,
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                SelectionBackColor = AppTheme.PrimaryLight,
                SelectionForeColor = AppTheme.Text,
                Padding = new Padding(6, 0, 6, 0)
            };
        matrix.AlternatingRowsDefaultCellStyle =
            new DataGridViewCellStyle {
                BackColor = AppTheme.SurfaceSecondary,
                ForeColor = AppTheme.Text,
                Font = AppTheme.NormalFont,
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                SelectionBackColor = AppTheme.PrimaryLight,
                SelectionForeColor = AppTheme.Text,
                Padding = new Padding(6, 0, 6, 0)
            };
        var classColumn =
            new DataGridViewTextBoxColumn {
                Name = "Class",
                HeaderText = "CLASS",
                ReadOnly = true,
                Frozen = true,
                ToolTipText = "Click a class name to assign all its sections, or clear them if all are assigned.",
                Width = AppScale.Scale(220),
                MinimumWidth = AppScale.Scale(180),
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        classColumn.DefaultCellStyle =
            new DataGridViewCellStyle {
                BackColor = AppTheme.Surface,
                ForeColor = AppTheme.Text,
                Font = AppTheme.ButtonFont,
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 10, 0),
                SelectionBackColor = AppTheme.PrimaryLight,
                SelectionForeColor = AppTheme.Text
            };
        matrix.Columns.Add(classColumn);
        matrix.CellValueChanged += (_, _) => { if (!updatingMatrix) UpdateAssignmentSummary(); };
        matrix.SelectionChanged += (_, _) => { if (!updatingMatrix) UpdateAssignmentSummary(); };
        // Own the toggle so the painted checkbox and the rest of the cell share
        // one hit area. Read-only cells prevent a second native checkbox toggle.
        matrix.CellMouseDown += (_, e) => {
            if (!matrixReady || e.Button != MouseButtons.Left || e.Clicks > 1 ||
                e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (e.ColumnIndex == 0)
                ToggleAssignments(matrix.Rows[e.RowIndex].Cells.Cast<DataGridViewCell>().Where(c => c.ColumnIndex > 0));
            else
                BeginAssignmentGesture(e.RowIndex, e.ColumnIndex, ModifierKeys);
        };
        matrix.MouseMove += (_, e) => {
            if (e.Button != MouseButtons.Left || dragStart is null) return;
            var hit = matrix.HitTest(e.X, e.Y);
            ExtendAssignmentGesture(hit.RowIndex, hit.ColumnIndex);
        };
        matrix.MouseUp += (_, _) => dragStart = null;
        matrix.MouseCaptureChanged += (_, _) => { if (!matrix.Capture) dragStart = null; };
        matrix.ColumnHeaderMouseClick += (_, e) => {
            if (e.ColumnIndex > 0 && matrixReady && e.Button == MouseButtons.Left && e.Clicks <= 1)
                ToggleAssignments(matrix.Rows.Cast<DataGridViewRow>().Select(row => row.Cells[e.ColumnIndex]));
        };
        matrix.KeyDown += (_, e) => {
            if (e.KeyCode != Keys.Space || !matrixReady) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            ToggleAssignments(HighlightedCells());
        };
        matrix.CellPainting +=
            (_, e) => {
                if (e.RowIndex < 0 ||
                    e.ColumnIndex <= 0 ||
                    matrix.Columns[e.ColumnIndex]
                        is not DataGridViewCheckBoxColumn)
                {
                    return;
                }
                e.PaintBackground(e.CellBounds, true);
                bool selected =
                    Convert.ToBoolean(e.FormattedValue ?? false);
                const int size = 22;
                int x =
                    e.CellBounds.X +
                    (e.CellBounds.Width - size) / 2;
                int y =
                    e.CellBounds.Y +
                    (e.CellBounds.Height - size) / 2;
                var box =
                    new Rectangle(x, y, size, size);
                using var borderPen =
                    new Pen(
                        selected
                            ? AppTheme.Primary
                            : AppTheme.Border,
                        2);
                using var fillBrush =
                    new SolidBrush(
                        selected
                            ? AppTheme.Primary
                            : AppTheme.Surface);
                // DataGridView supplies Graphics on every CellPainting event; it is never null here.
                e.Graphics!.FillRectangle(fillBrush, box);
                e.Graphics.DrawRectangle(borderPen, box);
                if (selected)
                {
                    using var checkPen =
                        new Pen(
                            Color.White,
                            2.5f) {
                            StartCap =
                                System.Drawing.Drawing2D.LineCap.Round,
                            EndCap =
                                System.Drawing.Drawing2D.LineCap.Round,
                            LineJoin =
                                System.Drawing.Drawing2D.LineJoin.Round
                        };
                    e.Graphics.DrawLines(
                        checkPen,
                        new[]
                        {
                        new Point(
                            box.Left + 5,
                            box.Top + 11),
                        new Point(
                            box.Left + 9,
                            box.Top + 15),
                        new Point( box.Left + 17, box.Top + 6)
                        });
                }
                e.Handled = true;
            };
        matrix.DataError +=
            (_, e) => {
                e.ThrowException = false;
            };
    }
    private void ManageCatalog(bool manageClasses)
    {
        if (!CurrentUser.HasPermission("System.Setup"))
        {
            Warn("School setup permission is required.");
            return;
        }
        using var dialog = new ClassSectionCatalogDialog(manageClasses);
        dialog.ShowDialog(FindForm());
        RefreshCatalogs();
    }

    private void RefreshCatalogs()
    {
        if (!CurrentUser.HasPermission("System.Setup")) return;
        // Preserve only local differences, not the entire stale baseline. Fresh
        // saved assignments remain authoritative for untouched combinations.
        if (matrixReady)
        {
            catalogDraft.Clear();
            foreach (var cell in AssignmentCells())
            {
                var key = (Convert.ToInt32(cell.OwningRow.Tag), sections[cell.ColumnIndex - 1].SectionId);
                bool selected = Convert.ToBoolean(cell.Value ?? false);
                if (selected != savedAssignments[key]) catalogDraft[key] = selected;
            }
        }
        try
        {
            // Read both catalogs before replacing either in-memory index.
            using var classRows = DatabaseHelper.QueryUser("GetClasses");
            using var sectionRows = DatabaseHelper.QueryUser("GetSections");
            var refreshedClasses = classRows.AsEnumerable().Select(row => new ClassInfo {
                ClassId = Convert.ToInt32(row["ClassId"]), ClassName = Convert.ToString(row["ClassName"]) ?? ""
            }).ToArray();
            var refreshedSections = sectionRows.AsEnumerable().Select(row => new SectionInfo {
                SectionId = Convert.ToInt32(row["SectionId"]), SectionName = Convert.ToString(row["SectionName"]) ?? ""
            }).ToArray();
            classes.Clear(); classes.AddRange(refreshedClasses);
            sections.Clear(); sections.AddRange(refreshedSections);
            LoadCampusMatrix();
            if (!matrixReady) return; // Retain the draft for Refresh Catalogs retry.
            RestoreCatalogDraft();
            SetStatus("Catalogs refreshed. New entries are unassigned; unapplied selections were retained.");
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void RestoreCatalogDraft()
    {
        updatingMatrix = true;
        try
        {
            foreach (var cell in AssignmentCells())
            {
                var key = (Convert.ToInt32(cell.OwningRow.Tag), sections[cell.ColumnIndex - 1].SectionId);
                if (catalogDraft.TryGetValue(key, out bool selected)) cell.Value = selected;
            }
            catalogDraft.Clear();
        }
        finally { updatingMatrix = false; UpdateAssignmentSummary(); }
    }

    private void BindLookups()
    {
        try
        {
            suppressCampusChanged = true;
            campus.Bind(
                DatabaseHelper.QueryUser("GetCampuses"), "CampusId", "CampusName");
            if (campus.Items.Count > 1)
                campus.SelectedIndex = 1;
            BindClasses();
            BindSections();
            suppressCampusChanged = false;
            LoadCampusMatrix();
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            suppressCampusChanged = false;
        }
    }
    private void BindClasses()
    {
        classes.Clear();
        var dt =
            DatabaseHelper.QueryUser("GetClasses");
        foreach (DataRow row in dt.Rows)
        {
            classes.Add(
                new ClassInfo {
                    ClassId =
                        Convert.ToInt32(
                            row["ClassId"]),
                    ClassName =
                        Convert.ToString(
                            row["ClassName"]) ?? string.Empty
                });
        }
    }
    private void BindSections()
    {
        sections.Clear();
        /* All defined sections are available for placement. */
        var dt =
            DatabaseHelper.QueryUser(
                "GetSections");
        foreach (DataRow row in dt.Rows)
        {
            sections.Add(
                new SectionInfo {
                    SectionId =
                        Convert.ToInt32(
                            row["SectionId"]),
                    SectionName =
                        Convert.ToString(
                            row["SectionName"]) ?? string.Empty
                });
        }
    }
    private void BuildMatrixColumns()
    {
        while (matrix.Columns.Count > 1)
            matrix.Columns.RemoveAt(1);
        foreach (var section in sections)
        {
            var column =
                new DataGridViewCheckBoxColumn {
                    Name =
                        $"Section_{section.SectionId}",
                    HeaderText =
                        section.SectionName,
                    ReadOnly = true,
                    ToolTipText = "Click this heading to assign this section to every class, or clear it if all are assigned. Click anywhere in a cell to toggle one assignment.",
                    MinimumWidth = AppScale.Scale(80),
                    AutoSizeMode =
                        DataGridViewAutoSizeColumnMode.None,
                    FillWeight = 12,
                    SortMode =
                        DataGridViewColumnSortMode.NotSortable,
                    FlatStyle =
                        FlatStyle.Flat
                };
            column.DefaultCellStyle =
                new DataGridViewCellStyle {
                    Alignment =
                        DataGridViewContentAlignment.MiddleCenter,
                    Padding =
                        new Padding(4),
                    BackColor =
                        AppTheme.Surface,
                    SelectionBackColor =
                        AppTheme.PrimaryLight,
                    SelectionForeColor =
                        AppTheme.Text
                };
            matrix.Columns.Add(column);
        }
    }
    /// <summary>every tick in the matrix, in one comparable string: each row's class id followed by its section ticks, in column order.</summary>
    private string MatrixSignature()
    {
        var parts = new List<string>();
        foreach (DataGridViewRow row in matrix.Rows)
        {
            if (row.IsNewRow || row.Tag is null)
                continue;
            parts.Add(Convert.ToString(row.Tag) ?? string.Empty);
            for (int column = 1;
                 column < matrix.Columns.Count;
                 column++)
            {
                parts.Add(
                    Convert.ToBoolean(row.Cells[column].Value ?? false)
                        ? "1"
                        : "0");
            }
        }
        return string.Join("|", parts);
    }
    /// <summary>True when the ticks on screen differ from the ones the last load put there, i.e. changes the operator has not applied yet.</summary>
    public bool HasUnsavedChanges =>
        loadedCampusId > 0 &&
        appliedMatrixSignature is not null &&
        !string.Equals(appliedMatrixSignature, MatrixSignature(), StringComparison.Ordinal);
    bool IUnsavedChanges.HasUnsavedChanges => false;
    public string UnsavedChangesMessage =>
        $"The class/section ticks for \"{campus.Text}\" have not been applied yet.";
    private void LoadCampusMatrix()
    {
        updatingMatrix = true;
        matrixReady = false;
        assignmentAnchor = null;
        dragStart = null;
        savedAssignments.Clear();
        loadedCampusId = 0;
        appliedMatrixSignature = null;
        try
        {
            matrix.Rows.Clear();
            if (campus.SelectedId <= 0)
            {
                /* nothing is loaded, so there is nothing unapplied either. */
                loadedCampusId = 0;
                appliedMatrixSignature = null;
                SetStatus("Select a campus to configure its class and section assignments.");
                return;
            }
            var existing =
                DatabaseHelper.QueryUser(
                    "GetClassSections",
                    DatabaseHelper.Int(
                        "@CampusId",
                        campus.SelectedId),
                    DatabaseHelper.Int(
                        "@ClassId",
                        0),
                    DatabaseHelper.Int("@SectionId", 0));
            var activePlacements =
                new HashSet<(int ClassId, int SectionId)>();
            foreach (DataRow row in existing.Rows)
            {
                activePlacements.Add(
                    (
                        Convert.ToInt32(
                            row["ClassId"]),
                        Convert.ToInt32(row["SectionId"])
                    ));
            }
            BuildMatrixColumns();
            foreach (var cls in classes)
            {
                int rowIndex =
                    matrix.Rows.Add();
                var row =
                    matrix.Rows[rowIndex];
                row.Cells["Class"].Value =
                    cls.ClassName;
                row.Cells["Class"].Style = matrix.Columns["Class"].DefaultCellStyle.Clone();
                row.Tag =
                    cls.ClassId;
                for (int column = 1;
                     column < matrix.Columns.Count;
                     column++)
                {
                    int sectionId =
                        sections[column - 1].SectionId;
                    row.Cells[column].Value =
                        activePlacements.Contains(
                            (
                                cls.ClassId,
                                sectionId
                            ));
                    savedAssignments[(cls.ClassId, sectionId)] = Convert.ToBoolean(row.Cells[column].Value);
                }
            }
            SetStatus($"{matrix.Rows.Count:N0} class(es) loaded for {campus.Text}.");
            /* The ticks just loaded are the baseline "unapplied changes" is measured against. Captured here rather than in the change handler so that the reload after Apply Changes resets it too. */
            loadedCampusId =
                campus.SelectedId;
            appliedMatrixSignature =
                MatrixSignature();
            matrixReady = true;
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            updatingMatrix = false;
            matrix.Enabled = matrixReady;
            UpdateAssignmentSummary();
        }
    }
    private IEnumerable<DataGridViewCell> AssignmentCells() =>
        matrix.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow && row.Tag is not null)
            .SelectMany(row => row.Cells.Cast<DataGridViewCell>().Where(cell => cell.ColumnIndex > 0));

    private void BeginAssignmentGesture(int row, int column, Keys modifiers)
    {
        if (!matrixReady || row < 0 || column <= 0 ||
            row >= matrix.Rows.Count || column >= matrix.Columns.Count) return;
        var clicked = new Point(column, row);
        if ((modifiers & Keys.Shift) != Keys.None && assignmentAnchor is Point anchor)
        {
            dragStart = anchor;
            dragAssigned = Convert.ToBoolean(matrix.Rows[anchor.Y].Cells[anchor.X].Value ?? false);
        }
        else
        {
            assignmentAnchor = dragStart = clicked;
            dragAssigned = !Convert.ToBoolean(matrix.Rows[row].Cells[column].Value ?? false);
        }
        ExtendAssignmentGesture(row, column);
    }

    private void ExtendAssignmentGesture(int row, int column)
    {
        if (!matrixReady || dragStart is not Point start || row < 0 || column <= 0 ||
            row >= matrix.Rows.Count || column >= matrix.Columns.Count) return;
        // Write assignment values directly; highlighting is only visual feedback.
        SetAssignments(AssignmentCells().Where(cell =>
            cell.RowIndex >= Math.Min(start.Y, row) && cell.RowIndex <= Math.Max(start.Y, row) &&
            cell.ColumnIndex >= Math.Min(start.X, column) && cell.ColumnIndex <= Math.Max(start.X, column)), dragAssigned);
    }

    private DataGridViewCell[] HighlightedCells() => matrix.SelectedCells.Cast<DataGridViewCell>()
        .Where(cell => cell.RowIndex >= 0 && cell.ColumnIndex > 0).ToArray();

    private void ToggleAssignments(IEnumerable<DataGridViewCell> source)
    {
        matrix.EndEdit();
        var cells = source.ToArray();
        if (cells.Length == 0) return;
        SetAssignments(cells, !cells.All(cell => Convert.ToBoolean(cell.Value ?? false)));
    }

    private void SetAssignments(IEnumerable<DataGridViewCell> source, bool selected)
    {
        if (!matrixReady || campus.SelectedId != loadedCampusId) return;
        matrix.EndEdit();
        updatingMatrix = true;
        try
        {
            foreach (var cell in source)
                cell.Value = selected;
        }
        finally
        {
            updatingMatrix = false;
            UpdateAssignmentSummary();
        }
        SetStatus("Unsaved assignments. Use Apply Changes to save or Reset Changes to restore.");
    }

    private void ResetChanges()
    {
        if (!matrixReady) return;
        matrix.EndEdit();
        updatingMatrix = true;
        try
        {
            foreach (var cell in AssignmentCells())
                cell.Value = savedAssignments[(Convert.ToInt32(cell.OwningRow.Tag), sections[cell.ColumnIndex - 1].SectionId)];
        }
        finally
        {
            updatingMatrix = false;
            UpdateAssignmentSummary();
        }
        SetStatus("Saved assignments restored. No changes to apply.");
    }

    private void UpdateAssignmentSummary()
    {
        int additions = 0, removals = 0;
        if (matrixReady)
        {
            foreach (var cell in AssignmentCells())
            {
                bool selected = Convert.ToBoolean(cell.Value ?? false);
                bool saved = savedAssignments[(Convert.ToInt32(cell.OwningRow.Tag), sections[cell.ColumnIndex - 1].SectionId)];
                if (selected && !saved) additions++;
                if (!selected && saved) removals++;
            }
        }
        foreach (var button in matrixActions) button.Enabled = matrixReady && matrix.Rows.Count > 0 && sections.Count > 0;
        applyButton.Enabled = resetButton.Enabled = matrixReady && additions + removals > 0;
    }
    private void SelectAll()
    {
        SetAssignments(AssignmentCells(), true);
    }
    private void ClearAll()
    {
        SetAssignments(AssignmentCells(), false);
    }
    private void Save()
    {
        matrix.EndEdit();
        if (!matrixReady || loadedCampusId != campus.SelectedId || !HasUnsavedChanges)
            return;
        if (!ValidateRequiredInputs())
            return;
        applyButton.Enabled = false;
        try
        {
            if (campus.SelectedId <= 0)
            {
                Warn("Select a campus first.");
                return;
            }
            var assignments = new DataTable();
            assignments.Columns.Add("ClassId", typeof(int));
            assignments.Columns.Add("SectionId", typeof(int));
            assignments.Columns.Add("IsSelected", typeof(bool));
            foreach (DataGridViewRow row in matrix.Rows)
            {
                if (row.IsNewRow ||
                    row.Tag is null)
                    continue;
                int classId =
                    Convert.ToInt32(row.Tag);
                for (int column = 1;
                     column < matrix.Columns.Count;
                     column++)
                {
                    int sectionId =
                        sections[column - 1].SectionId;
                    bool selected =
                        Convert.ToBoolean(row.Cells[column].Value ?? false);
                    assignments.Rows.Add(classId, sectionId, selected);
                }
            }
            DatabaseHelper.Execute(
                "SaveCampusClassSections",
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@CampusId",
                    campus.SelectedId),
                new SqlParameter(
                    "@Assignments",
                    System.Data.SqlDbType.Structured) {
                    TypeName =
                        "dbo.CampusClassSectionAssignmentType",
                    Value =
                        assignments
                });
            LoadCampusMatrix();
            if (matrixReady)
            {
                Info("Campus class and section assignments saved successfully.");
                matrix.Focus();
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            UpdateAssignmentSummary();
        }
    }
    private sealed class ClassInfo
    {
        public int ClassId { get; init; }
        public string ClassName { get; init; } = string.Empty;
    }
    private sealed class SectionInfo
    {
        public int SectionId { get; init; }
        public string SectionName { get; init; } = string.Empty;
    }
}
