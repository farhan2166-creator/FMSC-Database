SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
/* ============================================================================
   FMSC_SMS - Expose FeeProrationCutoffDay through SchoolProfile
   ============================================================================
   Companion to sql/AddBillingStartDateAndFeeProration.sql, which added the
   dbo.SchoolProfile.FeeProrationCutoffDay column but did not touch the two
   procedures the School Information screen (SchoolProfilePage.cs) actually
   calls. Run this AFTER that script.

   @FeeProrationCutoffDay defaults to NULL so existing callers (and the
   current UI, until it's updated) keep behaving exactly as they do today.
============================================================================ */

CREATE PROCEDURE dbo.GetSchoolProfile
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.RequireAuthenticatedUser @UserId;

    SELECT
        SchoolProfileId,
        SchoolName,
        AddressLine,
        City,
        ContactNumber,
        Email,
        LogoImage,
        LogoFileName,
        LogoContentType,
        FeeProrationCutoffDay,
        CreatedAt,
        CreatedByUserId,
        UpdatedAt,
        UpdatedByUserId
    FROM dbo.SchoolProfile
    WHERE SchoolProfileId = 1;
END;

GO
