SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
CREATE   PROCEDURE dbo.WriteAuditLog
    @OperationId uniqueidentifier,@UserId int,@TableName nvarchar(128),@RecordId nvarchar(200),
    @Action nvarchar(100),@OldValues nvarchar(max),@NewValues nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;
    -- Routine setup, school-profile/contact edits and family-list housekeeping
    -- do not need audit events. Financial, lifecycle, certificate and security
    -- operations remain protected.
    IF @TableName NOT IN
    (N'Users',N'UserRoles',N'Roles',N'RolePermissions',N'Students',N'Enrollments',N'PromotionBatches',N'SchoolProfile',
     N'StudentFeeAssignments',N'FeeConfigurations',N'BillingCharges',N'FeePayments',N'DailyCollectionClosings',
     N'DatabaseBackups',N'BackupHistory',N'LeaveCertificates',N'ProvisionalCertificates',N'CharacterCertificates',
     N'DateOfBirthCertificates',N'NoObjectionCertificates',N'RegularStudentCertificates',N'RegularLecturerCertificates',
     N'BankAccountOpeningCertificates',N'SportsCertificates') RETURN;
    IF NOT EXISTS(SELECT 1 FROM dbo.Users WHERE UserId=@UserId)
        THROW 50001,'The specified user does not exist.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.AuditOperations WHERE OperationId=@OperationId AND UserId=@UserId)
        THROW 50002,'Audit operation context was not found.',1;
    DECLARE @AuditAction nvarchar(30)=CASE UPPER(LTRIM(RTRIM(@Action)))
        WHEN N'CORRECTION_CREATE' THEN N'INSERT' WHEN N'CORRECTION' THEN N'UPDATE'
        WHEN N'STATUS_CORRECTION' THEN N'UPDATE' WHEN N'BULK_PROMOTION' THEN N'PROMOTE'
        WHEN N'STATUS_SYNC' THEN N'UPDATE' WHEN N'REMOVE_SIBLING' THEN N'DELETE'
        WHEN N'DEACTIVATE' THEN N'UPDATE' WHEN N'MIGRATION_BILLING_CLEANUP' THEN N'OTHER'
        ELSE UPPER(LTRIM(RTRIM(@Action))) END;
    IF @AuditAction NOT IN(N'OTHER',N'LOCK',N'PUBLISH',N'REJECT',N'APPROVE',N'REVERSAL',N'REFUND',N'PAYMENT',
        N'TRANSFER_SECTION',N'TRANSFER_CLASS',N'TRANSFER_CAMPUS',N'PROMOTE',N'LEAVE',N'RE-ADMIT',N'DELETE',N'UPDATE',N'INSERT')
        THROW 50004,'The audit action is not one of the permitted values.',1;
    IF (@OldValues IS NOT NULL AND ISJSON(@OldValues)<>1) OR (@NewValues IS NOT NULL AND ISJSON(@NewValues)<>1)
        THROW 59941,'Audit values must be valid JSON.',1;
    DECLARE @Old TABLE(Path nvarchar(4000),FieldName nvarchar(4000),Value nvarchar(max),JsonType int);
    DECLARE @New TABLE(Path nvarchar(4000),FieldName nvarchar(4000),Value nvarchar(max),JsonType int);
    INSERT @Old SELECT * FROM dbo.AuditValueLeaves(@OldValues);
    INSERT @New SELECT * FROM dbo.AuditValueLeaves(@NewValues);
    -- Some callers describe the same field as OldStatus / NewStatus.
    -- Pair those names before comparing so an unchanged status is skipped.
    IF @OldValues IS NOT NULL AND @NewValues IS NOT NULL
    BEGIN
        DECLARE @Names TABLE(OldName nvarchar(4000),NewName nvarchar(4000),FieldName nvarchar(4000));
        INSERT @Names SELECT DISTINCT O.FieldName,N.FieldName,SUBSTRING(O.FieldName,4,4000)
        FROM @Old O JOIN @New N ON N.FieldName=N'New'+SUBSTRING(O.FieldName,4,4000)
        WHERE LEFT(O.FieldName,3)=N'Old';
        UPDATE O SET Path=LEFT(O.Path,LEN(O.Path)-LEN(O.FieldName))+M.FieldName,FieldName=M.FieldName FROM @Old O JOIN @Names M ON M.OldName=O.FieldName;
        UPDATE N SET Path=LEFT(N.Path,LEN(N.Path)-LEN(N.FieldName))+M.FieldName,FieldName=M.FieldName FROM @New N JOIN @Names M ON M.NewName=N.FieldName;
    END;
    -- Rows identified only by generic metadata are deliberately omitted on
    -- creation/deletion. Store the event and its financial/lifecycle context.
    DECLARE @SummaryFields TABLE(Name nvarchar(128) PRIMARY KEY);
    INSERT @SummaryFields VALUES
      (N'AdmissionId'),(N'EnrollmentId'),(N'AcademicYearId'),(N'ClassSectionId'),(N'CampusId'),(N'ClassId'),(N'FeeTypeId'),
      (N'AdmissionNumber'),(N'Status'),(N'Username'),(N'RoleName'),(N'RoleId'),(N'PermissionId'),(N'MustChangePassword'),
      (N'Amount'),(N'PaymentAmount'),(N'TotalAmount'),(N'GrossAmount'),(N'DiscountAmount'),(N'NetAmount'),(N'Frequency'),
      (N'EffectiveFrom'),(N'EffectiveTo'),(N'CancelledAt'),(N'BillingMonth'),(N'BillingStartDate'),(N'UsesTransport'),
      (N'ReceiptNumber'),(N'FeePaymentId'),(N'ResultPaymentId'),(N'TransactionType'),(N'PaymentMethod'),
      (N'CertificateType'),(N'LeaveDate'),(N'IssueDate'),(N'CertificateNumber'),(N'LeaveRecordId'),(N'LeaveCertificateIssueId'),
      (N'IsRetroactive'),(N'LeavingDate'),(N'WithdrawalDate'),(N'EnrollmentDate'),(N'DateOfAdmission'),
      (N'ClosingDate'),(N'Revision'),(N'SnapshotHash'),(N'ChargesCreated'),(N'RowsRequested'),(N'RowsNotPosted'),
      (N'BillingChargeId'),(N'StudentFeeAssignmentId'),(N'Reason'),(N'PasswordChanged'),(N'FeeProrationCutoffDay'),
      (N'CertificateDate'),(N'ExamType'),(N'ExamYear'),(N'RollNumber'),(N'MarksObtained'),(N'MarksOutOf'),(N'Grade'),(N'AdmissionDate'),(N'AdmissionCampusId'),
      (N'SubjectId'),(N'SubjectName'),(N'DisplayOrder');
    IF @AuditAction IN(N'INSERT',N'DELETE')
    BEGIN
        DELETE O FROM @Old O WHERE NOT EXISTS(SELECT 1 FROM @SummaryFields F WHERE F.Name=O.FieldName);
        DELETE N FROM @New N WHERE NOT EXISTS(SELECT 1 FROM @SummaryFields F WHERE F.Name=N.FieldName);
    END;
    -- Routine student demographic edits are excluded; lifecycle and admission
    -- number changes remain critical. Enrollment changes still record billing.
    IF @TableName=N'Students'
    BEGIN
        DELETE O FROM @Old O WHERE O.FieldName NOT IN(N'Status',N'OldStatus',N'NewStatus',N'AdmissionNumber',N'WithdrawalDate',N'LeavingDate',N'AdmissionDate',N'AdmissionCampusId',N'AdmissionId');
        DELETE N FROM @New N WHERE N.FieldName NOT IN(N'Status',N'OldStatus',N'NewStatus',N'AdmissionNumber',N'WithdrawalDate',N'LeavingDate',N'AdmissionDate',N'AdmissionCampusId',N'AdmissionId');
    END;
    IF @TableName=N'SchoolProfile'
    BEGIN
        DELETE FROM @Old WHERE FieldName<>N'FeeProrationCutoffDay';
        DELETE FROM @New WHERE FieldName<>N'FeeProrationCutoffDay';
        IF NOT EXISTS(SELECT 1 FROM @Old) AND NOT EXISTS(SELECT 1 FROM @New) RETURN;
    END;
    IF @TableName IN(N'FeeConfigurations',N'StudentFeeAssignments')
    BEGIN
        DELETE FROM @Old WHERE FieldName=N'Remarks';
        DELETE FROM @New WHERE FieldName=N'Remarks';
    END;
    DECLARE @Changes TABLE(Path nvarchar(4000),OldValue nvarchar(max),OldType int,NewValue nvarchar(max),NewType int);
    INSERT @Changes
    SELECT COALESCE(O.Path,N.Path),O.Value,O.JsonType,N.Value,N.JsonType
    FROM @Old O FULL JOIN @New N ON O.Path COLLATE Latin1_General_100_BIN2=N.Path COLLATE Latin1_General_100_BIN2
    WHERE O.JsonType IS NULL OR N.JsonType IS NULL OR O.JsonType<>N.JsonType
       OR ISNULL(O.Value,N'') COLLATE Latin1_General_100_BIN2<>ISNULL(N.Value,N'') COLLATE Latin1_General_100_BIN2;
    IF NOT EXISTS(SELECT 1 FROM @Changes) AND @OldValues IS NOT NULL AND @NewValues IS NOT NULL RETURN;
    IF @TableName=N'Students' AND @AuditAction=N'UPDATE' AND NOT EXISTS(SELECT 1 FROM @Changes) RETURN;
    SELECT @OldValues=N'{'+STRING_AGG(CONVERT(nvarchar(max),N'"'+STRING_ESCAPE(Path,'json')+N'":'+
        CASE OldType WHEN 0 THEN N'null' WHEN 1 THEN N'"'+STRING_ESCAPE(OldValue,'json')+N'"' ELSE OldValue END),N',') WITHIN GROUP(ORDER BY Path)+N'}'
        FROM @Changes WHERE OldType IS NOT NULL;
    SELECT @NewValues=N'{'+STRING_AGG(CONVERT(nvarchar(max),N'"'+STRING_ESCAPE(Path,'json')+N'":'+
        CASE NewType WHEN 0 THEN N'null' WHEN 1 THEN N'"'+STRING_ESCAPE(NewValue,'json')+N'"' ELSE NewValue END),N',') WITHIN GROUP(ORDER BY Path)+N'}'
        FROM @Changes WHERE NewType IS NOT NULL;
    INSERT dbo.AuditLogs(OperationId,UserId,TableName,RecordId,Action,OldValues,NewValues)
        VALUES(@OperationId,@UserId,@TableName,@RecordId,@AuditAction,@OldValues,@NewValues);
END;

GO
