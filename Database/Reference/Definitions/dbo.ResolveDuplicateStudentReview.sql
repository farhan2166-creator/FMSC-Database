SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE   PROCEDURE dbo.ResolveDuplicateStudentReview
    @UserId   INT,
    @ReviewId INT,
    @Status   NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequirePermission @UserId, N'Students.Administer';

    IF @Status NOT IN (N'Confirmed', N'Dismissed')
        THROW 52701, 'Status must be Confirmed or Dismissed.', 1;

    UPDATE dbo.DuplicateStudentReviews
    SET
        Status = @Status,
        ReviewedByUserId = @UserId,
        ReviewedAt = SYSDATETIME()
    WHERE ReviewId = @ReviewId;

    IF @@ROWCOUNT = 0
        THROW 52702, 'The candidate pair was not found - it may have already been resolved.', 1;
END;

GO
