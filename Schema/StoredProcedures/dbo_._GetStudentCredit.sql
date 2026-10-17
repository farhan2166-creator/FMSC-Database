-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE PROCEDURE dbo.GetStudentCredit @UserId int,@AdmissionId int
AS
BEGIN
 SET NOCOUNT ON; EXEC dbo.RequirePermission @UserId,N'Fees.View';
 SELECT @AdmissionId AdmissionId,COALESCE(SUM(Amount),0) AvailableCredit FROM dbo.StudentCreditEntries WHERE AdmissionId=@AdmissionId;
 SELECT CreditEntryId,EntryKind,Amount,FeePaymentId,CounterpartyAdmissionId,Reason,CreatedAt
 FROM dbo.StudentCreditEntries WHERE AdmissionId=@AdmissionId ORDER BY CreditEntryId DESC;
END;
