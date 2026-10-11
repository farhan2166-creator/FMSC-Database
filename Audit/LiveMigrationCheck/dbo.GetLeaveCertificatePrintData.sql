
/* ============================================================================
   PART 10 � dbo.GetLeaveCertificatePrintData: surface CopyType for the RDLC

   The RDLC report definitions (LeaveCertificate.rdlc /
   CollegeLeaveCertificate.rdlc) already had an unused "CopyType" field
   wired into their dataset -- this fills it in. The report prints nothing
   extra for the Original, and "DUPLICATE COPY" for either duplicate.

   @LeaveCertificateIssueId is optional: when supplied (the UI now passes
   the specific issuance row the user selected/printed), CopyType comes
   from that issue. When omitted/0 (any other/older caller), it falls back
   to the most recently issued copy, same as before this change.
============================================================================ */

CREATE PROCEDURE dbo.GetLeaveCertificatePrintData
    @UserId INT,
    @LeaveRecordId INT,
    @LeaveCertificateIssueId INT = 0
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Students.Leave';

    IF @LeaveRecordId <= 0
        THROW 57310,
            'A valid leave certificate is required.',
            1;

    DECLARE @CopyType NVARCHAR(20);

    IF @LeaveCertificateIssueId > 0
        SELECT @CopyType = IssueType
        FROM dbo.LeaveCertificateIssues
        WHERE LeaveCertificateIssueId = @LeaveCertificateIssueId
          AND LeaveRecordId = @LeaveRecordId;

    IF @CopyType IS NULL
        SELECT TOP (1)
            @CopyType = IssueType
        FROM dbo.LeaveCertificateIssues
        WHERE LeaveRecordId = @LeaveRecordId
        ORDER BY
            IssuedAt DESC,
            LeaveCertificateIssueId DESC;

    SELECT
        LC.LeaveRecordId,
        LC.AdmissionId,

        LC.CertificateNumber AS CertificateNumber,

        LC.StudentName,
        LC.FatherName,
        LC.DateOfBirth,
        LC.DateOfAdmission,
        LC.ClassStudied,
        LC.ClassPromotedTo,
        LC.LeaveDate,
        LC.AcademicRecord,
        LC.AttendanceRecord,
        LC.Remarks,
        LC.CreatedAt AS DateOfIssue,
        ISNULL(@CopyType, N'Original') AS CopyType

    FROM dbo.LeaveCertificates AS LC
    WHERE LC.LeaveRecordId = @LeaveRecordId;
END;

