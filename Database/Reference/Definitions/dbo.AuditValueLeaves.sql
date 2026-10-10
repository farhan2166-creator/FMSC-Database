SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
-- Audit paths are flattened only for changed values, including nested objects
-- and bulk arrays. JSON types, explicit nulls and absent properties remain distinct.
CREATE   FUNCTION dbo.AuditValueLeaves(@Json nvarchar(max))
RETURNS @Leaves TABLE (Path nvarchar(4000), FieldName nvarchar(4000), Value nvarchar(max), JsonType int)
AS
BEGIN
    IF @Json IS NULL OR ISJSON(@Json)<>1 RETURN;
    DECLARE @Nodes TABLE (NodeId int IDENTITY, Path nvarchar(4000), FieldName nvarchar(4000), Value nvarchar(max), JsonType int);
    INSERT @Nodes(Path,FieldName,Value,JsonType)
        VALUES(N'',N'',@Json,CASE WHEN LEFT(LTRIM(@Json),1)=N'[' THEN 4 ELSE 5 END);
    DECLARE @Id int=1,@Max int,@Path nvarchar(4000),@Name nvarchar(4000),@Value nvarchar(max),@Type int;
    WHILE EXISTS(SELECT 1 FROM @Nodes WHERE NodeId=@Id)
    BEGIN
        SELECT @Path=Path,@Name=FieldName,@Value=Value,@Type=JsonType FROM @Nodes WHERE NodeId=@Id;
        IF @Type IN (4,5) AND EXISTS(SELECT 1 FROM OPENJSON(@Value))
        BEGIN
            INSERT @Nodes(Path,FieldName,Value,JsonType)
            SELECT CASE WHEN @Type=4 THEN CONCAT(@Path,N'[',[key],N']')
                        WHEN @Path=N'' THEN [key] ELSE CONCAT(@Path,N'.',[key]) END,
                   [key],value,type
            FROM OPENJSON(@Value)
            WHERE [key] NOT IN (N'CreatedAt',N'UpdatedAt',N'CreatedByUserId',N'UpdatedByUserId',N'PasswordHash',N'PasswordSalt');
        END
        ELSE INSERT @Leaves VALUES(CASE WHEN @Path=N'' THEN N'$' ELSE @Path END,@Name,@Value,@Type);
        SET @Id+=1;
    END;
    RETURN;
END;


GO
