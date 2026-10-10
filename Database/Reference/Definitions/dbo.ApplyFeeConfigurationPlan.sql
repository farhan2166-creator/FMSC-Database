SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- ApplyFeeConfigurationPlan
CREATE   PROCEDURE dbo.ApplyFeeConfigurationPlan
@UserId INT, @Plan dbo.FeeConfigurationPlanType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Configuration.Manage';
    DECLARE @Total AS INT = (SELECT COUNT(*)
                             FROM   @Plan);
    IF @Total = 0
        THROW 53160, 'There is nothing to apply - the plan is empty.', 1;
    DECLARE @Rows TABLE (
        RowNum             INT             IDENTITY (1, 1) PRIMARY KEY,
        FeeConfigurationId INT             NOT NULL,
        CampusId           INT             NOT NULL,
        ClassId            INT             NOT NULL,
        FeeTypeId          INT             NOT NULL,
        Amount             DECIMAL (12, 2) NOT NULL,
        Frequency          NVARCHAR (30)   NULL,
        Remarks            NVARCHAR (500)  NULL);
    INSERT INTO @Rows (FeeConfigurationId, CampusId, ClassId, FeeTypeId, Amount, Frequency, Remarks)
    SELECT FeeConfigurationId,
           CampusId,
           ClassId,
           FeeTypeId,
           Amount,
           Frequency,
           Remarks
    FROM   @Plan;
    DECLARE @RowNum AS INT;
    DECLARE @Message AS NVARCHAR (2048);
    IF EXISTS (SELECT 1
               FROM   @Rows
               WHERE  Amount < 0)
        BEGIN
            SELECT @RowNum = MIN(RowNum)
            FROM   @Rows
            WHERE  Amount < 0;
            SET @Message = N'Fee amount cannot be negative (plan row ' + CONVERT (NVARCHAR (10), @RowNum) + N').';
            THROW 53161, @Message, 1;
        END
    IF EXISTS (SELECT 1
               FROM   @Rows AS r
               WHERE  NOT EXISTS (SELECT 1
                                  FROM   dbo.Campuses AS c
                                  WHERE  c.CampusId = r.CampusId))
        BEGIN
            SELECT @RowNum = MIN(r.RowNum)
            FROM   @Rows AS r
            WHERE  NOT EXISTS (SELECT 1
                               FROM   dbo.Campuses AS c
                               WHERE  c.CampusId = r.CampusId);
            SET @Message = N'Plan row ' + CONVERT (NVARCHAR (10), @RowNum) + N' names a campus that is not active. Nothing was applied.';
            THROW 53162, @Message, 1;
        END
    IF EXISTS (SELECT 1
               FROM   @Rows AS r
               WHERE  NOT EXISTS (SELECT 1
                                  FROM   dbo.Classes AS c
                                  WHERE  c.ClassId = r.ClassId))
        BEGIN
            SELECT @RowNum = MIN(r.RowNum)
            FROM   @Rows AS r
            WHERE  NOT EXISTS (SELECT 1
                               FROM   dbo.Classes AS c
                               WHERE  c.ClassId = r.ClassId);
            SET @Message = N'Plan row ' + CONVERT (NVARCHAR (10), @RowNum) + N' names a class that is not active. Nothing was applied.';
            THROW 53163, @Message, 1;
        END
    IF EXISTS (SELECT 1
               FROM   @Rows AS r
               WHERE  NOT EXISTS (SELECT 1
                                  FROM   dbo.FeeTypes AS f
                                  WHERE  f.FeeTypeId = r.FeeTypeId))
        BEGIN
            SELECT @RowNum = MIN(r.RowNum)
            FROM   @Rows AS r
            WHERE  NOT EXISTS (SELECT 1
                               FROM   dbo.FeeTypes AS f
                               WHERE  f.FeeTypeId = r.FeeTypeId);
            SET @Message = N'Plan row ' + CONVERT (NVARCHAR (10), @RowNum) + N' names a fee type that is not active. Nothing was applied.';
            THROW 53164, @Message, 1;
        END
    IF EXISTS (SELECT 1
               FROM   @Rows AS r
               WHERE  r.FeeConfigurationId <> 0
                      AND NOT EXISTS (SELECT 1
                                      FROM   dbo.FeeConfigurations AS fc
                                      WHERE  fc.FeeConfigurationId = r.FeeConfigurationId))
        BEGIN
            SELECT @RowNum = MIN(r.RowNum)
            FROM   @Rows AS r
            WHERE  r.FeeConfigurationId <> 0
                   AND NOT EXISTS (SELECT 1
                                   FROM   dbo.FeeConfigurations AS fc
                                   WHERE  fc.FeeConfigurationId = r.FeeConfigurationId);
            SET @Message = N'Plan row ' + CONVERT (NVARCHAR (10), @RowNum) + N' updates a fee configuration that no longer exists. ' + N'Nothing was applied - re-run the preview.';
            THROW 53165, @Message, 1;
        END
    IF EXISTS (SELECT   CampusId,
                        ClassId,
                        FeeTypeId
               FROM     @Rows
               GROUP BY CampusId, ClassId, FeeTypeId
               HAVING   COUNT(*) > 1)
        BEGIN
            SELECT @RowNum = MIN(r.RowNum)
            FROM   @Rows AS r
            WHERE  EXISTS (SELECT 1
                           FROM   @Rows AS other
                           WHERE  other.CampusId = r.CampusId
                                  AND other.ClassId = r.ClassId
                                  AND other.FeeTypeId = r.FeeTypeId
                                  AND other.RowNum <> r.RowNum);
            SET @Message = N'The plan contains more than one row for the same campus, ' + N'class and fee type (first at row ' + CONVERT (NVARCHAR (10), @RowNum) + N'). Nothing was applied - re-run the preview.';
            THROW 53166, @Message, 1;
        END
    DECLARE @Succeeded AS INT = 0;
    DECLARE @i AS INT = 1;
    DECLARE @ConfigId AS INT;
    DECLARE @CampusId AS INT;
    DECLARE @ClassId AS INT;
    DECLARE @FeeTypeId AS INT;
    DECLARE @Amount AS DECIMAL (12, 2);
    DECLARE @Frequency AS NVARCHAR (30);
    DECLARE @Remarks AS NVARCHAR (500);
    BEGIN TRY
        BEGIN TRANSACTION;
        WHILE @i <= @Total
            BEGIN
                SELECT @ConfigId = FeeConfigurationId,
                       @CampusId = CampusId,
                       @ClassId = ClassId,
                       @FeeTypeId = FeeTypeId,
                       @Amount = Amount,
                       @Frequency = Frequency,
                       @Remarks = Remarks
                FROM   @Rows
                WHERE  RowNum = @i;
                EXECUTE dbo.SaveFeeConfiguration @UserId = @UserId, @CampusId = @CampusId, @ClassId = @ClassId, @FeeTypeId = @FeeTypeId, @Amount = @Amount, @Frequency = @Frequency, @FeeConfigurationId = @ConfigId, @Remarks = @Remarks;
                SET @Succeeded = @Succeeded + 1;
                SET @i = @i + 1;
            END
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK;
        SET @Message = N'Nothing was applied - the whole plan was rolled back. Plan row ' + CONVERT (NVARCHAR (10), @i) + N' failed: ' + ERROR_MESSAGE();
        THROW 53170, @Message, 1;
    END CATCH
    SELECT @Total AS TotalRows,
           @Succeeded AS Succeeded,
           (@Total - @Succeeded) AS Failed,
           CAST (1 AS BIT) AS AppliedAtomically;
END



GO
