SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE   PROCEDURE dbo.ReserveAdmissionNumber
    @AdmissionNumber VARCHAR(40) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF @@TRANCOUNT = 0 THROW 59201, 'Admission number allocation requires an admission transaction.', 1;
    DECLARE @Last BIGINT;
    SELECT @Last=LastNumber FROM dbo.AdmissionNumberCounter WITH (UPDLOCK,HOLDLOCK) WHERE CounterId=1;
    IF @Last IS NULL THROW 59202, 'The admission number counter is missing.', 1;
    IF NULLIF(LTRIM(RTRIM(@AdmissionNumber)), '') IS NULL
        SET @AdmissionNumber=CONVERT(VARCHAR(40), @Last+1);
    SET @AdmissionNumber=dbo.NormalizeAdmissionNumber(@AdmissionNumber);
    IF @AdmissionNumber IS NULL THROW 59203, 'Use a positive admission number with an optional letter suffix, such as 1 or 1a.', 1;
    IF EXISTS (SELECT 1 FROM dbo.Students WHERE AdmissionNumber=@AdmissionNumber)
        THROW 59204, 'This admission number is already assigned to another student. Choose a different number.', 1;
    DECLARE @Base BIGINT = TRY_CONVERT(BIGINT, LEFT(@AdmissionNumber, PATINDEX('%[^0-9]%', @AdmissionNumber+'x')-1));
    IF @Base>@Last UPDATE dbo.AdmissionNumberCounter SET LastNumber=@Base WHERE CounterId=1;
END;

GO
