global using System.Data;
using FMSC_SMS.Pages.Finance.Configuration;
int checks=0;
void Check(bool value,string message) {if(!value)throw new Exception(message);checks++;}
DataSet Result(int total,int succeeded,int failed) {
 var data=new DataSet();var summary=new DataTable();summary.Columns.Add("TotalAssignments",typeof(int));summary.Columns.Add("Succeeded",typeof(int));summary.Columns.Add("Failed",typeof(int));summary.Rows.Add(total,succeeded,failed);data.Tables.Add(summary);return data;
}
var progress=new FeeRateApplyProgress(3);
int attempted=0;
try {
 foreach(string name in new[]{"Class A / Tuition","Class B / Tuition","Class C / Tuition"}) {
  progress.Begin(name);attempted++;
  if(attempted==2)throw new IOException("Controlled connection loss");
  progress.Confirm(FeeRateApplyResult.Read(Result(45,40,5)));
 }
} catch(IOException) {}
string interruption=progress.Interrupted();
Check(attempted==2,"Later groups must not run after interruption.");
Check(progress.Total==45 && progress.Succeeded==40 && progress.Failed==5,"Only confirmed counts may be reported.");
Check(interruption.Contains("Class A / Tuition") && interruption.Contains("Class B / Tuition") && interruption.Contains("unknown"),"Completed and uncertain groups must be distinguished.");
Check(interruption.Contains("1 later group(s) were not started"),"Unstarted groups must be reported.");
Check(interruption.Contains("Review Fee Changes before retrying"),"Recovery must require another read-only review.");
var complete=new FeeRateApplyProgress(2);
complete.Begin("A");complete.Confirm(FeeRateApplyResult.Read(Result(40,40,0)));
complete.Begin("B");complete.Confirm(FeeRateApplyResult.Read(Result(30,30,0)));
Check(complete.Total==70 && complete.Succeeded==70 && complete.PendingGroup is null,"Complete batch counts must be preserved.");
Check(!complete.Interrupted().Contains("outcome for") && complete.Interrupted().Contains("refresh"),"A display/refresh failure must preserve known outcomes.");
foreach(var data in new[]{Result(10,8,1),Result(-1,0,0),new DataSet()}) {
 bool rejected=false;try{FeeRateApplyResult.Read(data);}catch(InvalidOperationException){rejected=true;}
 Check(rejected,"Invalid results must not become confirmed outcomes.");
}
Console.WriteLine($"PASS {checks} fee-rate outcome checks, including controlled interruption");
