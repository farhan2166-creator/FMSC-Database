SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
CREATE   TRIGGER dbo.GuardFeeAssignmentPeriods ON dbo.StudentFeeAssignments AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted WHERE CancelledAt IS NULL AND EffectiveFrom>EffectiveTo)
  THROW 57104,'A fee period cannot end before it starts.',1;
 IF EXISTS(SELECT 1 FROM inserted I JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=I.EnrollmentId
   WHERE E.Status<>N'Active') OR EXISTS(SELECT 1 FROM deleted D JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=D.EnrollmentId WHERE E.Status<>N'Active')
  THROW 57105,'This enrollment is closed. Its fee assignments are preserved as history.',1;
 IF EXISTS(SELECT 1 FROM inserted I JOIN dbo.StudentFeeAssignments A WITH(UPDLOCK,HOLDLOCK) ON A.EnrollmentId=I.EnrollmentId AND A.FeeTypeId=I.FeeTypeId
   AND A.StudentFeeAssignmentId<>I.StudentFeeAssignmentId WHERE I.CancelledAt IS NULL AND A.CancelledAt IS NULL
   AND ISNULL(I.EffectiveFrom,'19000101')<=ISNULL(A.EffectiveTo,'99991231')
   AND ISNULL(A.EffectiveFrom,'19000101')<=ISNULL(I.EffectiveTo,'99991231'))
  THROW 57106,'Fee periods overlap. Review the existing periods before applying this change.',1;
END;

GO
