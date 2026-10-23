using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Finance.StudentAccounts;

public sealed partial class ChangeFeeDiscountForm
{
    private readonly Label scopeNote = new() { AutoSize=true, Dock=DockStyle.Fill, Padding=new Padding(6) },
        taskNote = new() { AutoSize=true, Dock=DockStyle.Fill, Padding=new Padding(6) },
        periodNote = new() { AutoSize=true, Dock=DockStyle.Fill, Padding=new Padding(6) },
        fixedScope = new() { AutoSize=true, Dock=DockStyle.Fill, Padding=new Padding(6) };
    private readonly TabControl comparisons = new() { Dock=DockStyle.Top, Height=210, MinimumSize=new Size(0,130) };
    private readonly Panel reviewScroll = new() { Dock=DockStyle.Fill,AutoScroll=true };
    private readonly TabPage billTab = new("Issued bills"), planTab = new("Saved fee plan");
    private readonly ModernButton editChoices = new() { Text="Edit Choices",ThemeRole="Secondary" };
    private Panel actionField=null!;
    private Panel amountField=null!, rateField=null!, concessionField=null!, fromField=null!, throughField=null!, paidField=null!, reasonField=null!, scopeField=null!;

    private string ScopeText() => billId is not null
        ? $"Only this {feeName} bill changes. The saved fee plan and other bills stay as recorded."
        : UpdateAgreement && ApplyBills ? "The saved fee plan and existing bills change for the selected period."
        : UpdateAgreement ? "Only the saved fee plan changes for later fee posting. Already issued bills stay as recorded."
        : "Only existing bills in the selected period change. The saved fee plan stays as recorded.";

    private void Build()
    {
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        fields.RowCount=8;
        for(int row=0;row<8;row++)fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Panel AddField(string title,Control input,int col,int row)
        {
            var panel=HumanUi.Field(title,input,true); panel.Height=AppMetrics.FieldRowHeight;
            fields.Controls.Add(panel,col,row);return panel;
        }
        void Wide(Control input,int row)
        {fields.Controls.Add(input,0,row);fields.SetColumnSpan(input,2);}
        actionField=AddField("1. What would you like to do?",action,0,0);
        scopeField=AddField("Apply to",scope,1,0);
        fields.Controls.Add(fixedScope,1,0);
        Wide(scopeNote,1);
        amountField=AddField("2. Discount amount (Rs.)",amount,0,2);
        concessionField=AddField("Discount type",concession,1,2);
        rateField=AddField("2. Correct full fee (Rs.)",newRate,0,3);
        Wide(taskNote,4);
        fromField=AddField("From month",fromMonth,0,5);
        throughField=AddField("Through month",throughMonth,1,5);
        Wide(periodNote,5);
        reasonField=AddField("Reason for this change",reason,0,6); fields.SetColumnSpan(reasonField,2);
        paidField=AddField("Excess payment found: choose what happens to it",paidReduction,0,7); fields.SetColumnSpan(paidField,2);

        bills.Dock=schedule.Dock=DockStyle.Fill; billTab.Controls.Add(bills);planTab.Controls.Add(schedule);
        var close=new ModernButton { Text="Close",DialogResult=DialogResult.Cancel };CancelButton=close;
        var buttons=HumanUi.ButtonRow(review,editChoices,save,check,close);buttons.Dock=DockStyle.Fill;
        var root=new TableLayoutPanel { Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(14),ColumnCount=1,RowCount=2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var content=new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,RowCount=5 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int row=0;row<5;row++)content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading=new Label { Text=identity,AutoSize=true,Dock=DockStyle.Fill,Padding=new Padding(6) };
        var current=new Label { AutoSize=true,Dock=DockStyle.Fill,Padding=new Padding(6),
            Text=$"{feeName}: Rs. {standardAmount:N0}. Current discount: Rs. {originalDiscount:N0}. Net fee: Rs. {(originalWaiver?0:standardAmount-originalDiscount):N0}." };
        content.Controls.Add(heading,0,0);content.Controls.Add(current,0,1);content.Controls.Add(fields,0,2);
        content.Controls.Add(impact,0,3);content.Controls.Add(comparisons,0,4);
        reviewScroll.Controls.Add(content);root.Controls.Add(reviewScroll,0,0);root.Controls.Add(buttons,0,1);Controls.Add(root);
        reviewScroll.SizeChanged+=(_,_)=>{
            int width=Math.Max(200,reviewScroll.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-6);
            content.MinimumSize=new Size(width,0);content.MaximumSize=new Size(width,0);
            foreach(var label in new[]{heading,current,scopeNote,taskNote,periodNote,impact}) label.MaximumSize=new Size(width-12,0);
        };
        FinanceHelp.Attach(this,amount,FinanceHelp.Topic.Discount);
        FinanceHelp.Attach(this,newRate,FinanceHelp.Topic.FeeRate);
        FinanceHelp.Attach(this,scope,FinanceHelp.Topic.FeeAgreement);
        FinanceHelp.Attach(this,paidReduction,FinanceHelp.Topic.PaidReduction);
        FinanceHelp.Attach(this,fromMonth,FinanceHelp.Topic.Period);FinanceHelp.Attach(this,throughMonth,FinanceHelp.Topic.Period);
        FinanceHelp.Attach(this,review,FinanceHelp.Topic.Review);FinanceHelp.Attach(this,check,FinanceHelp.Topic.SaveOutcome);
    }

