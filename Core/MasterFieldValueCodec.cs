namespace FMSC_SMS.Core;

internal static class MasterFieldValueCodec
{
    internal static object? Read(Control control) => control switch {
        TextBox text => string.IsNullOrWhiteSpace(text.Text) ? null : text.Text.Trim(),
        ModernNumericTextBox number => number.Value,
        ModernTextBox text => string.IsNullOrWhiteSpace(text.Text) ? null : text.Text.Trim(),
        DateTimePicker date => date.Format == DateTimePickerFormat.Time
            ? date.Value.TimeOfDay
            : date.ShowCheckBox && !date.Checked ? null : date.Value.Date,
        ModernDatePicker date => date.TimeOnly
            ? date.Value.TimeOfDay
            : date.ShowCheckBox && !date.Checked ? null : date.Value.Date,
        CheckBox check => check.Checked,
        ComboBox combo => combo.DataSource is null ? combo.SelectedItem : combo.SelectedValue,
        ModernComboBox combo => combo.DataSource is null
            ? string.IsNullOrWhiteSpace(combo.Text) ? null : combo.Text.Trim()
            : combo.SelectedValue,
        _ => null
    };

    internal static void Write(Control control, object? value)
    {
        // Every field belongs to the newly selected record, including SQL NULLs.
        // Leaving a previous value here can copy one person's data into another record.
        if (value is null or DBNull)
        {
            switch (control)
            {
                case ModernNumericTextBox number: number.Value = number.Minimum; break;
                case TextBox text: text.Clear(); break;
                case ModernTextBox text: text.Clear(); break;
                case DateTimePicker date:
                    date.Value = DateTime.Today;
                    if (date.ShowCheckBox) date.Checked = false;
                    break;
                case ModernDatePicker date:
                    date.Value = DateTime.Today;
                    if (date.ShowCheckBox) date.Checked = false;
                    break;
                case CheckBox check: check.Checked = false; break;
                case ComboBox combo: combo.SelectedIndex = -1; combo.Text = string.Empty; break;
                case ModernComboBox combo: combo.SelectedIndex = -1; combo.Text = string.Empty; break;
            }
            return;
        }
        switch (control)
        {
            case TextBox text: text.Text = Convert.ToString(value) ?? string.Empty; break;
            case ModernNumericTextBox number:
                number.Value = Math.Clamp(Convert.ToDecimal(value), number.Minimum, number.Maximum);
                break;
            case ModernTextBox text: text.Text = Convert.ToString(value) ?? string.Empty; break;
            case DateTimePicker date: WriteDate(date, value); break;
            case ModernDatePicker date: WriteDate(date, value); break;
            case CheckBox check: check.Checked = Convert.ToBoolean(value); break;
            case ComboBox combo:
                if (combo.DataSource is null) combo.SelectedItem = value; else combo.SelectedValue = value;
                break;
            case ModernComboBox combo:
                if (combo.DataSource is null) combo.SelectedItem = value; else combo.SelectedValue = value;
                break;
        }
    }

    private static void WriteDate(DateTimePicker date, object value)
    {
        if (date.Format == DateTimePickerFormat.Time && TimeSpan.TryParse(Convert.ToString(value), out TimeSpan time))
            date.Value = DateTime.Today.Add(time);
        else if (date.Format != DateTimePickerFormat.Time && DateTime.TryParse(Convert.ToString(value), out DateTime parsed))
        {
            date.Value = parsed;
            if (date.ShowCheckBox) date.Checked = true;
        }
    }

    private static void WriteDate(ModernDatePicker date, object value)
    {
        if (date.TimeOnly && TimeSpan.TryParse(Convert.ToString(value), out TimeSpan time))
            date.Value = DateTime.Today.Add(time);
        else if (!date.TimeOnly && DateTime.TryParse(Convert.ToString(value), out DateTime parsed))
        {
            date.Value = parsed;
            if (date.ShowCheckBox) date.Checked = true;
        }
    }
}
