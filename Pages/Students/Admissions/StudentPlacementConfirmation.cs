using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Students.Admissions;

internal static class StudentPlacementConfirmation
{
    internal static async Task<bool> ConfirmAsync(IWin32Window? owner, int enrollment, string placement)
    {
        var review = await DatabaseHelper.QueryUserAsync("ConfirmStudentPlacement", DatabaseHelper.Int("@EnrollmentId", enrollment));
        var plan = review.Rows[0];
        string billing = Convert.ToString(plan["Status"]) == "Active"
            ? $"Assigned fee lines: {plan["FeeCount"]}. Opening billing starts {Convert.ToDateTime(plan["BillingStart"]):dd MMM yyyy}.\nReview the fee amounts in Admission before confirming."
            : "This confirms historical placement only. No bills will be posted.";
        if (ModernMessageDialog.Show(owner,
            $"{placement}\n{plan["PlacementName"]}\nEnrollment start: {Convert.ToDateTime(plan["EnrollmentStart"]):dd MMM yyyy}\n\nConfirm this placement?\n\n{billing}\n\nAfter confirmation, use Promotion, Transfer or Re-admission for movement. Academic year will be locked.",
            "Confirm Placement", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
        await DatabaseHelper.ExecuteUserAsync("ConfirmStudentPlacement", DatabaseHelper.Int("@EnrollmentId", enrollment),
            DatabaseHelper.Bit("@Preview", false), DatabaseHelper.P("@ExpectedSnapshot", Convert.ToString(plan["Snapshot"])));
        StudentSearchCache.Invalidate();
        return true;
    }
}
