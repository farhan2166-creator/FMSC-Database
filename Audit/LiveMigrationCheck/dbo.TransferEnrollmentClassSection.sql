
-- TransferEnrollmentClassSection
CREATE   PROCEDURE dbo.TransferEnrollmentClassSection
@UserId INT, @EnrollmentId INT, @NewClassSectionId INT, @TransferDate DATE, @Remarks NVARCHAR (500)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Enrollment.Manage';
    IF @EnrollmentId <= 0
        THROW 52105, 'A valid enrollment is required.', 1;
    IF @NewClassSectionId <= 0
        THROW 52106, 'A valid destination class/section is required.', 1;
    IF @TransferDate IS NULL
        THROW 52107, 'Transfer date is required.', 1;
    IF @TransferDate > CONVERT (DATE, SYSDATETIME())
        THROW 52101, 'Transfer date cannot be in the future.', 1;
    BEGIN TRANSACTION;
    DECLARE @EnrollmentStartDate AS DATE;
    DECLARE @AdmissionId AS INT, @OldClassSectionId AS INT, @AcademicYearId AS INT, @OldCampusId AS INT, @OldClassId AS INT, @NewCampusId AS INT, @NewClassId AS INT;
    SELECT @EnrollmentStartDate=E.EnrollmentDate,
           @AdmissionId = E.AdmissionId,
           @OldClassSectionId = E.ClassSectionId,
           @AcademicYearId = E.AcademicYearId,
           @OldCampusId = CCS.CampusId,
           @OldClassId = CCS.ClassId
    FROM   dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = E.ClassSectionId
    WHERE  E.EnrollmentId = @EnrollmentId
           AND E.Status = N'Active';
    IF @AdmissionId IS NULL
        THROW 52102, 'The selected enrollment is not active or does not exist.', 1;
    IF @TransferDate < @EnrollmentStartDate
        THROW 59308,'Transfer date cannot be before the enrollment date.',1;
    SELECT @NewCampusId = CCS.CampusId,
           @NewClassId = CCS.ClassId
    FROM   dbo.CampusClassSections AS CCS
    WHERE  CCS.ClassSectionId = @NewClassSectionId;
    IF @NewCampusId IS NULL
        THROW 52103, 'The destination class/section does not exist or is inactive.', 1;
    IF @OldClassSectionId = @NewClassSectionId
        THROW 52104, 'The destination class/section is the same as the current placement.', 1;
    IF @NewCampusId <> @OldCampusId
       OR @NewClassId <> @OldClassId
        THROW 52109, 'Transfer Section must stay within the current campus and class. Use Transfer Class or Transfer Campus instead.', 1;
    UPDATE dbo.Enrollments
    SET    ClassSectionId = @NewClassSectionId,
           UpdatedAt      = SYSDATETIME(),
           Remarks        = COALESCE (NULLIF (LTRIM(RTRIM(@Remarks)), N''), Remarks)
    WHERE  EnrollmentId = @EnrollmentId
           AND Status = N'Active';
    IF @@ROWCOUNT <> 1
        THROW 52108, 'The active enrollment could not be transferred.', 1;
    INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
    VALUES                       (@EnrollmentId, @AdmissionId, N'TRANSFER', @TransferDate, @OldClassSectionId, @NewClassSectionId, N'Active', N'Active', NULLIF (LTRIM(RTRIM(@Remarks)), N''), @UserId);
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'TRANSFER', CONVERT (NVARCHAR (200), @AdmissionId));
    DECLARE @TransferAuditValues AS NVARCHAR (MAX);
    DECLARE @TransferAuditRecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @EnrollmentId);
    SELECT @TransferAuditValues = (SELECT @AdmissionId AS AdmissionId,
                                          @EnrollmentId AS EnrollmentId,
                                          @AcademicYearId AS AcademicYearId,
                                          @OldClassSectionId AS OldClassSectionId,
                                          @NewClassSectionId AS NewClassSectionId,
                                          @TransferDate AS TransferDate,
                                          @Remarks AS Remarks
                                   FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'Enrollments', @RecordId = @TransferAuditRecordId, @Action = N'TRANSFER_SECTION', @OldValues = NULL, @NewValues = @TransferAuditValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @EnrollmentId AS EnrollmentId,
           @AdmissionId AS AdmissionId,
           @NewClassSectionId AS NewClassSectionId;
END

