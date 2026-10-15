-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
    DECLARE @Snapshot nvarchar(max),@Hash varchar(64);
    -- Both result sets describe one consistent captured payment dataset.
    EXEC dbo.DailyClosingSnapshot @ClosingDate,@Snapshot OUTPUT,@Hash OUTPUT;
    SELECT P.Method [Payment Method],COUNT(DISTINCT COALESCE(NULLIF(P.Receipt,N''),CONCAT('PAYMENT-',P.Id))) Receipts,
        SUM(P.Amount) [Recorded Amount],A.Amount [Actual Amount],
        CAST(NULL AS decimal(18,2)) Difference
    FROM OPENJSON(@Snapshot) WITH(Id int '$.FeePaymentId',Method nvarchar(30) '$.PaymentMethod',
        Receipt nvarchar(40) '$.ReceiptNumber',Amount decimal(18,2) '$.TotalAmount') P
    OUTER APPLY(SELECT TOP(1) J.Amount FROM dbo.DailyCollectionClosings C
        CROSS APPLY OPENJSON(C.ActualAmounts) WITH(Method nvarchar(30),Amount decimal(18,2)) J
        WHERE C.ClosingId=(SELECT MAX(ClosingId) FROM dbo.DailyCollectionClosings WHERE ClosingDate=@ClosingDate)
        AND J.Method=P.Method) A
    GROUP BY P.Method,A.Amount ORDER BY P.Method;
    SELECT @Hash SnapshotHash,COALESCE(C.ClosingId,0) ClosingId,COALESCE(C.Revision,0) Revision,
        CASE WHEN C.ClosingId IS NULL THEN N'Open'
             WHEN C.SnapshotHash<>@Hash THEN N'Needs review â€” payments changed'
             WHEN C.ApprovedAt IS NOT NULL THEN N'Approved' ELSE N'Submitted' END ClosingStatus,
        COALESCE(C.Notes,N'') Notes,U.Username SubmittedBy,C.SubmittedAt,V.Username ApprovedBy,C.ApprovedAt
    FROM (SELECT 1 X) X OUTER APPLY(SELECT TOP(1)* FROM dbo.DailyCollectionClosings
        WHERE ClosingDate=@ClosingDate ORDER BY Revision DESC) C
    LEFT JOIN dbo.Users U ON U.UserId=C.SubmittedBy LEFT JOIN dbo.Users V ON V.UserId=C.ApprovedBy;
END;
