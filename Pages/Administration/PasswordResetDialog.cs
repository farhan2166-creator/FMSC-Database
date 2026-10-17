using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Administration;

/// <summary>Password-only operation; account/role drafts never travel through this dialog.</summary>
internal sealed class PasswordResetDialog : ApplicationWindowForm
{
    private readonly ModernTextBox password=HumanUi.TextBox(false,100),confirmation=HumanUi.TextBox(false,100);
    private readonly ModernCheckBox forceChange=new(){Text="Require password change at next sign-in",Checked=true,AutoSize=true};
    private readonly Label status=new(){Dock=DockStyle.Fill,AutoSize=true,ForeColor=AppTheme.MutedText,UseMnemonic=false};
    private readonly ModernButton save=new(){Text="Reset Password",ThemeRole="Success"},cancel=new(){Text="Cancel",ThemeRole="Secondary"};
    private readonly FormValidator validator=new();
    private readonly Func<string,bool,Task<AccessSaveResult>> reset;
    private bool busy,finished;

    internal PasswordResetDialog(string username,Func<string,bool,Task<AccessSaveResult>> reset)
    {
        this.reset=reset;Text=$"Reset password · {username}";StartPosition=FormStartPosition.CenterParent;
        FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;
        ClientSize=new Size(AppScale.Scale(520),AppScale.Scale(410));MinimumSize=SizeFromClientSize(ClientSize);
        BackColor=AppTheme.PageBackground;Font=AppTheme.NormalFont;
        password.UseSystemPasswordChar=confirmation.UseSystemPasswordChar=true;password.PlaceholderText=PasswordPolicy.Requirement;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(24)};
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.InputControlHeight+12));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.ButtonRowHeight));
        root.Controls.Add(HumanUi.Field("New password",password,true),0,0);root.Controls.Add(HumanUi.Field("Confirm new password",confirmation,true),0,1);
        root.Controls.Add(forceChange,0,2);root.Controls.Add(status,0,3);root.Controls.Add(HumanUi.ButtonRow(save,cancel),0,4);Controls.Add(root);
        status.Text="This saves only the selected account's password and next-sign-in password requirement.";
        save.Click+=async(_,_)=>await SaveAsync();cancel.Click+=(_,_)=>Close();AcceptButton=save;CancelButton=cancel;SetSaveShortcut(save);
        FormFlow.EnterMovesToNext(this);Shown+=(_,_)=>password.Focus();
        FormClosing+=(_,e)=>{
            if(busy){e.Cancel=true;status.Text="Wait for the password operation to finish.";return;}
            if(!finished&&(password.Text.Length>0||confirmation.Text.Length>0))
                e.Cancel=!ModernMessageDialog.Confirm(this,"Discard the entered password?","Unsaved password",defaultYes:false);
        };
        Disposed+=(_,_)=>validator.Dispose();
    }
    private async Task SaveAsync()
    {
        if(busy)return;
        validator.Begin();validator.Check(password,PasswordPolicy.IsValid(password.Text),PasswordPolicy.Requirement,()=>PasswordPolicy.IsValid(password.Text));
        validator.Check(confirmation,password.Text==confirmation.Text,"Does not match",()=>password.Text==confirmation.Text);
        if(!validator.Finish())return;
        busy=true;save.Enabled=cancel.Enabled=password.Enabled=confirmation.Enabled=forceChange.Enabled=false;status.Text="Resetting password…";
        try
        {
            var result=await reset(password.Text,forceChange.Checked);
            if(IsDisposed)return;
            if(result.Outcome!=AccessSaveOutcome.Rejected||result.Error is AdministrativeStaleStateException)
            {finished=true;password.Clear();confirmation.Clear();busy=false;DialogResult=result.Outcome==AccessSaveOutcome.Rejected?DialogResult.Cancel:DialogResult.OK;Close();}
            else status.Text="Password was not reset. "+DbMessage.UserMessage(result.Error??new IOException("The write was refused."));
        }
        catch(Exception ex){AppExceptionHandler.LogNonFatal(ex,"Resetting a maintenance account password");if(!IsDisposed)status.Text="The operation could not complete. "+DbMessage.UserMessage(ex);}
        finally{busy=false;if(!IsDisposed)save.Enabled=cancel.Enabled=password.Enabled=confirmation.Enabled=forceChange.Enabled=true;}
    }
}
