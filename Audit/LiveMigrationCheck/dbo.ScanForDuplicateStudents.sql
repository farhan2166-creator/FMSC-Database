
CREATE   PROCEDURE dbo.ScanForDuplicateStudents
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission @UserId, N'Students.Administer';

    ;WITH Candidates AS
    (
        SELECT
            A.AdmissionId AS AdmissionId1,
            B.AdmissionId AS AdmissionId2
        FROM dbo.Students A
        INNER JOIN dbo.Students B
            ON B.AdmissionId > A.AdmissionId
           AND SOUNDEX(LTRIM(RTRIM(A.StudentName))) = SOUNDEX(LTRIM(RTRIM(B.StudentName)))
           AND SOUNDEX(LTRIM(RTRIM(ISNULL(A.FatherName, N'')))) = SOUNDEX(LTRIM(RTRIM(ISNULL(B.FatherName, N''))))
           AND A.DateOfBirth IS NOT NULL
           AND A.DateOfBirth = B.DateOfBirth
    )
    INSERT INTO dbo.DuplicateStudentReviews (AdmissionId1, AdmissionId2, MatchReason)
    SELECT
        C.AdmissionId1,
        C.AdmissionId2,
        N'Matching name/father-name (soundex) and date of birth'
    FROM Candidates C
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.DuplicateStudentReviews R
        WHERE R.AdmissionId1 = C.AdmissionId1
          AND R.AdmissionId2 = C.AdmissionId2
    );

    SELECT @@ROWCOUNT AS NewCandidateCount;
END;

