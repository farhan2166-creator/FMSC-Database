SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER OFF;
GO
CREATE   PROCEDURE dbo.SaveCampus
@UserId INT, @CampusCode NVARCHAR (30), @CampusName NVARCHAR (150), @CampusType NVARCHAR (30), @CampusId INT=0, @AddressLine NVARCHAR (500)=NULL, @City NVARCHAR (100)=NULL, @ContactNumber NVARCHAR (30)=NULL, @Email NVARCHAR (150)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @OrderLock int;
    EXEC @OrderLock=sys.sp_getapplock @Resource=N'SetupOrder:Campuses', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
    IF @OrderLock<0 THROW 57401, 'Unable to lock setup ordering. Please try again.', 1;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'System.Setup';
    IF NULLIF (LTRIM(RTRIM(@CampusCode)), N'') IS NULL
        THROW 56001, 'Campus code is required.', 1;
    IF NULLIF (LTRIM(RTRIM(@CampusName)), N'') IS NULL
        THROW 56002, 'Campus name is required.', 1;
    IF @CampusId = 0
        BEGIN
            DECLARE @NextOrder int;
            SELECT @NextOrder=ISNULL(MAX(DisplayOrder),0)+1 FROM dbo.Campuses WITH(UPDLOCK,HOLDLOCK);
            INSERT dbo.Campuses (DisplayOrder, CampusCode, CampusName, CampusType, AddressLine, City, ContactNumber, Email)
            VALUES (@NextOrder, @CampusCode, @CampusName, @CampusType, @AddressLine, @City, @ContactNumber, @Email);
            COMMIT TRANSACTION;
            SELECT CONVERT (INT, SCOPE_IDENTITY()) AS CampusId;
        END
    ELSE
        BEGIN
            UPDATE dbo.Campuses
            SET    CampusCode    = @CampusCode,
                   CampusName    = @CampusName,
                   CampusType    = @CampusType,
                   AddressLine   = @AddressLine,
                   City          = @City,
                   ContactNumber = @ContactNumber,
                   Email         = @Email,
                   UpdatedAt     = SYSDATETIME()
            WHERE  CampusId = @CampusId;
            IF @@ROWCOUNT = 0
                THROW 56003, 'The selected campus was not found.', 1;
            COMMIT TRANSACTION;
            SELECT @CampusId AS CampusId;
        END
END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END

GO
