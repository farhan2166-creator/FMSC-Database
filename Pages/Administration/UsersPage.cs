using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Administration;

// Available from the dashboard Account menu when its viewing permissions allow it.
// Administrative writes still require the separate management permissions.
public sealed class UsersPage : AccessEditorPage
{
    internal readonly ModernTextBox NewPassword = HumanUi.TextBox(false,100);
    internal readonly ModernTextBox ConfirmPassword = HumanUi.TextBox(false,100);
    internal readonly ModernButton ResetAction;
    private readonly Panel passwordField, confirmationField;
    private readonly RowStyle passwordRow, confirmationRow;
    private readonly FormValidator validator;

    public UsersPage() : this(new AdministrationStore(AccessEditorKind.Users),AccessRules.For(CurrentUser.HasPermission)) { }
    internal UsersPage(IAdministrationStore store, AccessRules rules, bool initialize=true)
        : base(AccessEditorKind.Users,store,rules)
    {
        validator=CreateValidator();
        NameInput.PlaceholderText="Enter a unique username";
        AddField(HumanUi.Field("Username",NameInput,true));
        AddField(ForceChange,AppMetrics.InputControlHeight+8);
        NewPassword.UseSystemPasswordChar=ConfirmPassword.UseSystemPasswordChar=true;
        NewPassword.PlaceholderText=PasswordPolicy.Requirement;ConfirmPassword.PlaceholderText="Re-enter the new user's password";
        passwordField=HumanUi.Field("Initial password",NewPassword,true);
        confirmationField=HumanUi.Field("Confirm initial password",ConfirmPassword,true);
        passwordRow=AddField(passwordField);confirmationRow=AddField(confirmationField);
        NewPassword.TextChanged+=(_,_)=>PasswordDraftChanged();ConfirmPassword.TextChanged+=(_,_)=>PasswordDraftChanged();
        ResetAction=AddAction("Reset Password",async(_,_)=>await OpenResetAsync(),"Secondary");
        UiAccessibility.Describe(ResetAction,"Reset the saved user's password",
            "Save or reload account and role changes first. Password reset does not submit a pending role selection.");
        CompleteLayout(initialize);
    }
    private protected override bool ExtraDirty => Creating&&(NewPassword.Text.Length>0||ConfirmPassword.Text.Length>0);
    private protected override string? CreatePassword => Creating?NewPassword.Text:null;
    private protected override bool ValidateExtra()
    {
        validator.Begin();validator.Required(NameInput,UiText.Validation.Required);
        if(Creating)
        {
            validator.Check(NewPassword,PasswordPolicy.IsValid(NewPassword.Text),PasswordPolicy.Requirement,()=>PasswordPolicy.IsValid(NewPassword.Text));
            validator.Check(ConfirmPassword,NewPassword.Text==ConfirmPassword.Text,"Does not match",()=>NewPassword.Text==ConfirmPassword.Text);
        }
        return validator.Finish();
    }
    private protected override void ClearSecrets(){NewPassword.Clear();ConfirmPassword.Clear();validator.ClearAll();}
    private protected override object CaptureExtra() => (NewPassword.Text,ConfirmPassword.Text);
    private protected override void RestoreExtra(object? state)
    {
        if(state is ValueTuple<string,string> values){NewPassword.Text=values.Item1;ConfirmPassword.Text=values.Item2;}
    }
    private protected override void ModeChanged()
    {
        passwordField.Visible=confirmationField.Visible=Creating;
        passwordRow.Height=confirmationRow.Height=Creating?AppMetrics.FieldRowHeight:0;
    }
    private protected override void ExtraEnablement(bool editable)
    {
        NewPassword.ReadOnly=ConfirmPassword.ReadOnly=!editable;
        ResetAction.Visible=!Creating;
        ResetAction.Enabled=editable&&Original is not null&&!HasUnsavedChanges;
    }
    internal Task<AccessSaveResult> ResetForSavedUserAsync(string password, bool forceChange) => ResetAsync(password,forceChange);
    private async Task OpenResetAsync()
    {
        if(!ResetAction.Enabled||Original is not { Protected:false } original)return;
        using var dialog=new PasswordResetDialog(original.Name,ResetForSavedUserAsync);
        dialog.ShowDialog(FindForm());
        await Task.CompletedTask;
    }
}
