using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Students.Admissions;

internal static class StudentPlacementConfirmation
{
    internal static async Task<bool> ConfirmAsync(IWin32Window? owner, int enrollment, string placement)
    {
        var review = await DatabaseHelper.QueryUserAsync("ConfirmStudentPlacement", DatabaseHelper.Int("@EnrollmentId", enrollment));
        var plan = review.Rows[0];
        bool active = Convert.ToString(plan["Status"]) == "Active";
        var facts = new List<(string, string)> {
            ("Record", placement),
            ("Placement", Convert.ToString(plan["PlacementName"]) ?? ""),
            ("Enrollment start", Convert.ToDateTime(plan["EnrollmentStart"]).ToString("dd MMM yyyy"))
        };
        if (active) {
            facts.Add(("Assigned fee lines", Convert.ToString(plan["FeeCount"]) ?? ""));
            facts.Add(("Billing starts", Convert.ToDateTime(plan["BillingStart"]).ToString("dd MMM yyyy")));
        }
        var confirmation = new MessageDialogContent(
            "Confirm this placement?",
            Explanation: active
                ? "Review the fee amounts in Admission before confirming."
                : "Historical placement only. No bills will be posted.",
            Facts: facts,
            Notice: "The academic year will be locked. For later movement, use Promotion, Transfer or Re-admission.");
        if (ModernMessageDialog.Show(owner, confirmation,
            "Confirm Placement", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
        await DatabaseHelper.ExecuteUserAsync("ConfirmStudentPlacement", DatabaseHelper.Int("@EnrollmentId", enrollment),
            DatabaseHelper.Bit("@Preview", false), DatabaseHelper.P("@ExpectedSnapshot", Convert.ToString(plan["Snapshot"])));
        StudentSearchCache.Invalidate();
        return true;
    }
}
