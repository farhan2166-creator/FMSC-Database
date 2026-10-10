SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- CreateLeaveCertificate
CREATE PROCEDURE dbo.CreateLeaveCertificate
@UserId INT, @AdmissionId INT, @LeaveDate DATE, @ClassPromotedTo NVARCHAR (300)=NULL, @AcademicRecord NVARCHAR (400)=NULL, @AttendanceRecord NVARCHAR (400)=NULL, @Remarks NVARCHAR (1000)=NULL, @CertificateType NVARCHAR (20)=N'School', @KeepLaterCharges BIT=0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Students.Leave';
    IF @AdmissionId <= 0
        THROW 57000, 'A valid student is required.', 1;
    IF @LeaveDate IS NULL
        THROW 57001, 'Leave date is required.', 1;
    IF @LeaveDate > CONVERT (DATE, SYSDATETIME())
        THROW 57002, 'Leave date cannot be in the future.', 1;
    IF @ClassPromotedTo IS NULL
       OR LTRIM(RTRIM(@ClassPromotedTo)) = N''
        THROW 57009, 'Class promoted to is required.', 1;
    IF @AcademicRecord IS NULL
       OR LTRIM(RTRIM(@AcademicRecord)) = N''
        THROW 57010, 'Academic record is required.', 1;
    IF @AttendanceRecord IS NULL
       OR LTRIM(RTRIM(@AttendanceRecord)) = N''
        THROW 57011, 'Attendance record is required.', 1;
    IF @Remarks IS NULL
       OR LTRIM(RTRIM(@Remarks)) = N''
        THROW 57012, 'Remarks are required.', 1;
    SET @CertificateType = NULLIF (LTRIM(RTRIM(@CertificateType)), N'');
    IF @CertificateType IS NULL
        SET @CertificateType = N'School';
    IF @CertificateType NOT IN (N'School', N'College')
        THROW 57014, 'Certificate type must be School or College.', 1;
    DECLARE @StudentName AS NVARCHAR (300), @FatherName AS NVARCHAR (300), @DateOfBirth AS DATE, @AdmissionDate AS DATE, @StudentStatus AS NVARCHAR (60), @EnrollmentId AS INT, @ClassSectionId AS INT, @ClassStudied AS NVARCHAR (300), @LeaveRecordId AS INT, @IssueId AS INT, @MaxBilledMonth AS DATE, @IsRetroactive AS BIT = 0;
    BEGIN TRANSACTION;
    SELECT @StudentName = S.StudentName,
           @FatherName = S.FatherName,
           @DateOfBirth = S.DateOfBirth,
           @AdmissionDate = S.AdmissionDate,
           @StudentStatus = S.Status
    FROM   dbo.Students AS S WITH (UPDLOCK, HOLDLOCK)
    WHERE  S.AdmissionId = @AdmissionId;
    IF @StudentName IS NULL
        THROW 57003, 'The student was not found.', 1;
    IF @StudentStatus = N'Left'
        SET @IsRetroactive = 1;
    ELSE
        IF @StudentStatus <> N'Active'
            THROW 57008, 'The student is not currently active and cannot be issued a new leave certificate.', 1;
    DECLARE @CheckEnrollmentId AS INT;
    IF @IsRetroactive = 1
        SELECT   TOP (1) @CheckEnrollmentId = E.EnrollmentId
        FROM     dbo.Enrollments AS E
        WHERE    E.AdmissionId = @AdmissionId
                 AND E.Status = N'Left'
        ORDER BY E.EnrollmentId DESC;
    ELSE
        SELECT   TOP (1) @CheckEnrollmentId = E.EnrollmentId
        FROM     dbo.Enrollments AS E
        WHERE    E.AdmissionId = @AdmissionId
                 AND E.Status = N'Active'
        ORDER BY E.EnrollmentId DESC;
    IF @CheckEnrollmentId IS NOT NULL
       AND EXISTS (SELECT 1
                   FROM   dbo.LeaveCertificates
                   WHERE  AdmissionId = @AdmissionId
                          AND CertificateType = @CertificateType
                          AND EnrollmentId = @CheckEnrollmentId)
        THROW 57013, 'A leaving certificate of this type has already been issued for this student''s current leaving record.', 1;
    IF @AdmissionDate IS NOT NULL
       AND @LeaveDate < @AdmissionDate
        THROW 57005, 'Leave date cannot be earlier than the admission date.', 1;
    IF @IsRetroactive = 1
        BEGIN
            SELECT   TOP (1) @EnrollmentId = E.EnrollmentId,
                             @ClassSectionId = E.ClassSectionId,
                             @ClassStudied = C.ClassName
            FROM     dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
                     INNER JOIN
                     dbo.CampusClassSections AS CCS
                     ON CCS.ClassSectionId = E.ClassSectionId
                     INNER JOIN
                     dbo.Classes AS C
                     ON C.ClassId = CCS.ClassId
            WHERE    E.AdmissionId = @AdmissionId
                     AND E.Status = N'Left'
            ORDER BY E.EnrollmentId DESC;
            IF @EnrollmentId IS NULL
                THROW 57004, 'No closed enrollment was found for this student to attach the certificate to.', 1;
            IF EXISTS (SELECT 1
                       FROM   dbo.Enrollments
                       WHERE  EnrollmentId = @EnrollmentId
                              AND @LeaveDate < EnrollmentDate)
                THROW 57016, 'Leave date cannot be earlier than the verified enrollment date.', 1;
            SELECT @MaxBilledMonth = (SELECT MAX(BillingMonth)
                                      FROM   dbo.BillingCharges
                                      WHERE  EnrollmentId = @EnrollmentId
                                             AND Status <> N'Cancelled');
            IF @MaxBilledMonth IS NOT NULL
               AND @KeepLaterCharges=0 AND DATEFROMPARTS(YEAR(@LeaveDate), MONTH(@LeaveDate), 1) < @MaxBilledMonth
                THROW 57015, 'This enrollment already has billing charges for a later month than the requested leave date. Cancel/correct those charges first, or choose a leave date in or after the last billed month.', 1;
            UPDATE dbo.Students
            SET    WithdrawalDate = @LeaveDate,
                   UpdatedAt      = SYSDATETIME()
            WHERE  AdmissionId = @AdmissionId;
            UPDATE dbo.Enrollments
            SET    LeavingDate = @LeaveDate,
                   Remarks     = @Remarks,
                   UpdatedAt   = SYSDATETIME()
            WHERE  EnrollmentId = @EnrollmentId;
            INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
            VALUES                       (@EnrollmentId, @AdmissionId, N'LEAVE_CERTIFICATE_RETROACTIVE', @LeaveDate, @ClassSectionId, NULL, N'Left', N'Left', @Remarks, @UserId);
            INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
            VALUES                          (@AdmissionId, N'Left', N'Left', N'Leave certificate issued retroactively', @LeaveDate, @Remarks, @UserId);
        END
    ELSE
        BEGIN
            SELECT   TOP (1) @EnrollmentId = E.EnrollmentId,
                             @ClassSectionId = E.ClassSectionId,
                             @ClassStudied = C.ClassName
            FROM     dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
                     INNER JOIN
                     dbo.CampusClassSections AS CCS
                     ON CCS.ClassSectionId = E.ClassSectionId
                     INNER JOIN
                     dbo.Classes AS C
                     ON C.ClassId = CCS.ClassId
            WHERE    E.AdmissionId = @AdmissionId
                     AND E.Status = N'Active'
            ORDER BY E.EnrollmentId DESC;
            IF @EnrollmentId IS NULL
                THROW 57004, 'No active enrollment exists for this student.', 1;
            IF EXISTS (SELECT 1
                       FROM   dbo.Enrollments
                       WHERE  EnrollmentId = @EnrollmentId
                              AND @LeaveDate < EnrollmentDate)
                THROW 57016, 'Leave date cannot be earlier than the verified enrollment date.', 1;
            SELECT @MaxBilledMonth = (SELECT MAX(BillingMonth)
                                      FROM   dbo.BillingCharges
                                      WHERE  EnrollmentId = @EnrollmentId
                                             AND Status <> N'Cancelled');
            IF @MaxBilledMonth IS NOT NULL
               AND @KeepLaterCharges=0 AND DATEFROMPARTS(YEAR(@LeaveDate), MONTH(@LeaveDate), 1) < @MaxBilledMonth
                THROW 57015, 'This enrollment already has billing charges for a later month than the requested leave date. Cancel/correct those charges first, or choose a leave date in or after the last billed month.', 1;
            UPDATE dbo.Enrollments
            SET    Status      = N'Left',
                   LeavingDate = @LeaveDate,
                   Remarks     = @Remarks,
                   UpdatedAt   = SYSDATETIME()
            WHERE  EnrollmentId = @EnrollmentId
                   AND Status = N'Active';
            IF @@ROWCOUNT <> 1
                THROW 57006, 'The active enrollment could not be updated.', 1;
            UPDATE dbo.Students
            SET    Status         = N'Left',
                   WithdrawalDate = @LeaveDate,
                   UpdatedAt      = SYSDATETIME()
            WHERE  AdmissionId = @AdmissionId
                   AND Status = N'Active';
            IF @@ROWCOUNT <> 1
                THROW 57007, 'The student status could not be updated.', 1;
            INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
            VALUES                       (@EnrollmentId, @AdmissionId, N'LEAVE', @LeaveDate, @ClassSectionId, NULL, N'Active', N'Left', @Remarks, @UserId);
            INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
            VALUES                          (@AdmissionId, N'Active', N'Left', N'Leave', @LeaveDate, @Remarks, @UserId);
        END
    INSERT  dbo.LeaveCertificates (AdmissionId, CertificateType, EnrollmentId, StudentName, FatherName, DateOfBirth, DateOfAdmission, ClassStudied, ClassPromotedTo, LeaveDate, AcademicRecord, AttendanceRecord, Remarks, CreatedByUserId)
    VALUES                       (@AdmissionId, @CertificateType, @EnrollmentId, @StudentName, @FatherName, @DateOfBirth, @AdmissionDate, @ClassStudied, @ClassPromotedTo, @LeaveDate, @AcademicRecord, @AttendanceRecord, @Remarks, @UserId);
    SET @LeaveRecordId = CONVERT (INT, SCOPE_IDENTITY());
    INSERT  dbo.LeaveCertificateIssues (LeaveRecordId, IssuedAt, IssuedByUserId, IssueType)
    VALUES                            (@LeaveRecordId, SYSDATETIME(), @UserId, N'Original');
    SET @IssueId = CONVERT (INT, SCOPE_IDENTITY());
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @RecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @LeaveRecordId);
    DECLARE @NewValues AS NVARCHAR (MAX);
    SELECT @NewValues = (SELECT @LeaveRecordId AS LeaveRecordId,
                                @IssueId AS LeaveCertificateIssueId,
                                @AdmissionId AS AdmissionId,
                                @CertificateType AS CertificateType,
                                @EnrollmentId AS EnrollmentId,
                                @LeaveDate AS LeaveDate,
                                @IsRetroactive AS IsRetroactive,
                                @StudentName AS StudentName,
                                @FatherName AS FatherName,
                                @DateOfBirth AS DateOfBirth,
                                @AdmissionDate AS DateOfAdmission,
                                @ClassStudied AS ClassStudied,
                                @ClassPromotedTo AS ClassPromotedTo,
                                @AcademicRecord AS AcademicRecord,
                                @AttendanceRecord AS AttendanceRecord,
                                @Remarks AS Remarks
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'LEAVE', CONVERT (NVARCHAR (200), @AdmissionId));
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'LeaveCertificates', @RecordId = @RecordId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @NewValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @LeaveRecordId AS LeaveRecordId,
           @IssueId AS LeaveCertificateIssueId,
           @AdmissionId AS AdmissionId,
           @CertificateType AS CertificateType,
           @EnrollmentId AS EnrollmentId,
           @LeaveDate AS LeaveDate;
END



GO
