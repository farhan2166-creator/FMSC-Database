using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Pages.Finance.Collection;

internal sealed class StudentCreditDialog : ApplicationWindowForm
{
    private readonly int admissionId;
    private readonly Label balance = new() { AutoSize = true, Padding = new Padding(8) };
    private readonly ModernComboBox action = HumanUi.Combo(), sibling = HumanUi.Combo();
    private readonly ModernTextBox amount = HumanUi.TextBox(false, 20), reason = HumanUi.TextBox(false, 500);
    private readonly DataGridView history = HumanUi.Grid(false);
    private readonly ModernButton save = new() { Text = "Review & Save", ThemeRole = "Primary" };
    private bool busy;

    internal StudentCreditDialog(int studentId, bool loadOnShow = true)
    {
        admissionId = studentId;
        Text = "Student Credit"; StartPosition = FormStartPosition.CenterParent;
        Size = new Size(AppScale.Scale(850), AppScale.Scale(620));
        MinimumSize = new Size(650, 450);
        action.Items.AddRange(new object[] { "Deposit", "Refund", "Transfer" }); action.SelectedIndex = 0;
        action.SelectedIndexChanged += (_, _) => sibling.Enabled = action.Text == "Transfer";
        sibling.Enabled = false;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        balance.Text = "Available credit will load here. Deposits are money received; refunds return cash; transfers move credit to a sibling.";
        balance.MaximumSize = new Size(AppScale.Scale(780), 0);
        root.Controls.Add(balance, 0, 0);
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight)); fields.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        fields.Controls.Add(HumanUi.Field("Action", action), 0, 0); fields.Controls.Add(HumanUi.Field("Amount (Rs.)", amount), 1, 0);
        fields.Controls.Add(HumanUi.Field("Transfer to sibling", sibling), 0, 1); fields.Controls.Add(HumanUi.Field("Reason", reason), 1, 1);
        root.Controls.Add(fields, 0, 1);
        history.Dock = DockStyle.Fill; root.Controls.Add(history, 0, 2);
        var close = new ModernButton { Text = "Close", DialogResult = DialogResult.Cancel };
        var buttons = HumanUi.ButtonRow(save, close); buttons.Dock = DockStyle.Fill;
        root.Controls.Add(buttons, 0, 3); Controls.Add(root); CancelButton = close;
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        save.Click += async (_, _) => await SaveAsync();
        if (loadOnShow) Shown += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            using DataSet data = await DatabaseHelper.QueryUserSetAsync("GetStudentCredit", DatabaseHelper.Int("@AdmissionId", admissionId));
            if (IsDisposed) return;
            balance.Text = $"Available credit: Rs. {Convert.ToDecimal(data.Tables[0].Rows[0]["AvailableCredit"]):N0}\n" +
                "Credit stays with this student across academic years and leaving/readmission. Using credit is not new money received.";
            history.DataSource = data.Tables[1].Copy();
            DataTable family = await DatabaseHelper.QuerySqlAsync("""
                EXEC dbo.RequirePermission @UserId,N'Fees.View';
                SELECT DISTINCT S.AdmissionId,S.StudentName FROM dbo.FamilyMembers A
                JOIN dbo.FamilyMembers B ON B.FamilyId=A.FamilyId JOIN dbo.Students S ON S.AdmissionId=B.AdmissionId
                WHERE A.AdmissionId=@AdmissionId AND B.AdmissionId<>@AdmissionId ORDER BY S.StudentName;
                """, DatabaseHelper.Int("@UserId", CurrentUser.UserId), DatabaseHelper.Int("@AdmissionId", admissionId));
            sibling.DataSource = family; sibling.DisplayMember = "StudentName"; sibling.ValueMember = "AdmissionId";
        }
        catch (Exception ex) { ModernMessageDialog.ShowError(this, DbMessage.UserMessage(ex), "Student Credit"); }
    }

    private async Task SaveAsync()
    {
        if (busy) return;
        if (!InputParse.TryWholeAmount(amount.Text, out decimal value) || value <= 0 || string.IsNullOrWhiteSpace(reason.Text))
        { ModernMessageDialog.ShowInfo(this, "Enter a positive whole-rupee amount and a reason."); return; }
        if (action.Text == "Transfer" && sibling.SelectedValue is not int)
        { ModernMessageDialog.ShowInfo(this, "Choose a sibling for this transfer."); return; }
        string operation = action.Text;
        int? target = operation == "Transfer" ? Convert.ToInt32(sibling.SelectedValue) : null;
        string effect = operation switch {
            "Deposit" => "Receive this money as credit toward fees. It remains available until used.",
            "Refund" => "Return this amount in cash and reduce the student's available credit.",
            _ => $"Move this credit to {sibling.Text}. Their future billing will use the credit automatically."
        };
        if (!ModernMessageDialog.Confirm(this, $"{effect}\nAmount: Rs. {value:N0}\nReason: {reason.Text.Trim()}", "Confirm Credit " + operation)) return;
        busy = true; save.Enabled = false;
        Guid request = Guid.NewGuid();
        CollectionRecoveryJournal? journal = null;
        try
        {
            journal = CollectionRecoveryJournal.Begin($"Student credit {operation}; student {admissionId}; Rs. {value}; request {request}; {reason.Text.Trim()}");
            DataTable result = await DatabaseHelper.QueryUserAsync("ProcessStudentCredit",
                DatabaseHelper.Int("@AdmissionId", admissionId), DatabaseHelper.P("@Action", operation),
                DatabaseHelper.P("@Amount", value), DatabaseHelper.P("@RequestId", request),
                DatabaseHelper.P("@Reason", reason.Text.Trim()), DatabaseHelper.P("@TargetAdmissionId", target));
            journal.Complete();
            string receipt = Convert.ToString(result.Rows[0]["ReceiptNumber"]) ?? "";
            ModernMessageDialog.ShowInfo(this, $"Credit {operation.ToLowerInvariant()} saved." + (receipt.Length > 0 ? $" Receipt: {receipt}." : ""));
            amount.Text = ""; reason.Text = "";
            await RefreshAsync();
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            journal?.Complete(); ModernMessageDialog.ShowError(this, DbMessage.UserMessage(ex), "Credit Not Saved");
        }
        catch (Exception ex)
        {
            ModernMessageDialog.ShowError(this, "The credit operation needs verification before another payment is entered. " +
                "Check Credit History and Payment History; do not repeat it.\n" + DbMessage.UserMessage(ex), "Credit Outcome Needs Review");
        }
        finally { journal?.Dispose(); busy = false; save.Enabled = true; }
    }
}
