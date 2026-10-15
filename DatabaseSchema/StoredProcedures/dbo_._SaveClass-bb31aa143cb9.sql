-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'System.Setup';
    BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @OrderLock int;
    EXEC @OrderLock = sys.sp_getapplock @Resource=N'SetupOrder:Classes', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
    IF @OrderLock < 0 THROW 57401, 'Unable to lock setup ordering. Please try again.', 1;
    IF NULLIF (LTRIM(RTRIM(@ClassName)), N'') IS NULL
        THROW 56210, 'Class name is required.', 1;
    IF @ClassId = 0
        BEGIN
            IF @AcademicProgramId IS NULL
               OR @AcademicProgramId <= 0
                THROW 56211, 'Academic program is required for a new class.', 1;
            IF NOT EXISTS (SELECT 1
                           FROM   dbo.AcademicPrograms
                           WHERE  AcademicProgramId = @AcademicProgramId)
                THROW 56214, 'The selected academic program is invalid.', 1;
            IF NULLIF (LTRIM(RTRIM(@ClassCode)), N'') IS NULL
                THROW 56212, 'Class code is required for a new class.', 1;
            SELECT @DisplayOrder=ISNULL(MAX(DisplayOrder),0)+1 FROM dbo.Classes WITH (UPDLOCK,HOLDLOCK) WHERE AcademicProgramId=@AcademicProgramId;
            INSERT  dbo.Classes (AcademicProgramId, ClassCode, ClassName, DisplayOrder)
            VALUES             (@AcademicProgramId, LTRIM(RTRIM(@ClassCode)), LTRIM(RTRIM(@ClassName)), @DisplayOrder);

    COMMIT TRANSACTION;
    SELECT CONVERT (INT, SCOPE_IDENTITY()) AS ClassId;
            RETURN;
        END
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.Classes
                   WHERE  ClassId = @ClassId)
        THROW 56215, 'The selected class was not found.', 1;
    IF @AcademicProgramId IS NOT NULL
       AND @AcademicProgramId <> ISNULL((SELECT C.AcademicProgramId
                                         FROM   dbo.Classes AS C
                                         WHERE  C.ClassId = @ClassId), 0)
       AND NOT EXISTS (SELECT 1
                       FROM   dbo.AcademicPrograms
                       WHERE  AcademicProgramId = @AcademicProgramId)
        THROW 56216, 'The selected academic program is invalid.', 1;
        IF @AcademicProgramId IS NOT NULL AND @AcademicProgramId <> (SELECT AcademicProgramId FROM dbo.Classes WHERE ClassId=@ClassId)
    BEGIN
        SELECT @DisplayOrder=ISNULL(MAX(DisplayOrder),0)+1 FROM dbo.Classes WITH (UPDLOCK,HOLDLOCK) WHERE AcademicProgramId=@AcademicProgramId;

    END
    UPDATE dbo.Classes
    SET    DisplayOrder = CASE WHEN @AcademicProgramId IS NOT NULL AND @AcademicProgramId<>AcademicProgramId THEN @DisplayOrder ELSE DisplayOrder END,
           AcademicProgramId = COALESCE (@AcademicProgramId, AcademicProgramId),
           ClassName         = LTRIM(RTRIM(@ClassName))
    WHERE  ClassId = @ClassId;

    COMMIT TRANSACTION;
    SELECT @ClassId AS ClassId;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
