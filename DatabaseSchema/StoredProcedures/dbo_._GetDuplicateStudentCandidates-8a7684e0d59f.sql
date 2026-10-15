-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission @UserId, N'Students.View';

    SELECT
        R.ReviewId,
        R.Status,
        R.MatchReason,
        R.DetectedAt,

        A.AdmissionId AS AdmissionId,
        A.RegistrationNumber AS RegistrationNumber,
        A.StudentName AS StudentName,
        A.FatherName AS FatherName,
        A.DateOfBirth AS DateOfBirth,
        A.ContactNumber AS ContactNumber,

        B.AdmissionId AS AdmissionId2,
        B.RegistrationNumber AS RegistrationNumber2,
        B.StudentName AS StudentName2,
        B.FatherName AS FatherName2,
        B.DateOfBirth AS DateOfBirth2,
        B.ContactNumber AS ContactNumber2

    FROM dbo.DuplicateStudentReviews R
    INNER JOIN dbo.Students A ON A.AdmissionId = R.AdmissionId1
    INNER JOIN dbo.Students B ON B.AdmissionId = R.AdmissionId2
    WHERE @StatusFilter IS NULL OR R.Status = @StatusFilter
    ORDER BY R.DetectedAt DESC;
END;
