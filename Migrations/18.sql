-- Agreed audit corrections: posting is isolated to the selected enrollment year;
-- occupied campus/class/section mappings cannot be deactivated.
-- Reversal retains all charge kinds assigned to the billing month and existing protections.
-- Read-only discovery/projection corrections do not change charges or payments.
-- Missing-enrollment creation passes valid JSON to the existing audit logger.
-- Apply before deploying this application. Compatibility baseline remains 3.
SET ANSI_NULLS ON;
GO

SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dbo.GetPostableMonths
@UserId INT, @AcademicYearId INT, @CampusId INT=0, @ClassId INT=0, @SectionId INT=0, @FeeTypeIds dbo.FeeTypeIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Collect';
    IF @AcademicYearId IS NULL
       OR @AcademicYearId <= 0
        THROW 54740, 'An academic year is required.', 1;
    DECLARE @First AS DATE, @Last AS DATE;
    SELECT @First = DATEFROMPARTS(YEAR(AY.StartDate), MONTH(AY.StartDate), 1),
           @Last = DATEFROMPARTS(YEAR(AY.EndDate), MONTH(AY.EndDate), 1)
    FROM   dbo.AcademicYears AS AY
    WHERE  AY.AcademicYearId = @AcademicYearId;
    IF @First IS NULL
        THROW 54741, 'The academic year was not found.', 1;
    IF @Last < @First
        THROW 54742, 'The academic year ends before it starts.', 1;
    IF DATEDIFF(MONTH, @First, @Last) > 23
        SET @Last = DATEADD(MONTH, 23, @First);
    DECLARE @AnyFeeType AS BIT = CASE WHEN EXISTS (SELECT 1
                                                   FROM   @FeeTypeIds) THEN 1 ELSE 0 END;
    WITH     Months
    AS       (SELECT @First AS BillingMonth
              UNION ALL
              SELECT DATEADD(MONTH, 1, BillingMonth)
              FROM   Months
              WHERE  BillingMonth < @Last)
    SELECT   M.BillingMonth,
             COUNT(*) AS ChargesToPost,
             COUNT(DISTINCT E.AdmissionId) AS EligibleStudents,
             SUM(SFA.Amount - SFA.DiscountAmount) AS TotalNet
    FROM     Months AS M CROSS JOIN dbo.StudentFeeAssignments AS SFA
             INNER JOIN
             dbo.Enrollments AS E
             ON E.EnrollmentId = SFA.EnrollmentId
             INNER JOIN
             dbo.CampusClassSections AS CCS
             ON CCS.ClassSectionId = E.ClassSectionId
    WHERE    E.AcademicYearId = @AcademicYearId AND E.Status = N'Active'
             AND E.BillingReady = 1
             AND SFA.CancelledAt IS NULL
             AND SFA.Frequency = N'Monthly'
             AND M.BillingMonth >= DATEFROMPARTS(YEAR(ISNULL(E.BillingStartDate, E.EnrollmentDate)), MONTH(ISNULL(E.BillingStartDate, E.EnrollmentDate)), 1)
             AND (@CampusId = 0
                  OR CCS.CampusId = @CampusId)
             AND (@ClassId = 0
                  OR CCS.ClassId = @ClassId)
             AND (@SectionId = 0
                  OR CCS.SectionId = @SectionId)
             AND (@AnyFeeType = 0
                  OR EXISTS (SELECT 1
                             FROM   @FeeTypeIds AS F
                             WHERE  F.FeeTypeId = SFA.FeeTypeId))
             AND (SFA.EffectiveFrom IS NULL
                  OR M.BillingMonth >= SFA.EffectiveFrom)
             AND (SFA.EffectiveTo IS NULL
                  OR M.BillingMonth <= SFA.EffectiveTo)
             AND (E.LeavingDate IS NULL
                  OR DATEDIFF(MONTH, E.LeavingDate, M.BillingMonth) <= 0)
             AND NOT (E.Status = N'Promoted'
                      AND EXISTS (SELECT 1
                                  FROM   dbo.Enrollments AS NX
                                  WHERE  NX.AdmissionId = E.AdmissionId
                                         AND NX.EnrollmentId > E.EnrollmentId
                                         AND NX.BillingReady = 1
                                         AND NX.Status IN (N'Active', N'Promoted', N'Left')
                                         AND M.BillingMonth >= DATEFROMPARTS(YEAR(ISNULL(NX.BillingStartDate, NX.EnrollmentDate)), MONTH(ISNULL(NX.BillingStartDate, NX.EnrollmentDate)), 1)))
             AND NOT EXISTS (SELECT 1
                             FROM   dbo.BillingCharges AS BC
                             WHERE  BC.EnrollmentId = SFA.EnrollmentId
                                    AND BC.FeeTypeId = SFA.FeeTypeId
                                    AND BC.BillingMonth = M.BillingMonth
                                    AND BC.ChargeKind = N'Monthly'
                                    AND BC.Status <> N'Cancelled')
    GROUP BY M.BillingMonth
    HAVING   COUNT(*) > 0
    ORDER BY M.BillingMonth
    OPTION (MAXRECURSION 100);
END



GO

SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dbo.PreviewMonthlyFees
@UserId INT, @BillingMonth DATE, @CampusId INT=0, @ClassId INT=0, @SectionId INT=0, @FeeTypeIds dbo.FeeTypeIdList READONLY, @ExcludeAlreadyPosted BIT=1, @AcademicYearId INT
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Collect';
    IF @BillingMonth IS NULL
        THROW 54710, 'A billing month is required.', 1;
    IF @AcademicYearId IS NULL OR @AcademicYearId<=0 OR NOT EXISTS(SELECT 1 FROM dbo.AcademicYears WHERE AcademicYearId=@AcademicYearId)
        THROW 54743, 'Select a valid academic year before reviewing or posting fees.', 1;
    DECLARE @Month AS DATE = DATEFROMPARTS(YEAR(@BillingMonth), MONTH(@BillingMonth), 1);
    DECLARE @AnyFeeType AS BIT = CASE WHEN EXISTS (SELECT 1
                                                   FROM   @FeeTypeIds) THEN 1 ELSE 0 END;
    DECLARE @Sample TABLE (
        AdmissionId INT);
    IF EXISTS (SELECT 1
               FROM   dbo.Users
               WHERE  UserId = @UserId)
        INSERT  @Sample (AdmissionId)
        VALUES         (NULL);
    SELECT   E.AdmissionId,
             E.AcademicYearId,
             S.RegistrationNumber,
             S.StudentName,
             CAMP.CampusName,
             C.ClassName,
             SEC.SectionName,
             FT.FeeTypeId,
             FT.FeeTypeName,
             SFA.StudentFeeAssignmentId,
             SFA.EnrollmentId,
             @Month AS BillingMonth,
             @Month AS ChargeDate,
             @Month AS DueDate,
             SFA.Amount AS GrossAmount,
             SFA.DiscountAmount,
             SFA.Amount - SFA.DiscountAmount AS NetAmount,
             CASE WHEN BC.BillingChargeId IS NOT NULL THEN 1 ELSE 0 END AS AlreadyPosted,
             CASE WHEN BC.BillingChargeId IS NOT NULL THEN N'Already posted for this month' WHEN EXISTS(SELECT 1 FROM dbo.BillingCharges R WHERE R.EnrollmentId=SFA.EnrollmentId AND R.FeeTypeId=SFA.FeeTypeId AND R.BillingMonth=@Month AND R.ChargeKind=N'Monthly' AND R.Status=N'Cancelled') THEN N'Previously reversed â€” selecting this creates a new charge' ELSE N'Eligible' END AS EligibilityNote
    FROM     dbo.StudentFeeAssignments AS SFA
             INNER JOIN
             dbo.Enrollments AS E
             ON E.EnrollmentId = SFA.EnrollmentId
             INNER JOIN
             dbo.Students AS S
             ON S.AdmissionId = E.AdmissionId
             INNER JOIN
             dbo.CampusClassSections AS CCS
             ON CCS.ClassSectionId = E.ClassSectionId
             INNER JOIN
             dbo.Campuses AS CAMP
             ON CAMP.CampusId = CCS.CampusId
             INNER JOIN
             dbo.Classes AS C
             ON C.ClassId = CCS.ClassId
             INNER JOIN
             dbo.Sections AS SEC
             ON SEC.SectionId = CCS.SectionId
             INNER JOIN
             dbo.FeeTypes AS FT
             ON FT.FeeTypeId = SFA.FeeTypeId
             LEFT OUTER JOIN
             @Sample AS SM
             ON SM.AdmissionId = E.AdmissionId OUTER APPLY (SELECT   TOP (1) BC.BillingChargeId
                                                            FROM     dbo.BillingCharges AS BC
                                                            WHERE    BC.EnrollmentId = SFA.EnrollmentId
                                                                     AND BC.FeeTypeId = SFA.FeeTypeId
                                                                     AND BC.BillingMonth = @Month
                                                                     AND BC.ChargeKind = N'Monthly'
                                                                     AND BC.Status <> N'Cancelled'
                                                            ORDER BY BC.BillingChargeId) AS BC
    WHERE    E.AcademicYearId = @AcademicYearId AND E.Status = N'Active'
             AND E.BillingReady = 1
             AND SFA.CancelledAt IS NULL
             AND SFA.Frequency = N'Monthly'
             AND @Month >= DATEFROMPARTS(YEAR(ISNULL(E.BillingStartDate, E.EnrollmentDate)), MONTH(ISNULL(E.BillingStartDate, E.EnrollmentDate)), 1)
             AND (@CampusId = 0
                  OR CCS.CampusId = @CampusId)
             AND (@ClassId = 0
                  OR CCS.ClassId = @ClassId)
             AND (@SectionId = 0
                  OR CCS.SectionId = @SectionId)
             AND (@AnyFeeType = 0
                  OR EXISTS (SELECT 1
                             FROM   @FeeTypeIds AS F
                             WHERE  F.FeeTypeId = SFA.FeeTypeId))
             AND (SFA.EffectiveFrom IS NULL
                  OR @Month >= SFA.EffectiveFrom)
             AND (SFA.EffectiveTo IS NULL
                  OR @Month <= SFA.EffectiveTo)
             AND (E.LeavingDate IS NULL
                  OR DATEDIFF(MONTH, E.LeavingDate, @Month) <= 0)
             AND NOT (E.Status = N'Promoted'
                      AND EXISTS (SELECT 1
                                  FROM   dbo.Enrollments AS NX
                                  WHERE  NX.AdmissionId = E.AdmissionId
                                         AND NX.EnrollmentId > E.EnrollmentId
                                         AND NX.BillingReady = 1
                                         AND NX.Status IN (N'Active', N'Promoted', N'Left')
                                         AND @Month >= DATEFROMPARTS(YEAR(ISNULL(NX.BillingStartDate, NX.EnrollmentDate)), MONTH(ISNULL(NX.BillingStartDate, NX.EnrollmentDate)), 1)))
             AND (@ExcludeAlreadyPosted = 0
                  OR BC.BillingChargeId IS NULL)
    ORDER BY CAMP.CampusName, C.ClassName, SEC.SectionName, S.StudentName, FT.DisplayOrder, FT.FeeTypeName;
    WITH   Eligible
    AS     (SELECT E.AdmissionId,
                   SFA.StudentFeeAssignmentId,
                   SFA.Amount,
                   SFA.DiscountAmount,
                   CASE WHEN BC.BillingChargeId IS NOT NULL THEN 1 ELSE 0 END AS AlreadyPosted
            FROM   dbo.StudentFeeAssignments AS SFA
                   INNER JOIN
                   dbo.Enrollments AS E
                   ON E.EnrollmentId = SFA.EnrollmentId
                   INNER JOIN
                   dbo.CampusClassSections AS CCS
                   ON CCS.ClassSectionId = E.ClassSectionId
                   LEFT OUTER JOIN
                   @Sample AS SM
                   ON SM.AdmissionId = E.AdmissionId OUTER APPLY (SELECT   TOP (1) BC.BillingChargeId
                                                                  FROM     dbo.BillingCharges AS BC
                                                                  WHERE    BC.EnrollmentId = SFA.EnrollmentId
                                                                           AND BC.FeeTypeId = SFA.FeeTypeId
                                                                           AND BC.BillingMonth = @Month
                                                                           AND BC.ChargeKind = N'Monthly'
                                                                           AND BC.Status <> N'Cancelled'
                                                                  ORDER BY BC.BillingChargeId) AS BC
            WHERE  E.AcademicYearId = @AcademicYearId AND E.Status = N'Active'
                   AND E.BillingReady = 1
                   AND SFA.CancelledAt IS NULL
                   AND SFA.Frequency = N'Monthly'
                   AND @Month >= DATEFROMPARTS(YEAR(ISNULL(E.BillingStartDate, E.EnrollmentDate)), MONTH(ISNULL(E.BillingStartDate, E.EnrollmentDate)), 1)
                   AND (@CampusId = 0
                        OR CCS.CampusId = @CampusId)
                   AND (@ClassId = 0
                        OR CCS.ClassId = @ClassId)
                   AND (@SectionId = 0
                        OR CCS.SectionId = @SectionId)
                   AND (@AnyFeeType = 0
                        OR EXISTS (SELECT 1
                                   FROM   @FeeTypeIds AS F
                                   WHERE  F.FeeTypeId = SFA.FeeTypeId))
                   AND (SFA.EffectiveFrom IS NULL
                        OR @Month >= SFA.EffectiveFrom)
                   AND (SFA.EffectiveTo IS NULL
                        OR @Month <= SFA.EffectiveTo)
                   AND (E.LeavingDate IS NULL
                        OR DATEDIFF(MONTH, E.LeavingDate, @Month) <= 0)
                   AND NOT (E.Status = N'Promoted'
                            AND EXISTS (SELECT 1
                                        FROM   dbo.Enrollments AS NX
                                        WHERE  NX.AdmissionId = E.AdmissionId
                                               AND NX.EnrollmentId > E.EnrollmentId
                                               AND NX.BillingReady = 1
                                               AND NX.Status IN (N'Active', N'Promoted', N'Left')
                                               AND @Month >= DATEFROMPARTS(YEAR(ISNULL(NX.BillingStartDate, NX.EnrollmentDate)), MONTH(ISNULL(NX.BillingStartDate, NX.EnrollmentDate)), 1))))
    SELECT @Month AS BillingMonth,
           COUNT(DISTINCT CASE WHEN AlreadyPosted = 0 THEN AdmissionId END) AS EligibleStudents,
           SUM(CASE WHEN AlreadyPosted = 0 THEN 1 ELSE 0 END) AS ChargesToPost,
           SUM(CASE WHEN AlreadyPosted = 0 THEN Amount ELSE 0 END) AS TotalGross,
           SUM(CASE WHEN AlreadyPosted = 0 THEN DiscountAmount ELSE 0 END) AS TotalDiscount,
           SUM(CASE WHEN AlreadyPosted = 0 THEN Amount - DiscountAmount ELSE 0 END) AS TotalNet,
           COUNT(DISTINCT CASE WHEN AlreadyPosted = 1 THEN AdmissionId END) AS AlreadyPostedStudents,
           SUM(CASE WHEN AlreadyPosted = 1 THEN 1 ELSE 0 END) AS AlreadyPostedCharges
    FROM   Eligible;
