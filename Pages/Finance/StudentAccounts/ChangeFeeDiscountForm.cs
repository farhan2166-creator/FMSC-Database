using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Pages.Finance.StudentAccounts;

/// <summary>Review fee terms and their explicit effect on the agreement and issued bills.</summary>
public sealed class ChangeFeeDiscountForm : ApplicationWindowForm
{
    private readonly int assignment;
    private readonly string identity;
    private readonly decimal standardAmount;
    private readonly ModernTextBox newRate=HumanUi.TextBox(false,20);
    private readonly CheckBox changeRate=new(){Text="Change the full fee before discount",AutoSize=true},
        fullWaiver=new(){Text="Waive the full fee for this period",AutoSize=true},
        updateAgreement=new(){Text="Change the student's saved fee plan",AutoSize=true,Checked=true};
    private readonly bool agreementAllowed;
    private readonly long? billId;
    private readonly ModernTextBox amount=HumanUi.TextBox(false,20),reason=HumanUi.TextBox(false,500);
    private readonly ModernComboBox concession=HumanUi.Combo(),paidReduction=HumanUi.Combo();
    private readonly ModernDatePicker fromMonth=HumanUi.Date(),throughMonth=HumanUi.Date();
    private readonly CheckBox applyBills=new(){Text="Also change existing bills in this period",AutoSize=true,Checked=true};
    private readonly CheckBox useCredit=new(){Text="Use advance money for due tuition first, then other due fees",AutoSize=true};
    private readonly Label impact=new(){AutoSize=true,Dock=DockStyle.Fill,Padding=new Padding(6)};
    private readonly DataGridView bills=HumanUi.Grid(false),schedule=HumanUi.Grid(false);
    private readonly ModernButton review=new(){Text="Review Fee Change",ThemeRole="Secondary"},
        save=new(){Text="Confirm Fee Change",ThemeRole="Success",Enabled=false},
        check=new(){Text="Check Save Outcome",ThemeRole="Secondary"};
    private readonly TableLayoutPanel fields=new(){Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,RowCount=9};
    private readonly Func<SqlParameter[],Task<DataSet>> query;
    private readonly Func<SqlParameter[],Task<MutationWriteResult>> submit;
    private readonly string initial;
    private DataSet? reviewed;
    private MutationIntent? pending;
    private bool busy,blocked,monthly,validContext;
    private int generation;
    internal bool Changed {get;private set;}

    public ChangeFeeDiscountForm(int id,string fee,decimal rate,decimal discount,DataRow? periodContext=null)
        :this(id,fee,rate,discount,periodContext,null,null) { }

