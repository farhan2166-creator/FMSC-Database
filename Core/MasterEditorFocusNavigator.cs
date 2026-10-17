namespace FMSC_SMS.Core;

internal static class MasterEditorFocusNavigator
{
    internal static bool TryAdvance(Control? active, IEnumerable<Control> controls, Button? saveButton)
    {
        if (active is null || active is TextBoxBase { Multiline: true } ||
            active is ComboBox { DroppedDown: true } ||
            active is not (TextBoxBase or ComboBox or DateTimePicker)) return false;
        List<Control> ordered = controls.Where(c => c.Visible && c.Enabled).OrderBy(c => c.TabIndex).ToList();
        int index = ordered.FindIndex(c => c == active || c.Contains(active));
        if (index < 0) return false;
        for (int next = index + 1; next < ordered.Count; next++)
        {
            if (ordered[next] is CheckBox or RadioButton) continue;
            if (FocusInside(ordered[next])) return true;
        }
        // A second Enter (or Ctrl+S) saves; finishing an input only focuses Save,
        // matching the application's other data-entry forms.
        if (saveButton is { Enabled: true, Visible: true } && saveButton.Focus()) return true;
        return false;
    }

    internal static bool FocusInside(Control control)
    {
        if (control.IsDisposed || !control.Visible || !control.Enabled) return false;
        if (control is ModernTextBox { ReadOnly: true } or TextBoxBase { ReadOnly: true }) return false;
        // These controls wrap native inputs and hide Control.Focus rather than
        // overriding it. Calling through Control focuses the decorative panel.
        if (control is ModernTextBox modernText)
        {
            if (!modernText.TabStop || !modernText.Focus()) return false;
            if (!modernText.Multiline) modernText.SelectAll();
            return true;
        }
        if (control is ModernComboBox modernCombo)
            return modernCombo.TabStop && modernCombo.Focus();
        if (control is ModernDatePicker modernDate)
            return modernDate.TabStop && modernDate.Focus();
        if (control.CanFocus && control.TabStop && control.Focus())
        {
            if (control is TextBoxBase { Multiline: false } text) text.SelectAll();
            return true;
        }
        return control.Controls.Cast<Control>().Any(FocusInside);
    }
}
