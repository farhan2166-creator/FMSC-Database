namespace FMSC_SMS.Core;

internal sealed class ReportAcademicPeriodDialog : ApplicationWindowForm
{
    private readonly LookupBox year=new();
    private readonly ModernComboBox range=new();
    internal int YearId=>year.SelectedId;
    internal ReportAcademicPeriod.Range? SelectedRange=>range.SelectedItem as ReportAcademicPeriod.Range;
    internal ReportAcademicPeriodDialog(DataTable years,int months,int selectedYear,DateTime? selectedStart)
    {
        Text="Choose Report Period";StartPosition=FormStartPosition.CenterParent;
        FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;
        ClientSize=AppMetrics.CompactChoiceDialogClientSize;
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(AppScale.S(16))};
        root.RowStyles.Add(new RowStyle(SizeType.Percent,50));root.RowStyles.Add(new RowStyle(SizeType.Percent,50));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(FMSC_SMS.Pages.Reports.ReportUi.Field("Academic Year",year),0,0);
        root.Controls.Add(FMSC_SMS.Pages.Reports.ReportUi.Field("Period",range),0,1);
        var apply=new ModernButton {Text="Apply Period",ThemeRole="Primary",Width=AppScale.S(140),Height=AppMetrics.ButtonHeight};
        var cancel=new ModernButton {Text=UiText.Actions.Cancel,ThemeRole="Secondary",DialogResult=DialogResult.Cancel,Width=AppScale.S(100),Height=AppMetrics.ButtonHeight};
        apply.Click+=(_,_)=>{if(SelectedRange is not null)DialogResult=DialogResult.OK;};
        root.Controls.Add(HumanUi.DialogButtons(cancel,apply),0,2);Controls.Add(root);AcceptButton=apply;CancelButton=cancel;
        void RefreshRanges()
        {
            range.Items.Clear();
            if(year.SelectedItem is DataRowView row && year.SelectedId>0 && !row.Row.IsNull("StartDate") && !row.Row.IsNull("EndDate"))
            {
                DateTime start=Convert.ToDateTime(row["StartDate"]),end=Convert.ToDateTime(row["EndDate"]);
                if(end>=start)range.Items.AddRange(ReportAcademicPeriod.Ranges(start,end,months).Cast<object>().ToArray());
            }
            if(range.Items.Count>0)range.SelectedItem=range.Items.Cast<ReportAcademicPeriod.Range>().FirstOrDefault(r=>r.From==selectedStart)
                ??range.Items.Cast<ReportAcademicPeriod.Range>().FirstOrDefault(r=>r.From<=DateTime.Today&&r.To>=DateTime.Today)??range.Items[0];
            apply.Enabled=range.SelectedItem is not null;
        }
        year.SelectedIndexChanged+=(_,_)=>RefreshRanges();
        year.Bind(years,"AcademicYearId","YearName","Choose academic year");
        if(selectedYear>0)year.SelectedValue=selectedYear;
        RefreshRanges();
    }
}