END



GO

SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dbo.PostSelectedMonthlyFees
@UserId INT, @BillingMonth DATE, @Selection dbo.PostingSelectionType READONLY, @Reason NVARCHAR (500)=NULL, @AcademicYearId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Collect';
    IF @BillingMonth IS NULL
        THROW 54720, 'A billing month is required. The month is a business decision and is never derived from today''s date.', 1;
    IF NOT EXISTS (SELECT 1
                   FROM   @Selection)
        THROW 54721, 'No charges were selected to post.', 1;
    IF @AcademicYearId IS NULL OR @AcademicYearId<=0 OR NOT EXISTS(SELECT 1 FROM dbo.AcademicYears WHERE AcademicYearId=@AcademicYearId)
        THROW 54743, 'Select a valid academic year before reviewing or posting fees.', 1;
    DECLARE @Month AS DATE = DATEFROMPARTS(YEAR(@BillingMonth), MONTH(@BillingMonth), 1);
    DECLARE @Remarks AS NVARCHAR (500) = LEFT(ISNULL(NULLIF (LTRIM(RTRIM(@Reason)), N''), N'Manual fee posting (selected charges).'), 500);
    DECLARE @RowsRequested AS INT = (SELECT COUNT(*)
                                     FROM   @Selection);
    BEGIN TRANSACTION;
    IF EXISTS(SELECT 1 FROM @Selection S JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.EnrollmentId=S.EnrollmentId WHERE E.AcademicYearId<>@AcademicYearId)
    BEGIN
        ROLLBACK;
        THROW 54744, 'Some selected enrollments belong to another academic year. Refresh the review; nothing was posted.', 1;
    END;
    DECLARE @CreatedCharges TABLE (
        BillingChargeId BIGINT PRIMARY KEY);
    INSERT dbo.BillingCharges (StudentFeeAssignmentId, EnrollmentId, AdmissionId, FeeTypeId, BillingMonth, ChargeDate, DueDate, GrossAmount, DiscountAmount, Remarks, ChargeKind)
    OUTPUT inserted.BillingChargeId INTO @CreatedCharges (BillingChargeId)
    SELECT SFA.StudentFeeAssignmentId,
           SFA.EnrollmentId,
           SFA.AdmissionId,
           SFA.FeeTypeId,
           @Month,
           @Month,
           @Month,
           SFA.Amount,
           SFA.DiscountAmount,
           @Remarks,
           N'Monthly'
    FROM   dbo.StudentFeeAssignments AS SFA
           INNER JOIN
           dbo.Enrollments AS E
           ON E.EnrollmentId = SFA.EnrollmentId
    WHERE  E.AcademicYearId = @AcademicYearId AND E.Status = N'Active'
           AND E.BillingReady = 1
           AND SFA.CancelledAt IS NULL
           AND SFA.Frequency = N'Monthly'
           AND @Month >= DATEFROMPARTS(YEAR(ISNULL(E.BillingStartDate, E.EnrollmentDate)), MONTH(ISNULL(E.BillingStartDate, E.EnrollmentDate)), 1)
           AND (SFA.EffectiveFrom IS NULL
                OR @Month >= SFA.EffectiveFrom)
           AND (SFA.EffectiveTo IS NULL
                OR @Month <= SFA.EffectiveTo)
           AND (E.LeavingDate IS NULL
                OR DATEDIFF(MONTH, E.LeavingDate, @Month) <= 0)
           AND NOT (E.Status = N'Promoted'
                    AND EXISTS (SELECT 1
                                FROM   dbo.Enrollments AS NX
                                WHERE  NX.AdmissionId = E.AdmissionId
                                       AND NX.EnrollmentId > E.EnrollmentId
                                       AND NX.BillingReady = 1
                                       AND NX.Status IN (N'Active', N'Promoted', N'Left')
                                       AND @Month >= DATEFROMPARTS(YEAR(ISNULL(NX.BillingStartDate, NX.EnrollmentDate)), MONTH(ISNULL(NX.BillingStartDate, NX.EnrollmentDate)), 1)))
           AND EXISTS (SELECT 1
                       FROM   @Selection AS S
                       WHERE  S.EnrollmentId = SFA.EnrollmentId
                              AND S.FeeTypeId = SFA.FeeTypeId
                              AND DATEFROMPARTS(YEAR(S.BillingMonth), MONTH(S.BillingMonth), 1) = @Month)
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.BillingCharges AS BC
                           WHERE  BC.EnrollmentId = SFA.EnrollmentId
                                  AND BC.FeeTypeId = SFA.FeeTypeId
                                  AND BC.BillingMonth = @Month
                                  AND BC.ChargeKind = N'Monthly'
                                  AND BC.Status <> N'Cancelled');
    DECLARE @Created AS INT = @@ROWCOUNT;
    DECLARE @RowsNotPosted AS INT = @RowsRequested - @Created;
    IF @Created > 0
        BEGIN
            DECLARE @OperationId AS UNIQUEIDENTIFIER = NEWID();
            INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
            VALUES                     (@OperationId, @UserId, N'FEE_POSTING', N'BillingMonth=' + CONVERT (NVARCHAR (10), @Month, 120) + N'; Requested=' + CONVERT (NVARCHAR (10), @RowsRequested) + N'; Created=' + CONVERT (NVARCHAR (10), @Created));
            DECLARE @SummaryJson AS NVARCHAR (MAX) = (SELECT @Month AS BillingMonth,
                                                             @RowsRequested AS RowsRequested,
                                                             @Created AS ChargesCreated,
                                                             @RowsNotPosted AS RowsNotPosted,
                                                             @Reason AS Reason,
                                                             N'Manual monthly fee posting of the selected charges. Each charge was created Open with no payment allocated.' AS Note
                                                      FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
            EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'BillingCharges', @RecordId = NULL, @Action = N'INSERT', @OldValues = NULL, @NewValues = @SummaryJson;
            DECLARE @ChargesJson AS NVARCHAR (MAX) = (SELECT BC.BillingChargeId AS BillingChargeId,
                                                             BC.StudentFeeAssignmentId AS StudentFeeAssignmentId,
                                                             BC.EnrollmentId AS EnrollmentId,
                                                             BC.AdmissionId AS AdmissionId,
                                                             BC.FeeTypeId AS FeeTypeId,
                                                             BC.BillingMonth AS BillingMonth,
                                                             BC.GrossAmount AS GrossAmount,
                                                             BC.DiscountAmount AS DiscountAmount,
                                                             BC.NetAmount AS NetAmount,
                                                             BC.Status AS Status
                                                      FROM   dbo.BillingCharges AS BC
                                                             INNER JOIN
                                                             @CreatedCharges AS C
                                                             ON C.BillingChargeId = BC.BillingChargeId
                                                      FOR    JSON PATH);
            IF @ChargesJson IS NOT NULL
                EXECUTE dbo.WriteAuditLog @OperationId = @OperationId, @UserId = @UserId, @TableName = N'BillingCharges', @RecordId = NULL, @Action = N'INSERT', @OldValues = NULL, @NewValues = @ChargesJson;
        END
    DELETE dbo.AuditOperations WHERE OperationId=@OperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@OperationId);
