
/* ---------------------------------------------------------------------
   1. dbo.GetProvisionalCertificateRecords - return OtherSubjects
   --------------------------------------------------------------------- */

CREATE   PROCEDURE dbo.GetProvisionalCertificateRecords
    @UserId INT,
    @AdmissionId INT = 0
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Create';

    SELECT
        PC.ProvisionalCertificateId,
        PC.AdmissionId,

        PC.StudentName,
        PC.FatherName,

        PC.ExamType,
        CASE PC.ExamType
            WHEN N'SSC'   THEN N'SSC (Matric)'
            WHEN N'HSSC1' THEN N'HSSC Part I'
            WHEN N'HSSC2' THEN N'HSSC Part II'
        END AS ExamTypeDisplay,

        PC.ExamYear,
        PC.BoardName,
        PC.RollNumber,
        PC.MarksObtained,
        PC.MarksOutOf,
        PC.Grade,
        PC.CertificateDate,

        PC.CreatedAt,
        PC.UpdatedAt,

        -- Appended at the END: the editor needs the stored value when a row is
        -- selected, or Update writes the box over it.
        PC.OtherSubjects

    FROM dbo.ProvisionalCertificates AS PC

    WHERE
        @AdmissionId = 0
        OR PC.AdmissionId = @AdmissionId

    ORDER BY
        PC.ProvisionalCertificateId DESC;
END;

