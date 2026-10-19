namespace FMSC_SMS.Core;

internal static partial class StudentReceiptReport
{
    internal static DataTable BuildData(InstitutionProfile.PrintIdentity institution,DataRow header,DataTable students,DataTable lines)
    {
        if(students.Rows.Count!=1)throw new ArgumentException("A student receipt requires exactly one student.",nameof(students));
        return FeeReceiptDataBuilder.BuildGrouped(institution,header,students,lines,false);
    }
}
