using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Pages.Finance.Collection;

internal sealed class StudentCreditDialog : ApplicationWindowForm
{
    private readonly int admissionId;
    private readonly Label balance = new() { AutoSize = true, Padding = new Padding(8) };
    private readonly Label creditBalance = new() { AutoSize = true, Padding = new Padding(8, 0, 8, 6), Font = AppTheme.HeadingFont, ForeColor = AppTheme.Success, Text = "Available advance: loading…" };
    private readonly ModernComboBox action = HumanUi.Combo(), sibling = HumanUi.Combo();
    private readonly ModernTextBox amount = HumanUi.TextBox(false, 20), reason = HumanUi.TextBox(false, 500);
    private readonly DataGridView history = HumanUi.Grid(false);
    private readonly ModernButton save = new() { Text = "Save", ThemeRole = "Success" };
    private readonly Label savedStatus = new() { AutoSize = true, Dock = DockStyle.Fill,
        Padding = new Padding(8), ForeColor = AppTheme.Success, Visible = false };
    private bool busy;

    internal StudentCreditDialog(int studentId, bool loadOnShow = true)
    {
        admissionId = studentId;
        amount.ZeroAsPlaceholder = true;
        Text = "Student Advance Balance"; StartPosition = FormStartPosition.CenterParent;
        Size = new Size(AppScale.Scale(1050), AppScale.Scale(700));
        MinimumSize = new Size(AppScale.Scale(850), AppScale.Scale(520));
        action.Items.AddRange(new object[] { "Deposit", "Refund", "Transfer" }); action.SelectedIndex = 0;
        action.SelectedIndexChanged += (_, _) => { sibling.Enabled = action.Text == "Transfer"; if (action.Text == "Transfer" && sibling.Items.Count > 0) sibling.SelectedIndex = 0; else sibling.SelectedIndex = -1; };
        sibling.Enabled = false;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 6 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        balance.Text = "Unused advance money will load here. Deposit records new money; Refund returns unused money; Transfer moves it to a sibling.";
        balance.MaximumSize = new Size(AppScale.Scale(780), 0);
        root.Controls.Add(balance, 0, 0);
        root.Controls.Add(creditBalance, 0, 1);
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight)); fields.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        fields.Controls.Add(HumanUi.Field("Action", action), 0, 0); fields.Controls.Add(HumanUi.Field("Amount (Rs.)", amount), 1, 0);
        var siblingField = HumanUi.Field("Transfer to sibling", sibling);
        var reasonField = HumanUi.Field("Remarks (optional)", reason);
        fields.Controls.Add(siblingField, 0, 1); fields.Controls.Add(reasonField, 1, 1);
        void ArrangeCreditFields()
        {
            // Presentation only: retain the original action/recipient selection handler.
            bool transfer = action.Text == "Transfer";
            fields.SuspendLayout();
            siblingField.Visible = transfer;
            fields.SetColumn(reasonField, transfer ? 1 : 0);
            fields.SetColumnSpan(reasonField, transfer ? 1 : 2);
            fields.ResumeLayout(true);
        }
        action.SelectedIndexChanged += (_, _) => ArrangeCreditFields();
        ArrangeCreditFields();
        root.Controls.Add(fields, 0, 2);
        history.Dock = DockStyle.Fill; root.Controls.Add(history, 0, 3);
        ReportGridStyler.Apply(history, AppTheme.Primary);
        root.Controls.Add(savedStatus, 0, 4);
        var close = new ModernButton { Text = "Close", DialogResult = DialogResult.Cancel };
        var buttons = HumanUi.ButtonRow(save, close); buttons.Dock = DockStyle.Fill;
        root.Controls.Add(buttons, 0, 5); Controls.Add(root); CancelButton = close;
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        save.Click += async (_, _) => await SaveAsync();
        void ExplainAction()
        {
            var topic=action.Text switch {"Refund"=>FinanceHelp.Topic.RefundAdvance,"Transfer"=>FinanceHelp.Topic.TransferAdvance,_=>FinanceHelp.Topic.Deposit};
            FinanceHelp.Attach(this,action,topic);FinanceHelp.Attach(this,amount,topic);FinanceHelp.Attach(this,save,topic);
        }
        action.SelectedIndexChanged+=(_,_)=>ExplainAction();ExplainAction();
        FinanceHelp.Attach(this,sibling,FinanceHelp.Topic.TransferAdvance);
        FinanceHelp.Attach(this,creditBalance,FinanceHelp.Topic.AdvanceBalance);
        if (loadOnShow) Shown += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            using DataSet data = await DatabaseHelper.QueryUserSetAsync("GetStudentCredit", DatabaseHelper.Int("@AdmissionId", admissionId));
            var identities = await DatabaseHelper.QuerySqlAsync("""
                EXEC dbo.RequirePermission @UserId,N'Fees.View';
                SELECT S.AdmissionId,S.AdmissionNumber,S.StudentName FROM dbo.Students S
                WHERE S.AdmissionId=@AdmissionId OR EXISTS(
                    SELECT 1 FROM dbo.StudentCreditEntries C WHERE C.AdmissionId=@AdmissionId AND C.CounterpartyAdmissionId=S.AdmissionId);
                """, DatabaseHelper.Int("@UserId", CurrentUser.UserId), DatabaseHelper.Int("@AdmissionId", admissionId));
            if (IsDisposed) return;
            var owner = identities.AsEnumerable().FirstOrDefault(row => Convert.ToInt32(row["AdmissionId"]) == admissionId);
            string identity = owner is null ? "Student Credit" : $"{owner["AdmissionNumber"]} · {owner["StudentName"]}";
            Text = $"Student Advance Balance — {identity}";
            creditBalance.Text = $"Available advance: Rs. {Convert.ToDecimal(data.Tables[0].Rows[0]["AvailableCredit"]):N0}";
            balance.Text = $"{identity}\n" +
                "A plus amount adds to the advance balance; a minus amount reduces it. Transfers move advance money between siblings without receiving new money.";
            BindHistory(data.Tables[1], identities);
            DataTable family = await DatabaseHelper.QuerySqlAsync("""
                EXEC dbo.RequirePermission @UserId,N'Fees.View';
                SELECT DISTINCT S.AdmissionId,S.StudentName FROM dbo.FamilyMembers A
                JOIN dbo.FamilyMembers B ON B.FamilyId=A.FamilyId JOIN dbo.Students S ON S.AdmissionId=B.AdmissionId
                WHERE A.AdmissionId=@AdmissionId AND B.AdmissionId<>@AdmissionId ORDER BY S.StudentName;
                """, DatabaseHelper.Int("@UserId", CurrentUser.UserId), DatabaseHelper.Int("@AdmissionId", admissionId));
            sibling.DataSource = family; sibling.DisplayMember = "StudentName"; sibling.ValueMember = "AdmissionId";
            if (action.Text != "Transfer") sibling.SelectedIndex = -1;
        }
        catch (Exception ex) { ModernMessageDialog.ShowError(this, DbMessage.UserMessage(ex), "Student Credit"); }
    }

    private void BindHistory(DataTable source, DataTable identities)
    {
        var entries = source.Copy();
        // This timestamp is ledger history, so expose it with a presentation name
        // rather than the generic CreatedAt field hidden by shared grid styling.
        if (entries.Columns.Contains("CreatedAt")) entries.Columns["CreatedAt"]!.ColumnName = "TransactionTime";
        entries.Columns.Add("TransferDetails", typeof(string));
        foreach (DataRow entry in entries.Rows)
        {
            string kind = Convert.ToString(entry["EntryKind"]) ?? "";
            entry["TransferDetails"] = Convert.ToString(entry["Reason"]) ?? "";
            if (!entries.Columns.Contains("CounterpartyAdmissionId") || entry["CounterpartyAdmissionId"] is DBNull) continue;
            var other = identities.AsEnumerable().FirstOrDefault(row => Convert.ToInt32(row["AdmissionId"]) == Convert.ToInt32(entry["CounterpartyAdmissionId"]));
            if (other is not null)
            {
                string note = Convert.ToString(entry["Reason"]) ?? "";
                entry["TransferDetails"] = $"{(kind == "TransferIn" ? "From" : "To")} {other["AdmissionNumber"]} · {other["StudentName"]}" +
                    (string.IsNullOrWhiteSpace(note) ? "" : $" — {note}");
            }
        }
        history.DataSource = entries;
        foreach (string column in new[] { "CounterpartyAdmissionId", "CounterpartyAdmissionNumber", "Reason" })
            if (history.Columns.Contains(column)) history.Columns[column].Visible = false;
        history.Columns["TransferDetails"].HeaderText = "Details";
        history.Columns["TransferDetails"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        history.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders;
        history.Columns["EntryKind"].HeaderText = "Advance Activity";
        history.Columns["Amount"].HeaderText = "Advance Change (Rs.)";
        history.Columns["Amount"].DefaultCellStyle.Format = "+#,0.##;-#,0.##;0";
        if (history.Columns.Contains("TransactionTime"))
        {
            history.Columns["TransactionTime"].HeaderText = "Date / Time";
            history.Columns["TransactionTime"].DefaultCellStyle.Format = "dd-MMM-yyyy h:mm tt";
            history.Columns["TransactionTime"].DisplayIndex = 0;
        }
        history.CellFormatting -= FormatCreditActivity;
        history.CellFormatting += FormatCreditActivity;
        ReportGridStyler.Apply(history, AppTheme.Primary);
    }

    private void FormatCreditActivity(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (history.Columns[e.ColumnIndex].Name != "EntryKind") return;
        e.Value = Convert.ToString(e.Value) switch {
            "TransferIn" => "Transfer received", "TransferOut" => "Transfer sent",
            "Deposit" => "Advance received", "Refund" => "Unused advance refunded",
            "Apply" or "Application" => "Applied to fees", _ => e.Value
        };
        e.FormattingApplied = true;
    }

    private async Task SaveAsync()
    {
        if (busy) return;
        savedStatus.Visible = false;
        if (!InputParse.TryWholeAmount(amount.Text, out decimal value) || value <= 0)
        { ModernMessageDialog.ShowInfo(this, "Enter a positive whole-rupee amount."); return; }
        if (action.Text == "Transfer" && sibling.SelectedValue is not int)
        { ModernMessageDialog.ShowInfo(this, "Choose a sibling for this transfer."); return; }
        string operation = action.Text;
        int? target = operation == "Transfer" ? Convert.ToInt32(sibling.SelectedValue) : null;
        string effect = operation switch {
            "Deposit" => $"Record Rs. {value:N0} new money received. The student's available advance increases by this amount.",
            "Refund" => $"Return Rs. {value:N0} unused advance in cash. The student's available advance decreases by this amount.",
            _ => $"Move Rs. {value:N0} advance to {sibling.Text}. The sender's available advance decreases and the sibling's increases. No new money is received."
        };
        string remarks = reason.Text.Trim();
        if (!ModernMessageDialog.Confirm(this, effect + (remarks.Length > 0 ? $"\nRemarks: {remarks}" : ""), "Confirm Credit " + operation)) return;
        busy = true; save.Enabled = false;
        Guid request = Guid.NewGuid();
        CollectionRecoveryJournal? journal = null;
        try
        {
            journal = CollectionRecoveryJournal.Begin($"Student credit {operation}; student {admissionId}; Rs. {value}; request {request}; {reason.Text.Trim()}");
            DataTable result = await DatabaseHelper.QueryUserAsync("ProcessStudentCredit",
                DatabaseHelper.Int("@AdmissionId", admissionId), DatabaseHelper.P("@Action", operation),
                DatabaseHelper.P("@Amount", value), DatabaseHelper.P("@RequestId", request),
                DatabaseHelper.P("@Reason", remarks), DatabaseHelper.P("@TargetAdmissionId", target));
            journal.Complete();
            string receipt = Convert.ToString(result.Rows[0]["ReceiptNumber"]) ?? "";
            savedStatus.Text = $"Advance {operation.ToLowerInvariant()} saved." + (receipt.Length > 0 ? $" Receipt: {receipt}." : "");
            savedStatus.Visible = true;
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
