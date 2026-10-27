using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Finance.StudentAccounts;

public sealed partial class ChangeFeeDiscountForm
{
    private readonly Label scopeNote = new() { AutoSize=true, Dock=DockStyle.Fill, Padding=new Padding(4) },
        taskNote = new() { AutoSize=true, Dock=DockStyle.Fill, Padding=new Padding(4) },
        periodNote = new() { AutoSize=true, Dock=DockStyle.Fill, Padding=new Padding(4) },
        fixedScope = new() { AutoSize=true, Dock=DockStyle.Fill, Padding=new Padding(4) };
    private readonly TabControl comparisons = new() { Dock=DockStyle.Top, Height=200, MinimumSize=new Size(0,130) };
    private readonly Panel reviewScroll = new() { Dock=DockStyle.Fill,AutoScroll=true };
    private readonly TabPage billTab = new("Issued bills"), planTab = new("Saved fee plan");
    private readonly ModernButton editChoices = new() { Text="Edit Choices",ThemeRole="Secondary" };
    private readonly ModernButton[] taskButtons = new ModernButton[4];
    private TableLayoutPanel footer=null!;
    private ModernButton[] footerButtons=Array.Empty<ModernButton>();
    private Panel actionField=null!;
    private Panel amountField=null!, rateField=null!, concessionField=null!, fromField=null!, throughField=null!, paidField=null!, reasonField=null!, scopeField=null!;

    private string ScopeText() => billId is not null
        ? $"Only this {feeName} bill changes. The saved fee plan and other bills stay as recorded."
        : UpdateAgreement && ApplyBills ? "The saved fee plan and existing bills change for the selected period."
        : UpdateAgreement ? "Only the saved fee plan changes for later fee posting. Already issued bills stay as recorded."
        : "Only existing bills in the selected period change. The saved fee plan stays as recorded.";

    private void Build()
    {
        BackColor=AppTheme.PageBackground;
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        fields.RowCount=8;
        for(int row=0;row<8;row++)fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Panel AddField(string title,Control input,int col,int row)
        {
            var panel=HumanUi.Field(title,input,input!=reason);panel.Height=AppMetrics.FieldRowHeight;
            fields.Controls.Add(panel,col,row);return panel;
        }
        void Wide(Control input,int row)
        {fields.Controls.Add(input,0,row);fields.SetColumnSpan(input,2);}

        var tasks=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty };
        for(int index=0;index<taskButtons.Length;index++)
        {
            int choice=index;
            tasks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
            var button=new ModernButton {
                Text=Convert.ToString(action.Items[index])??"",ThemeRole="TabInactive",Dock=DockStyle.Fill,
                Margin=new Padding(2),Height=AppMetrics.ButtonHeight,AutoSize=false,
                AccessibleName=Convert.ToString(action.Items[index]),TabIndex=index
            };
            button.Click+=(_,_)=>{
                action.SelectedIndex=choice;
                if(DiscountAction)amount.Focus();else if(RateAction)newRate.Focus();
                else if(WaiverAction)concession.Focus();else reason.Focus();
            };
            taskButtons[index]=button;tasks.Controls.Add(button,index,0);
        }
        actionField=new Panel { Dock=DockStyle.Fill,Height=AppMetrics.ButtonHeight+AppScale.Scale(8),Padding=new Padding(0,3,0,3),Margin=Padding.Empty };
        actionField.Controls.Add(tasks);Wide(actionField,0);
        amountField=AddField("Discount amount (Rs.)",amount,0,1);
        concessionField=AddField("Discount type",concession,1,1);
        rateField=AddField("Full fee amount (Rs.)",newRate,0,1);fields.SetColumnSpan(rateField,2);
        Wide(taskNote,2);
        fromField=AddField("From month",fromMonth,0,3);
        throughField=AddField("Through month",throughMonth,1,3);
        Wide(periodNote,3);
        scopeField=AddField("Apply this change to",scope,0,4);fields.SetColumnSpan(scopeField,2);
        Wide(fixedScope,4);Wide(scopeNote,5);
        reasonField=AddField("Reason (optional)",reason,0,6);fields.SetColumnSpan(reasonField,2);
        paidField=AddField("Excess payment: choose what happens to it",paidReduction,0,7);fields.SetColumnSpan(paidField,2);

