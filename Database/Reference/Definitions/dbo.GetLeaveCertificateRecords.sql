SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* ---------------------------------------------------------------------
   2. dbo.GetLeaveCertificateRecords - return EnrollmentId
   --------------------------------------------------------------------- */

CREATE   PROCEDURE dbo.GetLeaveCertificateRecords
    @UserId INT,
    @AdmissionId INT = 0,
    @CertificateType NVARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Students.Leave';

    SET @CertificateType = NULLIF(LTRIM(RTRIM(@CertificateType)), N'');

    SELECT
        LC.LeaveRecordId,
        LCI.LeaveCertificateIssueId,
        LC.AdmissionId,
        LC.CertificateType,

        LC.StudentName,
        LC.FatherName,
        LC.ClassStudied,
        LC.ClassPromotedTo,
        LC.LeaveDate,

        LCI.IssuedAt,
        LCI.IssuedByUserId,
        U.Username AS IssuedBy,
        LCI.IssueType,

        LCI.CollectedAt,
        LCI.CollectedBy,

        CASE
            WHEN LCI.CollectedAt IS NULL
                THEN N'Not Collected'
            ELSE N'Collected'
        END AS CollectionStatus,

        -- Appended at the END: says which leaving episode each record belongs
        -- to, which is exactly what dbo.CreateLeaveCertificate scopes its
        -- "already issued" refusal to.
        LC.EnrollmentId

    FROM dbo.LeaveCertificates AS LC

    INNER JOIN dbo.LeaveCertificateIssues AS LCI
        ON LCI.LeaveRecordId = LC.LeaveRecordId

    LEFT JOIN dbo.Users AS U
        ON U.UserId = LCI.IssuedByUserId

    WHERE
        (@AdmissionId = 0 OR LC.AdmissionId = @AdmissionId)
        AND (@CertificateType IS NULL OR LC.CertificateType = @CertificateType)

    ORDER BY
        LC.LeaveRecordId DESC,
        LCI.IssuedAt DESC,
        LCI.LeaveCertificateIssueId DESC;
END;

GO
