-- Validate the complete fee timeline before a transfer changes assignments.
-- Existing scheduled decisions are never silently replaced.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @sql nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.SyncTransferStudentFees_Internal'));
    IF @sql IS NULL THROW 59320,'Transfer fee procedure is missing.',1;
    IF CHARINDEX(N'TRANSFER_TIMELINE_GUARD',@sql)=0
    BEGIN
        SET @sql=REPLACE(@sql,N'@Reason NVARCHAR (500)',N'@Reason NVARCHAR (500), @EffectiveDate DATE=NULL, @FeeEffectiveMonth DATE=NULL, @ReplaceScheduledFees BIT=0');
        SET @sql=REPLACE(@sql,N'(N''NEXT_MONTH'', N''CURRENT_MONTH'')',N'(N''NEXT_MONTH'', N''CURRENT_MONTH'', N''CUSTOM_MONTH'')');
        SET @sql=REPLACE(@sql,N'CAST (GETDATE() AS DATE)',N'COALESCE(@EffectiveDate,CAST(GETDATE() AS DATE))');
        SET @sql=REPLACE(@sql,N'CASE @FeePolicy WHEN N''CURRENT_MONTH'' THEN @CurrentMonthStart ELSE DATEADD(MONTH, 1, @CurrentMonthStart) END',
            N'CASE @FeePolicy WHEN N''CURRENT_MONTH'' THEN @CurrentMonthStart WHEN N''CUSTOM_MONTH'' THEN DATEFROMPARTS(YEAR(@FeeEffectiveMonth),MONTH(@FeeEffectiveMonth),1) ELSE DATEADD(MONTH, 1, @CurrentMonthStart) END');
        SET @sql=REPLACE(@sql,N'    IF OBJECT_ID(''tempdb..#FeeSyncPlan'')',N'
    IF @TargetMonth IS NULL OR @TargetMonth<@CurrentMonthStart
        THROW 59327,''Choose an effective month on or after the transfer month.'',1;
    DECLARE @InitialLockedRows int;
    SELECT @InitialLockedRows=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
    SELECT @InitialLockedRows=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
    IF OBJECT_ID(''tempdb..#FeeSyncPlan'')');
        SET @sql=REPLACE(@sql,N'OR CO.Frequency <> DC.Frequency)',N'OR CO.Frequency <> DC.Frequency
                          OR (@ReplaceScheduledFees=1 AND EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments S
                              WHERE S.EnrollmentId=@EnrollmentId AND S.FeeTypeId=CO.FeeTypeId AND S.CancelledAt IS NULL
                              AND S.StudentFeeAssignmentId<>CO.StudentFeeAssignmentId AND ISNULL(S.EffectiveTo,''99991231'')>=@TargetMonth)))');
        DECLARE @marker nvarchar(200)=N'    IF NOT EXISTS (SELECT 1'+CHAR(13)+CHAR(10)+N'                   FROM   #FeeSyncPlan';
        DECLARE @position int=CHARINDEX(@marker,@sql);
        IF @position=0
        BEGIN
            SET @marker=REPLACE(@marker,CHAR(13)+CHAR(10),CHAR(10));
            SET @position=CHARINDEX(@marker,@sql);
        END;
        IF @position=0 THROW 59320,'Transfer fee procedure structure has changed.',1;
        DECLARE @guard nvarchar(max)=N'
    -- TRANSFER_TIMELINE_GUARD: validate all versions and bills under the caller transaction.
    IF @@TRANCOUNT=0 THROW 59321,''Transfer fee changes require a transaction.'',1;
    DECLARE @LockedRows int;
    SELECT @LockedRows=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK)
        WHERE EnrollmentId=@EnrollmentId;
    SELECT @LockedRows=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK)
        WHERE EnrollmentId=@EnrollmentId;
    IF EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments
        WHERE EnrollmentId=@EnrollmentId AND CancelledAt IS NULL
          AND EffectiveFrom>EffectiveTo)
        THROW 59322,''An existing fee period ends before it starts. Review Fee Changes before applying transfer fees. You can keep current fees.'',1;
    IF EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments A JOIN dbo.StudentFeeAssignments B
        ON A.EnrollmentId=B.EnrollmentId AND A.FeeTypeId=B.FeeTypeId
        AND A.StudentFeeAssignmentId<B.StudentFeeAssignmentId
        WHERE A.EnrollmentId=@EnrollmentId AND A.CancelledAt IS NULL AND B.CancelledAt IS NULL
        AND ISNULL(A.EffectiveFrom,''19000101'')<=ISNULL(B.EffectiveTo,''99991231'')
        AND ISNULL(B.EffectiveFrom,''19000101'')<=ISNULL(A.EffectiveTo,''99991231''))
        THROW 59323,''Existing fee periods overlap. Review Fee Changes before applying transfer fees. You can keep current fees.'',1;
    IF EXISTS(SELECT 1 FROM #FeeSyncPlan P JOIN dbo.BillingCharges BC
        ON BC.EnrollmentId=@EnrollmentId AND BC.FeeTypeId=P.FeeTypeId
        WHERE P.PlanAction<>N''NONE'' AND BC.BillingMonth>=@TargetMonth)
        THROW 59324,''A changed fee is already billed for this month or later. Keep current fees and choose a later effective month in Fee Changes.'',1;
    IF EXISTS(SELECT 1 FROM #FeeSyncPlan P JOIN dbo.StudentFeeAssignments A
        ON A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=P.FeeTypeId
        WHERE @ReplaceScheduledFees=0 AND P.PlanAction<>N''NONE'' AND A.CancelledAt IS NULL
        AND ((A.StudentFeeAssignmentId=P.CurrentAssignmentId AND A.EffectiveFrom>=@TargetMonth)
          OR (A.StudentFeeAssignmentId<>ISNULL(P.CurrentAssignmentId,-1)
            AND ISNULL(A.EffectiveTo,''99991231'')>=@TargetMonth)))
        THROW 59325,''A changed fee has a scheduled rate or waiver. Keep current fees, then explicitly review that schedule in Fee Changes.'',1;
    IF EXISTS(SELECT 1 FROM #FeeSyncPlan WHERE PlanAction=N''CHANGE''
        AND CurrentDiscountAmount>DestAmount)
        THROW 59326,''The existing discount exceeds the new fee. Keep current fees and review the discount explicitly.'',1;