        bills.Dock=schedule.Dock=DockStyle.Fill;billTab.Controls.Add(bills);planTab.Controls.Add(schedule);
        var close=new ModernButton { Text="Close",DialogResult=DialogResult.Cancel };CancelButton=close;
        footerButtons=new[]{review,save,editChoices,check,close};
        footer=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=footerButtons.Length+1,RowCount=1,Margin=Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int index=0;index<footerButtons.Length;index++)
        {
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,0));
            var button=footerButtons[index];button.Dock=DockStyle.Fill;button.AutoSize=false;button.Margin=new Padding(3,5,3,5);
            footer.Controls.Add(button,index+1,0);
        }
        var root=new TableLayoutPanel { Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(12),ColumnCount=1,RowCount=2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.ButtonRowHeight));
        var content=new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,RowCount=5 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int row=0;row<5;row++)content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading=new Label { Text=$"{feeName} · {identity}",AutoSize=true,Dock=DockStyle.Fill,Padding=new Padding(4),Font=AppTheme.ButtonFont };
        var current=new Label { AutoSize=true,Dock=DockStyle.Fill,Padding=new Padding(6),BackColor=AppTheme.SurfaceSecondary,
            Text=$"Fee: Rs. {standardAmount:N0}     •     Discount: Rs. {originalDiscount:N0}     •     Net fee: Rs. {(originalWaiver?0:standardAmount-originalDiscount):N0}" };
        content.Controls.Add(heading,0,0);content.Controls.Add(current,0,1);
        content.Controls.Add(fields,0,2);content.Controls.Add(impact,0,3);content.Controls.Add(comparisons,0,4);
        reviewScroll.Controls.Add(content);root.Controls.Add(reviewScroll,0,0);root.Controls.Add(footer,0,1);Controls.Add(root);
        reviewScroll.SizeChanged+=(_,_)=>{
            int width=Math.Max(200,reviewScroll.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-6);
            content.MinimumSize=new Size(width,0);content.MaximumSize=new Size(width,0);
            foreach(var label in new[]{heading,current,scopeNote,taskNote,periodNote,impact,fixedScope})
                label.MaximumSize=new Size(width-12,0);
        };
        FinanceHelp.Attach(this,amount,FinanceHelp.Topic.Discount);FinanceHelp.Attach(this,newRate,FinanceHelp.Topic.FeeRate);
        FinanceHelp.Attach(this,scope,FinanceHelp.Topic.FeeAgreement);FinanceHelp.Attach(this,paidReduction,FinanceHelp.Topic.PaidReduction);
        FinanceHelp.Attach(this,fromMonth,FinanceHelp.Topic.Period);FinanceHelp.Attach(this,throughMonth,FinanceHelp.Topic.Period);
        FinanceHelp.Attach(this,review,FinanceHelp.Topic.Review);FinanceHelp.Attach(this,check,FinanceHelp.Topic.SaveOutcome);
    }

    private void UpdateQuickControls(bool canChange)
    {
        for(int index=0;index<taskButtons.Length;index++)
        {
            taskButtons[index].Enabled=canChange;
            string role=action.SelectedIndex==index?"TabActive":"TabInactive";
            if(taskButtons[index].ThemeRole!=role)taskButtons[index].ThemeRole=role;
            taskButtons[index].AccessibleDescription=action.SelectedIndex==index?"Selected action":"Choose this action";
        }
        for(int index=0;index<footerButtons.Length;index++)
        {
            var button=footerButtons[index];
            bool visible=index switch { 0=>!blocked&&(!reviewStage||reviewed is null), 1 or 2=>!blocked&&reviewStage, 3=>blocked, _=>true };
            button.Visible=visible;
            footer.ColumnStyles[index+1].Width=visible
                ? Math.Max(AppScale.Scale(120),TextRenderer.MeasureText(button.Text,AppTheme.ButtonFont).Width+AppScale.Scale(32)) : 0;
        }
    }

    private string DraftNetText()
    {
        decimal rate=standardAmount,discount=originalDiscount;
        if(DiscountAction)
        {
            if(!InputParse.TryWholeAmount(amount.Text,out discount)||discount<0||discount>rate)return "";
        }
        else if(RateAction)
        {
            if(!InputParse.TryWholeAmount(newRate.Text,out rate)||rate<0||!originalWaiver&&discount>rate)return "";
            if(originalWaiver)discount=rate;
        }
        else if(WaiverAction)return "Full discount for the selected months. Existing charges retain paid money and discount only remaining dues.";
        else if(RemoveAction)discount=0;
        else return "";
        return $"Selected fee after this change: Rs. {rate-discount:N0}. Review confirms the effect on each bill or period.";
    }

    private void UpdateVisibleFields()
    {
        actionField.Visible=!reviewStage;
        scopeField.Visible=!reviewStage&&HasAction&&scope.Items.Count>1;
        fixedScope.Visible=!reviewStage&&HasAction&&scope.Items.Count==1;
        fixedScope.Text=$"Apply to: {scope.Text}";
        scopeNote.Visible=!reviewStage&&HasAction;scopeNote.Text=ScopeText();
        amountField.Visible=!reviewStage&&DiscountAction;rateField.Visible=!reviewStage&&RateAction;
        concessionField.Visible=!reviewStage&&(DiscountAction||WaiverAction);
        concessionField.Controls.OfType<Label>().Single().Text=WaiverAction?"Waiver type *":"Discount type *";
        concession.AccessibleName=WaiverAction?"Waiver type":"Discount type";
        fields.SetColumn(concessionField,WaiverAction?0:1);fields.SetColumnSpan(concessionField,WaiverAction?2:1);
        taskNote.Visible=!reviewStage&&HasAction;
        string note=WaiverAction?"The concession fully discounts the chosen bill or period, up to unpaid dues on issued charges."
            : RemoveAction?"The discount or full waiver will be removed. The full fee becomes payable."
            : RateAction?originalWaiver?"The existing full waiver will be kept."
                : $"The existing Rs. {originalDiscount:N0} discount will be kept."
            : "Enter the discount to subtract from the fee.";
        string net=DraftNetText();taskNote.Text=net.Length>0?note+"\r\n"+net:note;
        bool choosePeriod=!reviewStage&&HasAction&&monthly&&billId is null;
        fromField.Visible=throughField.Visible=choosePeriod;
        periodNote.Visible=!reviewStage&&HasAction&&!choosePeriod;
        periodNote.Text=billId is not null?$"Bill period: {fromMonth.Value:MMMM yyyy}.":$"Fee period: {fromMonth.Value:MMM yyyy} through {throughMonth.Value:MMM yyyy}.";
        reasonField.Visible=!reviewStage&&HasAction;
        paidField.Visible=RateAction&&ApplyBills&&releasedPayment>0;
        comparisons.Visible=reviewed is not null;
        if(ApplyBills&&!comparisons.TabPages.Contains(billTab))comparisons.TabPages.Add(billTab);
        if(!ApplyBills&&comparisons.TabPages.Contains(billTab))comparisons.TabPages.Remove(billTab);
        if(UpdateAgreement&&!comparisons.TabPages.Contains(planTab))comparisons.TabPages.Add(planTab);
        if(!UpdateAgreement&&comparisons.TabPages.Contains(planTab))comparisons.TabPages.Remove(planTab);
    }
}
