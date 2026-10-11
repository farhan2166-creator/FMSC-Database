CREATE   PROCEDURE dbo.SaveAcademicProgram
@UserId INT, @AcademicProgramId INT, @ProgramType NVARCHAR (30), @ProgramCode NVARCHAR (30)=NULL, @ProgramName NVARCHAR (150), @DisplayOrder INT=0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'System.Setup';
    BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @OrderLock int;
    EXEC @OrderLock = sys.sp_getapplock @Resource=N'SetupOrder:AcademicPrograms', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
    IF @OrderLock < 0 THROW 57401, 'Unable to lock setup ordering. Please try again.', 1;
    IF NULLIF (LTRIM(RTRIM(@ProgramType)), N'') IS NULL
        THROW 56420, 'Program type is required.', 1;
    IF NULLIF (LTRIM(RTRIM(@ProgramName)), N'') IS NULL
        THROW 56421, 'Program name is required.', 1;
    IF @AcademicProgramId = 0
        BEGIN
            SELECT @DisplayOrder = ISNULL(MAX(DisplayOrder), 0) + 1
                    FROM   dbo.AcademicPrograms WITH (UPDLOCK,HOLDLOCK);
            INSERT  dbo.AcademicPrograms (ProgramCode, ProgramName, ProgramType, DisplayOrder, CreatedAt)
            VALUES                      (NULLIF (LTRIM(RTRIM(@ProgramCode)), N''), LTRIM(RTRIM(@ProgramName)), LTRIM(RTRIM(@ProgramType)), @DisplayOrder, SYSUTCDATETIME());

    COMMIT TRANSACTION;
    SELECT CONVERT (INT, SCOPE_IDENTITY()) AS AcademicProgramId;
            RETURN;
        END
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.AcademicPrograms
                   WHERE  AcademicProgramId = @AcademicProgramId)
        THROW 56422, 'The selected program was not found.', 1;
    UPDATE dbo.AcademicPrograms
    SET    ProgramCode  = NULLIF (LTRIM(RTRIM(@ProgramCode)), N''),
           ProgramName  = LTRIM(RTRIM(@ProgramName)),
           ProgramType  = LTRIM(RTRIM(@ProgramType)),
           UpdatedAt    = SYSUTCDATETIME()
    WHERE  AcademicProgramId = @AcademicProgramId;

    COMMIT TRANSACTION;
    SELECT @AcademicProgramId AS AcademicProgramId;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END

