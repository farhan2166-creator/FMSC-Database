-- Reference only; deploy numbered migrations.
CREATE TABLE dbo.FeeBillCorrections(
  FeeBillCorrectionId bigint IDENTITY PRIMARY KEY,
  BillingChargeId bigint NOT NULL REFERENCES dbo.BillingCharges(BillingChargeId),
  RequestId uniqueidentifier NOT NULL UNIQUE,
  PreviousNet decimal(12,2) NOT NULL,NewGross decimal(12,2) NOT NULL,NewDiscount decimal(12,2) NOT NULL,
  Difference decimal(12,2) NOT NULL,ReleasedPaidAmount decimal(12,2) NOT NULL,
  PaidReductionAction nvarchar(10) NOT NULL CHECK(PaidReductionAction IN(N'Credit',N'Refund')),
  Reason nvarchar(500) NOT NULL,CreatedByUserId int NOT NULL REFERENCES dbo.Users(UserId),
  CreatedAt datetime2(0) NOT NULL DEFAULT SYSDATETIME()
 );
