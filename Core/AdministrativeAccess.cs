using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FMSC_SMS.Core;

internal enum AccessEditorKind { Users, Roles }
internal sealed record AccessRules(bool CanReadUsers, bool CanManageUsers, bool CanReadRoles, bool CanManageRoles)
{
    internal static AccessRules For(Func<string, bool> has)
    {
        bool administration = has("System.Administration");
        bool roles = administration || has("System.Roles");
        bool users = roles && (administration || has("System.Users"));
        return new(users, users && (administration || has("System.Users.Manage")),
            roles, roles && (administration || has("System.Roles.Manage")));
    }
}
internal sealed record AccessPermission(int Id, string Code, string Name, string Description);
internal sealed record AccessRole(int Id, string Name, string Description, int[] Permissions)
{
    internal bool Protected => AdministrativeAccess.IsAdministrator(Name);
}
internal sealed record AccessListItem(int Id, string Name, string Summary, bool Protected);
internal sealed record AccessCatalog(AccessRole[] Roles, AccessPermission[] Permissions, AccessListItem[] Items, string Token);
internal sealed record AccessRecord(int Id, string Name, string Description, bool ForcePasswordChange,
    bool Protected, int[] Choices, string Snapshot, string CredentialProof = "", int MemberCount = 0);
internal sealed record AccessDraft(int Id, string Name, string Description, bool ForcePasswordChange, int[] Choices)
{
    internal bool SameValues(AccessDraft other) => Id == other.Id && Name == other.Name &&
        Description == other.Description && ForcePasswordChange == other.ForcePasswordChange &&
        Choices.ToHashSet().SetEquals(other.Choices);
    internal AccessDraft Clean() => this with { Name = Name.Trim(), Description = Description.Trim(), Choices = Choices.Distinct().Order().ToArray() };
    internal static AccessDraft From(AccessRecord record) => new(record.Id, record.Name, record.Description, record.ForcePasswordChange, record.Choices.ToArray());
}
internal sealed record AccessWriteIntent(AccessEditorKind Kind, AccessDraft Values, string CredentialProof = "", string OriginalSnapshot = "")
{
    internal bool Matches(AccessRecord record) => (Values.Id == 0 || Values.Id == record.Id) &&
        (Values with { Id = record.Id }).SameValues(AccessDraft.From(record)) &&
        (CredentialProof.Length == 0 || string.Equals(CredentialProof, record.CredentialProof, StringComparison.Ordinal));
}
internal enum AccessSaveOutcome { Saved, Rejected, Unconfirmed }
internal sealed record AccessSaveResult(AccessSaveOutcome Outcome, int Id = 0, AccessWriteIntent? Intent = null, Exception? Error = null);
internal enum AccessCheckOutcome { Saved, NotApplied, Changed }
internal sealed record AccessCheckResult(AccessCheckOutcome Outcome, AccessCatalog Catalog, AccessRecord? Record);
internal sealed class AdministrativeStaleStateException(string message) : InvalidOperationException(message);

