-- Same-year class/campus transfer changes tuition and existing transport only.
-- Admission, annual, purchase and other fee agreements and charges are preserved.
SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT=0 THROW 51000,'Migration 44 requires an outer transaction.',1;
IF ISNULL((SELECT TOP(1) TRY_CONVERT(int,VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC),-1)<>43
 THROW 51000,'Migration 44 requires schema 43.',1;
GO
CREATE OR ALTER PROCEDURE dbo.SyncTransferStudentFees_Internal
@UserId INT, @EnrollmentId INT, @FeePolicy NVARCHAR (20), @Reason NVARCHAR (500), @EffectiveDate DATE=NULL, @FeeEffectiveMonth DATE=NULL, @ReplaceScheduledFees BIT=0,@PaidReductionAction nvarchar(10)=N'Credit'
AS
BEGIN
    SET NOCOUNT ON;
    IF @FeePolicy NOT IN (N'NEXT_MONTH', N'CURRENT_MONTH', N'CUSTOM_MONTH',N'SCHOOL_CUTOFF')
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
    -- Same-year transfer fees are limited to tuition and transport already in use.
    DECLARE @TransferFees TABLE(FeeTypeId int NOT NULL PRIMARY KEY);
    INSERT @TransferFees SELECT FT.FeeTypeId FROM dbo.FeeTypes FT
    WHERE FT.FeeTypeCode=N'TUITION' OR (FT.IsTransportLinked=1 AND FT.FeeTypeCode<>N'ADMISSION'
      AND EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments A WHERE A.EnrollmentId=@EnrollmentId
        AND A.FeeTypeId=FT.FeeTypeId AND A.Frequency=N'Monthly' AND A.CancelledAt IS NULL AND A.EffectiveTo IS NULL));
    IF NOT EXISTS (SELECT 1
                   FROM   dbo.FeeConfigurations WITH (HOLDLOCK)
                   WHERE  CampusId = @CampusId
                          AND ClassId = @ClassId
                          AND Frequency=N'Monthly' AND FeeTypeId IN(SELECT FeeTypeId FROM @TransferFees))
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
                      AND SFA.Frequency=N'Monthly' AND SFA.FeeTypeId IN(SELECT FeeTypeId FROM @TransferFees)
                      AND NOT EXISTS (SELECT 1
                                      FROM   dbo.FeeConfigurations AS FC WITH (HOLDLOCK)
                                      WHERE  FC.CampusId = @CampusId
                                             AND FC.ClassId = @ClassId
                                             AND FC.FeeTypeId = SFA.FeeTypeId AND FC.Frequency=N'Monthly'))
        THROW 59307, 'Required destination fees are missing. Keep existing fees or complete Fee Setup first.', 1;
    DECLARE @Today AS DATE = COALESCE(@EffectiveDate,CAST(GETDATE() AS DATE));
    DECLARE @CurrentMonthStart AS DATE = DATEFROMPARTS(YEAR(@Today), MONTH(@Today), 1);
    DECLARE @TargetMonth AS DATE = CASE @FeePolicy WHEN N'CURRENT_MONTH' THEN @CurrentMonthStart WHEN N'CUSTOM_MONTH' THEN DATEFROMPARTS(YEAR(@FeeEffectiveMonth),MONTH(@FeeEffectiveMonth),1) ELSE DATEADD(MONTH, 1, @CurrentMonthStart) END;

    DECLARE @CutoffDate date=dbo.GetMonthlyBillingStartDate(@Today);
    DECLARE @CutoffMonth date=DATEFROMPARTS(YEAR(@CutoffDate),MONTH(@CutoffDate),1);
    IF @FeePolicy=N'SCHOOL_CUTOFF' SET @TargetMonth=@CutoffMonth;
    IF @TargetMonth<@CutoffMonth THROW 59327,'The selected month is before the School Profile cutoff permits the new class rate.',1;
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
                 AND CancelledAt IS NULL
                 AND Frequency=N'Monthly' AND FeeTypeId IN(SELECT FeeTypeId FROM @TransferFees)),
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
                 AND FC.Frequency=N'Monthly' AND FC.FeeTypeId IN(SELECT FeeTypeId FROM @TransferFees)
                 -- A transfer must not undo an explicit cancellation.
                 AND NOT EXISTS (SELECT 1 FROM dbo.StudentFeeAssignments CX
                                 WHERE CX.EnrollmentId=@EnrollmentId AND CX.FeeTypeId=FC.FeeTypeId
                                   AND CX.EffectiveTo IS NULL AND CX.CancelledAt IS NOT NULL)
                 AND (FT.FeeTypeCode=N'TUITION'
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
          AND EffectiveFrom>EffectiveTo AND Frequency=N'Monthly' AND FeeTypeId IN(SELECT FeeTypeId FROM @TransferFees))
        THROW 59322,'An existing fee period ends before it starts. Review Fee Changes before applying transfer fees. You can keep current fees.',1;
    IF EXISTS(SELECT 1 FROM dbo.StudentFeeAssignments A JOIN dbo.StudentFeeAssignments B
        ON A.EnrollmentId=B.EnrollmentId AND A.FeeTypeId=B.FeeTypeId
        AND A.StudentFeeAssignmentId<B.StudentFeeAssignmentId
        AND A.Frequency=N'Monthly' AND B.Frequency=N'Monthly' AND A.FeeTypeId IN(SELECT FeeTypeId FROM @TransferFees)
        WHERE A.EnrollmentId=@EnrollmentId AND A.CancelledAt IS NULL AND B.CancelledAt IS NULL
        AND ISNULL(A.EffectiveFrom,'19000101')<=ISNULL(B.EffectiveTo,'99991231')
        AND ISNULL(B.EffectiveFrom,'19000101')<=ISNULL(A.EffectiveTo,'99991231'))
        THROW 59323,'Existing fee periods overlap. Review Fee Changes before applying transfer fees. You can keep current fees.',1;
    DECLARE @CorrectionBill bigint,@DestinationAmount decimal(12,2),@Request uniqueidentifier;
    DECLARE @CorrectionResult TABLE(BillingChargeId bigint,Difference decimal(12,2),ReleasedPaidAmount decimal(12,2));
    DECLARE CorrectBills CURSOR LOCAL FAST_FORWARD FOR
      SELECT B.BillingChargeId,CASE WHEN P.PlanAction=N'DISCONTINUE' THEN 0 ELSE P.DestAmount END
      FROM #FeeSyncPlan P JOIN dbo.BillingCharges B ON B.EnrollmentId=@EnrollmentId AND B.FeeTypeId=P.FeeTypeId
      WHERE P.PlanAction<>N'NONE' AND B.ChargeKind=N'Monthly' AND B.CorrectionOfBillingChargeId IS NULL
        AND B.Status<>N'Cancelled' AND B.BillingMonth>=@TargetMonth ORDER BY B.BillingChargeId;
    OPEN CorrectBills;FETCH NEXT FROM CorrectBills INTO @CorrectionBill,@DestinationAmount;
    WHILE @@FETCH_STATUS=0 BEGIN
      SET @Request=NEWID();
      INSERT @CorrectionResult EXEC dbo.AdjustBillingChargeRate @UserId,@CorrectionBill,@DestinationAmount,@PaidReductionAction,@Reason,@Request;
      FETCH NEXT FROM CorrectBills INTO @CorrectionBill,@DestinationAmount;
    END;
    CLOSE CorrectBills;DEALLOCATE CorrectBills;
    DECLARE @LastIssued date=(SELECT MAX(B.BillingMonth) FROM dbo.BillingCharges B JOIN #FeeSyncPlan P ON P.FeeTypeId=B.FeeTypeId
      WHERE B.EnrollmentId=@EnrollmentId AND P.PlanAction<>N'NONE' AND B.ChargeKind=N'Monthly' AND B.Status<>N'Cancelled');
    IF @LastIssued>=@TargetMonth SET @TargetMonth=DATEADD(month,1,@LastIssued);
    -- An unbilled version already starting on the permitted month needs a rate edit, not schedule replacement.
    UPDATE A SET Amount=P.DestAmount,DiscountAmount=P.CurrentDiscountAmount,UpdatedAt=SYSDATETIME()
     FROM dbo.StudentFeeAssignments A JOIN #FeeSyncPlan P ON P.CurrentAssignmentId=A.StudentFeeAssignmentId
     WHERE P.PlanAction=N'CHANGE' AND A.EffectiveFrom=@TargetMonth
       AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.StudentFeeAssignmentId=A.StudentFeeAssignmentId);
    UPDATE P SET PlanAction=N'NONE' FROM #FeeSyncPlan P JOIN dbo.StudentFeeAssignments A ON A.StudentFeeAssignmentId=P.CurrentAssignmentId
     WHERE P.PlanAction=N'CHANGE' AND A.EffectiveFrom=@TargetMonth AND A.Amount=P.DestAmount
       AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.StudentFeeAssignmentId=A.StudentFeeAssignmentId);
    IF EXISTS(SELECT 1 FROM #FeeSyncPlan P JOIN dbo.StudentFeeAssignments A
        ON A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=P.FeeTypeId
        WHERE @ReplaceScheduledFees=0 AND P.PlanAction<>N'NONE' AND A.CancelledAt IS NULL
        AND ((A.StudentFeeAssignmentId=P.CurrentAssignmentId AND A.EffectiveFrom>=@TargetMonth)
          OR (A.StudentFeeAssignmentId<>ISNULL(P.CurrentAssignmentId,-1)
            AND ISNULL(A.EffectiveTo,'99991231')>=@TargetMonth)))
        THROW 59325,'A changed fee has a scheduled rate or waiver. Keep current fees, then explicitly review that schedule in Fee Changes.',1;
    UPDATE P SET CurrentDiscountAmount=CASE WHEN A.ConcessionLabel IS NOT NULL AND A.DiscountAmount=A.Amount THEN P.DestAmount
      WHEN P.CurrentDiscountAmount>P.DestAmount THEN P.DestAmount ELSE P.CurrentDiscountAmount END
    FROM #FeeSyncPlan P JOIN dbo.StudentFeeAssignments A ON A.StudentFeeAssignmentId=P.CurrentAssignmentId WHERE P.DestAmount IS NOT NULL;
    IF NOT EXISTS (SELECT 1
                   FROM   #FeeSyncPlan
                   WHERE  PlanAction <> N'NONE')
        RETURN;
    DECLARE @PlanFeeTypeId AS INT, @PlanAssignmentId AS INT, @PlanDiscount AS DECIMAL (12, 2), @PlanRemarks AS NVARCHAR (500), @PlanDestAmount AS DECIMAL (12, 2), @PlanDestFrequency AS NVARCHAR (20), @PlanAction AS NVARCHAR (20), @PlanFeeTypeName AS NVARCHAR (100);
    DECLARE @PlanConcession nvarchar(20);
    DECLARE @LastBilledMonth AS DATE, @NewAssignmentId AS INT, @DiscontinueEffectiveTo AS DATE;
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
                        UPDATE A SET EffectiveTo=CASE WHEN A.EffectiveFrom IS NULL OR A.EffectiveFrom<@TargetMonth
                                THEN DATEADD(MONTH,-1,@TargetMonth) ELSE COALESCE(A.EffectiveTo,A.EffectiveFrom) END,
                            CancelledAt=CASE WHEN A.EffectiveFrom>=@TargetMonth THEN SYSDATETIME() ELSE A.CancelledAt END,
                            UpdatedAt=SYSDATETIME()
                        FROM dbo.StudentFeeAssignments A
                        WHERE A.EnrollmentId=@EnrollmentId AND A.FeeTypeId=@PlanFeeTypeId AND A.CancelledAt IS NULL
                            AND ISNULL(A.EffectiveTo,'99991231')>=@TargetMonth;
                    END;
            SET @PlanConcession=(SELECT ConcessionLabel FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=@PlanAssignmentId);
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
                    INSERT  dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo, ConcessionLabel)
                    VALUES                           (@EnrollmentId, @AdmissionId, @PlanFeeTypeId, @PlanDestAmount, @PlanDiscount, @PlanDestFrequency, @PlanRemarks, @TargetMonth, NULL, @PlanConcession);
                    SET @NewAssignmentId = CONVERT (INT, SCOPE_IDENTITY());

                END
            ELSE
                IF @PlanAction = N'ADD'
                    BEGIN
                        INSERT  dbo.StudentFeeAssignments (EnrollmentId, AdmissionId, FeeTypeId, Amount, DiscountAmount, Frequency, Remarks, EffectiveFrom, EffectiveTo)
                        VALUES                           (@EnrollmentId, @AdmissionId, @PlanFeeTypeId, @PlanDestAmount, 0, @PlanDestFrequency, N'Added via transfer fee sync', @TargetMonth, NULL);
                        SET @NewAssignmentId = CONVERT (INT, SCOPE_IDENTITY());

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

                        END
            FETCH NEXT FROM plan_cursor INTO @PlanFeeTypeId, @PlanAssignmentId, @PlanDiscount, @PlanRemarks, @PlanDestAmount, @PlanDestFrequency, @PlanAction, @PlanFeeTypeName;
        END
    CLOSE plan_cursor;
    DEALLOCATE plan_cursor;

END

GO
INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes)
 VALUES(N'44',SYSDATETIME(),SUSER_SNAME(),N'Same-year transfers update tuition and existing transport without changing admission or other fees');
GO