-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    IF @FeePolicy NOT IN (N'NEXT_MONTH', N'CURRENT_MONTH', N'CUSTOM_MONTH')
        THROW 59300, 'Invalid fee synchronization policy.', 1;
    IF @Reason IS NULL
       OR LTRIM(RTRIM(@Reason)) = N''
        THROW 59301, 'A reason is required to synchronize fees after a transfer.', 1;
    DECLARE @AdmissionId AS INT, @CampusId AS INT, @ClassId AS INT;
    SELECT @AdmissionId = E.AdmissionId,
           @CampusId = CCS.CampusId,
           @ClassId = CCS.ClassId
    FROM   dbo.Enrollments AS E
           INNER JOIN
           dbo.CampusClassSections AS CCS
           ON CCS.ClassSectionId = E.ClassSectionId
    WHERE  E.EnrollmentId = @EnrollmentId
           AND E.Status = N'Active';
    IF @AdmissionId IS NULL
        THROW 59302, 'The selected enrollment does not exist or is not active.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.FeeConfigurations WITH (HOLDLOCK)
                   WHERE  CampusId = @CampusId
                          AND ClassId = @ClassId)
        THROW 59306, 'No destination fees are configured. Keep existing fees or complete Fee Setup first.', 1;
    IF EXISTS (SELECT 1
               FROM   dbo.StudentFeeAssignments AS SFA
                      INNER JOIN
                      dbo.FeeTypes AS FT
                      ON FT.FeeTypeId = SFA.FeeTypeId
               WHERE  SFA.EnrollmentId = @EnrollmentId
                      AND SFA.EffectiveTo IS NULL
                      AND SFA.CancelledAt IS NULL
                      AND FT.IsMandatory = 1
                      AND NOT EXISTS (SELECT 1
                                      FROM   dbo.FeeConfigurations AS FC WITH (HOLDLOCK)
                                      WHERE  FC.CampusId = @CampusId
                                             AND FC.ClassId = @ClassId
                                             AND FC.FeeTypeId = SFA.FeeTypeId))
        THROW 59307, 'Required destination fees are missing. Keep existing fees or complete Fee Setup first.', 1;
    DECLARE @Today AS DATE = COALESCE(@EffectiveDate,CAST(GETDATE() AS DATE));
    DECLARE @CurrentMonthStart AS DATE = DATEFROMPARTS(YEAR(@Today), MONTH(@Today), 1);
    DECLARE @TargetMonth AS DATE = CASE @FeePolicy WHEN N'CURRENT_MONTH' THEN @CurrentMonthStart WHEN N'CUSTOM_MONTH' THEN DATEFROMPARTS(YEAR(@FeeEffectiveMonth),MONTH(@FeeEffectiveMonth),1) ELSE DATEADD(MONTH, 1, @CurrentMonthStart) END;

    IF @TargetMonth IS NULL OR @TargetMonth<@CurrentMonthStart
        THROW 59327,'Choose an effective month on or after the transfer month.',1;
    DECLARE @InitialLockedRows int;
    SELECT @InitialLockedRows=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
    SELECT @InitialLockedRows=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK) WHERE EnrollmentId=@EnrollmentId;
    IF OBJECT_ID('tempdb..#FeeSyncPlan') IS NOT NULL
        DROP TABLE #FeeSyncPlan;
    CREATE TABLE #FeeSyncPlan (
        FeeTypeId             INT             NOT NULL,
        CurrentAssignmentId   INT             NULL,
        CurrentDiscountAmount DECIMAL (12, 2) NULL,
        CurrentRemarks        NVARCHAR (500)  NULL,
        DestAmount            DECIMAL (12, 2) NULL,
        DestFrequency         NVARCHAR (20)   NULL,
        PlanAction            NVARCHAR (20)   NOT NULL
    );
    WITH CurrentOpen
    AS   (SELECT StudentFeeAssignmentId,
                 FeeTypeId,
                 Amount,
                 Frequency,
                 DiscountAmount,
                 Remarks
          FROM   dbo.StudentFeeAssignments
          WHERE  EnrollmentId = @EnrollmentId
                 AND EffectiveTo IS NULL
                 AND CancelledAt IS NULL),
         DestConfig
    AS   (SELECT FC.FeeConfigurationId,
                 FC.FeeTypeId,
                 FC.Amount,
                 FC.Frequency
          FROM   dbo.FeeConfigurations AS FC
                 INNER JOIN
                 dbo.FeeTypes AS FT
                 ON FT.FeeTypeId = FC.FeeTypeId
          WHERE  FC.CampusId = @CampusId
                 AND FC.ClassId = @ClassId
                 AND FC.Frequency IN (N'Monthly', N'Yearly')
                 -- A transfer must not undo an explicit cancellation.
                 AND NOT EXISTS (SELECT 1 FROM dbo.StudentFeeAssignments CX
                                 WHERE CX.EnrollmentId=@EnrollmentId AND CX.FeeTypeId=FC.FeeTypeId
                                   AND CX.EffectiveTo IS NULL AND CX.CancelledAt IS NOT NULL)
                 AND (FT.IsMandatory = 1
                      OR (FT.IsTransportLinked = 1
                          AND EXISTS (SELECT 1
                                      FROM   CurrentOpen AS CO
                                      WHERE  CO.FeeTypeId = FC.FeeTypeId))))
    INSERT #FeeSyncPlan (FeeTypeId, CurrentAssignmentId, CurrentDiscountAmount, CurrentRemarks, DestAmount, DestFrequency, PlanAction)
    SELECT COALESCE (CO.FeeTypeId, DC.FeeTypeId),
           CO.StudentFeeAssignmentId,
           CO.DiscountAmount,
           CO.Remarks,
           DC.Amount,
           DC.Frequency,
           CASE WHEN CO.StudentFeeAssignmentId IS NOT NULL
                     AND DC.FeeConfigurationId IS NOT NULL
                     AND (CO.Amount <> DC.Amount
                          OR CO.Frequency <> DC.Frequency
                          OR (@ReplaceScheduledFees=1 AND EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments S
                              WHERE S.EnrollmentId=@EnrollmentId AND S.FeeTypeId=CO.FeeTypeId AND S.CancelledAt IS NULL
                              AND S.StudentFeeAssignmentId<>CO.StudentFeeAssignmentId AND ISNULL(S.EffectiveTo,'99991231')>=@TargetMonth))) THEN N'CHANGE' WHEN CO.StudentFeeAssignmentId IS NULL
                                                                               AND DC.FeeConfigurationId IS NOT NULL THEN N'ADD' WHEN CO.StudentFeeAssignmentId IS NOT NULL
                                                                                                                                      AND DC.FeeConfigurationId IS NULL THEN N'DISCONTINUE' ELSE N'NONE' END
    FROM   CurrentOpen AS CO
           FULL OUTER JOIN
           DestConfig AS DC
           ON DC.FeeTypeId = CO.FeeTypeId;

    -- TRANSFER_TIMELINE_GUARD: validate all versions and bills under the caller transaction.
    IF @@TRANCOUNT=0 THROW 59321,'Transfer fee changes require a transaction.',1;
    DECLARE @LockedRows int;
    SELECT @LockedRows=COUNT(*) FROM dbo.StudentFeeAssignments WITH(UPDLOCK,HOLDLOCK)
        WHERE EnrollmentId=@EnrollmentId;
    SELECT @LockedRows=COUNT(*) FROM dbo.BillingCharges WITH(UPDLOCK,HOLDLOCK)
        WHERE EnrollmentId=@EnrollmentId;
    IF EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments
        WHERE EnrollmentId=@EnrollmentId AND CancelledAt IS NULL
          AND EffectiveFrom>EffectiveTo)
        THROW 59322,'An existing fee period ends before it starts. Review Fee Changes before applying transfer fees. You can keep current fees.',1;
    IF EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments A JOIN dbo.StudentFeeAssignments B
        ON A.EnrollmentId=B.EnrollmentId AND A.FeeTypeId=B.FeeTypeId
        AND A.StudentFeeAssignmentId<B.StudentFeeAssignmentId
        WHERE A.EnrollmentId=@EnrollmentId AND A.CancelledAt IS NULL AND B.CancelledAt IS NULL
        AND ISNULL(A.EffectiveFrom,'19000101')<=ISNULL(B.EffectiveTo,'99991231')
        AND ISNULL(B.EffectiveFrom,'19000101')<=ISNULL(A.EffectiveTo,'99991231'))
        THROW 59323,'Existing fee periods overlap. Review Fee Changes before applying transfer fees. You can keep current fees.',1;
    IF EXISTS(SELECT 1 FROM #FeeSyncPlan P JOIN dbo.BillingCharges BC
        ON BC.EnrollmentId=@EnrollmentId AND BC.FeeTypeId=P.FeeTypeId
        WHERE P.PlanAction<>N'NONE' AND BC.BillingMonth>=@TargetMonth)
        THROW 59324,'A changed fee is already billed for this month or later. Keep current fees and choose a later effective month in Fee Changes.',1;
    IF EXISTS(SELECT 1 FROM #FeeSyncPlan P JOIN dbo.StudentFeeAssignments A
        ON A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=P.FeeTypeId
        WHERE @ReplaceScheduledFees=0 AND P.PlanAction<>N'NONE' AND A.CancelledAt IS NULL
        AND ((A.StudentFeeAssignmentId=P.CurrentAssignmentId AND A.EffectiveFrom>=@TargetMonth)
          OR (A.StudentFeeAssignmentId<>ISNULL(P.CurrentAssignmentId,-1)
            AND ISNULL(A.EffectiveTo,'99991231')>=@TargetMonth)))
        THROW 59325,'A changed fee has a scheduled rate or waiver. Keep current fees, then explicitly review that schedule in Fee Changes.',1;
    IF EXISTS(SELECT 1 FROM #FeeSyncPlan WHERE PlanAction=N'CHANGE'
        AND CurrentDiscountAmount>DestAmount)
        THROW 59326,'The existing discount exceeds the new fee. Keep current fees and review the discount explicitly.',1;
    IF NOT EXISTS (SELECT 1
                   FROM   #FeeSyncPlan
                   WHERE  PlanAction <> N'NONE')
        RETURN;
    DECLARE @PlanFeeTypeId AS INT, @PlanAssignmentId AS INT, @PlanDiscount AS DECIMAL (12, 2), @PlanRemarks AS NVARCHAR (500), @PlanDestAmount AS DECIMAL (12, 2), @PlanDestFrequency AS NVARCHAR (20), @PlanAction AS NVARCHAR (20), @PlanFeeTypeName AS NVARCHAR (100);
    DECLARE @LastBilledMonth AS DATE, @OperationId AS UNIQUEIDENTIFIER, @NewAssignmentId AS INT, @OldValues AS NVARCHAR (MAX), @NewValues AS NVARCHAR (MAX), @DiscontinueEffectiveTo AS DATE;
    DECLARE plan_cursor CURSOR LOCAL FAST_FORWARD
        FOR SELECT P.FeeTypeId,
                   P.CurrentAssignmentId,
                   P.CurrentDiscountAmount,
                   P.CurrentRemarks,
                   P.DestAmount,
                   P.DestFrequency,
                   P.PlanAction,
                   FT.FeeTypeName
            FROM   #FeeSyncPlan AS P
                   INNER JOIN
                   dbo.FeeTypes AS FT
                   ON FT.FeeTypeId = P.FeeTypeId
            WHERE  P.PlanAction <> N'NONE';
    OPEN plan_cursor;
    FETCH NEXT FROM plan_cursor INTO @PlanFeeTypeId, @PlanAssignmentId, @PlanDiscount, @PlanRemarks, @PlanDestAmount, @PlanDestFrequency, @PlanAction, @PlanFeeTypeName;
    WHILE @@FETCH_STATUS = 0
        BEGIN

                    IF @ReplaceScheduledFees=1 AND @PlanAction IN(N'CHANGE',N'ADD',N'DISCONTINUE')
                    BEGIN
                        DECLARE @Replaced TABLE(StudentFeeAssignmentId int,OldFrom date,OldTo date,NewTo date,CancelledAt datetime2);
                        UPDATE A SET EffectiveTo=CASE WHEN A.EffectiveFrom IS NULL OR A.EffectiveFrom<@TargetMonth
                                THEN DATEADD(MONTH,-1,@TargetMonth) ELSE COALESCE(A.EffectiveTo,A.EffectiveFrom) END,
                            CancelledAt=CASE WHEN A.EffectiveFrom>=@TargetMonth THEN SYSDATETIME() ELSE A.CancelledAt END,
                            UpdatedAt=SYSDATETIME()
                        OUTPUT inserted.StudentFeeAssignmentId,deleted.EffectiveFrom,deleted.EffectiveTo,inserted.EffectiveTo,inserted.CancelledAt INTO @Replaced
                        FROM dbo.StudentFeeAssignments A
                        WHERE A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=@PlanFeeTypeId AND A.CancelledAt IS NULL
                            AND ISNULL(A.EffectiveTo,'99991231')>=@TargetMonth;
                        DECLARE @ScheduleOperation uniqueidentifier=NEWID(),@ScheduleValues nvarchar(max);
                        SET @ScheduleValues=(SELECT @EnrollmentId EnrollmentId,@PlanFeeTypeId FeeTypeId,
                            @TargetMonth EffectiveFrom,@Reason Reason,JSON_QUERY((SELECT * FROM @Replaced FOR JSON PATH)) ReplacedPeriods
                            FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
                        INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote)
                        VALUES(@ScheduleOperation,@UserId,N'FEE_SYNC_TRANSFER_SCHEDULE',CONVERT(nvarchar(200),@EnrollmentId));
                        EXEC dbo.WriteAuditLog @OperationId=@ScheduleOperation,@UserId=@UserId,@TableName=N'StudentFeeAssignments',
                            @RecordId=@PlanAssignmentId,@Action=N'UPDATE',@OldValues=NULL,@NewValues=@ScheduleValues;
                        DELETE FROM @Replaced;
                    END;
            IF @PlanAction = N'CHANGE'
                BEGIN
                    SELECT @LastBilledMonth = MAX(BillingMonth)
                    FROM   dbo.BillingCharges
                    WHERE  StudentFeeAssignmentId = @PlanAssignmentId;
                    IF @LastBilledMonth IS NOT NULL
                       AND @TargetMonth <= @LastBilledMonth
                        BEGIN
                            CLOSE plan_cursor;
                            DEALLOCATE plan_cursor;
                            THROW 59304, 'The destination fees could not be applied from the selected month because that month has already been billed for one or more fees. Choose "next month" instead, or keep the existing fees, then adjust manually in Fee Setup if needed.', 1;
                        END
                    UPDATE dbo.StudentFeeAssignments
                    SET    EffectiveTo = DATEADD(MONTH, -1, @TargetMonth),
                           UpdatedAt   = SYSDATETIME()
                    WHERE  StudentFeeAssignmentId = @PlanAssignmentId
                           AND EffectiveTo IS NULL AND CancelledAt IS NULL
                           AND (EffectiveFrom IS NULL OR EffectiveFrom<@TargetMonth);
                    INSERT  dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo)
                    VALUES                           (@EnrollmentId, @AdmissionId, @PlanFeeTypeId, @PlanDestAmount, @PlanDiscount, @PlanDestFrequency, @PlanRemarks, @TargetMonth, NULL);
                    SET @NewAssignmentId = CONVERT (INT, SCOPE_IDENTITY());
                    SET @OperationId = NEWID();
                    INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
                    VALUES                     (@OperationId, @UserId, N'FEE_SYNC_TRANSFER', CONVERT (NVARCHAR (200), @PlanAssignmentId) + N' -> ' + CONVERT (NVARCHAR (200), @NewAssignmentId));
                    SET @OldValues = (SELECT @PlanAssignmentId AS StudentFeeAssignmentId,
                                             @PlanFeeTypeName AS FeeTypeName
                                      FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
                    SET @NewValues = (SELECT @NewAssignmentId AS StudentFeeAssignmentId,
                                             @PlanFeeTypeName AS FeeTypeName,
                                             @PlanDestAmount AS Amount,
                                             @TargetMonth AS EffectiveFrom,
                                             @Reason AS Reason,
                                             @EnrollmentId AS EnrollmentId,
                                             @PlanAssignmentId AS PreviousVersionId
                                      FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
                    EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'StudentFeeAssignments', @RecordId = @PlanAssignmentId, @Action = N'UPDATE', @OldValues = @OldValues, @NewValues = @NewValues;
                END
            ELSE
                IF @PlanAction = N'ADD'
                    BEGIN
                        INSERT  dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo)
                        VALUES                           (@EnrollmentId, @AdmissionId, @PlanFeeTypeId, @PlanDestAmount, 0, @PlanDestFrequency, N'Added via transfer fee sync', @TargetMonth, NULL);
                        SET @NewAssignmentId = CONVERT (INT, SCOPE_IDENTITY());
                        SET @OperationId = NEWID();
                        INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
                        VALUES                     (@OperationId, @UserId, N'FEE_SYNC_TRANSFER', CONVERT (NVARCHAR (200), @NewAssignmentId));
                        SET @NewValues = (SELECT @NewAssignmentId AS StudentFeeAssignmentId,
                                                 @PlanFeeTypeName AS FeeTypeName,
                                                 @PlanDestAmount AS Amount,
                                                 @TargetMonth AS EffectiveFrom,
                                                 @Reason AS Reason,
                                                 @EnrollmentId AS EnrollmentId
                                          FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
                        EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'StudentFeeAssignments', @RecordId = @NewAssignmentId, @Action = N'INSERT', @OldValues = NULL, @NewValues = @NewValues;
                    END
                ELSE
                    IF @PlanAction = N'DISCONTINUE'
                        BEGIN
                            SET @DiscontinueEffectiveTo = DATEADD(MONTH, -1, @TargetMonth);
                            SELECT @LastBilledMonth = MAX(BillingMonth)
                            FROM   dbo.BillingCharges
                            WHERE  StudentFeeAssignmentId = @PlanAssignmentId;
                            IF @LastBilledMonth IS NOT NULL
                               AND @DiscontinueEffectiveTo < @LastBilledMonth
                                BEGIN
                                    CLOSE plan_cursor;
                                    DEALLOCATE plan_cursor;
                                    THROW 59305, 'One or more fees that no longer apply at the destination could not be closed because a later month has already been billed for them. Please review those fees manually in Fee Setup.', 1;
                                END
                            UPDATE dbo.StudentFeeAssignments
                            SET    EffectiveTo = @DiscontinueEffectiveTo,
                                   UpdatedAt   = SYSDATETIME()
                            WHERE  StudentFeeAssignmentId = @PlanAssignmentId
                                   AND EffectiveTo IS NULL AND CancelledAt IS NULL
                           AND (EffectiveFrom IS NULL OR EffectiveFrom<@TargetMonth);
                            SET @OperationId = NEWID();
                            INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
                            VALUES                     (@OperationId, @UserId, N'FEE_SYNC_TRANSFER', CONVERT (NVARCHAR (200), @PlanAssignmentId));
                            SET @NewValues = (SELECT @PlanAssignmentId AS StudentFeeAssignmentId,
                                                     @PlanFeeTypeName AS FeeTypeName,
                                                     @DiscontinueEffectiveTo AS EffectiveTo,
                                                     @Reason AS Reason,
                                                     @EnrollmentId AS EnrollmentId
                                              FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
                            EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'StudentFeeAssignments', @RecordId = @PlanAssignmentId, @Action = N'UPDATE', @OldValues = NULL, @NewValues = @NewValues;
                        END
            FETCH NEXT FROM plan_cursor INTO @PlanFeeTypeId, @PlanAssignmentId, @PlanDiscount, @PlanRemarks, @PlanDestAmount, @PlanDestFrequency, @PlanAction, @PlanFeeTypeName;
        END
    CLOSE plan_cursor;
    DEALLOCATE plan_cursor;
DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
END
