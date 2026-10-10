SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* =====================================================================
   4. dbo.GetNoObjectionCertificateRecords - grid of NOCs for a student
      (page always loads this scoped to the selected student, same
      convention as GetSportsCertificateRecords/
      GetRegularStudentCertificateRecords).
   ===================================================================== */

CREATE   PROCEDURE dbo.GetNoObjectionCertificateRecords
    @UserId INT,
    @AdmissionId INT = 0
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Create';

    SELECT
        NOC.NoObjectionCertificateId,
        NOC.AdmissionId,

        NOC.StudentName,
        NOC.FatherName,
        NOC.CertificateDate,

        NOC.CreatedAt,
        NOC.UpdatedAt

    FROM dbo.NoObjectionCertificates AS NOC

    WHERE
        @AdmissionId = 0
        OR NOC.AdmissionId = @AdmissionId

    ORDER BY
        NOC.NoObjectionCertificateId DESC;
END;

GO
