using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Pages.Finance.StudentAccounts;

/// <summary>Choose one fee task, its scope, and review the financial result before saving.</summary>
public sealed partial class ChangeFeeDiscountForm : ApplicationWindowForm
{
    private readonly int assignment;
    private readonly string identity, feeName, originalLabel;
    private readonly decimal standardAmount, originalDiscount;
    private readonly bool originalWaiver, agreementAllowed;
    private readonly long? billId;
    private readonly ModernComboBox action = HumanUi.Combo(), scope = HumanUi.Combo();
    private readonly ModernTextBox newRate = HumanUi.TextBox(false,20), amount = HumanUi.TextBox(false,20), reason = HumanUi.TextBox(false,500);
    private readonly ModernComboBox concession = HumanUi.Combo(), paidReduction = HumanUi.Combo();
    private readonly ModernDatePicker fromMonth = HumanUi.Date(), throughMonth = HumanUi.Date();
    private readonly Label impact = new() { AutoSize=true, Dock=DockStyle.Fill, Padding=new Padding(6) };
    private readonly DataGridView bills = HumanUi.Grid(false), schedule = HumanUi.Grid(false);
    private readonly ModernButton review = new() { Text="Review Fee Change", ThemeRole="Secondary" },
        save = new() { Text="Confirm Fee Change", ThemeRole="Success", Enabled=false },
        check = new() { Text="Check Save Outcome", ThemeRole="Secondary" };
    private readonly TableLayoutPanel fields = new() { Dock=DockStyle.Top, AutoSize=true, ColumnCount=2 };
    private readonly Func<SqlParameter[],Task<DataSet>> query;
    private readonly Func<SqlParameter[],Task<MutationWriteResult>> submit;
    private string initial;
    private DataSet? reviewed;
    private MutationIntent? pending;
    private bool busy, blocked, monthly, validContext, reviewStage;
    private int generation;
    private decimal releasedPayment;
    private int? fixedBillAction;
    private decimal billBalance;
    private bool recoveryOnly;
    private bool HasAction => action.SelectedIndex>=0;
    private bool DiscountAction => action.SelectedIndex==0;
    private bool WaiverAction => action.SelectedIndex==1;
    private bool RateAction => action.SelectedIndex==2;
    private bool RemoveAction => action.SelectedIndex==3;
    private bool UpdateAgreement => billId is null && agreementAllowed && scope.SelectedIndex is 0 or 1;
    private bool ApplyBills => billId is not null || !agreementAllowed || scope.SelectedIndex is 1 or 2;
    private bool FullWaiver => WaiverAction || RateAction && originalWaiver;
    private decimal ChosenDiscount => FullWaiver || RemoveAction ? 0m : RateAction ? originalDiscount :
        ParseWholeAmount(string.IsNullOrWhiteSpace(amount.Text)?"0":amount.Text);
    private static decimal ParseWholeAmount(string text)=>InputParse.TryWholeAmount(text,out decimal value)
        ?value:throw new ArgumentException("Enter a whole-rupee amount using digits only.");
    private string? ChosenLabel => RemoveAction ? null : RateAction ? (string.IsNullOrWhiteSpace(originalLabel)||originalLabel=="None"?null:originalLabel) :
        Convert.ToString(concession.SelectedItem)=="None"?null:Convert.ToString(concession.SelectedItem);
    internal bool Changed { get; private set; }

    public ChangeFeeDiscountForm(int id,string fee,decimal rate,decimal discount,DataRow? periodContext=null)
        : this(id,fee,rate,discount,periodContext,null,null) { }

