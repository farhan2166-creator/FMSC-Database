using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckSavedViews()
    {
        var day=new DateTime(2026,10,23,23,59,0);
        Check(ReportDatePeriod.Resolve("Today",day)==(new DateTime(2026,10,23),new DateTime(2026,10,23)),"Today uses the current calendar date without time");
        Check(ReportDatePeriod.Resolve("This month",day)==(new DateTime(2026,10,1),new DateTime(2026,10,31)),"This month covers the complete calendar month");
        Check(ReportDatePeriod.Resolve("Previous month",new DateTime(2026,1,12))==(new DateTime(2025,12,1),new DateTime(2025,12,31)),"Previous month crosses the year boundary");
        Check(ReportDatePeriod.Resolve("Previous month",new DateTime(2024,3,1))==(new DateTime(2024,2,1),new DateTime(2024,2,29)),"Relative periods retain leap-year February");
        Check(ReportDatePeriod.Resolve("This calendar year",day)==(new DateTime(2026,1,1),new DateTime(2026,12,31)),"Calendar year is explicitly distinct from academic year");
        Check(ReportDatePeriod.Resolve("All dates",day)==(null,null),"All dates leaves both optional bounds unrestricted");
        Check(ReportDatePeriod.Resolve("Today",day.AddDays(1)).From==new DateTime(2026,10,24),"A saved Today preset resolves anew on a later day");
        bool refused=false;try { ReportDatePeriod.Resolve("Unsupported",day); } catch(ArgumentException) { refused=true; }
        Check(refused,"Unknown date modes cannot silently widen scope");
        string folder=Path.Combine(Path.GetTempPath(),"fmsc-preset-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            string path=Path.Combine(folder,"user-1.json");
            // The production publisher uses a Windows session mutex. Keep that path
            // on Windows; portable checks exercise the settings operations through
            // the existing publisher seam instead of changing production locking.
            Action<string,string>? publisher=OperatingSystem.IsWindows() ? null : (target,json)=>{
                string temporary=target+".test-tmp";
                File.WriteAllText(temporary,json);
                File.Move(temporary,target,overwrite:true);
            };
            var file=new ReportSettingsFile(path,publish:publisher);
            var initial=file.Update(data=>{
                data.Views["students"]=new() { new ReportSavedView {Name="Legacy class",State=new() { ["mode"]="list",["limit"]="1",["from"]="2026-01-01",["to"]="2026-10-23" }} };
                data.Views["finance.collections"]=new() {new ReportSavedView {Name="Daily cash",State=new() { ["$version"]="1",["$dates"]="Today",["Campus"]="id:2",["Payment method"]="choice:Cash" }},new ReportSavedView {Name="Other",State=new()}};
                data.Layouts["finance.collections.Receipts.Campus"]=new() {"Receipt / Reference","Money Received"};
                data.Preferences["students.last-scope"]="unchanged";return true;
            });
            Check(initial.Succeeded,"New and existing student saved views share the established settings file: "+initial.Error);
            byte[] before=File.ReadAllBytes(path);
            var collision=file.Update(data=>ReportViewOperations.Rename(data,"finance.collections","Daily cash","OTHER"));
            Check(!collision.Succeeded && File.ReadAllBytes(path).SequenceEqual(before),"Rename collision preserves both views and the original file");
            var renamed=file.Update(data=>ReportViewOperations.Rename(data,"finance.collections","daily CASH","  Today cash  "));
            Check(renamed is {Succeeded:true,Changed:true},"Rename is case-insensitive and trims the new name atomically");
            var restored=new ReportSettingsFile(path).Read();
            Check(restored.Views["finance.collections"].Single(v=>v.Name=="Today cash").State["$dates"]=="Today","Renaming preserves relative dates and filter state after reopening");
            Check(restored.Views["students"][0].State["limit"]=="1" && restored.Preferences["students.last-scope"]=="unchanged"
                && restored.Layouts["finance.collections.Receipts.Campus"].Count==2,"New operations preserve legacy student views, last scope and column layouts");
            Check(file.Update(data=>ReportViewOperations.Rename(data,"finance.collections","Absent","New")) is {Succeeded:true,Changed:false},"Renaming an absent view never creates a replacement");
            before=File.ReadAllBytes(path);
            var failure=new ReportSettingsFile(path,publish:(_,_)=>throw new IOException("Injected write failure"));
            Check(!failure.Update(data=>ReportViewOperations.Rename(data,"finance.collections","Today cash","Renamed")).Succeeded
                && File.ReadAllBytes(path).SequenceEqual(before),"A failed rename write leaves the existing settings intact");
            foreach(string invalid in new[]{"", "  ", new string('x',61)})
                Check(!file.Update(data=>ReportViewOperations.Rename(data,"finance.collections","Today cash",invalid)).Succeeded,"Invalid view names are refused without replacing saved views");
            Check(ReportViewOperations.ValidName(new string('x',60)).Length==60,"View name boundary remains compatible with the existing name dialog");
            var user2=new ReportSettingsFile(Path.Combine(folder,"user-2.json"));
            Check(user2.Read().Views.Count==0,"Separate user settings files do not inherit another user's saved views");
        }
        finally { Directory.Delete(folder,true); }
    }
}
