-- Reference only; deploy numbered migrations.
CREATE TABLE dbo.StudentCreditEntries(
  CreditEntryId bigint IDENTITY PRIMARY KEY,
  AdmissionId int NOT NULL REFERENCES dbo.Students(AdmissionId),
  Amount decimal(12,2) NOT NULL CONSTRAINT CK_StudentCreditEntries_Amount CHECK(Amount<>0 AND Amount=FLOOR(Amount)),
  EntryKind nvarchar(20) NOT NULL CONSTRAINT CK_StudentCreditEntries_Kind CHECK(EntryKind IN(N'Deposit',N'Application',N'Refund',N'TransferIn',N'TransferOut',N'BillReduction')),
  FeePaymentId int NULL REFERENCES dbo.FeePayments(FeePaymentId),
  RequestId uniqueidentifier NOT NULL,
  CounterpartyAdmissionId int NULL REFERENCES dbo.Students(AdmissionId),
  Reason nvarchar(500) NOT NULL,
  CreatedByUserId int NOT NULL REFERENCES dbo.Users(UserId),
  CreatedAt datetime2(0) NOT NULL DEFAULT SYSDATETIME(),
  CONSTRAINT UX_StudentCreditEntries_Request UNIQUE(RequestId,AdmissionId,EntryKind)
 );
