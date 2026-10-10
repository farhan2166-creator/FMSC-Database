namespace FMSC_SMS.Core;
/// <summary>Operator-facing sentences and captions that more than one screen says, in one place, so a wording change happens once instead of in fourteen files.</summary>
public static class UiText
{
    /// <summary>Asking the operator for the thing an action needs before it can run. These are the sentences that appear on the most screens.</summary>
    public static class Prompts
    {
        public const string SelectStudent = "Select a student first.";
        public const string SelectStudentAndEnrollment = "Select a student and academic enrolment first.";
        public const string SearchForStudent = "Search for and select a student.";
        public const string SelectCampusAndClass = "Select a campus and class first.";
        public const string SearchingStudents = "Searching students...";
        public const string NoStudentsFound = "No students found.";
        /// <summary>The student search boxes' placeholder text.</summary>
        public const string StudentSearchPlaceholder = "Admission Number or student name...";
    }
    /// <summary>The messages an empty grid shows (see UiHelper.SetGridEmptyMessage).</summary>
    public static class EmptyGrid
    {
        public const string SelectStudent = "Select a student to view records.";
        public const string NoRecords = "No records found.";
        public const string RunReport = "Run the report to view results.";
        /// <summary>The fetch failed (shown inline, see UiHelper.SetGridState).</summary>
        public const string LoadFailed = "Could not load the records. Try again.";
        public const string NoHistory = "No history recorded yet.";
    }
    /// <summary>Short field-level messages for inline validation (see FormValidator).</summary>
    public static class Validation
    {
        public const string Required = "Required";
        public const string SelectOne = "Select one";
        public const string InvalidDate = "Invalid date";
        public const string DateRange = "Start is after end";
        public const string EnterAmount = "Enter an amount";
    }
    /// <summary>Wording that belongs to a dialog rather than to a screen.</summary>
    public static class Dialogs
    {
        public const string UnsavedChanges = "Unsaved Changes";
        public const string Continue = "Do you want to continue?";
    }
    /// <summary>Captions for fields whose name is the same on every screen.</summary>
    public static class Fields
    {
        public const string Reason = "Reason (required)";
        public const string RegistrationNumber = "Registration No.";
    }
    /// <summary>The two status lines every report screen shows.</summary>
    public static class Reports
    {
        public const string MatchesFilters = "Report matches the current filters.";
        public const string FiltersChanged = "Filters changed since this report was run. Run the report again before exporting.";
    }
    /// <summary>The nine certificate screens (character, leave, college leave, date of birth, no objection, provisional, regular student, sports, regular lecturer and bank account) are separate pages with the same shape.</summary>
    public static class Certificates
    {
        /// <summary>Window title of a new certificate.</summary>
        public const string Create = "Create Certificate";
        /// <summary>Window title of an existing certificate.</summary>
        public const string Update = "Update Certificate";
        public const string Details = "Certificate Details";
        public const string Date = "Certificate Date";
        public const string IssuanceHistory = "Certificate Issuance History";
        public const string Collection = "Certificate Collection";
        public const string IssueAgain = "Issue Certificate Again";
        public const string PrintDataMissing = "Certificate print data was not found.";
        public const string FutureDateRefused = "Certificate date cannot be in the future.";
        public const string InvalidIssuance = "The selected certificate issuance is invalid.";
    }
}
