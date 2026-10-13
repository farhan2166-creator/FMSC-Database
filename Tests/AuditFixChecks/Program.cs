using FMSC_SMS.Core;
using System.Data;

int checks=0;
void Assert(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
void Reject(Action action,string message){try{action();}catch(ArgumentException){checks++;return;}throw new Exception(message);}

foreach(decimal amount in new[]{0m,1m,999m,1000.00m,-100m,9999999999m})
{
    WholeRupees.Require(amount);
    Assert(WholeRupees.IsWhole(amount),"Whole rupees must be retained exactly");
}
foreach(decimal amount in new[]{.01m,.50m,.99m,999.50m,999.999m,-.50m})
{
    Reject(()=>WholeRupees.Require(amount),"Fraction must never be rounded");
    Reject(()=>WholeRupees.ValidateParameter("@Amount",amount),"Scalar fee guard");
    Reject(()=>WholeRupees.ValidateParameter("@discountamount",amount),"Case-insensitive scalar fee guard");
    Reject(()=>Tvp.AdmissionFeeDiscounts(new[]{(1,amount)}),"QR/admission discounts");
    Reject(()=>Tvp.StudentFeeDiscounts(new[]{(1,amount)}),"Assignment discounts");
    Reject(()=>Tvp.ReturnFeeChoices(new[]{(1,amount,0m)}),"Readmission rates");
    Reject(()=>Tvp.FeeConfigurationPlan(new[]{(1,1,1,1,amount,(string?)"Monthly",(string?)null)}),"Bulk/configuration rates");
    Reject(()=>Tvp.FeeAllocations(new[]{(1L,amount)}),"Collection allocations");
    Reject(()=>Tvp.FamilyAllocations(new[]{(1,1,(IEnumerable<(long,decimal)>)new[]{(1L,amount)})}),"Family collection allocations");
}
Assert((decimal)Tvp.FeeAllocations(new[]{(1L,-100m)}).Rows[0]["Amount"]==-100m,"Whole-rupee negative discount allocations remain valid");
Assert((decimal)WholeRupees.ValidateParameter("@ExpectedBalance",999.50m)! == 999.50m,"Existing fractional state can be inspected without rounding");
Assert(WholeRupees.ValidateParameter("@Amount",null) is null,"Optional monetary parameters");

DataTable Fees(){var t=new DataTable();t.Columns.Add("FeeTypeId",typeof(int));t.Columns.Add("Amount",typeof(decimal));t.Columns.Add("Frequency",typeof(string));t.Columns.Add("IsMandatory",typeof(bool));t.Columns.Add("IsTransportLinked",typeof(bool));t.Rows.Add(1,2000m,"Monthly",true,false);t.Rows.Add(2,500m,"Yearly",false,false);return t;}
Assert(InputParse.TryWholeAmount("Rs. 1000.00",out decimal parsedWhole)&&parsedWhole==1000m,"Whole-rupee UI parsing accepts currency prefixes");
Assert(!InputParse.TryWholeAmount("999.50",out decimal fractionalDraft)&&fractionalDraft==999.50m,"Fractional UI draft is rejected without rounding");
Assert(!InputParse.TryWholeAmount("999.999",out _),"Sub-paisa input cannot round into an accepted fee");
var original=Fees();string quote=QuickRegistrationQuote.Create(original);
Assert(QuickRegistrationQuote.Create(original.Copy())==quote,"Unchanged rates accepted");
var reordered=original.Clone();reordered.ImportRow(original.Rows[1]);reordered.ImportRow(original.Rows[0]);
Assert(QuickRegistrationQuote.Create(reordered)==quote,"SQL row order is irrelevant");
foreach(var change in new (string,object)[]{("Amount",2500m),("Frequency","Yearly"),("IsMandatory",false),("IsTransportLinked",true)})
{var t=original.Copy();t.Rows[0][change.Item1]=change.Item2;Assert(QuickRegistrationQuote.Create(t)!=quote,"Rate/fee policy drift must invalidate quote");}
var added=original.Copy();added.Rows.Add(3,100m,"Monthly",false,false);Assert(QuickRegistrationQuote.Create(added)!=quote,"New fee invalidates quote");
var removed=original.Copy();removed.Rows.RemoveAt(1);Assert(QuickRegistrationQuote.Create(removed)!=quote,"Removed fee invalidates quote");
var missing=original.Copy();missing.Rows[0]["Amount"]=DBNull.Value;Assert(QuickRegistrationQuote.Create(missing)!=quote,"Missing configuration invalidates quote");
Console.WriteLine($"PASS: {checks} production money/TVP and quotation regression checks.");
Console.WriteLine($"PASS: {RemainingFindingChecks.Run()} role, literal search and working-area checks.");
Console.WriteLine($"PASS: {CompatibilityGuidanceChecks.Run()} database compatibility guidance checks.");
Console.WriteLine($"PASS: {ConnectionRecoveryChecks.Run()} connection recovery explanation and action checks.");
Console.WriteLine($"PASS: {await DurabilityChecks.RunAsync()} settings, photo input and payment journal durability checks.");
Console.WriteLine($"PASS: {DashboardChecks.Run()} dashboard count and academic-year checks.");
Console.WriteLine($"PASS: {await RegistrationPhotoChecks.RunAsync()} registration view and photo save/retry checks.");
Console.WriteLine($"PASS: {await AdmissionCompressionChecks.RunAsync()} admission confirmation and compact photo compression checks.");
Console.WriteLine($"PASS: {await FilePersistenceChecks.RunAsync()} logo preflight, report settings recovery and export durability checks.");
Console.WriteLine($"PASS: {AdministrativeAccessChecks.Run()} administrative drafts, access previews and transaction-outcome checks.");
Console.WriteLine($"PASS: {ThemeAppearanceChecks.Run()} lightweight palettes, saved preferences and refresh delivery checks.");

if(args.Contains("--profile-search"))ReviewFilterProfile.Run();
