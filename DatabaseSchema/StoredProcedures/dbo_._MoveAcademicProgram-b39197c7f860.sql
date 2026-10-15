-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.RequirePermission @UserId, N'System.Setup';
    IF @Direction NOT IN (-1,1) THROW 57402, 'Invalid move direction.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Lock int;
        EXEC @Lock=sys.sp_getapplock @Resource=N'SetupOrder:AcademicPrograms',
            @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
        IF @Lock<0 THROW 57401, 'Unable to lock setup ordering. Please try again.', 1;
        DECLARE @Items TABLE(Id int PRIMARY KEY, Position int);
        INSERT @Items SELECT AcademicProgramId,
            ROW_NUMBER() OVER(ORDER BY DisplayOrder,AcademicProgramId)
            FROM dbo.AcademicPrograms WITH(UPDLOCK,HOLDLOCK);
        DECLARE @Position int=(SELECT Position FROM @Items WHERE Id=@AcademicProgramId);
        IF @Position IS NULL THROW 57403, 'The selected program was not found.', 1;
        IF EXISTS(SELECT 1 FROM @Items WHERE Position=@Position+@Direction)
            UPDATE @Items SET Position=CASE WHEN Id=@AcademicProgramId
                THEN @Position+@Direction ELSE @Position END
                WHERE Position IN(@Position,@Position+@Direction);
        UPDATE P SET DisplayOrder=-I.Position FROM dbo.AcademicPrograms P JOIN @Items I ON I.Id=P.AcademicProgramId;
        UPDATE dbo.AcademicPrograms SET DisplayOrder=-DisplayOrder;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
