-- Module body only. See companion XML for signatures and relationships.

BEGIN
 SET NOCOUNT ON;
 SET @Snapshot=(SELECT S.AdmissionId,S.AdmissionDate,S.Status,S.WithdrawalDate,
  JSON_QUERY((SELECT E.EnrollmentId,E.AcademicYearId,E.EnrollmentDate,E.LeavingDate,E.BillingStartDate,E.Status,E.UpdatedAt,
   JSON_QUERY((SELECT L.LeaveRecordId,L.LeaveDate FROM dbo.LeaveCertificates L WHERE L.EnrollmentId=E.EnrollmentId
      ORDER BY L.LeaveRecordId FOR JSON PATH,INCLUDE_NULL_VALUES)) Certificates
   FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId ORDER BY E.EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES)) Enrollments
  FROM dbo.Students S WHERE S.AdmissionId=@AdmissionId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES);
END;
