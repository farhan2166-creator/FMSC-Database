namespace FMSC_SMS.Core;

/// <summary>Tab visits editable cells; readonly grids behave as a single keyboard stop.</summary>
internal class KeyboardDataGridView : DataGridView
{
    protected override bool ProcessDialogKey(Keys keyData) =>
        (keyData & Keys.KeyCode) == Keys.Tab ? MoveTab(keyData) : base.ProcessDialogKey(keyData);
    protected override bool ProcessDataGridViewKey(KeyEventArgs e) =>
        e.KeyCode == Keys.Tab ? MoveTab(e.KeyData) : base.ProcessDataGridViewKey(e);
    private bool MoveTab(Keys keys)
    {
        bool forward = (keys & Keys.Shift) == 0;
        if (!EndEdit()) return true;
        if (!ReadOnly && !StandardTab) {
            var columns = Columns.Cast<DataGridViewColumn>().Where(column => column.Visible).OrderBy(column => column.DisplayIndex).ToArray();
            var cells = Rows.Cast<DataGridViewRow>().Where(row => row.Visible && !row.IsNewRow)
                .SelectMany(row => columns.Select(column => row.Cells[column.Index])).ToArray();
            int current = CurrentCell == null ? (forward ? -1 : cells.Length) : Array.IndexOf(cells, CurrentCell);
            for (int index = current + (forward ? 1 : -1); index >= 0 && index < cells.Length; index += forward ? 1 : -1)
                if (!cells[index].ReadOnly) { CurrentCell = cells[index]; return true; }
        }
        Control currentControl = this;
        for (Control? scope = Parent; scope != null; scope = scope.Parent) {
            if (scope.SelectNextControl(currentControl, forward, true, true, false)) return true;
            currentControl = scope;
        }
        TopLevelControl?.SelectNextControl(null, forward, true, true, false);
        return true;
    }
}
