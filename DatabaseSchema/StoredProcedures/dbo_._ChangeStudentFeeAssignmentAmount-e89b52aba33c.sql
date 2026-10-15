-- Module body only. See companion XML for signatures and relationships.

BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission @UserId, N'Fees.Adjust';

    EXEC dbo.ChangeStudentFeeAssignmentAmount_Internal
        @UserId = @UserId,
        @StudentFeeAssignmentId = @StudentFeeAssignmentId,
        @NewAmount = @NewAmount,
        @EffectiveFrom = @EffectiveFrom,
        @Reason = @Reason;
END;
