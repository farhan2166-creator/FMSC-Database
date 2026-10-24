using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace FMSC_SMS.Core;

/// <summary>Formatting for human-readable record fields, never codes or identifiers.</summary>
internal static class RecordTextPolicy
{
    private sealed record InputPolicy(string Parameter);
    private static readonly ConditionalWeakTable<Control, InputPolicy> InputPolicies = new();
    private static readonly HashSet<string> PersonNameParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "@StudentName", "@FatherName", "@GuardianName", "@PersonName", "@StaffName"
    };
    private static readonly HashSet<string> LetterRequiredParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "@SubjectName", "@FeeTypeName", "@ProgramName", "@SchoolName", "@CampusName",
        "@Designation", "@GuardianRelation", "@City", "@PlaceOfBirth", "@PreviousSchool",
        "@FatherOccupation", "@Religion", "@CasteTribe", "@BankName", "@BranchName", "@RoleTitle"
    };
    private static readonly Dictionary<string,string> Acronyms=new(StringComparer.OrdinalIgnoreCase)
    {
        ["FMSC"]="FMSC",["SSC"]="SSC",["HSSC"]="HSSC",["BISE"]="BISE",["CNIC"]="CNIC",
        ["BPS"]="BPS",["BS"]="BS",["MS"]="MS",["BA"]="BA",["MA"]="MA",
        ["BSc"]="BSc",["MSc"]="MSc",["MPhil"]="MPhil",["PhD"]="PhD",["IT"]="IT"
    };
    private static readonly Dictionary<string,string> DisplayAliases=new(StringComparer.OrdinalIgnoreCase)
    {
        ["Student"]="StudentName",["Staff"]="StaffName",["Father"]="FatherName",["Parent"]="FatherName",
        ["Class"]="ClassName",["Section"]="SectionName",["Campus"]="CampusName",["Subject"]="SubjectName",
        ["Fee"]="FeeTypeName",["FeeName"]="FeeTypeName",["ClassSection"]="ClassName",
        ["InstitutionName"]="SchoolName",["InstitutionLine"]="SchoolName",["StudentFullName"]="StudentName",
        ["CurrentClass"]="ClassName",["CurrentClassName"]="ClassName",["CurrentCampusName"]="CampusName",
        ["AdmissionCampusName"]="CampusName",["FatherOccupationText"]="FatherOccupation",
        ["AddressText"]="Address",["PlaceOfBirthText"]="PlaceOfBirth",["ReligionText"]="Religion",
        ["PreviousSchoolText"]="PreviousSchool",["CampusNameText"]="CampusName",["ClassNameText"]="ClassName",
        ["SectionNameText"]="SectionName"
    };

    private static bool IsTitleCaseField(string field)
    {
        string key=new(field.Where(char.IsLetterOrDigit).ToArray());
        key=DisplayAliases.GetValueOrDefault(key,key);
        return TitleCaseParameters.Contains("@"+key);
    }

    internal static string DisplayText(string field,string text) => IsTitleCaseField(field)?TitleCase(text):text;

    internal static void FormatDisplayRows(DataTable table)
    {
        foreach(DataColumn column in table.Columns)
        {
            if(column.DataType!=typeof(string) || column.Expression.Length>0 || !IsTitleCaseField(column.ColumnName))continue;
            bool readOnly=column.ReadOnly;
            try
            {
                column.ReadOnly=false;
                foreach(DataRow row in table.Rows)
                {
                    if(row.RowState==DataRowState.Deleted || row[column] is not string text)continue;
                    string formatted=TitleCase(text);
                    if(formatted!=text)row[column]=formatted;
                }
            }
            finally{column.ReadOnly=readOnly;}
        }
    }

    internal static DataTable PrintCopy(DataTable source)
    {
        var data=source.Copy();FormatDisplayRows(data);return data;
    }

    internal static void WireField(Control input,string caption)
    {
        string key=new(caption.Where(char.IsLetterOrDigit).ToArray());
        key=key.ToLowerInvariant() switch {
            "fathersname"=>"FatherName",
            "designationteacherlecturer"=>"RoleTitle",
            "othersubjectsnotinlistbelow"=>"OtherSubjects",
            _=>key
        };
        WireInput(input,"@"+key);
    }
    private static readonly HashSet<string> TitleCaseParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "@SubjectName", "@ClassName", "@SectionName", "@FeeTypeName", "@ProgramName",
        "@CampusName", "@SchoolName", "@Designation", "@PersonName", "@StaffName",
        "@StudentName", "@FatherName", "@GuardianName", "@GuardianRelation",
        "@Address", "@AddressLine", "@City", "@PlaceOfBirth", "@PreviousSchool",
        "@FatherOccupation", "@Religion", "@CasteTribe", "@BankName", "@BranchName", "@RoleTitle",
        "@AcademicRecord", "@AttendanceRecord", "@ClassStudied", "@ClassPromotedTo",
        "@SportName", "@BoardName", "@SubjectTaught", "@OtherSubjects", "@LeavingReason"
    };
    private static readonly HashSet<string> RomanNumerals = new(StringComparer.OrdinalIgnoreCase)
    {
        "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X",
        "XI", "XII", "XIII", "XIV", "XV", "XVI", "XVII", "XVIII", "XIX", "XX"
    };

    internal static object? NormalizeParameter(string parameter, object? value)
    {
        if (value is not string text) return value;
        string? error = ValidationMessage(parameter, text);
        if (error is not null) throw new ArgumentException(error, parameter.TrimStart('@'));
        if (parameter.Equals("@YearName", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Trim();
            if (!IsValidYearName(text))
                throw new ArgumentException("Year Name must contain digits and an optional hyphen only, for example 2025-2026.");
            return text;
        }
        return TitleCaseParameters.Contains(parameter) ? TitleCase(text) : value;
    }

    internal static bool IsValidYearName(string text) =>
        Regex.IsMatch(text, @"\A[0-9]+(?:-[0-9]+)?\z", RegexOptions.CultureInvariant);

    internal static string? ValidationMessage(string parameter, string text)
    {
        text = text.Trim();
        if (parameter.Equals("@YearName", StringComparison.OrdinalIgnoreCase))
            return IsValidYearName(text) ? null : "Use digits and an optional hyphen only, for example 2025-2026.";
        // Required-field checks decide whether optional names may be blank.
        if (text.Length == 0) return null;
        if (PersonNameParameters.Contains(parameter))
            return Regex.IsMatch(text, @"\A[\p{L}\p{M} .'’-]+\z") && Regex.IsMatch(text, @"\p{L}")
                ? null : "Use letters, spaces, hyphens, apostrophes or periods; numbers are not allowed in a person's name.";
        if (!TitleCaseParameters.Contains(parameter)) return null;
        if (LetterRequiredParameters.Contains(parameter) && !Regex.IsMatch(text, @"\p{L}"))
            return "Include at least one letter; a name or title cannot contain only numbers or punctuation.";
        bool address = parameter.Equals("@Address", StringComparison.OrdinalIgnoreCase) ||
            parameter.Equals("@AddressLine", StringComparison.OrdinalIgnoreCase);
        if (text.Any(character => char.IsControl(character) &&
            !(address && character is '\r' or '\n')))
            return "Remove unsupported control characters or line breaks.";
        return Regex.IsMatch(text, @"[\p{L}\p{Nd}]")
            ? null : "Enter text containing at least one letter or number.";
    }

    internal static void ValidateInputs(FormValidator validator, Control root)
    {
        if (!root.Visible || !root.Enabled) return;
        if (InputPolicies.TryGetValue(root, out var policy))
        {
            string? error = ValidationMessage(policy.Parameter, root.Text);
            if (error is not null)
                validator.Rule(root, () => ValidationMessage(policy.Parameter, root.Text) is null, error);
        }
        foreach (Control child in root.Controls) ValidateInputs(validator, child);
    }

    internal static void WireInput(Control input, string parameter)
    {
        if (input is not ModernTextBox text ||
            !(TitleCaseParameters.Contains(parameter) || parameter.Equals("@YearName", StringComparison.OrdinalIgnoreCase))) return;
        if (InputPolicies.TryGetValue(input, out _)) return;
        InputPolicies.Add(input, new InputPolicy(parameter));
        if (PersonNameParameters.Contains(parameter))
            text.KeyPress += (_, e) => {
                if (!char.IsControl(e.KeyChar) && !char.IsLetter(e.KeyChar) &&
                    CharUnicodeInfo.GetUnicodeCategory(e.KeyChar) is not
                        (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark) &&
                    e.KeyChar is not (' ' or '-' or '\'' or '’' or '.')) e.Handled = true;
            };
        if (!TitleCaseParameters.Contains(parameter)) return;
        text.Leave += (_, _) => {
            string normalized = TitleCase(text.Text);
            if (text.Text != normalized) text.Text = normalized;
        };
    }

    internal static string TitleCase(string text) => Regex.Replace(text.Trim(), @"[\p{L}\p{Nd}]+", match => {
        string word = match.Value;
        if (Acronyms.TryGetValue(word,out string? acronym)) return acronym;
        if (RomanNumerals.Contains(word)) return word.ToUpperInvariant();
        string lower = word.ToLowerInvariant();
        string first = StringInfo.GetNextTextElement(lower);
        return first.ToUpperInvariant() + lower[first.Length..];
    });
}
