using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Communication;

// Shared controls and guarded asynchronous persistence; no networking lives in this window.
internal sealed class CommunicationEditor : ApplicationWindowForm, IBusyPage, IUnsavedChanges
{
    private readonly Dictionary<string,Control> inputs=new();
    private readonly TableLayoutPanel fields=new(){Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Margin=Padding.Empty};
    private readonly ModernButton save=new(){Text="Save",ThemeRole="Primary"};
    private readonly Label status=new(){Dock=DockStyle.Bottom,Height=50,AutoEllipsis=true,ForeColor=AppTheme.Danger};
    private readonly Func<CommunicationEditor,Task> persist;
    private string baseline="";private bool saved;
    public bool IsBusy{get;private set;}
    public string BusyMessage=>"Communication settings are being saved.";
    public bool HasUnsavedChanges=>!saved && baseline!=Snapshot();
    public string UnsavedChangesMessage=>"Discard these communication changes?";
    public CommunicationEditor(string title,Func<CommunicationEditor,Task> persist)
    {
        this.persist=persist;Text=title;StartPosition=FormStartPosition.CenterParent;ClientSize=new Size(AppScale.Scale(760),AppScale.Scale(680));MinimumSize=new Size(AppScale.Scale(600),AppScale.Scale(440));ShowInTaskbar=false;
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(AppMetrics.PagePadding)};
        fields.ColumnCount=1;scroll.Controls.Add(fields);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=AppMetrics.ButtonRowHeight,FlowDirection=FlowDirection.RightToLeft};
        var cancel=new ModernButton{Text="Cancel",ThemeRole="Secondary"};cancel.Click+=(_,_)=>Close();actions.Controls.Add(save);actions.Controls.Add(cancel);
        Controls.Add(scroll);Controls.Add(status);Controls.Add(actions);
        save.Click+=async(_,_)=>{
            if(IsBusy)return;IsBusy=true;save.Enabled=false;fields.Enabled=false;status.Text="";
            try{await this.persist(this);saved=true;}
            catch(Exception ex){status.Text=DbMessage.UserMessage(ex);}
            finally{IsBusy=false;if(!IsDisposed){save.Enabled=true;fields.Enabled=true;}if(saved&&!IsDisposed){DialogResult=DialogResult.OK;Close();}}
        };
        FormClosing+=(_,e)=>{if(IsBusy){e.Cancel=true;return;}if(HasUnsavedChanges)e.Cancel=!ModernMessageDialog.Confirm(this,UnsavedChangesMessage,UiText.Dialogs.UnsavedChanges);};
        Shown+=(_,_)=>baseline=Snapshot();
    }
    public void TextField(string key,string label,string value="",bool multiline=false,bool password=false)
    {
        var input=HumanUi.TextBox(multiline,2000);input.Text=value;input.UseSystemPasswordChar=password;
        if(multiline){input.Height=AppScale.Scale(130);input.MinimumSize=new Size(0,AppScale.Scale(130));}
        Add(key,label,input,multiline?AppScale.Scale(170):AppMetrics.FieldRowHeight);
    }
    public void Choice(string key,string label,IEnumerable<string> values,string selected="")
    {
        var input=HumanUi.Combo();foreach(string value in values)input.Items.Add(value);
        input.SelectedItem=selected;if(input.SelectedIndex<0&&input.Items.Count>0)input.SelectedIndex=0;
        Add(key,label,input,AppMetrics.FieldRowHeight);
    }
    public void Flag(string key,string label,bool value=false)
    {var input=new CheckBox{Text=label,Checked=value,AutoSize=true,Font=AppTheme.NormalFont,ForeColor=AppTheme.Text};inputs[key]=input;fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));fields.Controls.Add(input,0,fields.RowCount++);}
    private void Add(string key,string label,Control input,int height)
    {inputs[key]=input;var field=HumanUi.Field(label,input);field.Dock=DockStyle.Top;field.MinimumSize=new Size(0,height);fields.RowStyles.Add(new RowStyle(SizeType.Absolute,height));fields.Controls.Add(field,0,fields.RowCount++);}
    public void OnChange(string key,Action action)=>inputs[key].TextChanged+=(_,_)=>action();
    public void ClearFlag(string key)=>((CheckBox)inputs[key]).Checked=false;
    public string Value(string key)=>inputs[key].Text.Trim();
    public bool Checked(string key)=>((CheckBox)inputs[key]).Checked;
    public int Integer(string key)=>int.TryParse(Value(key),out int value)?value:throw new ArgumentException("Enter a whole number for "+key+".");
    public decimal? Cost(string key)=>Value(key).Length==0?null:decimal.TryParse(Value(key),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out decimal value)?value:throw new ArgumentException("Enter a cost estimate using a decimal point.");
    private string Snapshot()=>string.Join("\u001f",inputs.Select(x=>x.Value is CheckBox c?c.Checked.ToString():x.Value.Text));
}