COMMIT TRANSACTION;
    SELECT @Month AS BillingMonth,
           @RowsRequested AS RowsRequested,
           @Created AS ChargesCreated,
           @RowsNotPosted AS RowsNotPosted;
END



GO

SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dbo.SaveCampusClassSections
    @UserId int, @CampusId int,
    @Assignments dbo.CampusClassSectionAssignmentType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.RequirePermission @UserId, N'System.Setup';
    IF NOT EXISTS (SELECT 1 FROM dbo.Campuses WHERE CampusId = @CampusId)
        THROW 50102, 'Please select a valid campus.', 1;
    IF EXISTS (SELECT 1 FROM @Assignments A WHERE A.IsSelected = 1
        AND NOT EXISTS (SELECT 1 FROM dbo.Classes C WHERE C.ClassId = A.ClassId))
        THROW 50104, 'One or more selected classes are invalid.', 1;
    IF EXISTS (SELECT 1 FROM @Assignments A WHERE A.IsSelected = 1
        AND NOT EXISTS (SELECT 1 FROM dbo.Sections S WHERE S.SectionId = A.SectionId))
        THROW 50105, 'One or more selected sections are invalid.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @PlacementLocks int;
        SELECT @PlacementLocks=COUNT(*) FROM dbo.CampusClassSections CCS WITH(UPDLOCK,HOLDLOCK)
        WHERE CCS.CampusId=@CampusId AND EXISTS(SELECT 1 FROM @Assignments A WHERE A.ClassId=CCS.ClassId AND A.SectionId=CCS.SectionId);
        IF EXISTS(SELECT 1 FROM dbo.CampusClassSections CCS
            JOIN dbo.Enrollments E WITH(UPDLOCK,HOLDLOCK) ON E.ClassSectionId=CCS.ClassSectionId AND E.Status=N'Active'
            WHERE CCS.CampusId=@CampusId AND CCS.IsActive=1
              AND EXISTS(SELECT 1 FROM @Assignments A WHERE A.ClassId=CCS.ClassId AND A.SectionId=CCS.SectionId)
              AND NOT EXISTS(SELECT 1 FROM @Assignments A WHERE A.ClassId=CCS.ClassId AND A.SectionId=CCS.SectionId AND A.IsSelected=1))
            THROW 50106, 'A class and section still has active students. Move those students before removing the assignment. Nothing was saved.', 1;

        MERGE dbo.CampusClassSections WITH (HOLDLOCK) AS Target
        USING (SELECT DISTINCT @CampusId AS CampusId, ClassId, SectionId
               FROM @Assignments WHERE IsSelected = 1) AS Source
        ON Target.CampusId = Source.CampusId AND Target.ClassId = Source.ClassId
            AND Target.SectionId = Source.SectionId
        WHEN MATCHED THEN UPDATE SET IsActive = 1, UpdatedAt = SYSDATETIME()
        WHEN NOT MATCHED THEN INSERT (CampusId, ClassId, SectionId, IsActive)
            VALUES (Source.CampusId, Source.ClassId, Source.SectionId, 1);

        UPDATE CCS SET IsActive = 0, UpdatedAt = SYSDATETIME()
        FROM dbo.CampusClassSections CCS
        WHERE CCS.CampusId = @CampusId
            AND EXISTS (SELECT 1 FROM @Assignments A
                WHERE A.ClassId = CCS.ClassId AND A.SectionId = CCS.SectionId)
            AND NOT EXISTS (SELECT 1 FROM @Assignments A
                WHERE A.ClassId = CCS.ClassId AND A.SectionId = CCS.SectionId AND A.IsSelected = 1);
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dbo.GetReversibleMonths

(

      @UserId      INT

    , @CampusId    INT = 0

    , @ClassId     INT = 0

    , @SectionId   INT = 0

    , @FeeTypeIds  dbo.FeeTypeIdList READONLY

)

AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission @UserId, N'Fees.Collect';

    DECLARE @AnyFeeType BIT =
        CASE WHEN EXISTS (SELECT 1 FROM @FeeTypeIds) THEN 1 ELSE 0 END;

    /* The same live-charge test dbo.PreviewPostingReversal uses: a charge that
       is not Cancelled, in a billing month, matching the scope. A charge with no
       billing month, or one not dated the first of its month, can never be
       reached by a month-based reversal, so it is not offered as a month. */
    SELECT
        BC.BillingMonth,
        COUNT(*)                                                   AS ChargesAffected,
        SUM(CASE WHEN ISNULL(PA.Collected, 0) = 0 THEN 1 ELSE 0 END) AS ReversibleCharges,
        SUM(BC.NetAmount)                                          AS TotalPosted,
        SUM(ISNULL(PA.Collected, 0))                               AS TotalCollected
    FROM dbo.BillingCharges BC
    INNER JOIN dbo.Enrollments E
        ON E.EnrollmentId = BC.EnrollmentId
    INNER JOIN dbo.CampusClassSections CCS
        ON CCS.ClassSectionId = E.ClassSectionId
    OUTER APPLY
    (
        SELECT SUM(A.Amount) AS Collected
        FROM dbo.FeePaymentAllocations A
        INNER JOIN dbo.FeePayments FP ON FP.FeePaymentId = A.FeePaymentId AND FP.Status = N'Completed'
        WHERE A.BillingChargeId = BC.BillingChargeId
    ) PA
    WHERE E.Status=N'Active' AND BC.Status <> N'Cancelled'
      AND BC.BillingMonth IS NOT NULL
      AND BC.BillingMonth = DATEFROMPARTS(YEAR(BC.BillingMonth), MONTH(BC.BillingMonth), 1)
      AND (@CampusId  = 0 OR CCS.CampusId  = @CampusId)
      AND (@ClassId   = 0 OR CCS.ClassId   = @ClassId)
      AND (@SectionId = 0 OR CCS.SectionId = @SectionId)
      AND (@AnyFeeType = 0 OR EXISTS (SELECT 1 FROM @FeeTypeIds F WHERE F.FeeTypeId = BC.FeeTypeId))
    GROUP BY BC.BillingMonth
    ORDER BY BC.BillingMonth DESC;
END;
GO

SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dbo.PreviewPostingReversal
@UserId INT, @BillingMonth DATE, @CampusId INT=0, @ClassId INT=0, @SectionId INT=0, @FeeTypeIds dbo.FeeTypeIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    EXECUTE dbo.RequirePermission @UserId, N'Fees.Collect';
    IF @BillingMonth IS NULL
        THROW 54730, 'A billing month is required.', 1;
    DECLARE @Month AS DATE = DATEFROMPARTS(YEAR(@BillingMonth), MONTH(@BillingMonth), 1);
    DECLARE @AnyFeeType AS BIT = CASE WHEN EXISTS (SELECT 1
                                                   FROM   @FeeTypeIds) THEN 1 ELSE 0 END;
    SELECT   BC.BillingChargeId,
             E.AdmissionId,
             S.RegistrationNumber,
             S.StudentName,
             CAMP.CampusName,
             C.ClassName,
             SEC.SectionName,
             FT.FeeTypeName,
             BC.ChargeKind,
             BC.BillingMonth,
             BC.GrossAmount,
             BC.DiscountAmount,
             BC.NetAmount,
             ISNULL(PA.Collected, 0) AS CollectedAmount,
             BC.NetAmount - ISNULL(PA.Collected, 0) AS UncollectedAmount,
             CASE WHEN ISNULL(PA.Collected, 0) > 0 THEN 1 ELSE 0 END AS HasCollectedMoney,
             CASE WHEN ISNULL(PA.Collected, 0) > 0 THEN N'Collected - use Fee Collection Correction/Refund/Reversal' ELSE N'Uncollected - can be reversed here' END AS ReversalNote
    FROM     dbo.BillingCharges AS BC
             INNER JOIN
             dbo.Enrollments AS E
             ON E.EnrollmentId = BC.EnrollmentId
             INNER JOIN
             dbo.Students AS S
             ON S.AdmissionId = BC.AdmissionId
             INNER JOIN
             dbo.CampusClassSections AS CCS
             ON CCS.ClassSectionId = E.ClassSectionId
             INNER JOIN
             dbo.Campuses AS CAMP
             ON CAMP.CampusId = CCS.CampusId
             INNER JOIN
             dbo.Classes AS C
             ON C.ClassId = CCS.ClassId
             INNER JOIN
             dbo.Sections AS SEC
             ON SEC.SectionId = CCS.SectionId
             INNER JOIN
             dbo.FeeTypes AS FT
             ON FT.FeeTypeId = BC.FeeTypeId OUTER APPLY (SELECT SUM(A.Amount) AS Collected
                                                         FROM   dbo.FeePaymentAllocations AS A
                                                                INNER JOIN
                                                                dbo.FeePayments AS FP
                                                                ON FP.FeePaymentId = A.FeePaymentId
                                                                   AND FP.Status = N'Completed'
                                                         WHERE  A.BillingChargeId = BC.BillingChargeId) AS PA
    WHERE    BC.BillingMonth = @Month AND E.Status=N'Active' AND E.Status=N'Active'
             AND BC.Status <> N'Cancelled'
             AND (@CampusId = 0
                  OR CCS.CampusId = @CampusId)
             AND (@ClassId = 0
                  OR CCS.ClassId = @ClassId)
             AND (@SectionId = 0
                  OR CCS.SectionId = @SectionId)
             AND (@AnyFeeType = 0
                  OR EXISTS (SELECT 1
                             FROM   @FeeTypeIds AS F
                             WHERE  F.FeeTypeId = BC.FeeTypeId))
    ORDER BY CAMP.CampusName, C.ClassName, SEC.SectionName, S.StudentName, FT.DisplayOrder, FT.FeeTypeName;
    WITH   C
    AS     (SELECT BC.BillingChargeId,
                   BC.AdmissionId,
                   BC.NetAmount,
                   ISNULL(PA.Collected, 0) AS Collected
            FROM   dbo.BillingCharges AS BC
                   INNER JOIN
                   dbo.Enrollments AS E
                   ON E.EnrollmentId = BC.EnrollmentId
                   INNER JOIN
                   dbo.CampusClassSections AS CCS
                   ON CCS.ClassSectionId = E.ClassSectionId OUTER APPLY (SELECT SUM(A.Amount) AS Collected
                                                                         FROM   dbo.FeePaymentAllocations AS A
                                                                                INNER JOIN
                                                                                dbo.FeePayments AS FP
                                                                                ON FP.FeePaymentId = A.FeePaymentId
                                                                                   AND FP.Status = N'Completed'
                                                                         WHERE  A.BillingChargeId = BC.BillingChargeId) AS PA
            WHERE  BC.BillingMonth = @Month AND E.Status=N'Active' AND E.Status=N'Active'
                   AND BC.Status <> N'Cancelled'
                   AND (@CampusId = 0
                        OR CCS.CampusId = @CampusId)
                   AND (@ClassId = 0
                        OR CCS.ClassId = @ClassId)
                   AND (@SectionId = 0
                        OR CCS.SectionId = @SectionId)
                   AND (@AnyFeeType = 0
                        OR EXISTS (SELECT 1
                                   FROM   @FeeTypeIds AS F
                                   WHERE  F.FeeTypeId = BC.FeeTypeId)))
    SELECT @Month AS BillingMonth,
           COUNT(DISTINCT AdmissionId) AS StudentsAffected,
           COUNT(*) AS ChargesAffected,
           ISNULL(SUM(NetAmount), 0) AS TotalPosted,
           ISNULL(SUM(Collected), 0) AS TotalCollected,
           ISNULL(SUM(NetAmount - Collected), 0) AS TotalUncollected,
           SUM(CASE WHEN Collected = 0 THEN 1 ELSE 0 END) AS ReversibleCharges,
           SUM(CASE WHEN Collected > 0 THEN 1 ELSE 0 END) AS BlockedCharges,
           ISNULL(SUM(CASE WHEN Collected = 0 THEN NetAmount - Collected ELSE 0 END), 0) AS ReversibleAmount
    FROM   C;
END



GO

SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dbo.GetStudentFeeProjection
    @UserId INT,
    @EnrollmentId INT,
    @AsOfDate DATE = NULL,
    @MonthsAhead INT = 6
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission @UserId, N'Fees.View';

    IF NOT EXISTS (SELECT 1 FROM dbo.Enrollments WHERE EnrollmentId = @EnrollmentId)
        THROW 53120, 'The selected enrollment does not exist.', 1;

    IF @AsOfDate IS NULL
        SET @AsOfDate = CAST(GETDATE() AS DATE);

    IF @MonthsAhead IS NULL OR @MonthsAhead < 1
        SET @MonthsAhead = 6;

    -- Preserve the existing maximum future horizon. Historical months are
    -- enumerated separately under the outer query's recursion option.
    IF @MonthsAhead > 24
        SET @MonthsAhead = 24;

    DECLARE @CurrentMonth DATE = DATEFROMPARTS(YEAR(@AsOfDate), MONTH(@AsOfDate), 1);
    DECLARE @HorizonDate DATE = DATEADD(MONTH, @MonthsAhead, @AsOfDate);

    SELECT
        X.BillingMonth AS ExpectedBillingMonth,
        FT.FeeTypeId,
        FT.FeeTypeName,
        SFA.Frequency,
        SFA.Amount AS ExpectedAmount,
        SFA.DiscountAmount AS ExpectedDiscountAmount,
        SFA.Amount - SFA.DiscountAmount AS ExpectedNetAmount,
        SFA.StudentFeeAssignmentId
    FROM dbo.fn_ExpectedRecurringChargeMonths(@EnrollmentId, @HorizonDate) X
    INNER JOIN dbo.StudentFeeAssignments SFA ON SFA.StudentFeeAssignmentId = X.StudentFeeAssignmentId
    INNER JOIN dbo.FeeTypes FT ON FT.FeeTypeId = X.FeeTypeId
    WHERE X.BillingMonth > @CurrentMonth
      AND NOT EXISTS
      (
          SELECT 1 FROM dbo.BillingCharges BC
          WHERE BC.EnrollmentId = SFA.EnrollmentId
            AND BC.FeeTypeId = SFA.FeeTypeId
            AND BC.BillingMonth = X.BillingMonth
            AND BC.ChargeKind = N'Monthly'
            AND BC.Status <> N'Cancelled'
      )
    ORDER BY X.BillingMonth, FT.FeeTypeName
    OPTION (MAXRECURSION 0);