    internal ChangeFeeDiscountForm(int id,string fee,decimal rate,decimal discount,DataRow? context,
        Func<SqlParameter[],Task<DataSet>>? query,Func<SqlParameter[],Task<MutationWriteResult>>? submit)
    {
        assignment=id;standardAmount=rate;
        this.query=query??(args=>DatabaseHelper.QueryUserSetAsync("SaveStudentFeeDiscount",args));
        this.submit=submit??SubmitRecoverableAsync;
        Text=$"Review Fee Terms — {fee}";StartPosition=FormStartPosition.CenterParent;
        Size=new Size(AppScale.Scale(1040),AppScale.Scale(820));MinimumSize=new Size(850,650);
        amount.ZeroAsPlaceholder = newRate.ZeroAsPlaceholder = true;
        amount.Text=discount.ToString("0.##");newRate.Text=rate.ToString("0.##");
        concession.Items.AddRange(new object[]{"None","Free","Scholarship","Waived"});concession.SelectedIndex=0;
        paidReduction.Items.AddRange(new object[]{"Keep excess payment as advance","Refund the excess payment"});paidReduction.SelectedIndex=0;
        context??=id>0?FeeDiscountPeriod.Context(id):null;
        monthly=context is not null&&Convert.ToString(context["Frequency"])=="Monthly";
        validContext=context is not null;
        identity=context?.Table.Columns.Contains("StudentName")==true?$"{context["AdmissionNumber"]} · {context["StudentName"]} · {context["YearName"]}":"";
        agreementAllowed=context is not null&&(!context.Table.Columns.Contains("Status")||Convert.ToString(context["Status"]) is "Active" or "Pending");
        billId=context?.Table.Columns.Contains("BillingChargeId")==true?Convert.ToInt64(context["BillingChargeId"]):null;
        updateAgreement.Checked=agreementAllowed&&billId is null;
        if(context is not null)
        {
            if(context.Table.Columns.Contains("ConcessionLabel")&&context["ConcessionLabel"] is string label&&concession.Items.Contains(label))concession.SelectedItem=label;
            DateTime Date(string column,DateTime fallback)=>context.Table.Columns.Contains(column)&&context[column] is DateTime value?First(value):fallback;
            DateTime first=Date("StartDate",First(DateTime.Today)),last=Date("EndDate",First(DateTime.Today));
            first=new[]{first,Date("EnrollmentDate",first)}.Max();
            if(first<=last)
            {
                fromMonth.Format=throughMonth.Format=DateTimePickerFormat.Custom;
                fromMonth.CustomFormat=throughMonth.CustomFormat="MMMM yyyy";
                fromMonth.Value=throughMonth.Value=first;
                fromMonth.MinDate=throughMonth.MinDate=first;fromMonth.MaxDate=throughMonth.MaxDate=last;
                fromMonth.Value=First(DateTime.Today)<first?first:First(DateTime.Today)>last?last:First(DateTime.Today);
                throughMonth.Value=last;
                if(!monthly)fromMonth.Value=first;
                if(billId is not null&&context.Table.Columns.Contains("BillingMonth")&&context["BillingMonth"] is DateTime billMonth)
                    fromMonth.Value=throughMonth.Value=First(billMonth);
            }
            else validContext=false;
        }
        fullWaiver.Checked=concession.Text is "Free" or "Waived"||(concession.Text=="Scholarship"&&discount==rate);
        Build(fee);
        void InvalidateReview(){generation++;bills.DataSource=schedule.DataSource=null;reviewed?.Dispose();reviewed=null;
            if(!busy)impact.Text="Choices changed. Review the period and financial effect before confirming.";State();}
        amount.TextChanged+=(_,_)=>InvalidateReview();reason.TextChanged+=(_,_)=>InvalidateReview();
        fromMonth.ValueChanged+=(_,_)=>InvalidateReview();throughMonth.ValueChanged+=(_,_)=>InvalidateReview();
        applyBills.CheckedChanged+=(_,_)=>InvalidateReview();
        newRate.TextChanged+=(_,_)=>InvalidateReview();changeRate.CheckedChanged+=(_,_)=>InvalidateReview();
        fullWaiver.CheckedChanged+=(_,_)=>{if(fullWaiver.Checked&&concession.Text=="None")concession.SelectedItem="Scholarship";InvalidateReview();};
        useCredit.CheckedChanged+=(_,_)=>InvalidateReview();updateAgreement.CheckedChanged+=(_,_)=>InvalidateReview();paidReduction.SelectedIndexChanged+=(_,_)=>InvalidateReview();
        concession.SelectedIndexChanged+=(_,_)=>{if(concession.Text is "Free" or "Waived")fullWaiver.Checked=true;InvalidateReview();};
        review.Click+=async(_,_)=>await ReviewAsync();save.Click+=async(_,_)=>await SaveAsync();check.Click+=async(_,_)=>await CheckOutcomeAsync();
        initial=Draft();
        FormClosing+=(_,e)=>{
            if(busy&&!Changed){e.Cancel=true;return;}
            if(e.CloseReason==CloseReason.UserClosing&&!Changed&&Draft()!=initial)
                e.Cancel=!ModernMessageDialog.Confirm(this,"Discard the unsaved fee choices?","Review Fee Terms",defaultYes:false);
        };
        Disposed+=(_,_)=>reviewed?.Dispose();
        impact.Text=$"Current fee: Rs. {rate:N0}. Review the proposed agreement and bills before confirming. " +
            (!agreementAllowed?"This placement is closed. Only existing bills can be changed; its saved fee plan remains historical.":
             billId is not null?"Only the selected bill is reviewed. Its assignment and other bills stay unchanged.":"Choose the period and whether the agreement, issued bills, or both should change.");
        ReadPending();State();
    }
    private static DateTime First(DateTime value)=>new(value.Year,value.Month,1);
    private string Draft()=>string.Join("|",amount.Text,concession.Text,First(fromMonth.Value),First(throughMonth.Value),applyBills.Checked,paidReduction.Text,reason.Text,changeRate.Checked,newRate.Text,fullWaiver.Checked,updateAgreement.Checked,useCredit.Checked);
    private void Build(string fee)
    {
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        for(int i=0;i<5;i++)fields.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        for(int i=0;i<4;i++)fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.Controls.Add(HumanUi.Field("Proposed fee (Rs.)",newRate,true),0,0);fields.Controls.Add(changeRate,1,0);
        fields.Controls.Add(HumanUi.Field($"Discount on {fee} (Rs.)",amount,true),0,1);
        fields.Controls.Add(HumanUi.Field("Discount reason / type",concession),1,1);
        fields.Controls.Add(HumanUi.Field("From month",fromMonth,true),0,2);fields.Controls.Add(HumanUi.Field("Through month",throughMonth,true),1,2);
        fields.Controls.Add(HumanUi.Field("Reason",reason,true),0,3);fields.SetColumnSpan(fields.Controls[fields.Controls.Count-1],2);
        fields.Controls.Add(HumanUi.Field("If a bill has already been paid",paidReduction),0,4);fields.SetColumnSpan(fields.Controls[fields.Controls.Count-1],2);
        fields.Controls.Add(fullWaiver,0,5);fields.SetColumnSpan(fullWaiver,2);
        fields.Controls.Add(updateAgreement,0,6);fields.SetColumnSpan(updateAgreement,2);
        fields.Controls.Add(applyBills,0,7);fields.SetColumnSpan(applyBills,2);
        fields.Controls.Add(useCredit,0,8);fields.SetColumnSpan(useCredit,2);
        var tabs=new TabControl{Dock=DockStyle.Fill};
        foreach(var (title,grid) in new[]{("Issued bills",bills),("Agreed fee periods",schedule)})
        {var tab=new TabPage(title);grid.Dock=DockStyle.Fill;tab.Controls.Add(grid);tabs.TabPages.Add(tab);}
        var close=new ModernButton{Text="Close",DialogResult=DialogResult.Cancel};CancelButton=close;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=1,RowCount=5};
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label{Text=identity,AutoSize=true,Dock=DockStyle.Fill,Padding=new Padding(6)},0,0);root.Controls.Add(fields,0,1);root.Controls.Add(impact,0,2);root.Controls.Add(tabs,0,3);root.Controls.Add(HumanUi.ButtonRow(review,save,check,close),0,4);Controls.Add(root);
        UiHelper.SetGridEmptyMessage(bills,"Review to see current and proposed issued bills.");
        UiHelper.SetGridEmptyMessage(schedule,"Review to see concession periods, including scheduled fee rates.");
        FinanceHelp.Attach(this,newRate,FinanceHelp.Topic.FeeRate);
        FinanceHelp.Attach(this,changeRate,FinanceHelp.Topic.FeeRate);
        FinanceHelp.Attach(this,amount,FinanceHelp.Topic.Discount);
        FinanceHelp.Attach(this,concession,FinanceHelp.Topic.Discount);
        FinanceHelp.Attach(this,fullWaiver,"Remove the whole fee at each agreed rate in the selected period.","A Rs. 400 fee becomes Rs. 0. If the agreed rate later becomes Rs. 500, the full discount covers Rs. 500.");
        FinanceHelp.Attach(this,fromMonth,FinanceHelp.Topic.Period);FinanceHelp.Attach(this,throughMonth,FinanceHelp.Topic.Period);
        FinanceHelp.Attach(this,updateAgreement,FinanceHelp.Topic.FeeAgreement);
        FinanceHelp.Attach(this,applyBills,FinanceHelp.Topic.ExistingBills);
        FinanceHelp.Attach(this,paidReduction,FinanceHelp.Topic.PaidReduction);
        FinanceHelp.Attach(this,useCredit,FinanceHelp.Topic.UseAdvance);
        FinanceHelp.Attach(this,review,FinanceHelp.Topic.Review);FinanceHelp.Attach(this,check,FinanceHelp.Topic.SaveOutcome);
    }
    private void State()
    {
        if(IsDisposed)return;
        bool canChange=validContext&&!busy&&!blocked&&!Changed&&CurrentUser.HasPermission("Fees.Adjust");
        fields.Enabled=canChange;amount.Enabled=!fullWaiver.Checked;
        newRate.Enabled=changeRate.Checked;
        fullWaiver.Enabled=concession.Text is not("Free" or "Waived");
        updateAgreement.Enabled=agreementAllowed&&billId is null;
        fromMonth.Enabled=throughMonth.Enabled=monthly&&billId is null;
        paidReduction.Enabled=useCredit.Enabled=applyBills.Checked;
        review.Enabled=canChange;save.Enabled=canChange&&reviewed is not null;check.Enabled=!busy;
    }
    private bool ValidateChoices()
    {
        if(!CurrentUser.HasPermission("Fees.Adjust")){impact.Text="Your permission to change fees is no longer available.";return false;}
        if(!updateAgreement.Checked&&!applyBills.Checked){impact.Text="Choose the agreement, issued bills, or both.";return false;}
        decimal rate=standardAmount;
        if(changeRate.Checked&&(!InputParse.TryWholeAmount(newRate.Text,out rate)||rate<0))
        {impact.Text="Enter a non-negative fee in whole rupees.";newRate.Focus();return false;}
        if(!fullWaiver.Checked&&(!InputParse.TryWholeAmount(string.IsNullOrWhiteSpace(amount.Text)?"0":amount.Text,out decimal value)||value<0||value>rate))
        {impact.Text=$"Enter a concession from zero to Rs. {rate:N0}, in whole rupees.";amount.Focus();return false;}
        if(First(fromMonth.Value)>First(throughMonth.Value)){impact.Text="The through month must be on or after the from month.";return false;}
        if(string.IsNullOrWhiteSpace(reason.Text)){impact.Text="Enter the reason so the agreed fee change can be understood later.";reason.Focus();return false;}
        return true;
    }
    private SqlParameter[] Args(bool preview)=>new[]{
        DatabaseHelper.Int("@StudentFeeAssignmentId",assignment),DatabaseHelper.P("@DiscountAmount",fullWaiver.Checked?0m:decimal.Parse(string.IsNullOrWhiteSpace(amount.Text)?"0":amount.Text)),
        DatabaseHelper.P("@ConcessionLabel",concession.Text=="None"?null:concession.Text),
        DatabaseHelper.Date("@EffectiveFrom",First(fromMonth.Value)),DatabaseHelper.Date("@EffectiveThrough",First(throughMonth.Value)),
        DatabaseHelper.P("@NewAmount",changeRate.Checked?decimal.Parse(newRate.Text):null),
        DatabaseHelper.Bit("@ApplyAvailableCredit",applyBills.Checked&&useCredit.Checked),DatabaseHelper.Bit("@UpdateAgreement",updateAgreement.Checked),DatabaseHelper.P("@BillingChargeId",billId),DatabaseHelper.Bit("@FullFeeWaiver",fullWaiver.Checked),
        DatabaseHelper.Bit("@ApplyToBills",applyBills.Checked),DatabaseHelper.P("@PaidReductionAction",paidReduction.SelectedIndex==1?"Refund":"Credit"),
        DatabaseHelper.P("@Reason",reason.Text.Trim()),DatabaseHelper.Bit("@Preview",preview),
        DatabaseHelper.P("@ExpectedSnapshot",preview?null:Convert.ToString(reviewed!.Tables[0].Rows[0]["Snapshot"]))};
    private async Task ReviewAsync()
    {
        if(busy||blocked||Changed||!ValidateChoices())return;
        int version=++generation;busy=true;State();impact.Text="Reviewing the agreed period and issued bills…";
        try
        {
            var result=await query(Args(true));
            if(IsDisposed||version!=generation){result.Dispose();return;}
            if(result.Tables.Count<3||result.Tables[0].Rows.Count!=1){result.Dispose();throw new InvalidDataException("The fee review is incomplete. Reload before saving.");}
            reviewed?.Dispose();reviewed=result;
            bills.DataSource=result.Tables[1];schedule.DataSource=result.Tables[2];
            foreach(var grid in new[]{bills,schedule})foreach(DataGridViewColumn column in grid.Columns)
                if(column.ValueType==typeof(decimal))column.DefaultCellStyle.Format="N0";else if(column.ValueType==typeof(DateTime))column.DefaultCellStyle.Format="MMM yyyy";
            var row=result.Tables[0].Rows[0];
            decimal delta=Convert.ToDecimal(row["Difference"]),released=Convert.ToDecimal(row["ReleasedPaidAmount"]);
            impact.Text=$"{First(fromMonth.Value):MMM yyyy} through {First(throughMonth.Value):MMM yyyy}: " +
                (applyBills.Checked?$"{row["IssuedBills"]} existing bill(s) reviewed. Fees {(delta<0?"decrease by":delta>0?"increase by":"change by")} Rs. {Math.Abs(delta):N0}. Excess already paid, {(paidReduction.SelectedIndex==1?"to refund":"to keep as advance")}: Rs. {released:N0}.":"Existing bills and payments stay as recorded. Only the saved fee plan changes.")+
                (row.Table.Columns.Contains("CurrentCredit")?$" Advance available before: Rs. {row["CurrentCredit"]:N0}. Advance to use: Rs. {row["CreditToUse"]:N0}. Advance left: Rs. {row["RemainingCredit"]:N0}.":"")+
                "\r\nThe tables show the proposed changes. Other periods stay unchanged. Advance money stays available unless you choose to use it.";
        }
        catch(Exception ex){reviewed?.Dispose();reviewed=null;if(!IsDisposed)impact.Text=DbMessage.UserMessage(ex);}
        finally{busy=false;State();}
    }
    private async Task SaveAsync()
    {
        if(busy||blocked||Changed||reviewed is null||!ValidateChoices())return;
        var args=Args(false);busy=true;State();
        try
        {
            var outcome=await submit(args);
            if(outcome.Outcome==MutationOutcome.Unconfirmed){blocked=true;impact.Text="The save outcome is uncertain. Use Check Save Outcome before another fee change.";return;}
            if(outcome.Outcome==MutationOutcome.Saved)Changed=true;
            if(pending is not null){MutationRecoveryJournal.ClearVerified(pending);pending=null;}
            blocked=false;
            if(outcome.Outcome==MutationOutcome.Rejected)throw outcome.Error??new InvalidOperationException("The fee change was not saved.");
            LookupDataCache.OperationCompleted("SaveStudentFeeDiscount");DialogResult=DialogResult.OK;Close();
        }
        catch(Exception ex){ReadPending();impact.Text=(Changed?"The fee change was saved. Check its outcome before repeating it. ":"")+DbMessage.UserMessage(ex);}
        finally{busy=false;if(!Changed){bills.DataSource=schedule.DataSource=null;reviewed?.Dispose();reviewed=null;}State();}
    }
    private async Task<MutationWriteResult> SubmitRecoverableAsync(SqlParameter[] args)
    {
        int actor=CurrentUser.UserId;string scope=await RecoverableMutationWriter.GetScopeAsync();
        if(IsDisposed||CurrentUser.UserId!=actor)throw new InvalidOperationException("The operator or window changed before submission. Review again.");
        using var journal=MutationRecoveryJournal.Begin(MutationKind.FeeConcession,actor,scope,
            RecoverableMutationWriter.PayloadHash(MutationKind.FeeConcession,actor,args),$"Fee change for assignment {assignment}, {First(fromMonth.Value):MMM yyyy}–{First(throughMonth.Value):MMM yyyy}");
        pending=journal.Intent;blocked=true;
        return await RecoverableMutationWriter.SaveAsync(journal.Intent,args);
    }
    private void ReadPending()
    {
        try{pending=MutationRecoveryJournal.ReadPending(MutationKind.FeeConcession);blocked=pending is not null;
            if(blocked)impact.Text="An earlier fee change needs checking. Use Check Save Outcome before submitting.";}
        catch(Exception ex){blocked=true;impact.Text="Another concession may be saving, or recovery needs review. "+DbMessage.UserMessage(ex);}
    }
    private async Task CheckOutcomeAsync()
    {
        if(busy)return;ReadPending();if(pending is null){State();return;}
        busy=true;State();
        try
        {
            var result=await RecoverableMutationWriter.CheckAsync(pending);
            MutationRecoveryJournal.ClearVerified(pending);pending=null;blocked=false;
            if(result is not null){Changed=true;LookupDataCache.OperationCompleted("SaveStudentFeeDiscount");DialogResult=DialogResult.OK;Close();}
            else{bills.DataSource=schedule.DataSource=null;reviewed?.Dispose();reviewed=null;impact.Text="The earlier fee change was not recorded. Review again before deciding whether to submit.";}
        }
        catch(Exception ex){impact.Text="The outcome is still unresolved. Keep recovery and check again. "+DbMessage.UserMessage(ex);}
        finally{busy=false;State();}
    }
}
