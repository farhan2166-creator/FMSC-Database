
-- UpdateBankAccountOpeningCertificate
CREATE   PROCEDURE dbo.UpdateBankAccountOpeningCertificate
@UserId INT, @BankAccountOpeningCertificateId INT, @Designation NVARCHAR (100), @BankName NVARCHAR (100), @BranchName NVARCHAR (150), @MonthlyIncome DECIMAL (18, 2)=NULL, @Purpose NVARCHAR (200), @CertificateDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    IF @BankAccountOpeningCertificateId <= 0
        THROW 58810, 'A valid certificate is required.', 1;
    IF @Designation IS NULL
       OR LTRIM(RTRIM(@Designation)) = N''
        THROW 58811, 'Designation is required.', 1;
    IF @BankName IS NULL
       OR LTRIM(RTRIM(@BankName)) = N''
        THROW 58812, 'Bank name is required.', 1;
    IF @BranchName IS NULL
       OR LTRIM(RTRIM(@BranchName)) = N''
        THROW 58813, 'Branch name is required.', 1;
    IF @Purpose IS NULL
       OR LTRIM(RTRIM(@Purpose)) = N''
        THROW 58814, 'Purpose is required.', 1;
    IF @MonthlyIncome IS NOT NULL
       AND @MonthlyIncome < 0
        THROW 58815, 'Monthly income cannot be negative.', 1;
    IF @CertificateDate IS NULL
        THROW 58816, 'Certificate date is required.', 1;
    IF @CertificateDate > CONVERT (DATE, SYSDATETIME())
        THROW 58817, 'Certificate date cannot be in the future.', 1;
    BEGIN TRANSACTION;
    DECLARE @StaffId AS INT;
    DECLARE @OldValues AS NVARCHAR (MAX);
    SELECT @StaffId = BAC.StaffId,
           @OldValues = (SELECT BAC.BankAccountOpeningCertificateId,
                                BAC.StaffId,
                                BAC.Designation,
                                BAC.BankName,
                                BAC.BranchName,
                                BAC.MonthlyIncome,
                                BAC.Purpose,
                                BAC.CertificateDate
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES)
    FROM   dbo.BankAccountOpeningCertificates AS BAC WITH (UPDLOCK, HOLDLOCK)
    WHERE  BAC.BankAccountOpeningCertificateId = @BankAccountOpeningCertificateId;
    IF @StaffId IS NULL
        THROW 58818, 'The certificate was not found.', 1;
    UPDATE dbo.BankAccountOpeningCertificates
    SET    Designation     = LTRIM(RTRIM(@Designation)),
           BankName        = LTRIM(RTRIM(@BankName)),
           BranchName      = LTRIM(RTRIM(@BranchName)),
           MonthlyIncome   = NULLIF (@MonthlyIncome, 0),
           Purpose         = LTRIM(RTRIM(@Purpose)),
           CertificateDate = @CertificateDate,
           UpdatedByUserId = @UserId,
           UpdatedAt       = SYSDATETIME()
    WHERE  BankAccountOpeningCertificateId = @BankAccountOpeningCertificateId;
    DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
    DECLARE @RecordId AS NVARCHAR (200) = CONVERT (NVARCHAR (200), @BankAccountOpeningCertificateId);
    DECLARE @NewValues AS NVARCHAR (MAX);
    SELECT @NewValues = (SELECT @BankAccountOpeningCertificateId AS BankAccountOpeningCertificateId,
                                @StaffId AS StaffId,
                                @Designation AS Designation,
                                @BankName AS BankName,
                                @BranchName AS BranchName,
                                @MonthlyIncome AS MonthlyIncome,
                                @Purpose AS Purpose,
                                @CertificateDate AS CertificateDate
                         FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
    VALUES                     (@OperationId, @UserId, N'BANK_ACCOUNT_OPENING_CERTIFICATE', CONVERT (NVARCHAR (200), @StaffId));
    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'BankAccountOpeningCertificates', @RecordId = @RecordId, @Action = N'UPDATE', @OldValues = @OldValues, @NewValues = @NewValues;
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @BankAccountOpeningCertificateId AS BankAccountOpeningCertificateId,
           @StaffId AS StaffId;
END