END;


GO

SET QUOTED_IDENTIFIER OFF;
GO
CREATE OR ALTER PROCEDURE dbo.GetMissingChargeCandidates
    @UserId int,
    @AcademicYearId int,
    @CampusId int = 0,
    @AsOfDate date = NULL
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId, N'Fees.Reports.View';
    SET @AsOfDate = COALESCE(@AsOfDate, CONVERT(date, SYSDATETIME()));

    SELECT E.EnrollmentId, E.AdmissionId, S.RegistrationNumber,
           S.StudentName, CAMP.CampusName, C.ClassName, SEC.SectionName,
           FT.FeeTypeId, FT.FeeTypeName,
           X.BillingMonth AS ExpectedBillingMonth,
           SFA.Amount AS ExpectedAmount, SFA.StudentFeeAssignmentId
    FROM dbo.Enrollments E
    JOIN dbo.Students S ON S.AdmissionId = E.AdmissionId
    JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId = E.ClassSectionId
    JOIN dbo.Campuses CAMP ON CAMP.CampusId = CCS.CampusId
    JOIN dbo.Classes C ON C.ClassId = CCS.ClassId
    JOIN dbo.Sections SEC ON SEC.SectionId = CCS.SectionId
    CROSS APPLY dbo.fn_ExpectedRecurringChargeMonths(E.EnrollmentId, @AsOfDate) X
    JOIN dbo.StudentFeeAssignments SFA ON SFA.StudentFeeAssignmentId = X.StudentFeeAssignmentId
    JOIN dbo.FeeTypes FT ON FT.FeeTypeId = X.FeeTypeId
    WHERE (@AcademicYearId = 0 OR E.AcademicYearId = @AcademicYearId)
      AND E.Status IN (N'Active', N'Promoted', N'Left')
      AND E.BillingReady = 1
      AND (@CampusId = 0 OR CCS.CampusId = @CampusId)
      AND NOT EXISTS
      (
          SELECT 1 FROM dbo.BillingCharges BC
          WHERE BC.EnrollmentId = E.EnrollmentId
            AND BC.FeeTypeId = X.FeeTypeId
            AND BC.BillingMonth = X.BillingMonth
            AND BC.ChargeKind = N'Monthly'
            AND BC.Status <> N'Cancelled'
      )
    ORDER BY CAMP.CampusName, C.ClassName, SEC.SectionName,
             S.StudentName, X.BillingMonth
    OPTION (MAXRECURSION 0);
END;

GO

SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dbo.CorrectEnrollmentStatus
@UserId INT, @EnrollmentId INT=NULL, @AdmissionId INT=NULL, @NewStatus NVARCHAR (30)=NULL, @NewEnrollmentDate DATE=NULL, @NewLeavingDate DATE=NULL, @ClearLeavingDate BIT=0, @NewClassSectionId INT=NULL, @AcademicYearId INT=NULL, @Reason NVARCHAR (500)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        EXECUTE dbo.RequirePermission @UserId, N'Students.Administer';
        IF NULLIF (LTRIM(RTRIM(@Reason)), N'') IS NULL
            THROW 57101, 'A reason is required for a manual correction.', 1;
        IF @ClearLeavingDate = 1
           AND @NewLeavingDate IS NOT NULL
            THROW 57102, 'Leaving date cannot both be cleared and set at the same time.', 1;
        BEGIN TRANSACTION;
        IF @EnrollmentId IS NULL
           OR @EnrollmentId = 0
            BEGIN
                IF @AdmissionId IS NULL
                   OR @AdmissionId <= 0
                    BEGIN
                        ROLLBACK;
                        THROW 57103, 'A valid student is required to create an enrollment.', 1;
                    END
                DECLARE @PriorStudentStatus AS NVARCHAR (30);
                DECLARE @PriorStudentWithdrawalDate AS DATE;
                SELECT @PriorStudentStatus = Status,
                       @PriorStudentWithdrawalDate = WithdrawalDate
                FROM   dbo.Students WITH (UPDLOCK, HOLDLOCK)
                WHERE  AdmissionId = @AdmissionId;
                IF @PriorStudentStatus IS NULL
                    BEGIN
                        ROLLBACK;
                        THROW 57104, 'The selected student was not found.', 1;
                    END
                IF @AcademicYearId IS NULL
                   OR @AcademicYearId <= 0
                    BEGIN
                        ROLLBACK;
                        THROW 57105, 'A valid academic year is required.', 1;
                    END
                IF @NewClassSectionId IS NULL
                   OR @NewClassSectionId <= 0
                    BEGIN
                        ROLLBACK;
                        THROW 57106, 'A valid class section is required.', 1;
                    END
                IF @NewEnrollmentDate IS NULL
                    BEGIN
                        ROLLBACK;
                        THROW 57107, 'Enrollment date is required.', 1;
                    END
                SET @NewStatus = ISNULL(NULLIF (@NewStatus, N''), N'Active');
                IF @NewStatus NOT IN (N'Active', N'Promoted', N'Left')
                    BEGIN
                        ROLLBACK;
                        THROW 57108, 'Status must be Active, Promoted or Left.', 1;
                    END
                IF NOT EXISTS (SELECT 1
                               FROM   dbo.CampusClassSections
                               WHERE  ClassSectionId = @NewClassSectionId)
                    BEGIN
                        ROLLBACK;
                        THROW 57109, 'The selected class section is invalid.', 1;
                    END
                DECLARE @NewEffectiveLeavingDate AS DATE = CASE WHEN @ClearLeavingDate = 1 THEN NULL ELSE @NewLeavingDate END;
                IF @NewEffectiveLeavingDate IS NOT NULL
                   AND @NewEffectiveLeavingDate < @NewEnrollmentDate
                    BEGIN
                        ROLLBACK;
                        THROW 57110, 'Leaving date cannot be before the enrollment date.', 1;
                    END
                IF @NewStatus = N'Left'
                   AND @NewEffectiveLeavingDate IS NULL
                    BEGIN
                        ROLLBACK;
                        THROW 57117, 'A leaving date is required when creating a Left enrollment record.', 1;
                    END
                IF @NewStatus = N'Promoted'
                   AND NOT EXISTS (SELECT 1
                                   FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
                                   WHERE  AdmissionId = @AdmissionId
                                          AND Status = N'Active')
                    BEGIN
                        ROLLBACK;
                        THROW 57118, 'Marking a new enrollment Promoted requires an existing Active enrollment for this student. Create/select the destination enrollment first, or use the Promotion function.', 1;
                    END
                IF @NewStatus = N'Active'
                   AND EXISTS (SELECT 1
                               FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
                               WHERE  AdmissionId = @AdmissionId
                                      AND Status = N'Active')
                    BEGIN
                        ROLLBACK;
                        THROW 57216, 'An Active enrollment already exists for this student. Select and correct the existing enrollment instead.', 1;
                    END
                INSERT  dbo.Enrollments (AdmissionId, AcademicYearId, ClassSectionId, EnrollmentDate, LeavingDate, Status, Remarks)
                VALUES                 (@AdmissionId, @AcademicYearId, @NewClassSectionId, @NewEnrollmentDate, @NewEffectiveLeavingDate, @NewStatus, @Reason);
                SET @EnrollmentId = CONVERT (INT, SCOPE_IDENTITY());
                INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
                VALUES                       (@EnrollmentId, @AdmissionId, N'CORRECTION_CREATE', @NewEnrollmentDate, NULL, @NewClassSectionId, NULL, @NewStatus, @Reason, @UserId);
                DECLARE @StudentStatusSynced AS BIT = 0;
                IF @NewStatus = N'Active'
                   AND @PriorStudentStatus <> N'Active'
                    BEGIN
                        UPDATE dbo.Students
                        SET    Status         = N'Active',
                               WithdrawalDate = NULL,
                               UpdatedAt      = SYSDATETIME()
                        WHERE  AdmissionId = @AdmissionId;
                        INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
                        VALUES                          (@AdmissionId, @PriorStudentStatus, N'Active', N'Synchronized by enrollment correction (create).', @NewEnrollmentDate, @Reason, @UserId);
                        SET @StudentStatusSynced = 1;
                    END
                DECLARE @CreateOperationId AS UNIQUEIDENTIFIER = NEWID();
                INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
                VALUES                     (@CreateOperationId, @UserId, N'ENROLLMENT_CORRECTION_CREATE', CONVERT (NVARCHAR (200), @AdmissionId));
                DECLARE @CreateAuditValues nvarchar(max)=(SELECT @EnrollmentId EnrollmentId,@AdmissionId AdmissionId,
                    @AcademicYearId AcademicYearId,@NewClassSectionId ClassSectionId,@NewEnrollmentDate EnrollmentDate,
                    @NewEffectiveLeavingDate LeavingDate,@NewStatus Status,@Reason Reason
                    FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES);
                EXECUTE dbo.WriteAuditLog @OperationId = @CreateOperationId, @UserId = @UserId, @TableName = N'Enrollments', @RecordId = @EnrollmentId, @Action = N'CORRECTION_CREATE', @OldValues = NULL, @NewValues = @CreateAuditValues;
                IF @StudentStatusSynced = 1
                    BEGIN
                        DECLARE @CreateSyncValues AS NVARCHAR (MAX) = (SELECT @AdmissionId AS AdmissionId,
                                                                              @PriorStudentStatus AS OldStatus,
                                                                              N'Active' AS NewStatus,
                                                                              @EnrollmentId AS EnrollmentId
                                                                       FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
                        EXECUTE dbo.WriteAuditLog @OperationId = @CreateOperationId, @UserId = @UserId, @TableName = N'Students', @RecordId = @AdmissionId, @Action = N'STATUS_SYNC', @OldValues = NULL, @NewValues = @CreateSyncValues;
                    END
                DELETE dbo.AuditOperations WHERE OperationId=@CreateOperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@CreateOperationId);
