namespace FMSC_SMS.Core;

// Same catalog sequence and natural admission-number ties as SettlementKey.
// Older in-memory documents without order metadata retain their saved order.
internal static class FeeStudentOrder
{
    internal static IEnumerable<DataRow> Rows(DataTable source,string reference)
    {
        if(!source.Columns.Contains("StudentClassOrder"))return source.AsEnumerable();
        int Position(DataRow row,string field)=>source.Columns.Contains(field)&&!row.IsNull(field)?Convert.ToInt32(row[field]):int.MaxValue;
        (long Number,string Suffix) Admission(DataRow row)
        {
            string value=Convert.ToString(row[reference])??"";
            if(!AdmissionNumbers.TryNormalize(value,out string normalized))return(long.MaxValue,value);
            string digits=new(normalized.TakeWhile(char.IsDigit).ToArray());
            return(long.Parse(digits,System.Globalization.CultureInfo.InvariantCulture),normalized[digits.Length..]);
        }
        return source.AsEnumerable().OrderBy(row=>Position(row,"StudentProgramGroup"))
            .ThenBy(row=>Position(row,"StudentProgramOrder")).ThenBy(row=>Position(row,"StudentProgramId"))
            .ThenBy(row=>Position(row,"StudentClassOrder")).ThenBy(row=>Position(row,"StudentClassId"))
            .ThenBy(row=>Admission(row).Number).ThenBy(row=>Admission(row).Suffix,StringComparer.Ordinal)
            .ThenBy(row=>source.Columns.Contains("AdmissionId")?Position(row,"AdmissionId"):0);
    }
}
