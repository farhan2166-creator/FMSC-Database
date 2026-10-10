-- User-reviewed leaving: retain later bills or explicitly reverse selected uncollected bills.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
DECLARE @sql nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.CreateLeaveCertificate'));
IF @sql IS NULL THROW 57030,'Leave certificate procedure is missing.',1;
IF CHARINDEX(N'@KeepLaterCharges',@sql)=0
BEGIN
    SET @sql=REPLACE(@sql,N'@CertificateType NVARCHAR (20)=N''School''',N'@CertificateType NVARCHAR (20)=N''School'', @KeepLaterCharges BIT=0');
    SET @sql=REPLACE(@sql,N'AND DATEFROMPARTS(YEAR(@LeaveDate), MONTH(@LeaveDate), 1) < @MaxBilledMonth',
        N'AND @KeepLaterCharges=0 AND DATEFROMPARTS(YEAR(@LeaveDate), MONTH(@LeaveDate), 1) < @MaxBilledMonth');
    SET @sql=REPLACE(@sql,N'CREATE   PROCEDURE',N'ALTER PROCEDURE');
    SET @sql=REPLACE(@sql,N'CREATE PROCEDURE',N'ALTER PROCEDURE');
    EXEC sys.sp_executesql @sql;
END;
GO
CREATE OR ALTER PROCEDURE dbo.CreateReviewedLeaveCertificate
    @UserId int,@AdmissionId int,@EnrollmentId int,@LeaveDate date,
    @ClassPromotedTo nvarchar(300),@AcademicRecord nvarchar(400),@AttendanceRecord nvarchar(400),
    @Remarks nvarchar(1000),@CertificateType nvarchar(20)=N'School',
    @ExpectedBalance decimal(18,2),@ReviewedCharges nvarchar(max)=N'[]',@ReverseChargeIds nvarchar(max)=N'[]'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.RequirePermission @UserId,N'Students.Leave';
    EXEC dbo.RequirePermission @UserId,N'Students.View';
    IF ISJSON(@ReviewedCharges)<>1 OR ISJSON(@ReverseChargeIds)<>1
        THROW 57031,'Reload the fee review before confirming.',1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Locked int,@LockResult int,@Resource nvarchar(255)=N'FMSC.GenerateEnrollmentCharges.'+CONVERT(nvarchar(20),@EnrollmentId);
        SELECT @Locked=COUNT(*) FROM dbo.Students WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
        IF NOT EXISTS(SELECT 1 FROM dbo.Enrollments WITH(UPDLOCK,HOLDLOCK)
            WHERE AdmissionId=@AdmissionId AND EnrollmentId=@EnrollmentId AND Status IN(N'Active',N'Left'))
            THROW 57032,'The enrollment changed. Reload the student and review again.',1;
        IF @EnrollmentId<>(SELECT TOP(1) EnrollmentId FROM dbo.Enrollments
            WHERE AdmissionId=@AdmissionId AND Status IN(N'Active',N'Left') ORDER BY CASE WHEN Status=N'Active' THEN 0 ELSE 1 END,EnrollmentId DESC)
            THROW 57032,'The enrollment changed. Reload the student and review again.',1;
        EXEC @LockResult=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=5000;
        IF @LockResult<0 THROW 57033,'Fees are being updated elsewhere. Try again.',1;
        SELECT @Locked=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE AdmissionId=@AdmissionId;
        SELECT @Locked=COUNT(*) FROM dbo.FeePaymentAllocations A WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.BillingCharges B ON B.BillingChargeId=A.BillingChargeId WHERE B.AdmissionId=@AdmissionId;
        IF @ExpectedBalance IS NULL OR @ExpectedBalance<>(SELECT ISNULL(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0)
            FROM dbo.BillingCharges WHERE AdmissionId=@AdmissionId AND Status<>N'Cancelled')
            THROW 57034,'The balance changed. Review fees again before creating the certificate.',1;
        DECLARE @Month date=DATEFROMPARTS(YEAR(@LeaveDate),MONTH(@LeaveDate),1);
        DECLARE @Actual TABLE(BillingChargeId bigint PRIMARY KEY,NetAmount decimal(18,2),Status nvarchar(40),Balance decimal(18,2));
        INSERT @Actual SELECT BillingChargeId,NetAmount,Status,dbo.GetFeeChargeBalance(BillingChargeId)
            FROM dbo.BillingCharges WHERE EnrollmentId=@EnrollmentId AND Status<>N'Cancelled'
            AND (BillingMonth>@Month OR (BillingMonth IS NULL AND ChargeDate>@LeaveDate));
        DECLARE @Reviewed TABLE(BillingChargeId bigint PRIMARY KEY,NetAmount decimal(18,2),Status nvarchar(40),Balance decimal(18,2));
        INSERT @Reviewed SELECT BillingChargeId,NetAmount,Status,Balance FROM OPENJSON(@ReviewedCharges)
            WITH(BillingChargeId bigint,NetAmount decimal(18,2),Status nvarchar(40),Balance decimal(18,2));
        IF EXISTS(SELECT * FROM @Actual EXCEPT SELECT * FROM @Reviewed)
            OR EXISTS(SELECT * FROM @Reviewed EXCEPT SELECT * FROM @Actual)
            THROW 57035,'Later charges changed. Review fees again before creating the certificate.',1;
        DECLARE @Reverse TABLE(BillingChargeId bigint PRIMARY KEY);
        INSERT @Reverse SELECT TRY_CONVERT(bigint,[value]) FROM OPENJSON(@ReverseChargeIds);
        IF EXISTS(SELECT 1 FROM @Reverse R LEFT JOIN @Actual A ON A.BillingChargeId=R.BillingChargeId
            WHERE A.BillingChargeId IS NULL OR A.Status<>N'Open' OR A.Balance<>A.NetAmount)
            THROW 57036,'Only reviewed, wholly uncollected later charges can be reversed.',1;
        IF EXISTS(SELECT 1 FROM @Reverse R JOIN dbo.FeePaymentAllocations A ON A.BillingChargeId=R.BillingChargeId
            JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId WHERE P.Status=N'Completed' AND A.Amount>0)
            THROW 57036,'A selected charge has a payment. Use the existing correction or refund process.',1;
        DECLARE @ChargeId bigint,@Reason nvarchar(500)=LEFT(N'Leave certificate: '+ISNULL(@Remarks,N''),500);
        DECLARE reverse_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT BillingChargeId FROM @Reverse;
        OPEN reverse_cursor;
        FETCH NEXT FROM reverse_cursor INTO @ChargeId;
        WHILE @@FETCH_STATUS=0
        BEGIN
            EXEC dbo.ReverseBillingCharge @UserId=@UserId,@BillingChargeId=@ChargeId,@Reason=@Reason;
            FETCH NEXT FROM reverse_cursor INTO @ChargeId;
        END;
        CLOSE reverse_cursor;
        DEALLOCATE reverse_cursor;
        EXEC dbo.CreateLeaveCertificate @UserId=@UserId,@AdmissionId=@AdmissionId,@LeaveDate=@LeaveDate,
            @ClassPromotedTo=@ClassPromotedTo,@AcademicRecord=@AcademicRecord,@AttendanceRecord=@AttendanceRecord,
            @Remarks=@Remarks,@CertificateType=@CertificateType,@KeepLaterCharges=1;
        DECLARE @Operation uniqueidentifier=NEWID(),@Values nvarchar(max);
        SET @Values=(SELECT @AdmissionId AdmissionId,@EnrollmentId EnrollmentId,@LeaveDate LeaveDate,
            @ExpectedBalance PreviousBalance,JSON_QUERY(@ReviewedCharges) ReviewedLaterCharges,
            JSON_QUERY(@ReverseChargeIds) ReversedChargeIds,N'All unselected charges and payments retained.' Decision
            FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
        INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote)
            VALUES(@Operation,@UserId,N'LEAVE_FEE_REVIEW',CONVERT(nvarchar(200),@AdmissionId));
        EXEC dbo.WriteAuditLog @OperationId=@Operation,@UserId=@UserId,@TableName=N'Enrollments',@RecordId=@EnrollmentId,
            @Action=N'UPDATE',@OldValues=NULL,@NewValues=@Values;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
IF NOT EXISTS(SELECT 1 FROM dbo.DatabaseVersion WHERE VersionNumber=N'6')
 INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'6',SYSDATETIME(),SUSER_SNAME(),N'Explicit leave fee review with protected payments and atomic selected charge reversals.');
COMMIT TRANSACTION;
