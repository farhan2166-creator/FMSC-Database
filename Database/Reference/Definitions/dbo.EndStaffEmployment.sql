SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
CREATE   PROCEDURE dbo.EndStaffEmployment
 @UserId int,@StaffId int,@LeavingDate date,@Remarks nvarchar(500)=NULL
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 EXEC dbo.RequirePermission @UserId,N'Staff.Manage';
 IF @LeavingDate IS NULL THROW 56421,'Leaving date is required.',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @EmploymentId int,@HireDate date;
  SELECT TOP(1) @EmploymentId=StaffEmploymentId,@HireDate=HireDate
  FROM dbo.StaffEmployment WITH(UPDLOCK,HOLDLOCK)
  WHERE StaffId=@StaffId AND Status=N'Active' ORDER BY StaffEmploymentId DESC;
  IF @EmploymentId IS NULL THROW 56420,'No active employment record was found for this staff member.',1;
  IF @LeavingDate<@HireDate THROW 56411,'Leaving date cannot be earlier than hire date.',1;
  UPDATE dbo.StaffEmployment SET LeavingDate=@LeavingDate,Status=N'Ended',
   Remarks=COALESCE(NULLIF(@Remarks,N''),Remarks),UpdatedAt=SYSDATETIME()
   WHERE StaffEmploymentId=@EmploymentId;
  UPDATE dbo.Staff SET Status=N'Left',UpdatedAt=SYSDATETIME() WHERE StaffId=@StaffId;
  COMMIT TRANSACTION;
  SELECT @StaffId StaffId,@EmploymentId StaffEmploymentId,@LeavingDate LeavingDate,N'Ended' Status;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END;

GO
