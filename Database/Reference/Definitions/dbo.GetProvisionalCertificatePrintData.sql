SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- GetProvisionalCertificatePrintData
CREATE   PROCEDURE dbo.GetProvisionalCertificatePrintData
@UserId INT, @ProvisionalCertificateId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Print';
    IF @ProvisionalCertificateId <= 0
        THROW 58840, 'A valid certificate is required.', 1;
    DECLARE @OtherSubjects AS NVARCHAR (300);
    DECLARE @PickedCount AS INT;
    SELECT @OtherSubjects = PC.OtherSubjects
    FROM   dbo.ProvisionalCertificates AS PC
    WHERE  PC.ProvisionalCertificateId = @ProvisionalCertificateId;
    SELECT @PickedCount = COUNT(*)
    FROM   dbo.ProvisionalCertificateSubjects
    WHERE  ProvisionalCertificateId = @ProvisionalCertificateId;
    DECLARE @SubjectsPassedLine AS NVARCHAR (MAX);
    WITH   PickedSubjects
    AS     (SELECT PCS.SubjectName,
                   ROW_NUMBER() OVER (ORDER BY PCS.DisplayOrder, PCS.SubjectName) AS Seq
            FROM   dbo.ProvisionalCertificateSubjects AS PCS
            WHERE  PCS.ProvisionalCertificateId = @ProvisionalCertificateId),
           OtherSubjectsList
    AS     (SELECT LTRIM(RTRIM(SPL.value)) AS SubjectName,
                   @PickedCount + ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Seq
            FROM   STRING_SPLIT (COALESCE (@OtherSubjects, N''), N',') AS SPL
            WHERE  LTRIM(RTRIM(SPL.value)) <> N''),
           AllSubjects
    AS     (SELECT SubjectName,
                   Seq
            FROM   PickedSubjects
            UNION ALL
            SELECT SubjectName,
                   Seq
            FROM   OtherSubjectsList)
    SELECT @SubjectsPassedLine = STRING_AGG(CONCAT(N'(', Seq, N')  ', SubjectName), N'   ') WITHIN GROUP (ORDER BY Seq)
    FROM   AllSubjects;
    SELECT PC.ProvisionalCertificateId,
           PC.AdmissionId,
           PC.CertificateNumber AS CertificateNumber,
           PC.StudentName,
           PC.FatherName,
           PC.Gender,
           PC.DateOfBirth,
           dbo.fn_DateToWords(PC.DateOfBirth) AS DateOfBirthWords,
           PC.ExamType,
           CASE PC.ExamType WHEN N'SSC' THEN N'Secondary School Certificate' WHEN N'HSSC1' THEN N'Higher Secondary School Certificate Part-I' WHEN N'HSSC2' THEN N'Higher Secondary School Certificate Part-II' END AS ExamTypeLabel,
           PC.ExamYear,
           PC.BoardName,
           PC.RollNumber,
           PC.MarksObtained,
           PC.MarksOutOf,
           PC.Grade,
           COALESCE (@SubjectsPassedLine, N'') AS SubjectsPassedLine,
           PC.CertificateDate,
           PC.CreatedAt AS DateOfIssue,
           SP.SchoolName AS InstitutionLine
    FROM   dbo.ProvisionalCertificates AS PC CROSS JOIN dbo.SchoolProfile AS SP
    WHERE  PC.ProvisionalCertificateId = @ProvisionalCertificateId
           AND SP.SchoolProfileId = 1;
END



GO
