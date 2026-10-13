-- Whole-rupee policy approved on 13 October 2026.
-- Apply to a disposable restored copy first. No historical amounts are rounded.
-- Any fractional existing row aborts the complete migration before DDL/version changes.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @Version nvarchar(20)=(SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion WITH(UPDLOCK,HOLDLOCK) ORDER BY VersionId DESC);
    IF @Version NOT IN(N'1',N'2') OR @Version IS NULL
        THROW 53200,'Whole-rupee migration requires schema version 1 or 2.',1;
    IF EXISTS(SELECT 1 FROM dbo.[FeeConfigurations] WITH(TABLOCKX,HOLDLOCK) WHERE [Amount]<>FLOOR([Amount]))
        THROW 53201,'Fractional amounts exist in FeeConfigurations. Review and correct them explicitly; no values have been rounded.',1;
    IF EXISTS(SELECT 1 FROM dbo.[StudentFeeAssignments] WITH(TABLOCKX,HOLDLOCK) WHERE [Amount]<>FLOOR([Amount]) OR [DiscountAmount]<>FLOOR([DiscountAmount]))
        THROW 53201,'Fractional amounts exist in StudentFeeAssignments. Review and correct them explicitly; no values have been rounded.',1;
    IF EXISTS(SELECT 1 FROM dbo.[BillingCharges] WITH(TABLOCKX,HOLDLOCK) WHERE [GrossAmount]<>FLOOR([GrossAmount]) OR [DiscountAmount]<>FLOOR([DiscountAmount]))
        THROW 53201,'Fractional amounts exist in BillingCharges. Review and correct them explicitly; no values have been rounded.',1;
    IF EXISTS(SELECT 1 FROM dbo.[FeePayments] WITH(TABLOCKX,HOLDLOCK) WHERE [TotalAmount]<>FLOOR([TotalAmount]))
        THROW 53201,'Fractional amounts exist in FeePayments. Review and correct them explicitly; no values have been rounded.',1;
    IF EXISTS(SELECT 1 FROM dbo.[FeePaymentAllocations] WITH(TABLOCKX,HOLDLOCK) WHERE [Amount]<>FLOOR([Amount]))
        THROW 53201,'Fractional amounts exist in FeePaymentAllocations. Review and correct them explicitly; no values have been rounded.',1;
    IF EXISTS(SELECT 1 FROM dbo.[ManualFeePostingRequests] WITH(TABLOCKX,HOLDLOCK) WHERE [Amount]<>FLOOR([Amount]) OR [DiscountAmount]<>FLOOR([DiscountAmount]))
        THROW 53201,'Fractional amounts exist in ManualFeePostingRequests. Review and correct them explicitly; no values have been rounded.',1;
    IF EXISTS(SELECT 1 FROM dbo.[StudentClearanceRecords] WITH(TABLOCKX,HOLDLOCK) WHERE [OutstandingBalance]<>FLOOR([OutstandingBalance]))
        THROW 53201,'Fractional amounts exist in StudentClearanceRecords. Review and correct them explicitly; no values have been rounded.',1;
    IF EXISTS(SELECT 1 FROM dbo.[BankAccountOpeningCertificates] WITH(TABLOCKX,HOLDLOCK) WHERE [MonthlyIncome]<>FLOOR([MonthlyIncome]))
        THROW 53201,'Fractional amounts exist in BankAccountOpeningCertificates. Review and correct them explicitly; no values have been rounded.',1;
    IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.FeeConfigurations') AND name=N'CK_FeeConfigurations_WholeRupees')
        ALTER TABLE dbo.[FeeConfigurations] DROP CONSTRAINT [CK_FeeConfigurations_WholeRupees];
    ALTER TABLE dbo.[FeeConfigurations] WITH CHECK ADD CONSTRAINT [CK_FeeConfigurations_WholeRupees] CHECK ([Amount]=FLOOR([Amount]));
    ALTER TABLE dbo.[FeeConfigurations] WITH CHECK CHECK CONSTRAINT [CK_FeeConfigurations_WholeRupees];
    IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.StudentFeeAssignments') AND name=N'CK_StudentFeeAssignments_WholeRupees')
        ALTER TABLE dbo.[StudentFeeAssignments] DROP CONSTRAINT [CK_StudentFeeAssignments_WholeRupees];
    ALTER TABLE dbo.[StudentFeeAssignments] WITH CHECK ADD CONSTRAINT [CK_StudentFeeAssignments_WholeRupees] CHECK ([Amount]=FLOOR([Amount]) AND [DiscountAmount]=FLOOR([DiscountAmount]));
    ALTER TABLE dbo.[StudentFeeAssignments] WITH CHECK CHECK CONSTRAINT [CK_StudentFeeAssignments_WholeRupees];
    IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.BillingCharges') AND name=N'CK_BillingCharges_WholeRupees')
        ALTER TABLE dbo.[BillingCharges] DROP CONSTRAINT [CK_BillingCharges_WholeRupees];
    ALTER TABLE dbo.[BillingCharges] WITH CHECK ADD CONSTRAINT [CK_BillingCharges_WholeRupees] CHECK ([GrossAmount]=FLOOR([GrossAmount]) AND [DiscountAmount]=FLOOR([DiscountAmount]));
    ALTER TABLE dbo.[BillingCharges] WITH CHECK CHECK CONSTRAINT [CK_BillingCharges_WholeRupees];
    IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.FeePayments') AND name=N'CK_FeePayments_WholeRupees')
        ALTER TABLE dbo.[FeePayments] DROP CONSTRAINT [CK_FeePayments_WholeRupees];
    ALTER TABLE dbo.[FeePayments] WITH CHECK ADD CONSTRAINT [CK_FeePayments_WholeRupees] CHECK ([TotalAmount]=FLOOR([TotalAmount]));
    ALTER TABLE dbo.[FeePayments] WITH CHECK CHECK CONSTRAINT [CK_FeePayments_WholeRupees];
    IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.FeePaymentAllocations') AND name=N'CK_FeePaymentAllocations_WholeRupees')
        ALTER TABLE dbo.[FeePaymentAllocations] DROP CONSTRAINT [CK_FeePaymentAllocations_WholeRupees];
    ALTER TABLE dbo.[FeePaymentAllocations] WITH CHECK ADD CONSTRAINT [CK_FeePaymentAllocations_WholeRupees] CHECK ([Amount]=FLOOR([Amount]));
    ALTER TABLE dbo.[FeePaymentAllocations] WITH CHECK CHECK CONSTRAINT [CK_FeePaymentAllocations_WholeRupees];
    IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.ManualFeePostingRequests') AND name=N'CK_ManualFeePostingRequests_WholeRupees')
        ALTER TABLE dbo.[ManualFeePostingRequests] DROP CONSTRAINT [CK_ManualFeePostingRequests_WholeRupees];
    ALTER TABLE dbo.[ManualFeePostingRequests] WITH CHECK ADD CONSTRAINT [CK_ManualFeePostingRequests_WholeRupees] CHECK ([Amount]=FLOOR([Amount]) AND [DiscountAmount]=FLOOR([DiscountAmount]));
    ALTER TABLE dbo.[ManualFeePostingRequests] WITH CHECK CHECK CONSTRAINT [CK_ManualFeePostingRequests_WholeRupees];
    IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.StudentClearanceRecords') AND name=N'CK_StudentClearanceRecords_WholeRupees')
        ALTER TABLE dbo.[StudentClearanceRecords] DROP CONSTRAINT [CK_StudentClearanceRecords_WholeRupees];
    ALTER TABLE dbo.[StudentClearanceRecords] WITH CHECK ADD CONSTRAINT [CK_StudentClearanceRecords_WholeRupees] CHECK ([OutstandingBalance]=FLOOR([OutstandingBalance]));
    ALTER TABLE dbo.[StudentClearanceRecords] WITH CHECK CHECK CONSTRAINT [CK_StudentClearanceRecords_WholeRupees];
    IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.BankAccountOpeningCertificates') AND name=N'CK_BankAccountOpeningCertificates_WholeRupees')
        ALTER TABLE dbo.[BankAccountOpeningCertificates] DROP CONSTRAINT [CK_BankAccountOpeningCertificates_WholeRupees];
    ALTER TABLE dbo.[BankAccountOpeningCertificates] WITH CHECK ADD CONSTRAINT [CK_BankAccountOpeningCertificates_WholeRupees] CHECK ([MonthlyIncome]=FLOOR([MonthlyIncome]));
    ALTER TABLE dbo.[BankAccountOpeningCertificates] WITH CHECK CHECK CONSTRAINT [CK_BankAccountOpeningCertificates_WholeRupees];
    IF @Version=N'1'
        INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
        VALUES(N'2',SYSDATETIME(),SUSER_SNAME(),N'Whole-rupee financial constraints; no historical rounding.');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
