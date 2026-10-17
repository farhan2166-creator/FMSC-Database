using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Administration;

// Available from the dashboard Account menu when role viewing is permitted.
// Administrator is always full authority; only Accountant grants may be edited.
public sealed class RolesPage : AccessEditorPage
{
    public RolesPage() : this(new AdministrationStore(AccessEditorKind.Roles),AccessRules.For(CurrentUser.HasPermission)) { }
    internal RolesPage(IAdministrationStore store, AccessRules rules, bool initialize=true)
        : base(AccessEditorKind.Roles,store,rules)
    {
        NameInput.PlaceholderText="Administrator or Accountant";
        DescriptionInput.PlaceholderText="Describe this role's responsibility";
        AddField(HumanUi.Field("Role",NameInput));
        AddField(HumanUi.Field("Description",DescriptionInput));
        CompleteLayout(initialize);
    }
}
