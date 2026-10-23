namespace FMSC_SMS.Pages.Students.Admissions;

internal static class MissingPlacementPolicy
{
    internal static readonly string Sql = ReadSql();
    private static string ReadSql()
    {
        using var stream = typeof(MissingPlacementPolicy).Assembly.GetManifestResourceStream(
            "FMSC_SMS.Pages.Students.Admissions.MissingPlacementWindows.sql")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static Task<DataTable> ReadAsync(int admissionId)
    {
        int user = CurrentUser.UserId;
        return Task.Run(() => DatabaseHelper.QuerySql(
            "EXEC dbo.RequirePermission @UserId,N'Students.Administer';\n" + Sql +
            "\nSELECT W.*,P.ClassSectionId,P.CanPromote FROM @MissingWindows W JOIN @MissingPlacements P ON P.WindowId=W.WindowId ORDER BY W.GapStart DESC,P.ClassSectionId;",
            DatabaseHelper.Int("@AdmissionId", admissionId),
            DatabaseHelper.Int("@UserId", user)));
    }
}