';
        SET @sql=STUFF(@sql,@position,0,@guard);
        DECLARE @updateMarker nvarchar(200)=N'            IF @PlanAction = N''CHANGE''';
        DECLARE @updatePosition int=CHARINDEX(@updateMarker,@sql);
        IF @updatePosition=0 THROW 59320,'Transfer update structure has changed.',1;
        DECLARE @replaceSchedule nvarchar(max)=N'
                    IF @ReplaceScheduledFees=1 AND @PlanAction IN(N''CHANGE'',N''ADD'',N''DISCONTINUE'')
                    BEGIN
                        DECLARE @Replaced TABLE(StudentFeeAssignmentId int,OldFrom date,OldTo date,NewTo date,CancelledAt datetime2);
                        UPDATE A SET EffectiveTo=CASE WHEN A.EffectiveFrom IS NULL OR A.EffectiveFrom<@TargetMonth
                                THEN DATEADD(MONTH,-1,@TargetMonth) ELSE COALESCE(A.EffectiveTo,A.EffectiveFrom) END,
                            CancelledAt=CASE WHEN A.EffectiveFrom>=@TargetMonth THEN SYSDATETIME() ELSE A.CancelledAt END,
                            UpdatedAt=SYSDATETIME()
                        OUTPUT inserted.StudentFeeAssignmentId,deleted.EffectiveFrom,deleted.EffectiveTo,inserted.EffectiveTo,inserted.CancelledAt INTO @Replaced
                        FROM dbo.StudentFeeAssignments A
                        WHERE A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=@PlanFeeTypeId AND A.CancelledAt IS NULL
                            AND ISNULL(A.EffectiveTo,''99991231'')>=@TargetMonth;
                        DECLARE @ScheduleOperation uniqueidentifier=NEWID(),@ScheduleValues nvarchar(max);
                        SET @ScheduleValues=(SELECT @EnrollmentId EnrollmentId,@PlanFeeTypeId FeeTypeId,
                            @TargetMonth EffectiveFrom,@Reason Reason,JSON_QUERY((SELECT * FROM @Replaced FOR JSON PATH)) ReplacedPeriods
                            FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
                        INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote)
                        VALUES(@ScheduleOperation,@UserId,N''FEE_SYNC_TRANSFER_SCHEDULE'',CONVERT(nvarchar(200),@EnrollmentId));
                        EXEC dbo.WriteAuditLog @OperationId=@ScheduleOperation,@UserId=@UserId,@TableName=N''StudentFeeAssignments'',
                            @RecordId=@PlanAssignmentId,@Action=N''UPDATE'',@OldValues=NULL,@NewValues=@ScheduleValues;
                        DELETE FROM @Replaced;
                    END;
