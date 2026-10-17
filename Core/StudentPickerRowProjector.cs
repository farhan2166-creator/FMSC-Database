using System.Globalization;

namespace FMSC_SMS.Core;

/// <summary>Projects database rows into the text and cells consumed by StudentPicker.</summary>
internal static class StudentPickerRowProjector
{
    internal static string DisplayFor(DataRow row)
    {
        string name = ReadText(row, "StudentName");
        string father = ReadText(row, "FatherName");
        string relation = string.Equals(ReadText(row, "Gender"), "Female", StringComparison.OrdinalIgnoreCase)
            ? "d/o"
            : "s/o";
        string display = father.Length == 0
            ? $"{AdmissionNumbers.Read(row)} - {name}"
            : $"{AdmissionNumbers.Read(row)} - {name} {relation} {father}";
        string status = ReadText(row, "Status");
        if (IsLeaver(status))
            display += $" [{status}]";
        return display;
    }

    internal static string[] CellsFor(DataRow row, bool showCampus)
    {
        int admissionId = ReadInt(row, "AdmissionId");
        string className = ReadText(row, "ClassName");
        string section = ReadText(row, "SectionName");
        string classSection = className.Length == 0
            ? string.Empty
            : section.Length == 0 ? className : $"{className} - {section}";
        string status = ReadText(row, "Status");
        return new[]
        {
            AdmissionNumbers.Read(row),
            ReadText(row, "StudentName"),
            ReadText(row, "FatherName"),
            classSection,
            showCampus ? ReadText(row, "CampusName") : string.Empty,
            IsLeaver(status) ? status : string.Empty,
            // Internal matching only. The column planner deliberately hides registration.
            ReadText(row, "RegistrationNumber")
        };
    }

    private static bool IsLeaver(string status) =>
        status.Length > 0 && !string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);

    private static string ReadText(DataRow row, string column) =>
        row.Table.Columns.Contains(column) && row[column] != DBNull.Value
            ? Convert.ToString(row[column], CultureInfo.CurrentCulture) ?? string.Empty
            : string.Empty;

    private static int ReadInt(DataRow row, string column) =>
        row.Table.Columns.Contains(column) && row[column] != DBNull.Value &&
        int.TryParse(Convert.ToString(row[column], CultureInfo.InvariantCulture), out int value)
            ? value
            : 0;
}
