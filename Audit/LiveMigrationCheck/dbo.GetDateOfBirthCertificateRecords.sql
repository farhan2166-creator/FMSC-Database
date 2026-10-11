
/* =====================================================================
   6. dbo.GetDateOfBirthCertificateRecords - issuance history grid
   ===================================================================== */

CREATE   PROCEDURE dbo.GetDateOfBirthCertificateRecords
    @UserId INT,
    @AdmissionId INT = 0
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Create';

    SELECT
        DBC.CertificateRecordId,
        DBI.DateOfBirthCertificateIssueId,
        DBC.AdmissionId,

        DBC.StudentName,
        DBC.FatherName,
        DBC.DateOfBirth,
        DBC.CertificateDate,

        DBI.IssuedAt,
        DBI.IssuedByUserId,
        U.Username AS IssuedBy,

        DBI.CollectedAt,
        DBI.CollectedBy,

        CASE
            WHEN DBI.CollectedAt IS NULL
                THEN N'Not Collected'
            ELSE N'Collected'
        END AS CollectionStatus

    FROM dbo.DateOfBirthCertificates AS DBC

    INNER JOIN dbo.DateOfBirthCertificateIssues AS DBI
        ON DBI.CertificateRecordId = DBC.CertificateRecordId

    LEFT JOIN dbo.Users AS U
        ON U.UserId = DBI.IssuedByUserId

    WHERE
        @AdmissionId = 0
        OR DBC.AdmissionId = @AdmissionId

    ORDER BY
        DBC.CertificateRecordId DESC,
        DBI.IssuedAt DESC,
        DBI.DateOfBirthCertificateIssueId DESC;
END;

