-- Reference only; deploy numbered migrations.
CREATE TABLE dbo.BillingAutomationState(
  StateId int NOT NULL CONSTRAINT PK_BillingAutomationState PRIMARY KEY CONSTRAINT CK_BillingAutomationState_Singleton CHECK(StateId=1),
  StartMonth date NOT NULL CONSTRAINT CK_BillingAutomationState_Month CHECK(DAY(StartMonth)=1),
  RecoveryReviewedThrough date NULL,
  UpdatedBy int NULL REFERENCES dbo.Users(UserId),
  UpdatedAt datetime2(0) NOT NULL DEFAULT SYSDATETIME()
 );