';
        SET @sql=STUFF(@sql,@updatePosition,0,@replaceSchedule);
        SET @sql=REPLACE(@sql,N'AND EffectiveTo IS NULL;',N'AND EffectiveTo IS NULL AND CancelledAt IS NULL
                           AND (EffectiveFrom IS NULL OR EffectiveFrom<@TargetMonth);');
        SET @sql=REPLACE(@sql,N'CREATE   PROCEDURE',N'ALTER PROCEDURE');
        SET @sql=REPLACE(@sql,N'CREATE PROCEDURE',N'ALTER PROCEDURE');
        EXEC sys.sp_executesql @sql;
        DECLARE @name sysname;
        DECLARE procedures_cursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT name FROM sys.procedures WHERE name IN(N'TransferEnrollmentClass',N'TransferEnrollmentCampus');
        OPEN procedures_cursor;
        FETCH NEXT FROM procedures_cursor INTO @name;
        WHILE @@FETCH_STATUS=0
        BEGIN
            SET @sql=OBJECT_DEFINITION(OBJECT_ID(N'dbo.'+@name));
            IF CHARINDEX(N'@Reason = @FeeSyncReason;',@sql)=0
                THROW 59320,'Transfer caller structure has changed.',1;
            SET @sql=REPLACE(@sql,N'@FeeSyncReason NVARCHAR (500)=NULL',N'@FeeSyncReason NVARCHAR (500)=NULL, @FeeEffectiveMonth DATE=NULL, @ReplaceScheduledFees BIT=0');
            SET @sql=REPLACE(@sql,N'@Reason = @FeeSyncReason;',N'@Reason = @FeeSyncReason, @EffectiveDate = @TransferDate, @FeeEffectiveMonth=@FeeEffectiveMonth, @ReplaceScheduledFees=@ReplaceScheduledFees;');
            SET @sql=REPLACE(@sql,N'CREATE   PROCEDURE',N'ALTER PROCEDURE');
            SET @sql=REPLACE(@sql,N'CREATE PROCEDURE',N'ALTER PROCEDURE');
            EXEC sys.sp_executesql @sql;
            FETCH NEXT FROM procedures_cursor INTO @name;
        END;
        CLOSE procedures_cursor;
        DEALLOCATE procedures_cursor;
    END;
    IF NOT EXISTS(SELECT 1 FROM dbo.DatabaseVersion WHERE VersionNumber=N'5')
        INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
        VALUES(N'5',SYSDATETIME(),SUSER_SNAME(),N'Guard transfer fees against scheduled periods, all billed versions and invalid timelines.');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
