namespace FMSC_SMS.Core;

// Keep the established database value; present the owner's chosen wording.
internal static class FeeConcessionLabels
{
    internal static string Display(string value)=>value=="Free"?"Sibling concession":value;
    internal static string FeeText(string value)=>value.Replace(" — Free"," — Sibling concession").Replace(" (Free)"," (Sibling concession)");
    internal static void Configure(DataGridViewComboBoxColumn column)
    {
        column.DisplayMember="Text";column.ValueMember="Value";
        column.DataSource=new[]{"None","Free","Scholarship","Waived"}
            .Select(value=>new { Value=value,Text=Display(value) }).ToArray();
    }
}
