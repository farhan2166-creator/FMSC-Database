-- Module body only. See companion XML for signatures and relationships.

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
