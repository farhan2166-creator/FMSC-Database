using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Finance.Collection;

public sealed partial class FeeCollectionPage
{
    private DataTable familyPaymentBreakdowns=new();
    private int historyReceiptChild;
    private bool changingHistorySelection;

    private void SelectHistoryReceipt(DataGridView? children=null)
    {
        if(changingHistorySelection || paymentHistoryDetails is null)return;
        changingHistorySelection=true;
        try
        {
            historyReceiptChild=children?.CurrentRow?.Tag is int id?id:0;
            if(children is not null)
            {
                recentPayments.ClearSelection();
                if(children.CurrentRow is not null)children.CurrentRow.Selected=true;
            }
            else
            {
                foreach(var grid in paymentHistoryDetails.Controls.OfType<DataGridView>())grid.ClearSelection();
                if(recentPayments.CurrentRow is not null)recentPayments.CurrentRow.Selected=true;
            }
            UpdateReprintAvailability();
        }
        finally{changingHistorySelection=false;}
    }

    private async Task LoadFamilyPaymentBreakdownsAsync(int selectedAdmission,int version)
    {
        try
        {
            var data=await ReceiptPaymentBreakdown.ReadAsync(selectedAdmission);
            if(IsDisposed||version!=loadVersion||selectedAdmission!=admissionId){data.Dispose();return;}
            familyPaymentBreakdowns.Dispose();familyPaymentBreakdowns=data;
        }
        catch(Exception ex){AppExceptionHandler.LogNonFatal(ex,"Recent Payments sibling breakdown");}
    }

