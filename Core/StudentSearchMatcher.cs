using System.Data;
using System.Globalization;
namespace FMSC_SMS.Core;
internal static class StudentSearchMatcher
{
    public static List<DataRow> Find(DataTable source, string? searchText, int maxRows = 0, Func<DataRow, bool>? additionalFilter = null)
    {
        string term = (searchText ?? string.Empty).Trim();
        string rawTerm = term;
        if (AdmissionNumbers.TryNormalize(term, out string normalizedNumber)) term = normalizedNumber;
        bool numeric = AdmissionNumbers.TryNormalize(term, out _);
        string[] words = term.Length == 0
            ? Array.Empty<string>()
              : rawTerm.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        DataColumn? admissionColumn = source.Columns.Contains("AdmissionNumber")
            ? source.Columns["AdmissionNumber"]
            : null;
        DataColumn? nameColumn = source.Columns.Contains("StudentName")
            ? source.Columns["StudentName"]
            : null;
        DataColumn? fatherColumn = source.Columns.Contains("FatherName")
            ? source.Columns["FatherName"]
            : null;
        DataColumn? registrationColumn = source.Columns.Contains("RegistrationNumber")
            ? source.Columns["RegistrationNumber"]
            : null;
        bool Contains(DataRow row, DataColumn? column, string word) =>
            column is not null &&
            row[column] != DBNull.Value &&
            (Convert.ToString(row[column], CultureInfo.InvariantCulture) ?? string.Empty)
                .IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
        bool MatchesText(DataRow row)
        {
            foreach (string word in words)
            {
                if (!Contains(row, nameColumn, word) &&
                    !Contains(row, fatherColumn, word) &&
                    !Contains(row, registrationColumn, word) &&
                    !Contains(row, admissionColumn, word))
                {
                    return false;
                }
            }
            return true;
        }
        bool MatchesAdmissionPrefix(DataRow row)
        {
            if (admissionColumn is null || row[admissionColumn] == DBNull.Value)
                return false;
            string id = Convert.ToString(row[admissionColumn], CultureInfo.InvariantCulture) ?? string.Empty;
            return id.StartsWith(term, StringComparison.OrdinalIgnoreCase);
        }
        string Text(DataRow row, DataColumn? column) =>
            column is not null && row[column] != DBNull.Value
                ? Convert.ToString(row[column], CultureInfo.InvariantCulture) ?? string.Empty
                : string.Empty;
        var matches = new List<DataRow>();
        foreach (DataRow row in source.Rows)
        {
            if (additionalFilter is not null && !additionalFilter(row))
                continue;
            if (term.Length > 0 &&
                !MatchesAdmissionPrefix(row) &&
                !MatchesText(row))
            {
                continue;
            }
            matches.Add(row);
        }
        matches.Sort((left, right) => {
            int ExactRank(DataRow row)
            {
                if (string.Equals(Text(row, admissionColumn), term, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Text(row, registrationColumn), rawTerm, StringComparison.OrdinalIgnoreCase)) return 0;
                if (numeric) return 1;
                if (string.Equals(Text(row, nameColumn), rawTerm, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Text(row, fatherColumn), rawTerm, StringComparison.OrdinalIgnoreCase)) return 1;
                if (Text(row, nameColumn).StartsWith(rawTerm, StringComparison.OrdinalIgnoreCase) ||
                    Text(row, fatherColumn).StartsWith(rawTerm, StringComparison.OrdinalIgnoreCase)) return 2;
                return 3;
            }
            if (term.Length > 0)
            {
                int exactOrder = ExactRank(left).CompareTo(ExactRank(right));
                if (exactOrder != 0) return exactOrder;
            }
            if (numeric)
            {
                int prefixOrder = (MatchesAdmissionPrefix(left) ? 0 : 1)
                    .CompareTo(MatchesAdmissionPrefix(right) ? 0 : 1);
                if (prefixOrder != 0)
                    return prefixOrder;
            }
            int idOrder = AdmissionNumbers.Compare(Text(left, admissionColumn), Text(right, admissionColumn));
            if (idOrder != 0)
                return idOrder;
            int nameOrder = StringComparer.OrdinalIgnoreCase.Compare(
                Text(left, nameColumn),
                Text(right, nameColumn));
            return nameOrder != 0
                ? nameOrder
                : StringComparer.OrdinalIgnoreCase.Compare(
                    Text(left, fatherColumn),
                    Text(right, fatherColumn));
        });
        if (maxRows > 0 && matches.Count > maxRows)
            matches.RemoveRange(maxRows, matches.Count - maxRows);
        return matches;
    }
}
