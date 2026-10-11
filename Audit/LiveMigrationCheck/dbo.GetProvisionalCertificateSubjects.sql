
-- GetProvisionalCertificateSubjects
CREATE   PROCEDURE dbo.GetProvisionalCertificateSubjects
@UserId INT, @ProvisionalCertificateId INT
AS
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



