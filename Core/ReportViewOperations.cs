namespace FMSC_SMS.Core;

internal sealed class ReportViewValidationException(string message) : Exception(message);

internal static class ReportViewOperations
{
    internal static string ValidName(string name)
    {
        string clean=name.Trim();
        if (clean.Length is 0 or >60) throw new ReportViewValidationException("Use a view name of 1 to 60 characters.");
        return clean;
    }
    internal static bool Rename(ReportUserData data,string pageKey,string original,string replacement)
    {
        string name=ValidName(replacement);
        if (!data.Views.TryGetValue(pageKey,out var views)) return false;
        var view=views.FirstOrDefault(v => string.Equals(v.Name,original,StringComparison.CurrentCultureIgnoreCase));
        if (view is null) return false;
        if (views.Any(v => !ReferenceEquals(v,view) && string.Equals(v.Name,name,StringComparison.CurrentCultureIgnoreCase)))
            throw new ReportViewValidationException("A view with that name already exists. Choose another name.");
        if (view.Name==name) return false;
        view.Name=name;
        return true;
    }
}
