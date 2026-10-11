

/* ============================================================================
   dbo.ChangeStudentFeeAssignmentAmount
   Now just an authorization wrapper: checks Fees.Adjust, then delegates
   to the internal procedure above. Callers (C# included) see no change
   in behavior, parameters, result shape, or error codes.
============================================================================ */
CREATE   PROCEDURE dbo.ChangeStudentFeeAssignmentAmount
    @UserId INT,
    @StudentFeeAssignmentId INT,
    @NewAmount DECIMAL(12,2),
    @EffectiveFrom DATE,
    @Reason NVARCHAR(500)
AS
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

