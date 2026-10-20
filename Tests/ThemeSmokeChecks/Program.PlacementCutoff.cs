using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Admissions;

internal static partial class Program
{
    private static void CheckPlacementCutoff()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var form = new StudentBillingSetupDialog(0);
        T Field<T>(string name) => (T)typeof(StudentBillingSetupDialog).GetField(name, flags)!.GetValue(form)!;
        var start = Field<ModernDatePicker>("start");
        var billing = Field<ModernDatePicker>("billing");
        billing.Value = new DateTime(2026, 10, 20);
        start.Value = new DateTime(2026, 4, 1);
        Check(billing.Value.Date == new DateTime(2026, 10, 20), "Historical class placement leaves the chosen billing date unchanged");
        start.Value = new DateTime(2026, 10, 16);
        Check(billing.Value.Date == new DateTime(2026, 10, 20), "Crossing the cutoff with the placement date does not change billing");
        billing.Value = new DateTime(2026, 10, 15);
        start.Value = new DateTime(2026, 4, 2);
        Check(billing.Value.Date == new DateTime(2026, 10, 15), "The legacy billing date remains independently editable");
        Check(billing.MinDate < new DateTime(2026, 4, 1), "The date control does not impose a placement-based cutoff");
        Check(!Field<LookupBox>("year").Enabled, "Placement setup retains the locked academic year");
    }
}
