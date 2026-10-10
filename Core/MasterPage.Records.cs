using System.Globalization;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Core;

public partial class MasterPage
{
    private void ScheduleGridColumnLayout()
    {
        if (_gridLayoutPending ||
            !Grid.IsHandleCreated)
        {
            return;
        }
        _gridLayoutPending = true;
        BeginInvoke(
            new Action(() => {
                _gridLayoutPending = false;
                MasterGridLayout.Apply(Grid, IsBooleanGridColumn);
            }));
    }
    private DataTable SortForDisplay(
        DataTable table)
    {
        // GetClasses orders by academic program position ASC, then class position ASC.
        // Sorting by program name here would override the configured program order.
        if (_getProc == "GetClasses")
            return LookupDisplayOrder.ForBinding(table, "ClassId");
        var sortParts = new List<string>();
        if (table.Columns.Contains("IsCurrent"))
            sortParts.Add("IsCurrent DESC");
        if (table.Columns.Contains("DisplayOrder"))
            sortParts.Add("DisplayOrder ASC");
        table.DefaultView.Sort =
            string.Join(", ", sortParts);
        return table.DefaultView.ToTable();
    }
    private static bool IsBooleanGridColumn(
        DataGridViewColumn? column)
    {
        if (column is null)
            return false;
        return column.Name.Equals(
                   "IsCurrent",
                   StringComparison.OrdinalIgnoreCase);
    }
    private void ValidateReorderConfiguration()
    {
        bool anyReorderSetting =
            !string.IsNullOrWhiteSpace(_reorderProcedure) ||
            !string.IsNullOrWhiteSpace(_reorderScopeField) ||
            !string.IsNullOrWhiteSpace(_reorderItemField);
        if (!anyReorderSetting)
            return;
        if (string.IsNullOrWhiteSpace(_reorderProcedure) ||
            string.IsNullOrWhiteSpace(_reorderItemField))
        {
            throw new InvalidOperationException(
                "Reorder configuration requires both a reorder procedure and item field.");
        }
        if (!string.IsNullOrWhiteSpace(_reorderScopeField) &&
            !_fields.Any(f =>
                string.Equals(
                    f.Key,
                    _reorderScopeField,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Reorder scope field '{_reorderScopeField}' was not found in the master fields.");
        }
    }
    private void ValidateDeleteConfiguration()
    {
        bool anyDeleteSetting =
            !string.IsNullOrWhiteSpace(_deleteProcedure) ||
            !string.IsNullOrWhiteSpace(_deleteIdParameter) ||
            !string.IsNullOrWhiteSpace(_deleteDisplayField);
        if (!anyDeleteSetting)
            return;
        if (string.IsNullOrWhiteSpace(_deleteProcedure) ||
            string.IsNullOrWhiteSpace(_deleteIdParameter))
        {
            throw new InvalidOperationException(
                "Delete configuration requires both a delete procedure and ID parameter.");
        }
    }
    private bool HasDeleteConfiguration =>
        !string.IsNullOrWhiteSpace(_deleteProcedure) &&
        !string.IsNullOrWhiteSpace(_deleteIdParameter);
    /// <summary>Hides the delete cross on rows the delete procedure would refuse.</summary>
    private void WireDeleteBlockedDisplay()
    {
        if (string.IsNullOrWhiteSpace(_deleteBlockedProcedure) ||
            !HasDeleteConfiguration)
        {
            return;
        }
        Grid.CellPainting += (_, e) => {
            if (e.RowIndex < 0 ||
                e.ColumnIndex < 0 ||
                e.ColumnIndex >= Grid.Columns.Count ||
                Grid.Columns[e.ColumnIndex].Name != "__Delete" ||
                !IsDeleteBlockedRow(e.RowIndex))
            {
                return;
            }
            // Background, selection and borders as usual; only the button and its cross are left out.
            e.Paint(
                e.ClipBounds,
                e.PaintParts &
                ~(DataGridViewPaintParts.ContentBackground |
                  DataGridViewPaintParts.ContentForeground));
            e.Handled = true;
        };
        Grid.CellToolTipTextNeeded += (_, e) => {
            if (e.RowIndex >= 0 &&
                e.ColumnIndex >= 0 &&
                e.ColumnIndex < Grid.Columns.Count &&
                Grid.Columns[e.ColumnIndex].Name == "__Delete" &&
                IsDeleteBlockedRow(e.RowIndex))
            {
                e.ToolTipText = "In use, so it cannot be deleted.";
            }
        };
        VisibleChanged += (_, _) => {
            if (Visible &&
                _recordsTable is not null)
            {
                _ = RefreshDeleteBlockedAsync();
            }
        };
    }
    private bool IsDeleteBlockedRow(int rowIndex)
    {
        if (_deleteBlockedIds is null ||
            _deleteBlockedIds.Count == 0 ||
            rowIndex < 0 ||
            rowIndex >= Grid.Rows.Count)
        {
            return false;
        }
        DataGridViewRow row = Grid.Rows[rowIndex];
        if (row.IsNewRow)
            return false;
        object? id = FindRowId(row);
        return id is not null &&
               id != DBNull.Value &&
               int.TryParse(
                   Convert.ToString(id, CultureInfo.InvariantCulture),
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out int recordId) &&
               _deleteBlockedIds.Contains(recordId);
    }
    /// <summary>Runs on a background thread.</summary>
    private HashSet<int>? QueryDeleteBlockedIds()
        => MasterDeleteBlockReader.Query(_deleteBlockedProcedure, HasDeleteConfiguration);
    private async Task RefreshDeleteBlockedAsync()
    {
        if (string.IsNullOrWhiteSpace(_deleteBlockedProcedure) ||
            !HasDeleteConfiguration)
        {
            return;
        }
        int version = _reloadSession.Generation;
        HashSet<int>? blocked =
            await Task.Run(QueryDeleteBlockedIds);
        // A reload that started meanwhile reads the list itself.
        if (IsDisposed ||
            version != _reloadSession.Generation)
        {
            return;
        }
        _deleteBlockedIds = blocked;
        Grid.Invalidate();
    }
    /* Deleting a record is a business operation, so it returns Task; the CellContentClick wiring is the only event boundary. */
    private async Task DeleteRecordAtRow(int rowIndex)
    {
        if (!HasDeleteConfiguration ||
            rowIndex < 0 ||
            rowIndex >= Grid.Rows.Count)
        {
            return;
        }
        DataGridViewRow row = Grid.Rows[rowIndex];
        if (row.IsNewRow)
            return;
        /* The cross is not drawn on these rows, but its click area still exists and Space on the focused cell still presses it. */
        if (IsDeleteBlockedRow(rowIndex))
            return;
        object? id = FindRowId(row);
        if (id is null ||
            id == DBNull.Value)
        {
            SetStatus("Unable to determine the selected record.", true);
            return;
        }
        int recordId;
        try
        {
            recordId = Convert.ToInt32(
                id,
                CultureInfo.InvariantCulture);
        }
        catch (Exception nonFatalException8)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException8, "MasterPage.Records: DeleteRecordAtRow fallback");
            SetStatus("Unable to determine the selected record.", true);
            return;
        }
        string displayName = GetDeleteDisplayName(row);
        string entityLabel = MasterEntityNaming.Singularize(NavigationTitle).ToLowerInvariant();
        string usageWarning = string.Empty;
        if (!string.IsNullOrWhiteSpace(_usageCountProcedure) &&
            !string.IsNullOrWhiteSpace(_usageCountIdParameter))
        {
            try
            {
                var count = Convert.ToInt32(
                    await DatabaseHelper.ScalarUserAsync(
                        _usageCountProcedure!,
                        DatabaseHelper.Int(
                            _usageCountIdParameter!,
                            recordId)));
                if (count > 0)
                {
                    string label = string.IsNullOrWhiteSpace(_usageCountLabel)
                        ? "linked record(s)"
                        : _usageCountLabel!;
                    usageWarning =
                        $"This {entityLabel} has {count:N0} {label}.\n\n";
                }
            }
            catch (Exception nonFatalException9)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException9, "MasterPage.Records: DeleteRecordAtRow fallback");
                // Non-critical; the delete procedure itself still enforces referential safety with its own specific error message.
            }
        }
        var confirm = ModernMessageDialog.Confirm(
            FindForm(),
            usageWarning +
            (string.IsNullOrWhiteSpace(displayName)
                ? $"Delete this {entityLabel}? This cannot be undone."
                : $"Delete the {entityLabel} \"{displayName}\"? This cannot be undone."),
            "Delete Record");
        if (!confirm)
            return;
        try
        {
            await DatabaseHelper.ExecuteUserAsync(
                _deleteProcedure!,
                DatabaseHelper.Int(
                    _deleteIdParameter!,
                    recordId));
            AfterDelete(recordId);
            /* Deleting one record must not erase unsaved work held in the editor for another one. */
            if (recordId == _id)
            {
                _id = 0;
                Clear();
            }
            await ReloadAsync();
            Info("Record deleted successfully.");
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    private string GetDeleteDisplayName(DataGridViewRow row)
    {
        if (!string.IsNullOrWhiteSpace(_deleteDisplayField) &&
            Grid.Columns.Contains(_deleteDisplayField))
        {
            object? value =
                row.Cells[_deleteDisplayField].Value;
            if (value is not null && value != DBNull.Value)
                return Convert.ToString(
                           value,
                           CultureInfo.InvariantCulture)
                       ?? string.Empty;
        }
        foreach (string field in new[]
                 {
                     "Name",
                     "SubjectName",
                     "ClassName",
                     "SectionName",
                     "CampusName",
                     "YearName",
                     "FeeTypeName",
                     "StatusName",
                     "PeriodName"
                 })
        {
            if (!Grid.Columns.Contains(field))
                continue;
            object? value = row.Cells[field].Value;
            if (value is not null && value != DBNull.Value)
                return Convert.ToString(
                           value,
                           CultureInfo.InvariantCulture)
                       ?? string.Empty;
        }
        return string.Empty;
    }
    private bool HasReorderConfiguration =>
        !string.IsNullOrWhiteSpace(_reorderProcedure) &&
        !string.IsNullOrWhiteSpace(_reorderItemField);
    private bool HasDisplayOrder =>
        HasReorderConfiguration;
    protected virtual DataTable QueryCurrentTable()
    {
        DataTable table = _getParams is null
            ? DatabaseHelper.QueryUser(_getProc)
            : DatabaseHelper.Query(
                _getProc,
                _getParams());
        return ShapeRecords(table);
    }
    /// <summary>Lets a derived page adjust the rows the records procedure returned before they are shown or used - add a column the procedure does not carry, drop one that is only noise.</summary>
    protected virtual DataTable ShapeRecords(DataTable table) => table;
    /// <summary>Runs after the delete procedure has committed and before this page reloads itself - the place for a derived page to tell other screens that read the same list.</summary>
    protected virtual void AfterDelete(int id)
    {
    }
    private int RowId(DataRow row)
    {
        if (_recordIdColumn is not null)
        {
            if (!row.Table.Columns.Contains(_recordIdColumn))
                throw new InvalidOperationException($"Records must include '{_recordIdColumn}'.");
            return row[_recordIdColumn] is DBNull ? 0 : Convert.ToInt32(row[_recordIdColumn], CultureInfo.InvariantCulture);
        }
        foreach (DataColumn column
                 in row.Table.Columns)
        {
            if (!column.ColumnName.EndsWith(
                    "Id",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (_fields.Any(field =>
                    string.Equals(
                        field.Key,
                        column.ColumnName,
                        StringComparison.OrdinalIgnoreCase)) ||
                column.ColumnName.Equals(
                    "CreatedByUserId",
                    StringComparison.OrdinalIgnoreCase) ||
                column.ColumnName.Equals(
                    "UpdatedByUserId",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            object value = row[column];
            if (value != DBNull.Value &&
                int.TryParse(
                    Convert.ToString(
                        value,
                        CultureInfo.InvariantCulture),
                    out int id))
            {
                return id;
            }
        }
        return 0;
    }
    private object? FindRowId(
        DataGridViewRow row)
    {
        if (_recordIdColumn is not null)
        {
            if (row.DataBoundItem is DataRowView record)
                return record.Row[_recordIdColumn];
            if (!Grid.Columns.Contains(_recordIdColumn))
                throw new InvalidOperationException($"Records must include '{_recordIdColumn}'.");
            return row.Cells[_recordIdColumn].Value;
        }
        foreach (DataGridViewCell cell
                 in row.Cells)
        {
            string? name =
                cell.OwningColumn?.Name;
            if (string.IsNullOrWhiteSpace(name) ||
                !name.EndsWith(
                    "Id",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (_fields.Any(field =>
                    string.Equals(
                        field.Key,
                        name,
                        StringComparison.OrdinalIgnoreCase)) ||
                name.Equals(
                    "CreatedByUserId",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Equals(
                    "UpdatedByUserId",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            return cell.Value;
        }
        return null;
    }
    private int FindGridRowIndex(
        int id)
    {
        foreach (DataGridViewRow row in Grid.Rows)
        {
            if (row.IsNewRow)
                continue;
            object? rowId = FindRowId(row);
            if (rowId is null ||
                rowId == DBNull.Value)
            {
                continue;
            }
            try
            {
                if (Convert.ToInt32(
                        rowId,
                        CultureInfo.InvariantCulture) == id)
                {
                    return row.Index;
                }
            }
            catch (Exception nonFatalException10)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException10, "MasterPage.Records: FindGridRowIndex fallback");
                continue;
            }
        }
        return -1;
    }
    private bool SameOrderScope(
        DataRow row,
        Dictionary<string, object?> values)
    {
        if (string.IsNullOrWhiteSpace(
                _reorderScopeField))
        {
            return true;
        }
        string scopeField =
            _reorderScopeField!;
        if (!row.Table.Columns.Contains(
                scopeField))
        {
            return false;
        }
        object? rowValue =
            row[scopeField] == DBNull.Value
                ? null
                : row[scopeField];
        object? currentValue =
            values.TryGetValue(
                scopeField,
                out var value)
                ? value
                : null;
        int rowId =
            rowValue is null
                ? 0
                : Convert.ToInt32(
                    rowValue,
                    CultureInfo.InvariantCulture);
        int currentId =
            currentValue is null
                ? 0
                : Convert.ToInt32(
                    currentValue,
                    CultureInfo.InvariantCulture);
        return rowId == currentId;
    }
    private int GetOrderId(
        DataRow row)
    {
        if (string.IsNullOrWhiteSpace(
                _reorderItemField))
        {
            return RowId(row);
        }
        string itemField =
            _reorderItemField!;
        if (!row.Table.Columns.Contains(
                itemField))
        {
            return RowId(row);
        }
        object value =
            row[itemField];
        return value == DBNull.Value
            ? 0
            : Convert.ToInt32(
                value,
                CultureInfo.InvariantCulture);
    }
    private List<DataRow> GetOrderedScopeRows(
        DataTable table,
        Dictionary<string, object?> values)
    {
        if (!table.Columns.Contains(
                "DisplayOrder"))
        {
            return new List<DataRow>();
        }
        return table.Rows
            .Cast<DataRow>()
            .Where(row =>
                SameOrderScope(
                    row,
                    values))
            .OrderBy(row =>
                Convert.ToInt32(
                    row["DisplayOrder"] ==
                    DBNull.Value
                        ? 0
                        : row["DisplayOrder"],
                    CultureInfo.InvariantCulture))
            .ThenBy(GetOrderId)
            .ToList();
    }
    private async Task MoveRecordAtRow(
    int rowIndex,
    int direction)
    {
        if (!HasReorderConfiguration ||
            rowIndex < 0 ||
            rowIndex >= Grid.Rows.Count)
        {
            return;
        }
        DataGridViewRow row = Grid.Rows[rowIndex];
        if (row.IsNewRow)
            return;
        object? id =
            FindRowId(row);
        if (id is null ||
            id == DBNull.Value)
        {
            return;
        }
        int recordId =
            Convert.ToInt32(
                id,
                CultureInfo.InvariantCulture);
        /* A reorder arrow is a grid-row action, not a record switch, so it must not retarget the editor. */
        var reorderValues =
            new Dictionary<string, object?>();
        reorderValues["Id"] = recordId;
        if (!string.IsNullOrWhiteSpace(_reorderScopeField))
        {
            string scopeField =
                _reorderScopeField!;
            DataGridViewCell? scopeCell =
                row.Cells
                    .Cast<DataGridViewCell>()
                    .FirstOrDefault(cell =>
                        string.Equals(
                            cell.OwningColumn?.Name,
                            scopeField,
                            StringComparison.OrdinalIgnoreCase));
            if (scopeCell is null ||
                scopeCell.Value is null ||
                scopeCell.Value == DBNull.Value)
            {
                SetStatus(
                    $"Unable to determine reorder scope '{scopeField}'.",
                    true);
                return;
            }
            reorderValues[scopeField] =
                Convert.ToInt32(
                    scopeCell.Value,
                    CultureInfo.InvariantCulture);
        }
        await MoveCurrentRecord(
            recordId,
            direction,
            reorderValues);
    }
    private async Task MoveCurrentRecord(
    int recordId,
    int direction,
    Dictionary<string, object?> currentValues)
    {
        if (!HasReorderConfiguration ||
            recordId <= 0)
        {
            return;
        }
        try
        {
            DataTable table =
                QueryCurrentTable();
            if (!table.Columns.Contains("DisplayOrder"))
            {
                SetStatus(
                    "DisplayOrder is not available in the records.",
                    true);
                return;
            }
            var rows =
                GetOrderedScopeRows(
                    table,
                    currentValues);
            if (rows.Count < 2)
            {
                SetStatus(
                    "There are not enough records in this ordering group.",
                    true);
                return;
            }
            int currentIndex =
                rows.FindIndex(
                    row =>
                        GetOrderId(row) == recordId);
            if (currentIndex < 0)
            {
                SetStatus(
                    "The selected record was not found in its ordering group.",
                    true);
                return;
            }
            int targetIndex =
                currentIndex + direction;
            if (targetIndex < 0 ||
                targetIndex >= rows.Count)
            {
                SetStatus(
                    direction < 0
                        ? "Already at the top."
                        : "Already at the bottom.",
                    true);
                return;
            }
            var ids =
                rows.Select(GetOrderId)
                    .ToList();
            int currentItemId =
                ids[currentIndex];
            int targetItemId =
                ids[targetIndex];
            ids[currentIndex] =
                targetItemId;
            ids[targetIndex] =
                currentItemId;
            Reorder(
                currentValues,
                ids);
            await ReloadAsync();
            SelectGridRow(recordId);
            SetStatus(
                direction < 0
                    ? "Record moved up."
                    : "Record moved down.");
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    private void Reorder(
        Dictionary<string, object?> currentValues,
        IReadOnlyList<int> orderedIds)
    {
        if (!HasReorderConfiguration)
            return;
        var items =
            new DataTable();
        items.Columns.Add(
            "ItemId",
            typeof(int));
        items.Columns.Add(
            "DisplayOrder",
            typeof(int));
        for (int i = 0;
             i < orderedIds.Count;
             i++)
        {
            items.Rows.Add(
                orderedIds[i],
                i + 1);
        }
        var parameters =
            new List<SqlParameter>
            {
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId)
            };
        if (!string.IsNullOrWhiteSpace(
                _reorderScopeField))
        {
            object? scopeValue =
                currentValues.TryGetValue(
                    _reorderScopeField!,
                    out var value)
                    ? value
                    : null;
            parameters.Add(
                DatabaseHelper.Int(
                    "@" + _reorderScopeField,
                    scopeValue is null
                        ? 0
                        : Convert.ToInt32(
                            scopeValue,
                            CultureInfo.InvariantCulture)));
        }
        var itemParameter =
            new SqlParameter(
                "@Items",
                SqlDbType.Structured) {
                TypeName =
                    "dbo.DisplayOrderItem",
                Value = items
            };
        parameters.Add(itemParameter);
        DatabaseHelper.ExecuteUser(
            _reorderProcedure!,
            parameters.ToArray());
    }
    private void NormalizeDisplayOrder(
        Dictionary<string, object?> currentValues)
    {
        if (!HasReorderConfiguration)
            return;
        DataTable table =
            QueryCurrentTable();
        var rows =
            GetOrderedScopeRows(
                table,
                currentValues);
        if (rows.Count == 0)
            return;
        var ids =
            rows.Select(GetOrderId)
                .Where(id => id > 0)
                .ToList();
        if (ids.Count == 0)
            return;
        bool alreadyNormalized =
            rows.Count == ids.Count &&
            rows.Select(
                    row =>
                        Convert.ToInt32(
                            row["DisplayOrder"] ==
                            DBNull.Value
                                ? 0
                                : row["DisplayOrder"],
                            CultureInfo.InvariantCulture))
                .Select(
                    (order, index) =>
                        order == index + 1)
                .All(x => x);
        if (alreadyNormalized)
            return;
        Reorder(
            currentValues,
            ids);
    }
    private void SelectGridRow(
        int id)
    {
        foreach (DataGridViewRow row
                 in Grid.Rows)
        {
            object? value =
                FindRowId(row);
            if (value is null ||
                value == DBNull.Value ||
                Convert.ToInt32(
                    value,
                    CultureInfo.InvariantCulture) != id)
            {
                continue;
            }
            row.Selected = true;
            DataGridViewCell? firstVisible =
                row.Cells
                    .Cast<DataGridViewCell>()
                    .FirstOrDefault(
                        cell => cell.Visible);
            if (firstVisible is not null)
                Grid.CurrentCell =
                    firstVisible;
            return;
        }
    }
    private void UpdateBooleanFromGrid(
    DataGridViewRow row,
    string columnName)
    {
        if (row.IsNewRow ||
            !Grid.Columns.Contains(columnName))
        {
            return;
        }
        object? id = FindRowId(row);
        if (id is null ||
            id == DBNull.Value)
        {
            return;
        }
        int recordId;
        try
        {
            recordId = Convert.ToInt32(
                id,
                CultureInfo.InvariantCulture);
        }
        catch (Exception nonFatalException11)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException11, "MasterPage.Records: UpdateBooleanFromGrid fallback");
            return;
        }
        object? cellValue =
            row.Cells[columnName].Value;
        bool newState =
            cellValue != null &&
            cellValue != DBNull.Value &&
            Convert.ToBoolean(cellValue);
        bool previousState = !newState;
        /* Saves and reloads the clicked row, replacing unsaved editor input. */
        if (!ConfirmDiscardChanges())
        {
            RestoreGridBoolean(
                row,
                columnName,
                previousState);
            return;
        }
        try
        {
            if (!LoadRow(row))
            {
                RestoreGridBoolean(row, columnName, previousState);
                return;
            }
            var values = Values();
            values["Id"] = recordId;
            values[columnName] = newState;
            if (HasReorderConfiguration)
            {
                values["DisplayOrder"] = 0;
            }
            DatabaseHelper.ExecuteUser(
                _saveProc,
                _buildParams(values));
            Reload();
            try
            {
                int rowIndex =
                    FindGridRowIndex(recordId);
                if (rowIndex >= 0 &&
                    Grid.Columns.Contains(columnName))
                {
                    Grid.CurrentCell =
                        Grid.Rows[rowIndex].Cells[columnName];
                }
            }
            catch (Exception nonFatalException12)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException12, "MasterPage.Records: UpdateBooleanFromGrid fallback");
            }
            SetStatus(
                GetBooleanStatusMessage(
                    columnName,
                    newState));
        }
        catch (Exception ex)
        {
            RestoreGridBoolean(
                row,
                columnName,
                previousState);
            Fail(ex);
        }
    }
    /// <summary>Puts a grid boolean back to the value it held before the operator toggled it, without re-entering the CellValueChanged handler that owns the toggle.</summary>
    private void RestoreGridBoolean(
        DataGridViewRow row,
        string columnName,
        bool previousState)
    {
        if (!Grid.Columns.Contains(columnName))
            return;
        _updatingGridBoolean = true;
        try
        {
            row.Cells[columnName].Value =
                previousState;
        }
        finally
        {
            _updatingGridBoolean = false;
        }
    }
    private static string GetBooleanStatusMessage(
        string columnName,
        bool value)
    {
        if (columnName.Equals(
                "IsCurrent",
                StringComparison.OrdinalIgnoreCase))
        {
            return value
                ? "Record marked as current."
                : "Record is no longer current.";
        }
        return value
            ? "Setting enabled."
            : "Setting disabled.";
    }
    protected new void Bind(
    DataTable table)
    {
        _recordsTable = SortForDisplay(table);
        if (_getProc == "GetStaff") _staffRecordPicker?.SetSource(_recordsTable);
        ApplyRecordSearch();
    }
    private Control BuildRecordsToolbar()
    {
        _recordSearchBox = HumanUi.TextBox();
        WorkspaceState.Remember(_recordSearchBox, "Records:Search");
        _recordSearchBox.PlaceholderText = "Search records...";
        _recordSearchBox.Height = AppMetrics.InputControlHeight;
        _recordCountLabel = new Label {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = AppTheme.FieldCaptionFont,
            ForeColor = AppTheme.MutedText,
            Padding = new Padding(UiDesignSystem.Spacing.Sm, 0, 0, 0)
        };
        var clearButton = new ModernButton {
            Text = UiText.Actions.Clear,
            Height = AppMetrics.InputControlHeight,
            ThemeRole = "Secondary",
            Image = UiHelper.CreateGlyphIcon("↺", AppTheme.MutedText, 16),
            ImageAlign = ContentAlignment.MiddleLeft,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Padding = new Padding(8, 0, 8, 0)
        };
        UiHelper.DisposeOwnedButtonImageOnDispose(clearButton);
        clearButton.Click += (_, _) => {
            _staffRecordPicker?.Clear();
            _recordSearchBox!.Clear();
            ApplyRecordSearch();
            if (_staffRecordPicker is not null) _staffRecordPicker.Focus();
            else _recordSearchBox.Focus();
        };
        int clearWidth = AppScale.Scale(105, AppScale.ControlSizeScale);
        int countWidth = AppScale.Scale(165, AppScale.ControlSizeScale);
        int columns = 4;
        var toolbar = new TableLayoutPanel {
            Dock = DockStyle.Top,
            Height = AppMetrics.InputControlHeight + UiDesignSystem.Spacing.Md,
            ColumnCount = columns,
            RowCount = 1,
            Padding = new Padding(0, 0, 0, UiDesignSystem.Spacing.Md),
            Margin = new Padding(0)
        };
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.InputControlHeight));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, clearWidth));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, countWidth));
        var searchIcon = new Label {
            Text = "⌕",
            Dock = DockStyle.Fill,
            Font = AppTheme.SymbolFont(16F),
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.MiddleCenter,
            Width = AppScale.Scale(30, AppScale.ControlSizeScale)
        };
        var searchHost = new Panel {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        searchHost.Controls.Add(_recordSearchBox);
        searchHost.Controls.Add(searchIcon);
        _recordSearchBox.Padding = new Padding(28, 0, 8, 0);
        if (_getProc is "GetStaff" or "GetStaffEmployment")
        {
            _staffRecordPicker = new StaffPicker();
            if (_getProc == "GetStaff")
                _staffRecordPicker.SelectionChangeGuard = _ => ConfirmDiscardChanges();
            _staffRecordPicker.UserSelectionChanged += (_, _) => {
                ApplyRecordSearch();
                if (_getProc == "GetStaff" && Grid.Rows.Count > 0)
                    LoadRow(Grid.Rows[0]);
            };
            _staffRecordPicker.SelectionCleared += (_, _) => ApplyRecordSearch();
            _staffRecordPicker.SearchFailed += ex => ModernMessageDialog.ShowWarning(this, DbMessage.UserMessage(ex));
            searchHost.Controls.Clear();
            searchHost.Controls.Add(_staffRecordPicker);
            _recordSearchBox.Visible = false;
            Disposed += (_, _) => { _recordSearchBox.Dispose(); searchIcon.Dispose(); };
        }
        toolbar.Controls.Add(searchHost, 0, 0);
        toolbar.Controls.Add(clearButton, 1, 0);
        toolbar.Controls.Add(_recordCountLabel, 2, 0);
        _recordSearchTimer = HumanUi.CreateSearchDebounceTimer(ApplyRecordSearch, 250);
        _recordSearchBox.TextChanged += (_, _) => {
            _recordSearchTimer!.Stop();
            _recordSearchTimer.Start();
        };
        Disposed += (_, _) => {
            _recordSearchTimer?.Dispose();
            _reloadSession.Dispose();
        };
        return toolbar;
    }
    private void ApplyRecordSearch()
    {
        if (_recordsTable is null)
            return;
        string term = (_recordSearchBox?.Text ?? string.Empty).Trim();
        int selectedId = 0;
        if (Grid.SelectedRows.Count == 1)
        {
            object? selectedValue = FindRowId(Grid.SelectedRows[0]);
            if (selectedValue is not null && selectedValue != DBNull.Value)
                int.TryParse(Convert.ToString(selectedValue, CultureInfo.InvariantCulture), out selectedId);
        }
        DataTable searchSource = _recordsTable;
        if (_staffRecordPicker is { SelectedId: > 0 } && _recordsTable.Columns.Contains("StaffId"))
        {
            searchSource = _recordsTable.Clone();
            foreach (DataRow row in _recordsTable.Rows)
                if (row["StaffId"] != DBNull.Value && Convert.ToInt32(row["StaffId"]) == _staffRecordPicker.SelectedId) searchSource.ImportRow(row);
        }
        MasterRecordFilter.Result filtered = MasterRecordFilter.Apply(searchSource, term);
        DataTable result = filtered.Table;
        UiHelper.SetGridEmptyMessage(Grid, filtered.EmptyText);
        base.Bind(filtered.Table);
        if (selectedId > 0)
            SelectGridRow(selectedId);
        if (_recordCountLabel is not null)
            _recordCountLabel.Text = filtered.CountText;
        SetStatus(filtered.StatusText);
        Grid.ReadOnly = false;
        foreach (DataGridViewColumn column in Grid.Columns)
        {
            if (MasterGridLayout.IsInternal(column))
                continue;
            column.ReadOnly = !IsBooleanGridColumn(column);
        }
        if (Grid.Columns.Contains("IsCurrent"))
        {
            Grid.Columns["IsCurrent"].ReadOnly = false;
            Grid.Columns["IsCurrent"].SortMode =
                DataGridViewColumnSortMode.Automatic;
            Grid.Columns["IsCurrent"].DisplayIndex =
                Grid.Columns.Count - 1;
        }
        if (Grid.Columns.Contains("DisplayOrder"))
        {
            Grid.Columns["DisplayOrder"].Visible = false;
            Grid.Columns["DisplayOrder"].HeaderText = "Display Order";
        }
        EnsureOrderButtons();
        EnsureDeleteButton();
        ScheduleGridColumnLayout();
    }
    protected void Reload() => _ = ReloadAsync();
    /// <summary>drops the editor back to "no record loaded" without asking, for a derived page whose record identity comes from a parent selection that has just moved (StaffEmploymentPage).</summary>
    protected void ResetEditorForContextChange()
    {
        _id = 0;
        Clear();
        CaptureEditorBaseline();
    }
    /// <summary>
    /// Marks values supplied by a parent workspace (rather than typed by the
    /// operator) as the clean starting state of the editor.
    /// </summary>
    protected void CaptureContextBaseline() => CaptureEditorBaseline();
    protected async Task ReloadAsync()
    {
        MasterReloadSession.Ticket ticket = _reloadSession.Begin();
        try
        {
            HashSet<int>? blocked = null;
            DataTable table = await Task.Run(() => {
                DataTable records = QueryCurrentTable();
                blocked = QueryDeleteBlockedIds();
                return records;
            });
            if (!IsDisposed && _reloadSession.IsCurrent(ticket))
            {
                _deleteBlockedIds = blocked;
                Bind(table);
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed && _reloadSession.IsCurrent(ticket))
                Fail(ex);
        }
    }
    private bool IsOrderColumn(
        int columnIndex)
    {
        if (columnIndex < 0 ||
            columnIndex >= Grid.Columns.Count)
        {
            return false;
        }
        string name =
            Grid.Columns[columnIndex].Name;
        return name is
            "__OrderUp" or
            "__OrderDown";
    }
    private int FirstOrderRowIndex()
    {
        for (int i = 0;
             i < Grid.Rows.Count;
             i++)
        {
            DataGridViewRow row = Grid.Rows[i];
            if (!row.IsNewRow)
            {
                return i;
            }
        }
        return -1;
    }
    private int LastOrderRowIndex()
    {
        for (int i = Grid.Rows.Count - 1;
             i >= 0;
             i--)
        {
            DataGridViewRow row = Grid.Rows[i];
            if (!row.IsNewRow)
            {
                return i;
            }
        }
        return -1;
    }
    private void EnsureDeleteButton()
    {
        if (!HasDeleteConfiguration)
            return;
        if (!Grid.Columns.Contains("__Delete"))
        {
            Grid.Columns.Add(
                CreateCrossActionColumn("__Delete"));
        }
        Grid.Columns["__Delete"].DisplayIndex =
            Grid.Columns.Count - 1;
        Grid.Columns["__Delete"].Visible = true;
    }
    private void EnsureOrderButtons()
    {
        if (!HasReorderConfiguration)
            return;
        MasterGridActionColumns.EnsureOrderButtons(Grid, Accent);
    }
}