    private void UpdateVisibleFields()
    {
        actionField.Visible=!reviewStage;
        scopeField.Visible=!reviewStage&&scope.Items.Count>1;
        fixedScope.Visible=!reviewStage&&scope.Items.Count==1;
        fixedScope.Text=$"Apply to: {scope.Text}";
        scopeNote.Visible=!reviewStage;
        scopeNote.Text=ScopeText();
        amountField.Visible=!reviewStage&&DiscountAction;rateField.Visible=!reviewStage&&RateAction;
        concessionField.Visible=!reviewStage&&(DiscountAction||WaiverAction);
        concessionField.Controls.OfType<Label>().Single().Text=WaiverAction?"Waiver type *":"Discount type *";
        concession.AccessibleName=WaiverAction?"Waiver type":"Discount type";
        // The type stays beside the amount for a discount and accompanies the waiver explanation otherwise.
        fields.SetRow(concessionField,WaiverAction?3:2);
        taskNote.Visible=!reviewStage&&HasAction;
        taskNote.Text=WaiverAction ? "2. Waive the full fee. The student will owe Rs. 0 for the chosen bill or period."
            : RemoveAction ? "2. Remove the discount or full waiver. The full fee will be payable for the chosen bill or period."
            : RateAction ? originalWaiver ? "The current full waiver will be kept when the full fee changes."
                : $"The existing Rs. {originalDiscount:N0} discount will be kept. To revise it, use Give Discount or Remove Discount."
            : "Enter the amount to subtract from the fee, not the amount the student should pay.";
        bool choosePeriod=!reviewStage&&HasAction&&monthly&&billId is null;
        fromField.Visible=throughField.Visible=choosePeriod;
        periodNote.Visible=!reviewStage&&HasAction&&!choosePeriod;
        periodNote.Text=billId is not null ? $"Bill period: {fromMonth.Value:MMMM yyyy}." : $"Fee period: {fromMonth.Value:MMM yyyy} through {throughMonth.Value:MMM yyyy}.";
        reasonField.Visible=!reviewStage&&HasAction;
        paidField.Visible=HasAction&&ApplyBills&&releasedPayment>0;
        comparisons.Visible=reviewed is not null;
        if(ApplyBills&&!comparisons.TabPages.Contains(billTab))comparisons.TabPages.Add(billTab);
        if(!ApplyBills&&comparisons.TabPages.Contains(billTab))comparisons.TabPages.Remove(billTab);
        if(UpdateAgreement&&!comparisons.TabPages.Contains(planTab))comparisons.TabPages.Add(planTab);
        if(!UpdateAgreement&&comparisons.TabPages.Contains(planTab))comparisons.TabPages.Remove(planTab);
    }
}