    internal ChangeFeeDiscountForm(int id,string fee,decimal rate,decimal discount,DataRow? context,
        Func<SqlParameter[],Task<DataSet>>? query,Func<SqlParameter[],Task<MutationWriteResult>>? submit)
    {
        assignment=id; standardAmount=rate; originalDiscount=discount; feeName=fee;
        this.query=query??(args=>DatabaseHelper.QueryUserSetAsync("SaveStudentFeeDiscount",args));
        this.submit=submit??SubmitRecoverableAsync;
        Text=$"Review Fee Terms — {fee}"; StartPosition=FormStartPosition.CenterParent;
        Size=new Size(AppScale.Scale(940),AppScale.Scale(700)); MinimumSize=new Size(AppScale.Scale(850),AppScale.Scale(650));
        amount.ZeroAsPlaceholder=newRate.ZeroAsPlaceholder=true;
        amount.Text=discount.ToString("0.############################",System.Globalization.CultureInfo.InvariantCulture);
        newRate.Text=rate.ToString("0.############################",System.Globalization.CultureInfo.InvariantCulture);
        context??=id>0?FeeDiscountPeriod.Context(id):null;
        monthly=context is not null && Convert.ToString(context["Frequency"])=="Monthly";
        validContext=context is not null;
        identity=context?.Table.Columns.Contains("StudentName")==true?$"{context["AdmissionNumber"]} · {context["StudentName"]} · {context["YearName"]}":"";
        agreementAllowed=context is not null && (!context.Table.Columns.Contains("Status") || Convert.ToString(context["Status"]) is "Active" or "Pending");
        billId=context?.Table.Columns.Contains("BillingChargeId")==true?Convert.ToInt64(context["BillingChargeId"]):null;
        billBalance=context?.Table.Columns.Contains("Balance")==true?Convert.ToDecimal(context["Balance"]):rate-discount;
        originalLabel=context?.Table.Columns.Contains("ConcessionLabel")==true?Convert.ToString(context["ConcessionLabel"])??"None":"None";
        originalWaiver=originalLabel is "Free" or "Waived" || originalLabel=="Scholarship" && discount==rate;
        action.Items.AddRange(new object[]{"Give Discount","Waive Fee","Correct Fee Amount","Remove Discount"});
        if(billId is not null) scope.Items.Add("This Bill");
        else if(!agreementAllowed) scope.Items.Add("Existing Bills Only");
        else scope.Items.AddRange(new object[]{"Saved Fee Plan","Saved Fee Plan and Existing Bills","Existing Bills in This Period"});
        scope.SelectedIndex=0;
        concession.Items.AddRange(new object[]{"Scholarship","None"});
        concession.SelectedItem=originalLabel=="Scholarship"?"Scholarship":"None";
        concession.FormattingEnabled=true;
        concession.Format+=(_,e)=>e.Value=FeeConcessionLabels.Display(Convert.ToString(e.ListItem)??"");
        paidReduction.Items.AddRange(new object[]{"Keep as advance credit","Refund the excess payment"}); paidReduction.SelectedIndex=0;
        if(context is not null)
        {
            DateTime Date(string column,DateTime fallback)=>context.Table.Columns.Contains(column)&&context[column] is DateTime value?First(value):fallback;
            DateTime first=Date("StartDate",First(DateTime.Today)),last=Date("EndDate",First(DateTime.Today));
            first=new[]{first,Date("EnrollmentDate",first)}.Max();
            if(first<=last)
            {
                fromMonth.Format=throughMonth.Format=DateTimePickerFormat.Custom;
                fromMonth.CustomFormat=throughMonth.CustomFormat="MMMM yyyy";
                fromMonth.Value=throughMonth.Value=first;
                fromMonth.MinDate=throughMonth.MinDate=first; fromMonth.MaxDate=throughMonth.MaxDate=last;
                fromMonth.Value=First(DateTime.Today)<first?first:First(DateTime.Today)>last?last:First(DateTime.Today);
                throughMonth.Value=last;
                if(!monthly) fromMonth.Value=first;
                if(billId is not null && context.Table.Columns.Contains("BillingMonth") && context["BillingMonth"] is DateTime month)
                    fromMonth.Value=throughMonth.Value=First(month);
            }
            else validContext=false;
        }
        Build();
        action.SelectedIndexChanged+=(_,_)=>{ConfigureConcession();InvalidateReview();};
        scope.SelectedIndexChanged+=(_,_)=>InvalidateReview();
        amount.TextChanged+=(_,_)=>InvalidateReview();
        reason.TextChanged+=(_,_)=>InvalidateReview(updateChoices:false);
        newRate.TextChanged+=(_,_)=>InvalidateReview(); concession.SelectedIndexChanged+=(_,_)=>InvalidateReview();
        fromMonth.ValueChanged+=(_,_)=>InvalidateReview(); throughMonth.ValueChanged+=(_,_)=>InvalidateReview();
        paidReduction.SelectedIndexChanged+=(_,_)=>InvalidateReview(keepPaidDecision:true);
        review.Click+=async(_,_)=>await ReviewAsync(); save.Click+=async(_,_)=>await SaveAsync(); check.Click+=async(_,_)=>await CheckOutcomeAsync();
        editChoices.Click+=(_,_)=>InvalidateReview();
        initial=Draft();
        FormClosing+=(_,e)=>{
            if(busy&&!Changed){e.Cancel=true;return;}
            if(e.CloseReason==CloseReason.UserClosing&&!Changed&&Draft()!=initial)
                e.Cancel=!ModernMessageDialog.Confirm(this,"Discard the unsaved fee choices?","Review Fee Terms",defaultYes:false);
        };
        Disposed+=(_,_)=>reviewed?.Dispose();
        impact.Text="Choose what you want to change. Review shows the result before anything is saved.";
        ReadPending(); State();
    }
    private static DateTime First(DateTime value)=>new(value.Year,value.Month,1);
    internal void ConfigureRecoveryOnly()
    {
        recoveryOnly=true;Text="Check Fee Change Save";
        currentTerms.Visible=false;
        MinimumSize=new Size(AppScale.Scale(620),AppScale.Scale(350));
        Size=new Size(AppScale.Scale(680),AppScale.Scale(400));
        if(!blocked)impact.Text="No earlier fee change needs checking.";
        State();
    }
    internal void ConfigureCollectionBill(bool removeDiscount=false)
    {
        if(billId is null || billBalance<=0m)
            throw new InvalidOperationException("Only an unpaid bill can be changed here. Reload the fees.");
        if(removeDiscount && originalDiscount<=0m)
            throw new InvalidOperationException("This bill has no discount to remove. Reload the fees.");
        fixedBillAction=removeDiscount?3:0;
        action.SelectedIndex=fixedBillAction.Value;
        concession.SelectedItem="None";
        Text=$"{(removeDiscount?"Remove Discount":originalDiscount>0m?"Change Discount":"Give Discount")} — {feeName}";
        review.Text=removeDiscount?"Review Removal":"Review Discount";
        save.Text=removeDiscount?"Confirm Removal":"Confirm Discount";
        MinimumSize=new Size(AppScale.Scale(620),AppScale.Scale(500));
        Size=new Size(AppScale.Scale(700),AppScale.Scale(560));
        initial=Draft();State();
    }
    private string Draft()=>string.Join("|",action.SelectedIndex,scope.SelectedIndex,amount.Text,concession.Text,
        First(fromMonth.Value),First(throughMonth.Value),paidReduction.Text,reason.Text,newRate.Text);
    private void ConfigureConcession()
    {
        string previous=Convert.ToString(concession.SelectedItem)??"";
        concession.Items.Clear();
        concession.Items.AddRange(WaiverAction?new object[]{"Waived","Free","Scholarship"}:new object[]{"Scholarship","None"});
        concession.SelectedItem=concession.Items.Contains(previous)?previous:
            WaiverAction?"Waived":originalLabel=="Scholarship"?"Scholarship":"None";
    }
    private void InvalidateReview(bool keepPaidDecision=false,bool updateChoices=true)
    {
        bool stageChanged=reviewStage&&!keepPaidDecision;
        generation++;
        if(reviewed is not null){bills.DataSource=schedule.DataSource=null;reviewed.Dispose();reviewed=null;}
        if(!keepPaidDecision){releasedPayment=0;reviewStage=false;}
        if(!busy&&!blocked) impact.Text=keepPaidDecision
            ? $"Excess already paid: Rs. {releasedPayment:N0}. Review again to confirm the {(paidReduction.SelectedIndex==1?"refund":"advance credit")} choice."
            : "Review the selected change to see its effect before confirming.";
        State(updateChoices||stageChanged);
    }
    private void State(bool updateChoices=true)
    {
        if(IsDisposed)return;
        bool canChange=!recoveryOnly&&validContext&&!busy&&!blocked&&!Changed&&CurrentUser.HasPermission("Fees.Adjust");
        fields.Enabled=canChange;
        fields.Visible=!recoveryOnly;
        if(updateChoices)UpdateVisibleFields();
        review.Enabled=canChange&&HasAction; save.Enabled=canChange&&reviewed is not null;
        review.Visible=!reviewStage||reviewed is null;
        review.Text=reviewStage?"Review Again":fixedBillAction is not null
            ?RemoveAction?"Review Removal":"Review Discount":"Review Fee Change";
        save.Visible=reviewStage;
        editChoices.Visible=reviewStage;editChoices.Enabled=canChange;
        check.Visible=blocked; check.Enabled=!busy;
        if(updateChoices)UpdateQuickControls(canChange);
    }
    private bool ValidateChoices()
    {
        if(!validContext){impact.Text="This fee has no valid review period. Close and reload its bill or fee plan.";return false;}
        if(!CurrentUser.HasPermission("Fees.Adjust")){impact.Text="Your permission to change fees is no longer available.";return false;}
        if(fixedBillAction is int fixedAction && action.SelectedIndex!=fixedAction)
        {impact.Text="Close and choose the intended fee action again.";return false;}
        if(!HasAction){impact.Text="Choose Give Discount, Waive Fee, Correct Fee Amount or Remove Discount.";action.Focus();return false;}
        decimal rate=standardAmount;
        if(RateAction&&(!InputParse.TryWholeAmount(newRate.Text,out rate)||rate<0))
        {impact.Text="Enter a non-negative fee in whole rupees.";newRate.Focus();return false;}
        if(DiscountAction&&(!InputParse.TryWholeAmount(amount.Text,out decimal value)||value<=0||value>rate))
        {
            impact.Text=fixedBillAction is not null?"Enter a positive discount in whole rupees. Use Waive Remaining Dues to clear all unpaid dues."
                :$"Enter a discount above zero and no more than Rs. {rate:N0}. To clear it, choose Remove Discount.";
            amount.Focus();return false;
        }
        if(fixedBillAction is not null && DiscountAction)
        {
            decimal proposed=ParseWholeAmount(amount.Text);
            if(proposed==originalDiscount){impact.Text="Enter a different discount amount.";amount.Focus();return false;}
            if(proposed>=originalDiscount+billBalance)
            {impact.Text="To clear all remaining dues, close and choose Waive Remaining Dues on this fee.";amount.Focus();return false;}
        }
        if(RateAction&&!originalWaiver&&originalDiscount>rate)
        {impact.Text=$"The new fee is below the existing Rs. {originalDiscount:N0} discount. Review or remove that discount first.";return false;}
        if(First(fromMonth.Value)>First(throughMonth.Value)){impact.Text="The through month must be on or after the from month.";return false;}
        return true;
    }
    private SqlParameter[] Args(bool preview)=>new[]{
        DatabaseHelper.Int("@StudentFeeAssignmentId",assignment),DatabaseHelper.P("@DiscountAmount",ChosenDiscount),
        DatabaseHelper.P("@ConcessionLabel",ChosenLabel),
        DatabaseHelper.Date("@EffectiveFrom",First(fromMonth.Value)),DatabaseHelper.Date("@EffectiveThrough",First(throughMonth.Value)),
        DatabaseHelper.P("@NewAmount",RateAction?ParseWholeAmount(newRate.Text):null),
        DatabaseHelper.Bit("@ApplyAvailableCredit",false),DatabaseHelper.Bit("@UpdateAgreement",UpdateAgreement),DatabaseHelper.P("@BillingChargeId",billId),DatabaseHelper.Bit("@FullFeeWaiver",FullWaiver),
        DatabaseHelper.Bit("@ApplyToBills",ApplyBills),DatabaseHelper.P("@PaidReductionAction",RateAction&&paidReduction.SelectedIndex==1?"Refund":"Credit"),
        DatabaseHelper.P("@Reason",string.IsNullOrWhiteSpace(reason.Text)?"Concession revised":reason.Text.Trim()),DatabaseHelper.Bit("@Preview",preview),
        DatabaseHelper.P("@ExpectedSnapshot",preview?null:Convert.ToString(reviewed!.Tables[0].Rows[0]["Snapshot"]))};
    private async Task ReviewAsync()
    {
        if(busy||blocked||Changed||!ValidateChoices())return;
        int version=++generation; busy=true; State(); impact.Text="Reviewing the fee change…";
        try
        {
            var result=await query(Args(true));
            if(IsDisposed||version!=generation){result.Dispose();return;}
            if(result.Tables.Count<3||result.Tables[0].Rows.Count!=1){result.Dispose();throw new InvalidDataException("The fee review is incomplete. Reload before saving.");}
            var periods=result.Tables[2];
            if(RateAction&&UpdateAgreement&&periods.Columns.Contains("Old Fee")&&periods.Columns.Contains("Old Concession")&&
                periods.AsEnumerable().Any(period=>originalWaiver
                    ? Convert.ToDecimal(period["Old Concession"])!=Convert.ToDecimal(period["Old Fee"])
                    : Convert.ToDecimal(period["Old Concession"])!=originalDiscount))
            {
                result.Dispose();
                InvalidateReview();
                impact.Text="This period has different discount terms. Choose a narrower period or review each bill separately so correcting the fee does not replace those discounts.";
                return;
            }
            reviewed?.Dispose(); reviewed=result;
            reviewStage=true;
            bills.DataSource=result.Tables[1]; schedule.DataSource=result.Tables[2];
            foreach(var grid in new[]{bills,schedule}) foreach(DataGridViewColumn column in grid.Columns)
                if(column.ValueType==typeof(decimal))column.DefaultCellStyle.Format="N0"; else if(column.ValueType==typeof(DateTime))column.DefaultCellStyle.Format="MMM yyyy";
            if(fixedBillAction is not null)
                foreach(DataGridViewColumn column in bills.Columns)
                {
                    column.Visible=column.Name is "Current Bill" or "Proposed Bill";
                    if(column.Visible)column.AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;
                }
            releasedPayment=ApplyBills?Convert.ToDecimal(result.Tables[0].Rows[0]["ReleasedPaidAmount"]):0;
            impact.Text=ReviewText(result);
        }
        catch(Exception ex){reviewed?.Dispose();reviewed=null;releasedPayment=0;if(!IsDisposed)FMSC_SMS.Core.OperationFeedback.Set(impact,DbMessage.UserMessage(ex),true);}
        finally
        {
            busy=false;State();
            if(reviewed is not null) reviewScroll.ScrollControlIntoView(releasedPayment>0?paidField:impact);
        }
    }
    private string ReviewText(DataSet result)
    {
        var row=result.Tables[0].Rows[0];
        string summary=fixedBillAction is not null
            ?$"{(RemoveAction?"Review Discount Removal":"Review Discount")}\r\n{feeName} · {fromMonth.Value:MMM yyyy}. This bill only."
            :$"3. Review the change\r\n{action.Text}: {feeName}. {ScopeText()}";
        if(ApplyBills && result.Tables[1].Rows.Count>0 && result.Tables[1].Columns.Contains("Current Bill") && result.Tables[1].Columns.Contains("Proposed Bill"))
        {
            decimal before=result.Tables[1].AsEnumerable().Sum(b=>Convert.ToDecimal(b["Current Bill"])),
                after=result.Tables[1].AsEnumerable().Sum(b=>Convert.ToDecimal(b["Proposed Bill"]));
            summary+=$"\r\n{(billId is not null?feeName:"Issued bill total")}: Rs. {before:N0} → Rs. {after:N0}.";
        }
        else if(ApplyBills)
        {
            decimal delta=Convert.ToDecimal(row["Difference"]);
            summary+=$"\r\n{row["IssuedBills"]} bill(s): {(delta<0?"decrease":"increase")} of Rs. {Math.Abs(delta):N0}.";
        }
        if(DiscountAction) summary+=$" Discount: Rs. {ChosenDiscount:N0}.";
        else if(WaiverAction) summary+=" Full concession applied, limited to unpaid dues on existing charges.";
        else if(RemoveAction) summary+=fixedBillAction is not null?" The bill discount is removed. Earlier payments remain recorded.":" The discount or full waiver is removed.";
        else summary+=$" Full fee: Rs. {standardAmount:N0} → Rs. {ParseWholeAmount(newRate.Text):N0}. " +
            (originalWaiver?"The existing full waiver is retained.":$"Existing discount retained: Rs. {originalDiscount:N0}.");
        if(monthly&&billId is null) summary+=$"\r\nPeriod: {fromMonth.Value:MMM yyyy} through {throughMonth.Value:MMM yyyy}.";
        if(releasedPayment>0) summary+=$"\r\nExcess already paid: Rs. {releasedPayment:N0}, {(paidReduction.SelectedIndex==1?"to refund":"to keep as advance credit")}.";
        return summary+"\r\nReview the comparison below, then confirm. No payment is collected here.";
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
        if(!CurrentUser.HasPermission("Fees.Adjust")){impact.Text="Your permission to review fee changes is no longer available.";return;}
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
