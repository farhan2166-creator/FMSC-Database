-- Reference only; deploy numbered migrations, never execute directly on school data.

CREATE FUNCTION dbo.GetMonthlyBillingStartDate(@EventDate date)
RETURNS date
AS
BEGIN
 DECLARE @Cutoff int=(SELECT TOP(1) FeeProrationCutoffDay FROM dbo.SchoolProfile ORDER BY SchoolProfileId);
 RETURN CASE WHEN @Cutoff BETWEEN 1 AND 28 AND DAY(@EventDate)>=@Cutoff
  THEN DATEADD(month,1,DATEFROMPARTS(YEAR(@EventDate),MONTH(@EventDate),1)) ELSE @EventDate END;
END;
