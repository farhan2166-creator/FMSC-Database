-- Staff entry validation and clean employment refusal paths. No existing staff data is changed.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO
CREATE OR ALTER PROCEDURE dbo.SaveStaff
    @UserId INT,
    @StaffNumber NVARCHAR(30),
    @StaffName NVARCHAR(150),
    @StaffId INT=0,
    @FatherName NVARCHAR(150)=NULL,
    @DateOfBirth DATE=NULL,
    @Gender NVARCHAR(20)=NULL,
    @CNIC NVARCHAR(25)=NULL,
    @ContactNumber NVARCHAR(30)=NULL,
    @AlternateContactNumber NVARCHAR(30)=NULL,
    @Address NVARCHAR(500)=NULL,
    @Qualification NVARCHAR(250)=NULL,
    @Specialization NVARCHAR(250)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId,N'Staff.Manage';
    SET @StaffNumber=NULLIF(LTRIM(RTRIM(@StaffNumber)),N'');
    SET @StaffName=NULLIF(LTRIM(RTRIM(@StaffName)),N'');
    IF @StaffNumber IS NULL THROW 56402,'Staff number is required.',1;
    IF @StaffName IS NULL THROW 56403,'Staff name is required.',1;
    IF EXISTS(SELECT 1 FROM dbo.Staff WHERE StaffNumber=@StaffNumber AND StaffId<>@StaffId)
        THROW 56404,'This staff number is already assigned to another staff member.',1;

    IF @StaffId=0
    BEGIN
        INSERT dbo.Staff
        (
            StaffNumber,StaffName,FatherName,DateOfBirth,Gender,CNIC,
            ContactNumber,AlternateContactNumber,Address,Qualification,Specialization
        )
        VALUES
        (
            @StaffNumber,@StaffName,@FatherName,@DateOfBirth,@Gender,@CNIC,
            @ContactNumber,@AlternateContactNumber,@Address,@Qualification,@Specialization
        );
        SELECT CONVERT(INT,SCOPE_IDENTITY()) StaffId;
    END
    ELSE
    BEGIN
        UPDATE dbo.Staff
        SET StaffNumber=@StaffNumber,StaffName=@StaffName,FatherName=@FatherName,
            DateOfBirth=@DateOfBirth,Gender=@Gender,CNIC=@CNIC,
            ContactNumber=@ContactNumber,AlternateContactNumber=@AlternateContactNumber,
            Address=@Address,Qualification=@Qualification,Specialization=@Specialization,
            UpdatedAt=SYSDATETIME()
        WHERE StaffId=@StaffId;

        IF @@ROWCOUNT=0 THROW 56401,'The selected staff record was not found.',1;
        SELECT @StaffId StaffId;
    END;