internal static class AdministrativeAccess
{
    internal static bool IsAdministrator(string name) => string.Equals(name.Trim(), "Administrator", StringComparison.OrdinalIgnoreCase);
    internal static string Token(string material) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    internal static string CatalogToken(IEnumerable<AccessRole> roles, IEnumerable<AccessPermission> permissions) => Token(JsonSerializer.Serialize(new {
        Roles = roles.OrderBy(role => role.Id).Select(role => new { role.Id, role.Name, role.Description, Permissions = role.Permissions.Distinct().Order().ToArray() }),
        Permissions = permissions.OrderBy(permission => permission.Id)
    }));
    internal static HashSet<int> EffectivePermissions(AccessCatalog catalog, IEnumerable<int> roles) =>
        catalog.Roles.Where(role => roles.Contains(role.Id)).SelectMany(role => role.Permissions).ToHashSet();
    internal static string Filter(string term, params string[] columns)
    {
        if (string.IsNullOrWhiteSpace(term)) return "";
        var escaped = new StringBuilder();
        foreach (char character in term.Trim()) escaped.Append(character switch {
            '\'' => "''", '[' => "[[]", ']' => "[]]", '%' => "[%]", '*' => "[*]", _ => character.ToString()
        });
        return string.Join(" OR ", columns.Select(column => $"[{column}] LIKE '%{escaped}%'"));
    }
    internal static string? Validate(AccessEditorKind kind, AccessDraft draft, AccessCatalog catalog)
    {
        string name = draft.Name.Trim();
        if (name.Length == 0) return kind == AccessEditorKind.Users ? "Enter a username." : "Enter a role name.";
        if (name.Length > (kind == AccessEditorKind.Users ? 100 : 50)) return "The name is too long.";
        if (draft.Description.Trim().Length > 250) return "Keep the description within 250 characters.";
        if (kind == AccessEditorKind.Roles && IsAdministrator(name)) return "Administrator is a protected system role.";
        var valid = kind == AccessEditorKind.Users ? catalog.Roles.Where(role => !role.Protected).Select(role => role.Id).ToHashSet()
            : catalog.Permissions.Select(permission => permission.Id).ToHashSet();
        if (draft.Choices.Any(id => id <= 0 || !valid.Contains(id))) return "An access selection is no longer available. Reload and review it.";
        if (catalog.Items.Any(item => item.Id != draft.Id && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
            return "That name is already in use. Choose a different name.";
        if (kind == AccessEditorKind.Users && draft.Id == 0 && valid.Count == 0)
            return "Create a non-Administrator role before creating a user. No role is assigned automatically.";
        return null;
    }
    internal static string[] Hints(AccessCatalog catalog, IEnumerable<int> permissionIds)
    {
        var codes = catalog.Permissions.Where(permission => permissionIds.Contains(permission.Id)).Select(permission => permission.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hints = new List<string>();
        if (codes.Contains("System.Users.Manage") && (!codes.Contains("System.Users") || !codes.Contains("System.Roles")) && !codes.Contains("System.Administration"))
            hints.Add("Managing users also needs user and role viewing access.");
        if (codes.Contains("System.Roles.Manage") && !codes.Contains("System.Roles") && !codes.Contains("System.Administration"))
            hints.Add("Managing roles also needs role viewing access.");
        if (codes.Contains("Students.Admit") && !codes.Contains("Students.View"))
            hints.Add("Quick Registration also needs student viewing access.");
        return hints.ToArray();
    }
}

internal interface IAdministrativeTransaction : IDisposable
{
    bool SubmissionStarted { get; }
    void Commit();
    void Rollback();
}
/// <summary>Only a confirmed commit succeeds. A lost commit reply never becomes an automatic retry.</summary>
internal static class AdministrativeSaveCoordinator
{
    internal static AccessSaveResult Execute(IAdministrativeTransaction transaction, AccessWriteIntent intent, Func<int> write)
    {
        int id = 0;
        bool commitAttempted = false;
        try
        {
            id = write();
            if (id <= 0) throw new InvalidDataException("The database did not return a valid saved ID.");
            if (intent.Values.Id > 0 && id != intent.Values.Id)
                throw new InvalidDataException("The database returned a different administrative identity.");
            commitAttempted = true;
            transaction.Commit();
            return new(AccessSaveOutcome.Saved, id, intent with { Values = intent.Values with { Id = id } });
        }
        catch (Exception ex)
        {
            bool rolledBack = false;
            try { transaction.Rollback(); rolledBack = true; }
            catch (Exception rollbackError) { System.Diagnostics.Trace.TraceWarning("Administration rollback could not be confirmed: {0}", rollbackError.GetType().Name); }
            bool uncertain = commitAttempted || transaction.SubmissionStarted && !rolledBack;
            return new(uncertain ? AccessSaveOutcome.Unconfirmed : AccessSaveOutcome.Rejected, id,
                intent with { Values = intent.Values with { Id = intent.Values.Id > 0 ? intent.Values.Id : Math.Max(0,id) } }, ex);
        }
    }
}
