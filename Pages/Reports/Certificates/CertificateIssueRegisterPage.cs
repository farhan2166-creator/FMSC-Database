using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Pages.Reports.Certificates;

public sealed class CertificateIssueRegisterPage : V2Page
{
    private readonly ModernComboBox type=new(),recipient=new(),entry=new(),collection=new();
    private readonly ModernTextBox search=new() { PlaceholderText="Name, student/staff no. or certificate no." };
    private readonly ModernDatePicker from=new() { Format=DateTimePickerFormat.Custom,CustomFormat="dd-MM-yyyy",ShowCheckBox=true,Checked=false };
    private readonly ModernDatePicker to=new() { Format=DateTimePickerFormat.Custom,CustomFormat="dd-MM-yyyy",ShowCheckBox=true,Checked=false };
    private readonly Label summary=new() { Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(8),Text="Run the report to see totals.",Font=AppTheme.ButtonFont,ForeColor=AppTheme.HeadingText };
    private readonly ModernButton print,export;
    private readonly FormValidator validator;
    private string applied="",subtitle="";
    private bool running,hasRows;

    public CertificateIssueRegisterPage() : base("Certificate Issue Register",
        "Recorded issues and saved certificates. Saved record only does not confirm printing or collection. Dates filter Recorded On; only permitted certificate sources are included.")
    {
        validator=CreateValidator();
        type.Items.Add("All");
        if (CurrentUser.HasPermission("Certificates.Create")) type.Items.AddRange(CertificateIssueRegisterData.GeneralTypes);
        if (CurrentUser.HasPermission("Students.Leave")) type.Items.AddRange(CertificateIssueRegisterData.LeavingTypes);
        recipient.Items.AddRange(new object[] { "All","Student","Staff" });
        entry.Items.AddRange(new object[] { "All","Issue logged","Saved record only" });
        collection.Items.AddRange(new object[] { "All","Collected","Not Collected","Not tracked" });
        foreach (var combo in new[] { type,recipient,entry,collection })
        {
            combo.SelectedIndex=0; ReportUi.RegisterChoiceFilter(combo);
            combo.SelectedIndexChanged+=(_,_) => FiltersChanged();
        }
        from.ValueChanged+=(_,_) => FiltersChanged(); to.ValueChanged+=(_,_) => FiltersChanged(); search.TextChanged+=(_,_) => FiltersChanged();
        var run=AddBusyAction("Run Report",RunAsync,"Primary");
        print=AddBusyAction(UiText.Actions.Print,PrintAsync,"Secondary"); export=AddExportAction("FMSC_Certificate_Issue_Register");
        RelocateFromActions(run,print,export);
        SetContent(ReportUi.CompactLayout(ReportUi.Bar(("Certificate Type",type),("Recipient Type",recipient),
            ("From Recorded Date (optional)",from),("To Recorded Date (optional)",to),("Entry",entry),("Collection",collection),("Find",search)),
            Grid,"Certificate Issue Register",ClearReport,run,print,export));
        Grid.Parent?.Controls.Add(summary); Grid.BringToFront();
        UiHelper.SetGridPlaceholderMessage(Grid,UiText.EmptyGrid.RunReport);
        print.Enabled=export.Enabled=false;
    }
    private bool CanRead => CurrentUser.HasPermission("Reports.View")
        && (CurrentUser.HasPermission("Certificates.Create") || CurrentUser.HasPermission("Students.Leave"));
    private string Signature() => string.Join('|',CurrentUser.UserId,CurrentUser.HasPermission("Reports.View"),
        CurrentUser.HasPermission("Certificates.Create"),CurrentUser.HasPermission("Students.Leave"),
        type.Text,recipient.Text,entry.Text,collection.Text,search.Text.Trim(),from.Checked,from.Value.Date,to.Checked,to.Value.Date);
    protected override string ReportFilterSignature() => Signature();
    protected override string ReportExportSubtitle() => subtitle;
    private void ClearReport()
    {
        applied=subtitle=""; hasRows=false; print.Enabled=export.Enabled=false;
        summary.Text="Run the report to see totals."; InvalidateReport();
    }
    private void FiltersChanged()
    {
        if (running) CancelPendingReport();
        if (applied.Length==0) return;
        bool current=CanRead && applied==Signature(); print.Enabled=export.Enabled=current && hasRows && !running;
        if (current) RestoreReportStale(); else MarkReportStale();
        SetStatus(current ? UiText.Reports.MatchesFilters : UiText.Reports.FiltersChanged,!current);
    }
    private static string? Selected(ModernComboBox choice) => choice.Text=="All" ? null : choice.Text;
    private async Task RunAsync()
    {
        if (!CanRead) { Warn("Report and certificate access are required."); return; }
        if (!ReportUi.ValidDateRange(validator,from,to)) return;
        string requested=Signature();
        string query=CertificateIssueRegisterData.BuildQuery(CurrentUser.HasPermission("Certificates.Create"),CurrentUser.HasPermission("Students.Leave"));
        SqlParameter[] parameters={ DatabaseHelper.Int("@UserId",CurrentUser.UserId),
            DatabaseHelper.P("@FromDate",from.Checked ? from.Value.Date : null),DatabaseHelper.P("@ToDate",to.Checked ? to.Value.Date : null),
            DatabaseHelper.P("@CertificateType",Selected(type)),DatabaseHelper.P("@RecipientType",Selected(recipient)),
            DatabaseHelper.P("@EntryKind",Selected(entry)),DatabaseHelper.P("@CollectionStatus",Selected(collection)),DatabaseHelper.P("@Search",search.Text.Trim()) };
        string captured=$"Type: {type.Text} | Recipient: {recipient.Text} | Entry: {entry.Text} | Collection: {collection.Text}"
            +$" | Recorded dates: {(from.Checked ? from.Value.ToString("dd-MM-yyyy") : "Any start")} to {(to.Checked ? to.Value.ToString("dd-MM-yyyy") : "Any end")}"
            +$" | Find: {search.Text.Trim()} | Sources: {(CurrentUser.HasPermission("Certificates.Create") ? "General certificates" : "")}{(CurrentUser.HasPermission("Students.Leave") ? " Leaving certificates" : "")}"
            +$" | Report generated: {DateTime.Now:dd-MM-yyyy HH:mm}";
        try
        {
            running=true; hasRows=false; print.Enabled=export.Enabled=false; summary.Text="Generating report…";
            DataTable? rows=null;
            await BindReportAsync(token => Task.Run(() => rows=CertificateIssueRegisterData.Display(DatabaseHelper.QuerySql(query,parameters)),token));
            if (IsDisposed) return;
            if (!ReportLanded || rows is null) { summary.Text="Run the report again to see current totals."; return; }
            var totals=CertificateIssueRegisterData.Totals(rows);
            summary.Text=string.Join("     ",totals.Columns.Cast<DataColumn>().Select(c => $"{c.ColumnName}: {totals.Rows[0][c]}"));
            subtitle=captured+" | "+summary.Text+" | Saved record only does not confirm printing or collection.";
            applied=requested; hasRows=rows.Rows.Count>0;
            bool current=CanRead && ReportMatchesCurrentFilters(requested); print.Enabled=export.Enabled=current && hasRows;
        }
        catch (Exception ex)
        {
            if (!IsDisposed) { applied=subtitle=""; summary.Text="Could not generate the report. Run Report to retry."; Fail(ex); }
        }
        finally { running=false; }
    }
    private async Task PrintAsync()
    {
        if (!CanRead || applied!=Signature()) { Warn("Run the report again before printing."); return; }
        if (!hasRows) { Warn("No records match these filters."); return; }
        try { await PreviewReportAsync(); }
        catch (Exception ex) { if (!IsDisposed) Fail(ex); }
    }
}
