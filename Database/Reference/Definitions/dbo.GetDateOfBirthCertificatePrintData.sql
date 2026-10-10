SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* =====================================================================
   7. dbo.GetDateOfBirthCertificatePrintData

      CertificateNumber is derived the same way Leave Certificate /
      Character Certificate derive theirs - a formatted string built
      from the identity column, not a separately stored/tracked
      sequence. It is printed on the report as "S. No." to match the
      school's existing paper certificate.

      SchoolName is pulled LIVE from dbo.SchoolProfile at print time
      (never snapshotted onto the certificate row itself), same
      reasoning already documented in SchoolProfile_Schema.sql: the
      school's own identity does not change per-certificate, and any
      later correction to the school's profile should be reflected on
      certificates printed afterwards.

      DateOfBirthInFigures/DateOfBirthInWords are pre-formatted here in
      SQL (same approach already used for CertificateNumber) so the
      report itself stays a simple field-binding layout with no date
      arithmetic of its own:
        DateOfBirthInFigures -> "01-04-1998"
        DateOfBirthInWords   -> "01st, April, 1998"
   ===================================================================== */

CREATE PROCEDURE dbo.GetDateOfBirthCertificatePrintData
    @UserId INT,
    @CertificateRecordId INT
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Print';

    IF @CertificateRecordId <= 0
        THROW 58800, 'A valid date of birth certificate is required.', 1;

    SELECT
        DBC.CertificateRecordId,
        DBC.AdmissionId,

        DBC.CertificateNumber AS CertificateNumber,

        DBC.StudentName,
        DBC.FatherName,
        DBC.Gender,
        DBC.DateOfBirth,

        CONVERT(NVARCHAR(10), DBC.DateOfBirth, 105) AS DateOfBirthInFigures,

        CONCAT(
            RIGHT(N'0' + CONVERT(NVARCHAR(2), DAY(DBC.DateOfBirth)), 2),
            CASE
                WHEN DAY(DBC.DateOfBirth) IN (1, 21, 31) THEN N'st'
                WHEN DAY(DBC.DateOfBirth) IN (2, 22)     THEN N'nd'
                WHEN DAY(DBC.DateOfBirth) IN (3, 23)     THEN N'rd'
                ELSE N'th'
            END,
            N', ',
            DATENAME(MONTH, DBC.DateOfBirth),
            N', ',
            CONVERT(NVARCHAR(4), YEAR(DBC.DateOfBirth))
        ) AS DateOfBirthInWords,

        DBC.CertificateDate,
        DBC.CreatedAt AS DateOfIssue,

        SP.SchoolName

    FROM dbo.DateOfBirthCertificates AS DBC

    LEFT JOIN dbo.SchoolProfile AS SP
        ON 1 = 1

    WHERE DBC.CertificateRecordId = @CertificateRecordId;
END;

GO
