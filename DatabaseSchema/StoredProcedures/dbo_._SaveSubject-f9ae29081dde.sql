-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.RequirePermission @UserId,N'System.Setup';
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @OrderLock int;
        EXEC @OrderLock=sys.sp_getapplock @Resource=N'SetupOrder:Subjects',
            @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
        IF @OrderLock<0 THROW 57401,'Unable to lock setup ordering. Please try again.',1;
        IF @SubjectId=0
        BEGIN
            DECLARE @NextOrder int;
            SELECT @NextOrder=ISNULL(MAX(DisplayOrder),0)+1 FROM dbo.Subjects WITH(UPDLOCK,HOLDLOCK);
            INSERT dbo.Subjects(SubjectCode,SubjectName,Description,DisplayOrder)
            VALUES(@SubjectCode,@SubjectName,@Description,@NextOrder);
            SET @SubjectId=CONVERT(int,SCOPE_IDENTITY());
        END
        ELSE
        BEGIN
            UPDATE dbo.Subjects SET SubjectCode=@SubjectCode,SubjectName=@SubjectName,
                Description=@Description,UpdatedAt=SYSDATETIME() WHERE SubjectId=@SubjectId;
            IF @@ROWCOUNT=0 THROW 56241,'The selected subject was not found.',1;
        END;
        COMMIT TRANSACTION;
        SELECT @SubjectId AS SubjectId;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
