-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Adjust';
    IF @BillingStartDate IS NULL
        THROW 58001, 'A billing start date is required.', 1;
    IF NULLIF (LTRIM(RTRIM(@Reason)), N'') IS NULL
        THROW 58002, 'A reason is required for this change.', 1;
    BEGIN TRANSACTION;
    DECLARE @AdmissionId AS INT, @EnrollmentDate AS DATE, @OldBillingStartDate AS DATE, @Status AS NVARCHAR (30);
    SELECT @AdmissionId = E.AdmissionId,
           @EnrollmentDate = E.EnrollmentDate,
           @OldBillingStartDate = E.BillingStartDate,
           @Status = E.Status
    FROM   dbo.Enrollments AS E WITH (UPDLOCK, HOLDLOCK)
    WHERE  E.EnrollmentId = @EnrollmentId;
    IF @AdmissionId IS NULL
        BEGIN
            ROLLBACK;
            THROW 58003, 'The selected enrollment does not exist.', 1;
        END
    IF @BillingStartDate < @EnrollmentDate
        BEGIN
            ROLLBACK;
            THROW 58004, 'Billing start date cannot be earlier than the enrollment date.', 1;
        END
    UPDATE dbo.Enrollments
    SET    BillingStartDate = @BillingStartDate,
           UpdatedAt        = SYSDATETIME()
    WHERE  EnrollmentId = @EnrollmentId;
    DECLARE @OperationId AS UNIQUEIDENTIFIER;
    SET @OperationId = NEWID();
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'ENROLLMENT_BILLING_START_DATE_CHANGE', CONVERT (NVARCHAR (200), @EnrollmentId));
    DECLARE @OldValues AS NVARCHAR (MAX);
    SELECT @OldValues = (SELECT @EnrollmentId AS EnrollmentId,
                                @AdmissionId AS AdmissionId,
                                @OldBillingStartDate AS BillingStartDate
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    DECLARE @NewValues AS NVARCHAR (MAX);
    SELECT @NewValues = (SELECT @EnrollmentId AS EnrollmentId,
                                @AdmissionId AS AdmissionId,
                                @BillingStartDate AS BillingStartDate,
                                @Reason AS Reason
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    DECLARE @AuditRecordId AS NVARCHAR (200);
    SET @AuditRecordId = CONVERT (NVARCHAR (200), @EnrollmentId);
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'Enrollments', @RecordId = @AuditRecordId, @Action = N'UPDATE', @OldValues = @OldValues, @NewValues = @NewValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @EnrollmentId AS EnrollmentId,
           @AdmissionId AS AdmissionId,
           @OldBillingStartDate AS OldBillingStartDate,
           @BillingStartDate AS NewBillingStartDate;
END