    private bool ShowFamilyPaymentBreakdown(string reference)
    {
        if(paymentHistoryDetails is not {IsDisposed:false} panel)return false;
        if(panel.Tag is ValueTuple<DataTable,string> shown&&ReferenceEquals(shown.Item1,familyPaymentBreakdowns)&&shown.Item2==reference)return true;
        var members=familyPaymentBreakdowns.AsEnumerable()
            .Where(row=>string.Equals(GetText(row,"ReceiptNumber"),reference,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(members.Length<2)return false;
        SetPaymentHistoryMinimum(380);
        decimal original=GetDecimal(members[0],"OriginalTotal"),adjusted=members.Sum(row=>GetDecimal(row,"Adjusted")),net=members.Sum(row=>GetDecimal(row,"NetSettled"));
        var grid=HumanUi.Grid(false);
        grid.Name="receiptSiblingBreakdown";grid.Dock=DockStyle.Fill;grid.ReadOnly=true;
        grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
        grid.Columns.Add("Student","Student");
        foreach(var (name,title) in new[]{("Original","Original share"),("Adjusted","Adjustments"),("Net","Still recorded")})
        {
            grid.Columns.Add(name,title);
            grid.Columns[name].ValueType=typeof(decimal);
            grid.Columns[name].DefaultCellStyle.Format="N0";
            grid.Columns[name].DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleRight;
            grid.Columns[name].MinimumWidth=AppScale.Scale(100);
            grid.Columns[name].FillWeight=20;
        }
        grid.Columns.Add("Status","Status");grid.Columns["Status"].MinimumWidth=AppScale.Scale(112);grid.Columns["Status"].FillWeight=24;
        grid.Columns["Student"].MinimumWidth=AppScale.Scale(170);grid.Columns["Student"].FillWeight=44;
        foreach(DataGridViewColumn column in grid.Columns)column.SortMode=DataGridViewColumnSortMode.NotSortable;
        HumanUi.ApplyGridStyle(grid,AppTheme.Primary);
        foreach(string name in new[]{"Original","Adjusted","Net"})grid.Columns[name].DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleRight;
        grid.ColumnHeadersHeight=AppScale.Scale(34);grid.RowTemplate.Height=AppScale.Scale(28);
        int selected=-1;
        foreach(var member in members)
        {
            int index=grid.Rows.Add($"{GetText(member,"AdmissionNumber")} · {GetText(member,"StudentName")}",
                GetDecimal(member,"CashApplied")+GetDecimal(member,"CreditApplied"),GetDecimal(member,"Adjusted"),GetDecimal(member,"NetSettled"),GetText(member,"PaymentStatus"));
            grid.Rows[index].Tag=GetInt(member,"AdmissionId");
            if(GetInt(member,"AdmissionId")==admissionId)selected=index;
        }
        if(selected>=0)grid.CurrentCell=grid.Rows[selected].Cells[0];
        grid.ClearSelection();
        grid.Enter+=(_,_)=>SelectHistoryReceipt(grid);
        grid.CellClick+=(_,e)=>{if(e.RowIndex>=0)SelectHistoryReceipt(grid);};
        grid.CurrentCellChanged+=(_,_)=>{if(grid.ContainsFocus)SelectHistoryReceipt(grid);};
        grid.Layout+=(_,_)=>FitFamilyPaymentBreakdown(grid);
        string totals=$"Original receipt total: Rs. {original:N0}    Adjustments: Rs. {adjusted:N0}    Still recorded: Rs. {net:N0}";
        string heading=$"{reference} · Original shares and current adjustments";
        if(GetDecimal(members[0],"ReceiptCreditApplied")>0||GetDecimal(members[0],"ReceiptCashReceived")!=original)
            heading+=$"\r\nCash received then: Rs. {GetDecimal(members[0],"ReceiptCashReceived"):N0}; existing credit used: Rs. {GetDecimal(members[0],"ReceiptCreditApplied"):N0}.";
        panel.SuspendLayout();
        try
        {
            foreach(Control child in panel.Controls.Cast<Control>().ToArray())child.Dispose();
            panel.Controls.Clear();panel.RowStyles.Clear();panel.RowCount=3;
            for(int i=0;i<3;i++)panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var title=new Label{Text=heading,AutoSize=true,Dock=DockStyle.Fill,Font=AppTheme.NormalFont};
            var total=new Label{Text=totals,AutoSize=true,Dock=DockStyle.Fill,Font=AppTheme.ButtonFont,Margin=new Padding(0,4,0,0)};
            panel.Controls.Add(title,0,0);panel.SetColumnSpan(title,4);
            panel.Controls.Add(grid,0,1);panel.SetColumnSpan(grid,4);
            FitFamilyPaymentBreakdown(grid);
            panel.Controls.Add(total,0,2);panel.SetColumnSpan(total,4);
            panel.Text=heading+Environment.NewLine+totals;
            panel.Tag=(familyPaymentBreakdowns,reference);
        }
        finally{panel.ResumeLayout(true);}
        return true;
    }

    private void SetPaymentHistoryMinimum(int height)
    {
        if(paymentHistoryDetails?.Parent is TableLayoutPanel root&&root.Parent is ResponsiveViewport viewport)
        {
            root.MinimumSize=new Size(0,AppScale.Scale(height));
            viewport.PerformLayout();
        }
    }

    private void FitFamilyPaymentBreakdown(DataGridView grid)
    {
        if(paymentHistoryDetails is not {IsDisposed:false} panel)return;
        int rowHeight=grid.Rows.Count>0?grid.Rows[0].Height:grid.RowTemplate.Height;
        int width=Math.Max(AppScale.Scale(300),panel.ClientSize.Width-panel.Padding.Horizontal);
        int textHeight=panel.Controls.OfType<Label>().Sum(label=>label.GetPreferredSize(new Size(width,0)).Height+label.Margin.Vertical);
        int formHeight=panel.FindForm()?.ClientSize.Height??AppScale.Scale(520);
        int available=formHeight-AppScale.Scale(240)
            -textHeight-panel.Padding.Vertical-panel.Margin.Vertical-grid.Margin.Vertical;
        int visibleRows=Math.Clamp((available-grid.ColumnHeadersHeight-AppScale.Scale(6))/Math.Max(1,rowHeight),1,formHeight<AppScale.Scale(480)?2:3);
        int height=grid.ColumnHeadersHeight+rowHeight*Math.Min(visibleRows,grid.Rows.Count)+AppScale.Scale(6);
        if(grid.MinimumSize.Height==height&&grid.MaximumSize.Height==height)return;
        grid.MinimumSize=Size.Empty;grid.MaximumSize=new Size(0,height);grid.MinimumSize=new Size(0,height);grid.Height=height;
        if(grid.CurrentCell is not null&&grid.IsHandleCreated)grid.FirstDisplayedScrollingRowIndex=grid.CurrentCell.RowIndex;
    }
}
