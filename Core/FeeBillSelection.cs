namespace FMSC_SMS.Core;

// Group only the explicitly selected accounts, using stored family identity.
internal static class FeeBillSelection
{
    internal sealed record Student(int Id, int? FamilyId, string Reference, string Name, decimal Due, string Readiness, string Notice);
    internal static int[][] Groups(IEnumerable<Student> students, bool byFamily) => students
        .Where(s => s.Due > 0m).DistinctBy(s => s.Id)
        .GroupBy(s => byFamily && s.FamilyId is > 0 ? $"F{s.FamilyId}" : $"S{s.Id}")
        .Select(g => g.Select(s => s.Id).ToArray()).ToArray();
}
