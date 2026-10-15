-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission
        @UserId,
        N'Certificates.Create';

    SELECT
        RLC.RegularLecturerCertificateId,
        RLC.StaffId,

        RLC.StaffName,
        RLC.FatherName,
        RLC.RoleTitle,
        RLC.SubjectTaught,
        RLC.CertificateDate,

        RLC.CreatedAt,
        RLC.UpdatedAt

    FROM dbo.RegularLecturerCertificates AS RLC

    WHERE
        @StaffId = 0
        OR RLC.StaffId = @StaffId

    ORDER BY
        RLC.RegularLecturerCertificateId DESC;
END;