END;
GO
CREATE OR ALTER PROCEDURE dbo.SaveStaffEmployment
    @UserId INT,
    @StaffId INT,
    @Designation NVARCHAR(100),
    @HireDate DATE,
    @StaffEmploymentId INT = 0,
    @Department NVARCHAR(100) = NULL,
    @EmploymentType NVARCHAR(50) = NULL,
    @LeavingDate DATE = NULL,
    @Remarks NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.RequirePermission
        @UserId,
        N'Staff.Manage';
    IF @StaffId <= 0
        THROW 56409, 'A valid staff member is required.', 1;
    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Staff
        WHERE StaffId = @StaffId
    )
        THROW 56410, 'The selected staff member was not found.', 1;
    IF NULLIF(LTRIM(RTRIM(@Designation)), N'') IS NULL
        THROW 56413, 'Designation is required.', 1;
    IF @HireDate IS NULL THROW 56415, 'Hire date is required.', 1;
    IF @LeavingDate IS NOT NULL
       AND @LeavingDate < @HireDate
        THROW 56411, 'Leaving date cannot be earlier than hire date.', 1;
    /* FIX 077 - everything from here to the COMMIT is one attempt, so a failure
       anywhere inside it cannot leave the caller's transaction open. */
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @PreviousStaffId int;
        SELECT @PreviousStaffId=StaffId FROM dbo.StaffEmployment WITH(UPDLOCK,HOLDLOCK)
        WHERE StaffEmploymentId=@StaffEmploymentId;
        IF @StaffEmploymentId = 0
        BEGIN
            INSERT dbo.StaffEmployment
            (
                StaffId,
                Designation,
                Department,
                EmploymentType,
                HireDate,
                LeavingDate,
                Status,
                Remarks
            )
            VALUES
            (
                @StaffId,
                @Designation,
                @Department,
                @EmploymentType,
                @HireDate,
                @LeavingDate,
                CASE
                    WHEN @LeavingDate IS NULL
                        THEN N'Active'
                    ELSE N'Ended'
                END,
                @Remarks
            );
            SET @StaffEmploymentId =
                CONVERT(INT, SCOPE_IDENTITY());
        END
        ELSE
        BEGIN
            IF NOT EXISTS
            (
                SELECT 1
                FROM dbo.StaffEmployment
                WHERE StaffEmploymentId = @StaffEmploymentId
            )
                THROW 56412,
                    'The selected employment record was not found.',
                    1;
            UPDATE dbo.StaffEmployment
            SET
                StaffId = @StaffId,
                Designation = @Designation,
                Department = @Department,
                EmploymentType = @EmploymentType,
                HireDate = @HireDate,
                LeavingDate = @LeavingDate,
                Status =
                    CASE
                        WHEN @LeavingDate IS NULL
                            THEN N'Active'
                        ELSE N'Ended'
                    END,
                Remarks = @Remarks,
                UpdatedAt = SYSDATETIME()
            WHERE StaffEmploymentId = @StaffEmploymentId;
            IF @@ROWCOUNT = 0
                THROW 56412,
                    'The selected employment record was not found.',
                    1;
        END;
        /*
            Synchronize Staff.Status with the actual employment state.
            If this staff member has an active employment record,
            the staff member is Active.
            If no active employment record remains,
            the staff member is Left.
        */
        IF EXISTS
        (
            SELECT 1
            FROM dbo.StaffEmployment
            WHERE StaffId = @StaffId
              AND Status = N'Active'
        )
        BEGIN
            UPDATE dbo.Staff
            SET
                Status = N'Active',
                UpdatedAt = SYSDATETIME()
            WHERE StaffId = @StaffId;
        END
        ELSE
        BEGIN
            UPDATE dbo.Staff
            SET
                Status = N'Left',
                UpdatedAt = SYSDATETIME()
            WHERE StaffId = @StaffId;
        END;
        IF @PreviousStaffId IS NOT NULL AND @PreviousStaffId<>@StaffId
            UPDATE dbo.Staff SET Status=CASE WHEN EXISTS
                (SELECT 1 FROM dbo.StaffEmployment WHERE StaffId=@PreviousStaffId AND Status=N'Active')
                THEN N'Active' ELSE N'Left' END, UpdatedAt=SYSDATETIME()
            WHERE StaffId=@PreviousStaffId;
        COMMIT;
    END TRY
    BEGIN CATCH
        /* Leave the session clean whatever failed. Without this the caller is
           left with XACT_STATE() = -1 and @@TRANCOUNT = 1 after a constraint
           violation, and every later statement on that connection fails. */
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        /* A second ACTIVE employment is the one failure here the operator can
           actually act on, so it is named. 2601 is a unique INDEX violation,
           2627 a unique CONSTRAINT one - both mean the same thing to the caller,
           and @StaffEmploymentId is still 0 only on the insert path. */
        IF ERROR_NUMBER() IN (2601, 2627)
            THROW 56414,
                'This staff member already has an active employment record. Set a leaving date on that record first, then add the new one.',
                1;
        /* Anything else keeps its original number and message. */
        THROW;
    END CATCH;
    SELECT
        @StaffEmploymentId AS StaffEmploymentId;
END;
GO
CREATE OR ALTER PROCEDURE dbo.EndStaffEmployment
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
COMMIT TRANSACTION;
GO
