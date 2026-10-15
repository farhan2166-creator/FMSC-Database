-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;
    EXEC dbo.RequirePermission @UserId,N'Fees.Collect';
    SELECT C.Revision,C.SubmittedAt [Submitted At],U.Username [Submitted By],
        C.ApprovedAt [Approved At],V.Username [Approved By],
        R.Method [Payment Method],R.Recorded [Recorded Amount],A.Amount [Actual Amount],
        A.Amount-R.Recorded Difference,C.Notes
    FROM dbo.DailyCollectionClosings C
    LEFT JOIN dbo.Users U ON U.UserId=C.SubmittedBy LEFT JOIN dbo.Users V ON V.UserId=C.ApprovedBy
    OUTER APPLY(SELECT P.Method,SUM(P.Amount) Recorded FROM OPENJSON(C.PaymentSnapshot)
        WITH(Method nvarchar(30) '$.PaymentMethod',Amount decimal(18,2) '$.TotalAmount') P GROUP BY P.Method) R
    OUTER APPLY(SELECT P.Amount FROM OPENJSON(C.ActualAmounts) WITH(Method nvarchar(30),Amount decimal(18,2)) P
        WHERE P.Method=R.Method) A
    WHERE C.ClosingDate=@ClosingDate ORDER BY C.Revision DESC,R.Method;
END;
