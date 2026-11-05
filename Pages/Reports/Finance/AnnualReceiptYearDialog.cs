using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Reports.Finance;

internal sealed class AnnualReceiptYearDialog : ApplicationWindowForm
{
    private readonly LookupBox year=new();
    internal int AcademicYearId=>year.SelectedId;

    internal AnnualReceiptYearDialog(string student,DataTable years)
    {
        Text="Annual Receipts";StartPosition=FormStartPosition.CenterParent;
        ClientSize=AppMetrics.CompactChoiceDialogClientSize;
        FormBorderStyle=FormBorderStyle.FixedDialog;MinimizeBox=MaximizeBox=false;ShowInTaskbar=false;
        var view=new ModernButton {Text="View / Print Receipts",ThemeRole="Primary",DialogResult=DialogResult.OK};
        var close=new ModernButton {Text="Close",DialogResult=DialogResult.Cancel};
        AcceptButton=view;CancelButton=close;
        view.Enabled=false;year.SelectedIndexChanged+=(_,_)=>view.Enabled=year.SelectedId>0;
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(12)};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.TextHeight(AppTheme.NormalFont)*2+8));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label {Text=student,Dock=DockStyle.Fill,AutoEllipsis=true,Font=AppTheme.NormalFont},0,0);
        root.Controls.Add(HumanUi.Field("Academic year",year,true),0,1);year.MaximumSize=Size.Empty;
        var actions=HumanUi.ButtonRow(view,close);actions.Dock=DockStyle.Fill;
        foreach(var flow in actions.Controls.OfType<FlowLayoutPanel>())flow.WrapContents=false;
        root.Controls.Add(actions,0,2);Controls.Add(root);
        year.Bind(years,"AcademicYearId","YearName");HumanUi.SelectCurrentAcademicYear(year,years);
        view.Enabled=year.SelectedId>0;
    }
}
