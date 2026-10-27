using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Finance.StudentAccounts;

public sealed class TransportServiceDialog : ApplicationWindowForm
{
    private readonly int enrollment;
    private readonly bool start;
    private readonly int? initialFee;
    private readonly Func<Task<DataSet>> read;
    private readonly Func<int,DateTime,string,string,Guid,Task<DataTable>> write;
    private readonly ModernComboBox fee=HumanUi.Combo(),month=HumanUi.Combo();
    private readonly ModernTextBox reason=HumanUi.TextBox(false,500);
    private readonly Label consequence=new(){AutoSize=true,Dock=DockStyle.Fill,ForeColor=AppTheme.MutedText,Padding=new Padding(4)};
    private readonly TableLayoutPanel fields=new(){AutoSize=true,Dock=DockStyle.Top,ColumnCount=2,RowCount=3,Margin=Padding.Empty};
    private readonly ModernButton confirm=new(),refresh=new(){Text="Refresh",ThemeRole="Secondary"},cancel=new(){Text="Cancel",ThemeRole="Secondary"};
    private DataSet? context;
    private bool busy,loading,unconfirmed,saved;
    private Guid request=Guid.NewGuid();
    private sealed record FeeChoice(int Id,string Name,decimal Amount){public override string ToString()=>$"{Name} — Rs. {Amount:N0} / month";}
    private sealed record MonthChoice(DateTime Value){public override string ToString()=>Value.ToString("MMMM yyyy");}

