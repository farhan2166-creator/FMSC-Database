-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Certificates.Create';
    SELECT   PCS.SubjectId,
             PCS.SubjectName,
             PCS.DisplayOrder
    FROM     dbo.ProvisionalCertificateSubjects AS PCS
    WHERE    PCS.ProvisionalCertificateId = @ProvisionalCertificateId
    ORDER BY PCS.DisplayOrder, PCS.SubjectName;
END