COMMIT TRANSACTION;
                SELECT @EnrollmentId AS EnrollmentId,
                       @AdmissionId AS AdmissionId,
                       @NewStatus AS Status,
                       CAST (1 AS BIT) AS Changed;
                RETURN;
            END
        DECLARE @OldAdmissionId AS INT, @OldStatus AS NVARCHAR (30), @OldEnrollmentDate AS DATE, @OldLeavingDate AS DATE, @OldClassSectionId AS INT;
        SELECT @OldAdmissionId = AdmissionId,
               @OldStatus = Status,
               @OldEnrollmentDate = EnrollmentDate,
               @OldLeavingDate = LeavingDate,
               @OldClassSectionId = ClassSectionId
        FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
        WHERE  EnrollmentId = @EnrollmentId;
        IF @OldAdmissionId IS NULL
            BEGIN
                ROLLBACK;
                THROW 57111, 'The selected enrollment was not found.', 1;
            END
        DECLARE @EditPriorStudentStatus AS NVARCHAR (30);
        DECLARE @EditPriorStudentWithdrawalDate AS DATE;
        SELECT @EditPriorStudentStatus = Status,
               @EditPriorStudentWithdrawalDate = WithdrawalDate
        FROM   dbo.Students WITH (UPDLOCK, HOLDLOCK)
        WHERE  AdmissionId = @OldAdmissionId;
        IF @EditPriorStudentStatus IS NULL
            BEGIN
                ROLLBACK;
                THROW 57119, 'The student record for this enrollment was not found.', 1;
            END
        DECLARE @FinalStatus AS NVARCHAR (30) = ISNULL(NULLIF (@NewStatus, N''), @OldStatus);
        IF @FinalStatus NOT IN (N'Active', N'Promoted', N'Left')
            BEGIN
                ROLLBACK;
                THROW 57112, 'Status must be Active, Promoted or Left.', 1;
            END
        DECLARE @FinalEnrollmentDate AS DATE = ISNULL(@NewEnrollmentDate, @OldEnrollmentDate);
        DECLARE @FinalLeavingDate AS DATE = CASE WHEN @ClearLeavingDate = 1 THEN NULL WHEN @NewLeavingDate IS NOT NULL THEN @NewLeavingDate ELSE @OldLeavingDate END;
        DECLARE @FinalClassSectionId AS INT = ISNULL(@NewClassSectionId, @OldClassSectionId);
        IF @FinalLeavingDate IS NOT NULL
           AND @FinalLeavingDate < @FinalEnrollmentDate
            BEGIN
                ROLLBACK;
                THROW 57113, 'Leaving date cannot be before the enrollment date.', 1;
            END
        IF @FinalClassSectionId <> @OldClassSectionId
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.CampusClassSections
                           WHERE  ClassSectionId = @FinalClassSectionId)
            BEGIN
                ROLLBACK;
                THROW 57114, 'The selected class section is invalid.', 1;
            END
        IF @FinalStatus = @OldStatus
           AND @FinalEnrollmentDate = @OldEnrollmentDate
           AND ISNULL(@FinalLeavingDate, '19000101') = ISNULL(@OldLeavingDate, '19000101')
           AND @FinalClassSectionId = @OldClassSectionId
            BEGIN
                DELETE dbo.AuditOperations WHERE OperationId=@CreateOperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@CreateOperationId);
