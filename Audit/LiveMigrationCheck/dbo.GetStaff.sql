
/* ================================================================
   44. STAFF / TIMETABLE LOOKUPS
   ================================================================ */
CREATE   PROCEDURE dbo.GetStaff
    @UserId INT,
    @StaffId INT=NULL,
    @Search NVARCHAR(150)=NULL
AS
BEGIN
    EXEC dbo.RequirePermission @UserId,N'Staff.View';
    SET NOCOUNT ON;

    SELECT
        S.StaffId,S.StaffNumber,S.StaffName,S.FatherName,
        S.DateOfBirth,S.Gender,S.CNIC,S.ContactNumber,
        S.AlternateContactNumber,S.Address,S.Qualification,
        S.Specialization,S.Status,
        E.StaffEmploymentId,E.Designation,E.Department,
        E.EmploymentType,E.HireDate,E.LeavingDate,E.Status EmploymentStatus
    FROM dbo.Staff S
    OUTER APPLY
    (
        SELECT TOP(1) *
        FROM dbo.StaffEmployment SE
        WHERE SE.StaffId=S.StaffId
        ORDER BY CASE WHEN SE.Status=N'Active' THEN 0 ELSE 1 END,
                 SE.HireDate DESC,SE.StaffEmploymentId DESC
    ) E
    WHERE (@StaffId IS NULL OR S.StaffId=@StaffId)
      AND
      (
          @Search IS NULL OR @Search=N''
          OR S.StaffNumber LIKE N'%'+@Search+N'%'
          OR S.StaffName LIKE N'%'+@Search+N'%'
          OR S.FatherName LIKE N'%'+@Search+N'%'
          OR S.CNIC LIKE N'%'+@Search+N'%'
      )
    ORDER BY S.StaffName;
END;