    public TransportServiceDialog(int enrollmentId,bool start,int? feeTypeId=null):this(enrollmentId,start,feeTypeId,null,null){}
    internal TransportServiceDialog(int enrollmentId,bool start,int? feeTypeId,Func<Task<DataSet>>? read,
        Func<int,DateTime,string,string,Guid,Task<DataTable>>? write)
    {
        enrollment=enrollmentId;this.start=start;initialFee=feeTypeId;
        this.read=read??(()=>TransportServiceStore.ReadAsync(enrollment,start));
        this.write=write??((id,from,note,snapshot,key)=>TransportServiceStore.SaveAsync(enrollment,start,id,from,note,snapshot,key));
        Text=start?"Start Transport":"Stop Transport";StartPosition=FormStartPosition.CenterParent;
        ClientSize=new Size(AppScale.Scale(660),AppScale.Scale(460));MinimumSize=new Size(AppScale.Scale(600),AppScale.Scale(440));ShowInTaskbar=false;
        confirm.Text=Text;confirm.ThemeRole=start?"Success":"Warning";
        var card=HumanUi.Card(Text,"Choose the effective month. The effect on posted fees is shown below.");
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));fields.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        fields.Controls.Add(HumanUi.Field("Transport fee",fee,true),0,0);
        fields.Controls.Add(HumanUi.Field(start?"Start transport from":"Stop transport from",month,true),1,0);
        fields.Controls.Add(consequence,0,1);fields.SetColumnSpan(consequence,2);
        var reasonField=HumanUi.Field("Reason (optional)",reason);fields.Controls.Add(reasonField,0,2);fields.SetColumnSpan(reasonField,2);
        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true};scroll.Controls.Add(fields);card.Content.Controls.Add(scroll);
        scroll.SizeChanged+=(_,_)=>consequence.MaximumSize=new Size(Math.Max(200,scroll.ClientSize.Width-24),0);
        var buttons=HumanUi.ButtonRow(confirm,refresh,cancel);buttons.Dock=DockStyle.Fill;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(16)};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(card,0,0);root.Controls.Add(buttons,0,1);Controls.Add(root);CancelButton=cancel;
        fee.SelectedIndexChanged+=(_,_)=>{if(!loading)FillMonths();};month.SelectedIndexChanged+=(_,_)=>{if(!loading)Describe();};
        confirm.Click+=async(_,_)=>await SaveAsync();refresh.Click+=async(_,_)=>await LoadAsync();cancel.Click+=(_,_)=>{if(!busy)Close();};
        FormClosing+=(_,e)=>{if(busy)e.Cancel=true;};Disposed+=(_,_)=>context?.Dispose();
        Shown+=async(_,_)=>await LoadAsync();State();
    }
    private void State()
    {
        fields.Enabled=!busy&&!unconfirmed&&!saved;
        confirm.Text=unconfirmed?"Check Save Outcome":start?"Start Transport":"Stop Transport";
        confirm.Enabled=!busy&&!saved&&CurrentUser.HasPermission("Fees.Adjust")&&(unconfirmed||fee.SelectedItem is FeeChoice&&month.SelectedItem is MonthChoice);
        refresh.Enabled=!busy&&!unconfirmed&&!saved;cancel.Enabled=!busy;
    }
    private async Task LoadAsync()
    {
        if(busy||unconfirmed||saved)return;busy=true;State();
        try
        {
            var data=await read();
            if(IsDisposed){data.Dispose();return;}
            if(data.Tables.Count!=4||data.Tables[0].Rows.Count!=1){data.Dispose();throw new InvalidDataException("Transport options could not be loaded completely.");}
            context?.Dispose();context=data;loading=true;fee.Items.Clear();
            if(start)
            {
                bool hasOpen=data.Tables[1].AsEnumerable().Any(row=>row.IsNull("CancelledAt")&&row.IsNull("EffectiveTo"));
                if(!hasOpen)foreach(var row in data.Tables[2].AsEnumerable())fee.Items.Add(new FeeChoice(Convert.ToInt32(row["FeeTypeId"]),Convert.ToString(row["FeeTypeName"])??"Transport",Convert.ToDecimal(row["Amount"])));
            }
            else foreach(var group in data.Tables[1].AsEnumerable().Where(row=>row.IsNull("CancelledAt")&&row.IsNull("EffectiveTo")).GroupBy(row=>Convert.ToInt32(row["FeeTypeId"])))
            {var row=group.First();fee.Items.Add(new FeeChoice(group.Key,Convert.ToString(row["FeeTypeName"])??"Transport",Convert.ToDecimal(row["Amount"])));}
            if(fee.Items.Count>0)fee.SelectedIndex=Math.Max(0,fee.Items.Cast<FeeChoice>().ToList().FindIndex(item=>item.Id==initialFee));
            loading=false;FillMonths();
        }
        catch(Exception ex){loading=true;fee.Items.Clear();month.Items.Clear();consequence.Text=DbMessage.UserMessage(ex);}
        finally{loading=false;busy=false;State();}
    }
    private void FillMonths()
    {
        loading=true;month.Items.Clear();
        if(context is not null&&fee.SelectedItem is FeeChoice choice)
            foreach(var date in TransportMonthRules.Months(context,choice.Id,start))month.Items.Add(new MonthChoice(date));
        if(month.Items.Count>0)month.SelectedIndex=0;loading=false;Describe();State();
    }
    private void Describe()
    {
        if(context is null)return;
        if(fee.SelectedItem is not FeeChoice choice||month.SelectedItem is not MonthChoice selected)
        {consequence.Text="No transport month is available. Check the current plan, paid months and the class transport rate.";return;}
        var summary=context.Tables[0].Rows[0];DateTime today=Convert.ToDateTime(summary["Today"]);
        string cutoff=summary.IsNull("TransportCutoffDay")?"No transport cutoff day is set; the current month is allowed."
            :$"Transport cutoff: day {Convert.ToInt32(summary["TransportCutoffDay"])}. On or after that day, changes begin next month.";
        string protection="Months with any recorded payment, including a partial payment, are kept.";
        string effect;
        if(start)
        {
            bool current=selected.Value==TransportMonthRules.First(today);
            bool posted=context.Tables[3].AsEnumerable().Any(row=>Convert.ToInt32(row["FeeTypeId"])==choice.Id&&Convert.ToDateTime(row["BillingMonth"])==selected.Value);
            effect=current?posted?"This month's existing transport charge is kept; no duplicate charge is added.":$"Starts this month. Rs. {choice.Amount:N0} will be posted immediately."
                :$"Starts from {selected.Value:MMMM yyyy}. No charge is posted now; normal monthly billing will post it then.";
        }
        else
        {
            var charges=context.Tables[3].AsEnumerable().Where(row=>Convert.ToInt32(row["FeeTypeId"])==choice.Id&&Convert.ToDateTime(row["BillingMonth"])>=selected.Value&&!Convert.ToBoolean(row["HasPayment"])).ToArray();
            effect=$"Stops from {selected.Value:MMMM yyyy}. {charges.Length} unpaid posted charge(s), Rs. {charges.Sum(row=>Convert.ToDecimal(row["Balance"])):N0}, will be cancelled. Earlier dues remain owed.";
        }
        consequence.Text=cutoff+"\r\n"+protection+"\r\n"+effect;
    }
    private async Task SaveAsync()
    {
        if(busy||saved||!CurrentUser.HasPermission("Fees.Adjust"))return;
        if(unconfirmed){await CheckAsync();return;}
        if(context is null||fee.SelectedItem is not FeeChoice choice||month.SelectedItem is not MonthChoice selected)return;
        busy=true;State();
        try
        {
            using var result=await write(choice.Id,selected.Value,reason.Text,Convert.ToString(context.Tables[0].Rows[0]["Snapshot"])!,request);
            if(result.Rows.Count!=1)throw new InvalidDataException("The transport save result was incomplete.");
            saved=true;DialogResult=DialogResult.OK;
        }
        catch(Exception ex)
        {
            unconfirmed=!TransportServiceStore.IsBusinessRejection(ex);
            consequence.Text=unconfirmed?"The result could not be confirmed. Check Save Outcome before changing transport again.":DbMessage.UserMessage(ex);
        }
        finally{busy=false;State();if(saved)Close();}
    }
    private async Task CheckAsync()
    {
        busy=true;State();
        try
        {
            if(await TransportServiceStore.WasSavedAsync(enrollment,request)){saved=true;DialogResult=DialogResult.OK;}
            else{unconfirmed=false;request=Guid.NewGuid();consequence.Text="The change was not saved. Refresh the transport details before confirming again.";fee.Items.Clear();month.Items.Clear();}
        }
        catch(Exception ex){consequence.Text="The result is still unresolved. "+DbMessage.UserMessage(ex);}
        finally{busy=false;State();if(saved)Close();}
    }
}