COMMIT TRANSACTION;
                SELECT @EnrollmentId AS EnrollmentId,
                       @OldAdmissionId AS AdmissionId,
                       @OldStatus AS Status,
                       CAST (0 AS BIT) AS Changed;
                RETURN;
            END
        IF @FinalEnrollmentDate <> @OldEnrollmentDate
           AND (EXISTS (SELECT 1
                        FROM   dbo.BillingCharges
                        WHERE  EnrollmentId = @EnrollmentId)
                OR EXISTS (SELECT 1
                           FROM   dbo.FeePayments
                           WHERE  EnrollmentId = @EnrollmentId)
                OR EXISTS (SELECT 1
                           FROM   dbo.EnrollmentHistory
                           WHERE  EnrollmentId = @EnrollmentId))
            BEGIN
                ROLLBACK;
                THROW 57116, 'This enrollment already has billing, payment or status history, so its enrollment (start) date cannot be changed by an ordinary correction. Correct the dependent records first, or create a new enrollment with the correct start date.', 1;
            END
        IF @FinalStatus = N'Left'
           AND @FinalLeavingDate IS NULL
            BEGIN
                ROLLBACK;
                THROW 57117, 'A leaving date is required when setting an enrollment to Left.', 1;
            END
        IF @FinalStatus = N'Promoted'
           AND @OldStatus <> N'Promoted'
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
                           WHERE  AdmissionId = @OldAdmissionId
                                  AND Status = N'Active'
                                  AND EnrollmentId <> @EnrollmentId)
            BEGIN
                ROLLBACK;
                THROW 57118, 'Marking this enrollment Promoted requires an existing Active enrollment for this student. Create/select the destination enrollment first, or use the Promotion function.', 1;
            END
        IF @FinalLeavingDate IS NOT NULL
           AND (@OldLeavingDate IS NULL
                OR @FinalLeavingDate <> @OldLeavingDate)
            BEGIN
                DECLARE @MaxBilledMonth AS DATE = (SELECT MAX(BillingMonth)
                                                   FROM   dbo.BillingCharges
                                                   WHERE  EnrollmentId = @EnrollmentId
                                                          AND Status <> N'Cancelled');
                IF @MaxBilledMonth IS NOT NULL
                   AND DATEFROMPARTS(YEAR(@FinalLeavingDate), MONTH(@FinalLeavingDate), 1) < @MaxBilledMonth
                    BEGIN
                        ROLLBACK;
                        THROW 57120, 'This enrollment already has billing charges for a later month than the requested leaving date. Cancel/correct those charges first, or choose a leaving date in or after the last billed month.', 1;
                    END
            END
        IF @FinalStatus = N'Active'
           AND EXISTS (SELECT 1
                       FROM   dbo.Enrollments WITH (UPDLOCK, HOLDLOCK)
                       WHERE  AdmissionId = @OldAdmissionId
                              AND Status = N'Active'
                              AND EnrollmentId <> @EnrollmentId)
            BEGIN
                ROLLBACK;
                THROW 57216, 'An Active enrollment already exists for this student. Select and correct the existing enrollment instead.', 1;
            END
        UPDATE dbo.Enrollments
        SET    Status         = @FinalStatus,
               EnrollmentDate = @FinalEnrollmentDate,
               LeavingDate    = @FinalLeavingDate,
               ClassSectionId = @FinalClassSectionId,
               UpdatedAt      = SYSDATETIME(),
               Remarks        = COALESCE (NULLIF (@Reason, N''), Remarks)
        WHERE  EnrollmentId = @EnrollmentId;
        IF @@ROWCOUNT <> 1
            BEGIN
                ROLLBACK;
                THROW 57115, 'The enrollment could not be updated.', 1;
            END
        INSERT  dbo.EnrollmentHistory (EnrollmentId, AdmissionId, ActionType, ActionDate, OldClassSectionId, NewClassSectionId, OldStatus, NewStatus, Remarks, UserId)
        VALUES                       (@EnrollmentId, @OldAdmissionId, N'CORRECTION', CONVERT (DATE, SYSDATETIME()), @OldClassSectionId, @FinalClassSectionId, @OldStatus, @FinalStatus, @Reason, @UserId);
        DECLARE @EditStudentStatusSynced AS BIT = 0;
        DECLARE @NewStudentStatus AS NVARCHAR (30) = @EditPriorStudentStatus;
        DECLARE @NewStudentWithdrawalDate AS DATE = @EditPriorStudentWithdrawalDate;
        IF @FinalStatus = N'Active'
           AND @EditPriorStudentStatus <> N'Active'
            BEGIN
                SET @NewStudentStatus = N'Active';
                SET @NewStudentWithdrawalDate = NULL;
                SET @EditStudentStatusSynced = 1;
            END
        ELSE
            IF @OldStatus = N'Active'
               AND @FinalStatus = N'Left'
                BEGIN
                    SET @NewStudentStatus = N'Left';
                    SET @NewStudentWithdrawalDate = @FinalLeavingDate;
                    SET @EditStudentStatusSynced = 1;
                END
        IF @EditStudentStatusSynced = 1
            BEGIN
                UPDATE dbo.Students
                SET    Status         = @NewStudentStatus,
                       WithdrawalDate = @NewStudentWithdrawalDate,
                       UpdatedAt      = SYSDATETIME()
                WHERE  AdmissionId = @OldAdmissionId;
                INSERT  dbo.StudentStatusHistory (AdmissionId, OldStatus, NewStatus, Reason, EffectiveDate, Remarks, CreatedBy)
                VALUES                          (@OldAdmissionId, @EditPriorStudentStatus, @NewStudentStatus, N'Synchronized by enrollment correction.', ISNULL(@NewStudentWithdrawalDate, CONVERT (DATE, SYSDATETIME())), @Reason, @UserId);
            END
        DECLARE @EditOperationId AS UNIQUEIDENTIFIER = NEWID();
        INSERT  dbo.AuditOperations (OperationId, UserId, OperationType, CorrelationNote)
        VALUES                     (@EditOperationId, @UserId, N'ENROLLMENT_CORRECTION', CONVERT (NVARCHAR (200), @OldAdmissionId));
        DECLARE @EditValues AS NVARCHAR (MAX) = (SELECT @EnrollmentId AS EnrollmentId,
                                                        @OldStatus AS OldStatus,
                                                        @FinalStatus AS NewStatus,
                                                        @OldEnrollmentDate AS OldEnrollmentDate,
                                                        @FinalEnrollmentDate AS NewEnrollmentDate,
                                                        @OldLeavingDate AS OldLeavingDate,
                                                        @FinalLeavingDate AS NewLeavingDate,
                                                        @OldClassSectionId AS OldClassSectionId,
                                                        @FinalClassSectionId AS NewClassSectionId,
                                                        @Reason AS Reason
                                                 FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
        EXECUTE dbo.WriteAuditLog @OperationId = @EditOperationId, @UserId = @UserId, @TableName = N'Enrollments', @RecordId = @EnrollmentId, @Action = N'CORRECTION', @OldValues = NULL, @NewValues = @EditValues;
        IF @EditStudentStatusSynced = 1
            BEGIN
                DECLARE @EditSyncValues AS NVARCHAR (MAX) = (SELECT @OldAdmissionId AS AdmissionId,
                                                                    @EditPriorStudentStatus AS OldStatus,
                                                                    @NewStudentStatus AS NewStatus,
                                                                    @EditPriorStudentWithdrawalDate AS OldWithdrawalDate,
                                                                    @NewStudentWithdrawalDate AS NewWithdrawalDate,
                                                                    @EnrollmentId AS EnrollmentId
                                                             FOR    JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
                EXECUTE dbo.WriteAuditLog @OperationId = @EditOperationId, @UserId = @UserId, @TableName = N'Students', @RecordId = @OldAdmissionId, @Action = N'STATUS_SYNC', @OldValues = NULL, @NewValues = @EditSyncValues;
            END
        DELETE dbo.AuditOperations WHERE OperationId=@CreateOperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@CreateOperationId);
DELETE dbo.AuditOperations WHERE OperationId=@EditOperationId AND NOT EXISTS(SELECT 1 FROM dbo.AuditLogs WHERE OperationId=@EditOperationId);
COMMIT TRANSACTION;
        SELECT @EnrollmentId AS EnrollmentId,
               @OldAdmissionId AS AdmissionId,
               @FinalStatus AS Status,
               CAST (1 AS BIT) AS Changed;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK;
        THROW;
    END CATCH
END



GO
