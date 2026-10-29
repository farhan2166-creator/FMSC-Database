namespace FMSC_SMS.Core;

internal static class CertificateCollectionDialog
{
    internal static string? Show(Control owner,int maxLength=200)
    {
        using var dialog=new ApplicationWindowForm {
            Text=UiText.Certificates.Collection,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,
            MinimizeBox=false,MaximizeBox=false,ShowInTaskbar=false,ClientSize=new Size(AppScale.Scale(520),AppScale.Scale(250))
        };
        var input=HumanUi.TextBox(false,maxLength);input.AccessibleName="Name of person collecting the certificate";
        var intro=new Label {Text="Record who is receiving this certificate.",AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,0,0,10),Font=AppTheme.NormalFont};
        var note=new Label {Text="Collecting permanently locks this copy. You can still print it.",AutoSize=true,Dock=DockStyle.Fill,
            ForeColor=AppTheme.MutedText,Margin=new Padding(0,8,0,12),MaximumSize=new Size(AppScale.Scale(480),0),Font=AppTheme.NormalFont};
        var collect=new ModernButton {Text="Collect",ThemeRole="Primary",Width=AppScale.Scale(115),Height=AppMetrics.ButtonHeight,Enabled=false,Margin=new Padding(8,0,0,0)};
        var cancel=new ModernButton {Text=UiText.Actions.Cancel,DialogResult=DialogResult.Cancel,Width=AppScale.Scale(115),Height=AppMetrics.ButtonHeight,Margin=Padding.Empty};
        string? result=null;
        bool Valid()=>!string.IsNullOrWhiteSpace(input.Text)&&input.Text.Trim().Length<=maxLength;
        input.TextChanged+=(_,_)=>collect.Enabled=Valid();
        collect.Click+=(_,_)=>{if(!Valid())return;result=input.Text.Trim();dialog.DialogResult=DialogResult.OK;dialog.Close();};
        var actions=new FlowLayoutPanel {Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=new Padding(0,8,0,0)};
        actions.Controls.Add(collect);actions.Controls.Add(cancel);
        var body=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(AppScale.Scale(20)),Margin=Padding.Empty};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));body.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.Controls.Add(intro,0,0);body.Controls.Add(CertificateUi.Field("Collected by",input,true),0,1);body.Controls.Add(note,0,2);body.Controls.Add(actions,0,4);
        dialog.Controls.Add(body);dialog.AcceptButton=collect;dialog.CancelButton=cancel;dialog.Shown+=(_,_)=>input.Focus();
        return dialog.ShowDialog(owner.FindForm())==DialogResult.OK?result:null;
    }
}
