-- Module body only. See companion XML for signatures and relationships.

BEGIN
 SET NOCOUNT ON;
 EXEC dbo.RequirePermission @UserId,N'Students.Administer';
 DECLARE @Snapshot nvarchar(max);
 EXEC dbo.GetLifecycleRepairSnapshot @AdmissionId,@Snapshot OUTPUT;
 SELECT AdmissionNumber,StudentName,AdmissionDate,Status,WithdrawalDate,@Snapshot Snapshot FROM dbo.Students WHERE AdmissionId=@AdmissionId;
 SELECT E.EnrollmentId,AY.YearName,E.Status,E.EnrollmentDate,E.LeavingDate,E.BillingStartDate,
  C.CampusName+N' / '+CL.ClassName+N' - '+SEC.SectionName Placement,
  (SELECT COUNT(*) FROM dbo.BillingCharges B WHERE B.EnrollmentId=E.EnrollmentId) Charges,
  (SELECT ISNULL(SUM(dbo.GetFeeChargeBalance(B.BillingChargeId)),0) FROM dbo.BillingCharges B WHERE B.EnrollmentId=E.EnrollmentId AND B.Status<>N'Cancelled') Outstanding
 FROM dbo.Enrollments E JOIN dbo.AcademicYears AY ON AY.AcademicYearId=E.AcademicYearId
 JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId JOIN dbo.Campuses C ON C.CampusId=CCS.CampusId
 JOIN dbo.Classes CL ON CL.ClassId=CCS.ClassId JOIN dbo.Sections SEC ON SEC.SectionId=CCS.SectionId
 WHERE E.AdmissionId=@AdmissionId ORDER BY E.EnrollmentId;
 SELECT L.LeaveRecordId,L.EnrollmentId,L.CertificateNumber,L.LeaveDate,L.CreatedAt RecordedAt,
  (SELECT MIN(I.IssuedAt) FROM dbo.LeaveCertificateIssues I WHERE I.LeaveRecordId=L.LeaveRecordId) FirstIssuedAt
 FROM dbo.LeaveCertificates L WHERE L.AdmissionId=@AdmissionId ORDER BY L.EnrollmentId,L.LeaveRecordId;
END;
