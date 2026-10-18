using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Pages.Finance.StudentAccounts;

/// <summary>Review one agreed concession period and its explicit effect on issued bills.</summary>
public sealed class ChangeFeeDiscountForm : ApplicationWindowForm
{
    private readonly int assignment;
    private readonly decimal standardAmount;
    private readonly ModernTextBox amount=HumanUi.TextBox(false,20),reason=HumanUi.TextBox(false,500);
    private readonly ModernComboBox concession=HumanUi.Combo(),paidReduction=HumanUi.Combo();
    private readonly ModernDatePicker fromMonth=HumanUi.Date(),throughMonth=HumanUi.Date();
    private readonly CheckBox applyBills=new(){Text="Apply this concession to issued monthly bills in the chosen period",AutoSize=true,Checked=true};
    private readonly Label impact=new(){AutoSize=true,Dock=DockStyle.Fill,Padding=new Padding(6)};
    private readonly DataGridView bills=HumanUi.Grid(false),schedule=HumanUi.Grid(false);
    private readonly ModernButton review=new(){Text="Review Concession",ThemeRole="Secondary"},
        save=new(){Text="Confirm Concession",ThemeRole="Success",Enabled=false},
        check=new(){Text="Check Save Outcome",ThemeRole="Secondary"};
    private readonly TableLayoutPanel fields=new(){Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,RowCount=5};
    private readonly Func<SqlParameter[],Task<DataSet>> query;
    private readonly Func<SqlParameter[],Task<MutationWriteResult>> submit;
    private readonly string initial;
    private DataSet? reviewed;
    private MutationIntent? pending;
    private bool busy,blocked,monthly;
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
        Text=$"Fee Concession — {fee}";StartPosition=FormStartPosition.CenterParent;
        Size=new Size(AppScale.Scale(980),AppScale.Scale(700));MinimumSize=new Size(800,550);
        amount.Text=discount.ToString("0.##");
        concession.Items.AddRange(new object[]{"None","Free","Scholarship","Waived"});concession.SelectedIndex=0;
        paidReduction.Items.AddRange(new object[]{"Keep paid reductions as credit","Refund paid reductions"});paidReduction.SelectedIndex=0;
        context??=id>0?FeeDiscountPeriod.Context(id):null;
        monthly=context is not null&&Convert.ToString(context["Frequency"])=="Monthly";
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
            }
            else monthly=false;
        }
        Build(fee);
        void InvalidateReview(){generation++;bills.DataSource=schedule.DataSource=null;reviewed?.Dispose();reviewed=null;
            if(!busy)impact.Text="Choices changed. Review the period and financial effect before confirming.";State();}
        amount.TextChanged+=(_,_)=>InvalidateReview();reason.TextChanged+=(_,_)=>InvalidateReview();
        fromMonth.ValueChanged+=(_,_)=>InvalidateReview();throughMonth.ValueChanged+=(_,_)=>InvalidateReview();
        applyBills.CheckedChanged+=(_,_)=>InvalidateReview();paidReduction.SelectedIndexChanged+=(_,_)=>InvalidateReview();
        concession.SelectedIndexChanged+=(_,_)=>{if(concession.Text is "Free" or "Waived")amount.Text=standardAmount.ToString("0.##");InvalidateReview();};
        review.Click+=async(_,_)=>await ReviewAsync();save.Click+=async(_,_)=>await SaveAsync();check.Click+=async(_,_)=>await CheckOutcomeAsync();
        initial=Draft();
        FormClosing+=(_,e)=>{
            if(busy&&!Changed){e.Cancel=true;return;}
            if(e.CloseReason==CloseReason.UserClosing&&!Changed&&Draft()!=initial)
                e.Cancel=!ModernMessageDialog.Confirm(this,"Discard the unsaved concession choices?","Fee Concession",defaultYes:false);
        };
        Disposed+=(_,_)=>reviewed?.Dispose();
        impact.Text=monthly?$"Agreed rate: Rs. {rate:N0}. Choose the concession period and whether issued bills should change. No new monthly bills are created."
            :"This is an annual or one-time fee. Open Fee Collection to review its issued bill and concession.";
        ReadPending();State();
    }
    private static DateTime First(DateTime value)=>new(value.Year,value.Month,1);
    private string Draft()=>string.Join("|",amount.Text,concession.Text,First(fromMonth.Value),First(throughMonth.Value),applyBills.Checked,paidReduction.Text,reason.Text);
    private void Build(string fee)
    {
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        for(int i=0;i<4;i++)fields.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.Controls.Add(HumanUi.Field($"Monthly concession for {fee} (Rs.)",amount,true),0,0);
        fields.Controls.Add(HumanUi.Field("Concession",concession),1,0);
        fields.Controls.Add(HumanUi.Field("From month",fromMonth,true),0,1);fields.Controls.Add(HumanUi.Field("Through month",throughMonth,true),1,1);
        fields.Controls.Add(HumanUi.Field("Reason",reason,true),0,2);fields.SetColumnSpan(fields.Controls[fields.Controls.Count-1],2);
        fields.Controls.Add(HumanUi.Field("If a bill has already been paid",paidReduction),0,3);fields.SetColumnSpan(fields.Controls[fields.Controls.Count-1],2);
        fields.Controls.Add(applyBills,0,4);fields.SetColumnSpan(applyBills,2);
        var tabs=new TabControl{Dock=DockStyle.Fill};
        foreach(var (title,grid) in new[]{("Issued bills",bills),("Agreed fee periods",schedule)})
        {var tab=new TabPage(title);grid.Dock=DockStyle.Fill;tab.Controls.Add(grid);tabs.TabPages.Add(tab);}
        var close=new ModernButton{Text="Close",DialogResult=DialogResult.Cancel};CancelButton=close;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=1,RowCount=4};
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(fields,0,0);root.Controls.Add(impact,0,1);root.Controls.Add(tabs,0,2);root.Controls.Add(HumanUi.ButtonRow(review,save,check,close),0,3);Controls.Add(root);
        UiHelper.SetGridEmptyMessage(bills,"Review to see current and proposed issued bills.");
        UiHelper.SetGridEmptyMessage(schedule,"Review to see concession periods, including scheduled fee rates.");
    }
    private void State()
    {
        if(IsDisposed)return;
        bool canChange=monthly&&!busy&&!blocked&&!Changed&&CurrentUser.HasPermission("Fees.Adjust");
        fields.Enabled=canChange;amount.Enabled=concession.Text is not("Free" or "Waived");
        paidReduction.Enabled=applyBills.Checked;
        review.Enabled=canChange;save.Enabled=canChange&&reviewed is not null;check.Enabled=!busy;
    }
    private bool ValidateChoices()
    {
        if(!CurrentUser.HasPermission("Fees.Adjust")){impact.Text="Your permission to change fees is no longer available.";return false;}
        if(!InputParse.TryWholeAmount(string.IsNullOrWhiteSpace(amount.Text)?"0":amount.Text,out decimal value)||value<0||value>standardAmount)
        {impact.Text=$"Enter a concession from zero to Rs. {standardAmount:N0}, in whole rupees.";amount.Focus();return false;}
        if(First(fromMonth.Value)>First(throughMonth.Value)){impact.Text="The through month must be on or after the from month.";return false;}
        if(string.IsNullOrWhiteSpace(reason.Text)){impact.Text="Enter the reason so the agreed fee change can be understood later.";reason.Focus();return false;}
        return true;
    }
    private SqlParameter[] Args(bool preview)=>new[]{
        DatabaseHelper.Int("@StudentFeeAssignmentId",assignment),DatabaseHelper.P("@DiscountAmount",decimal.Parse(string.IsNullOrWhiteSpace(amount.Text)?"0":amount.Text)),
        DatabaseHelper.P("@ConcessionLabel",concession.Text=="None"?null:concession.Text),
        DatabaseHelper.Date("@EffectiveFrom",First(fromMonth.Value)),DatabaseHelper.Date("@EffectiveThrough",First(throughMonth.Value)),
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
            if(result.Tables.Count<3||result.Tables[0].Rows.Count!=1){result.Dispose();throw new InvalidDataException("The concession review is incomplete. Reload before saving.");}
            reviewed?.Dispose();reviewed=result;
            bills.DataSource=result.Tables[1];schedule.DataSource=result.Tables[2];
            foreach(var grid in new[]{bills,schedule})foreach(DataGridViewColumn column in grid.Columns)
                if(column.ValueType==typeof(decimal))column.DefaultCellStyle.Format="N0";else if(column.ValueType==typeof(DateTime))column.DefaultCellStyle.Format="MMM yyyy";
            var row=result.Tables[0].Rows[0];
            decimal delta=Convert.ToDecimal(row["Difference"]),released=Convert.ToDecimal(row["ReleasedPaidAmount"]);
            impact.Text=$"{First(fromMonth.Value):MMM yyyy} through {First(throughMonth.Value):MMM yyyy}: " +
                (applyBills.Checked?$"{row["IssuedBills"]} issued bill(s) reviewed. Bill total change: Rs. {delta:N0}. Paid money released as {(paidReduction.SelectedIndex==1?"refund":"credit")}: Rs. {released:N0}.":"Issued bills and their payments stay as recorded. Only the agreed assignment schedule changes.")+
                "\r\nRates and concessions outside the selected period are retained. Existing credit may settle any additional dues.";
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
            if(outcome.Outcome==MutationOutcome.Unconfirmed){blocked=true;impact.Text="The save outcome is uncertain. Use Check Save Outcome before another concession change.";return;}
            if(outcome.Outcome==MutationOutcome.Saved)Changed=true;
            if(pending is not null){MutationRecoveryJournal.ClearVerified(pending);pending=null;}
            blocked=false;
            if(outcome.Outcome==MutationOutcome.Rejected)throw outcome.Error??new InvalidOperationException("The concession was not saved.");
            LookupDataCache.OperationCompleted("SaveStudentFeeDiscount");DialogResult=DialogResult.OK;Close();
        }
        catch(Exception ex){ReadPending();impact.Text=(Changed?"The concession was saved. Check its outcome before repeating it. ":"")+DbMessage.UserMessage(ex);}
        finally{busy=false;if(!Changed){bills.DataSource=schedule.DataSource=null;reviewed?.Dispose();reviewed=null;}State();}
    }
    private async Task<MutationWriteResult> SubmitRecoverableAsync(SqlParameter[] args)
    {
        int actor=CurrentUser.UserId;string scope=await RecoverableMutationWriter.GetScopeAsync();
        if(IsDisposed||CurrentUser.UserId!=actor)throw new InvalidOperationException("The operator or window changed before submission. Review again.");
        using var journal=MutationRecoveryJournal.Begin(MutationKind.FeeConcession,actor,scope,
            RecoverableMutationWriter.PayloadHash(MutationKind.FeeConcession,actor,args),$"Concession for assignment {assignment}, {First(fromMonth.Value):MMM yyyy}–{First(throughMonth.Value):MMM yyyy}");
        pending=journal.Intent;blocked=true;
        return await RecoverableMutationWriter.SaveAsync(journal.Intent,args);
    }
    private void ReadPending()
    {
        try{pending=MutationRecoveryJournal.ReadPending(MutationKind.FeeConcession);blocked=pending is not null;
            if(blocked)impact.Text="An earlier concession save needs checking. Use Check Save Outcome before submitting.";}
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
            else{bills.DataSource=schedule.DataSource=null;reviewed?.Dispose();reviewed=null;impact.Text="The earlier concession was not recorded. Review again before deciding whether to submit.";}
        }
        catch(Exception ex){impact.Text="The outcome is still unresolved. Keep recovery and check again. "+DbMessage.UserMessage(ex);}
        finally{busy=false;State();}
    }
}
