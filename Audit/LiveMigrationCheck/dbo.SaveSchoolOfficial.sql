CREATE   PROCEDURE dbo.SaveSchoolOfficial
    @UserId int,@SchoolOfficialId int=0,@Designation nvarchar(100),@PersonName nvarchar(150)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.RequirePermission @UserId,N'System.Setup';
    SET @Designation=LTRIM(RTRIM(@Designation));
    SET @PersonName=LTRIM(RTRIM(@PersonName));
    IF NULLIF(@Designation,N'') IS NULL THROW 59100,'Designation is required.',1;
    IF NULLIF(@PersonName,N'') IS NULL THROW 59101,'Person name is required.',1;
    BEGIN TRANSACTION;
    IF EXISTS(SELECT 1 FROM dbo.SchoolOfficials WITH(UPDLOCK,HOLDLOCK) WHERE Designation=@Designation AND SchoolOfficialId<>@SchoolOfficialId)
        THROW 59102,'An official with this designation already exists. Edit their record instead.',1;
    IF @SchoolOfficialId=0
    BEGIN
        INSERT dbo.SchoolOfficials(Designation,PersonName,CreatedByUserId,UpdatedByUserId) VALUES(@Designation,@PersonName,@UserId,@UserId);
        SET @SchoolOfficialId=CONVERT(int,SCOPE_IDENTITY());
    END
    ELSE
    BEGIN
        UPDATE dbo.SchoolOfficials SET Designation=@Designation,PersonName=@PersonName,UpdatedAt=SYSDATETIME(),UpdatedByUserId=@UserId WHERE SchoolOfficialId=@SchoolOfficialId;
        IF @@ROWCOUNT=0 THROW 59103,'The school official was not found.',1;
    END;
    COMMIT;
    SELECT @SchoolOfficialId AS SchoolOfficialId;
END;

