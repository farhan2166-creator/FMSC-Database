CREATE   PROCEDURE dbo.GetStaffEmployment
    @UserId INT,
    @StaffId INT = 0
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequireAuthenticatedUser @UserId;

    SELECT
        se.StaffEmploymentId,
        se.StaffId,
        s.StaffNumber,
        s.StaffName,
        s.FatherName,
        se.Designation,
        se.Department,
        se.EmploymentType,
        se.HireDate,
        se.LeavingDate,
        se.Status,
        se.Remarks,
        se.CreatedAt,
        se.UpdatedAt
    FROM dbo.StaffEmployment AS se
    INNER JOIN dbo.Staff AS s
        ON s.StaffId = se.StaffId
    WHERE
        @StaffId = 0
        OR se.StaffId = @StaffId
    ORDER BY
        s.StaffName,
        se.HireDate DESC,
        se.StaffEmploymentId DESC;
END;

