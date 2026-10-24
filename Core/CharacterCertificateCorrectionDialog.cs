using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Core;

internal sealed class CharacterCertificateCorrectionDialog : ApplicationWindowForm
{
    private readonly ModernComboBox exam = HumanUi.Combo();
    private readonly ModernDatePicker date = HumanUi.Date();
    private readonly Dictionary<string, ModernTextBox> inputs = new();
    internal SqlParameter[] Parameters { get; private set; } = Array.Empty<SqlParameter>();

    internal CharacterCertificateCorrectionDialog(DataRow row, bool reviewType)
    {
        Text = reviewType ? "Review Existing Certificate Type" : "Correct Character Certificate";
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(AppScale.Scale(740), AppScale.Scale(reviewType ? 360 : 410));
        MinimumSize = new Size(AppScale.Scale(550), AppScale.Scale(340));
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(AppMetrics.SpacingMd) };
        var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var guidance = new Label { AutoSize = true, MaximumSize = new Size(AppScale.Scale(680), 0), Text = reviewType
            ? "The original SSC/HSSC choice was not saved. Check the original document, choose its type and explain the evidence used."
            : "Correct the details entered for this certificate. For names or other Admission details, update Admission and use Issue Again. Earlier copies keep their original details." };
        fields.Controls.Add(guidance,0,0); fields.SetColumnSpan(guidance,2);
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        exam.Items.AddRange(new object[] { "SSC", "HSSC" });
        exam.SelectedIndex = Convert.ToString(row["ExamType"]) switch { "SSC" => 0, "HSSC" => 1, _ => -1 };
        AddField("Exam Type", exam);
        if (!reviewType)
        {
            foreach (var (key, label, max) in new[] { ("RollNumber", "BISE Roll No.", 50), ("Remarks", "Remarks", 1000) })
            {
                var input = HumanUi.TextBox(false, max); input.Text = Convert.ToString(row[key]) ?? "";
                inputs.Add(key, input); AddField(label, input);
            }
            date.Value = Convert.ToDateTime(row["CertificateDate"]); AddField("Certificate Date", date);
        }
        var reason = HumanUi.TextBox(false, 500); inputs.Add("Reason", reason); AddField(reviewType ? "Review Evidence / Reason" : "Correction Reason", reason);
        if(fields.GetColumn(reason.Parent!)!=0)
        {
            fields.SetCellPosition(reason.Parent!,new TableLayoutPanelCellPosition(0,fields.GetRow(reason.Parent!)+1));
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        fields.SetColumnSpan(reason.Parent!,2);
        var save = new ModernButton { Text = reviewType ? "Save Type Review" : "Save Replacement", Width = AppScale.Scale(190), Height = AppMetrics.ButtonHeight };
        var cancel = new ModernButton { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = AppScale.Scale(110), Height = AppMetrics.ButtonHeight };
        var actions = HumanUi.ButtonRow(save, cancel); actions.Dock = DockStyle.Bottom;
        save.Click += (_, _) => {
            if (exam.SelectedIndex < 0 || string.IsNullOrWhiteSpace(reason.Text))
            { ModernMessageDialog.ShowWarning(this, "Choose SSC or HSSC and enter a reason."); return; }
            var values = new List<SqlParameter> { DatabaseHelper.P("@ExamType", exam.SelectedItem), DatabaseHelper.P("@Reason", reason.Text.Trim()) };
            if (!reviewType)
            {
                if (date.Value.Date > DateTime.Today || new[] { "RollNumber", "Remarks" }.Any(key => string.IsNullOrWhiteSpace(inputs[key].Text)))
                { ModernMessageDialog.ShowWarning(this, "Complete the roll number and remarks. The certificate date cannot be in the future."); return; }
                foreach (string key in new[] { "RollNumber", "Remarks" }) values.Add(DatabaseHelper.P("@" + key, DatabaseHelper.OptionalText(inputs[key].Text)));
                values.Add(DatabaseHelper.Date("@CertificateDate", date.Value.Date));
            }
            Parameters = values.ToArray(); DialogResult = DialogResult.OK;
        };
        scroll.Controls.Add(fields); Controls.Add(scroll); Controls.Add(actions); CancelButton = cancel;
        void AddField(string caption, Control input, bool required=true)
        {
            int index=fields.Controls.Count-1;
            var field = CertificateUi.Field(caption, input, required); field.Height = AppMetrics.FieldRowHeight; field.Dock = DockStyle.Top;
            if(index%2==0)fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.Controls.Add(field,index%2,index/2+1);
        }
    }
}
