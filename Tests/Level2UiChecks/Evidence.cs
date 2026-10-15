using System.Text.Json;

internal sealed class Scenario
{
    public string Id {get;set;}="";
    public string Module {get;set;}="";
    public string ScenarioName {get;set;}="";
    public string Expected {get;set;}="";
    public string Actual {get;set;}="Not executed.";
    public string Status {get;set;}="Not Run";
    public string Evidence {get;set;}="";
    public string Defect {get;set;}="";
    public string DatabaseVerification {get;set;}="Not executed.";
}
internal sealed class Evidence
{
    internal List<Scenario> Scenarios {get;}
    internal string Output {get;}
    internal string Stage="Preflight";
    internal Evidence(string output)
    {
        Output=output; Directory.CreateDirectory(output);
        Scenarios=JsonSerializer.Deserialize<List<Scenario>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"scenarios.json")))!;
    }
    internal void Block(string reason)
    {
        foreach(var s in Scenarios.Where(s=>s.Status=="Not Run")) {s.Status="Blocked";s.Actual=reason;}
    }
    internal void Run(string id,Desktop desktop,Action action,string databaseResult="No unintended write observed.")
    {
        var s=Scenarios.Single(s=>s.Id==id); Stage=id;
        try
        {
            action(); s.Status="Pass";s.Actual=s.Expected;s.DatabaseVerification=databaseResult;
        }
        catch(Exception ex)
        {
            s.Status="Fail";s.Actual=ex is TimeoutException or InvalidOperationException?ex.Message:"Assertion or automation failure ("+ex.GetType().Name+").";
            s.Defect="Investigate application behavior versus locator/fixture failure; not yet classified.";
        }
        try {s.Evidence=desktop.Screenshot(id);desktop.Dump(id);} catch {s.Evidence="Capture unavailable; see outcome.";}
        Save(); Console.WriteLine(s.Status+": "+id+" "+s.ScenarioName);
    }
    internal bool Passed(string id)=>Scenarios.Single(s=>s.Id==id).Status=="Pass";
    internal void BlockIds(string reason,params string[] ids)
    {
        foreach(var s in Scenarios.Where(s=>ids.Contains(s.Id)&&s.Status=="Not Run")) {s.Status="Blocked";s.Actual=reason;}
    }
    internal void Save()
    {
        var counts=new[]{"Pass","Fail","Blocked","Not Run"}.ToDictionary(x=>x,x=>Scenarios.Count(s=>s.Status==x));
        var options=new JsonSerializerOptions{WriteIndented=true};
        File.WriteAllText(Path.Combine(Output,"report.json"),JsonSerializer.Serialize(new {
            level=2,sourceCommit=Environment.GetEnvironmentVariable("FMSC_LEVEL2_COMMIT"),runId=Identity.Owner,
            utc=DateTime.UtcNow,stage=Stage,total=Scenarios.Count,counts,scenarios=Scenarios,
            display=System.Windows.Forms.Screen.AllScreens.Select(s=>new {width=s.Bounds.Width,height=s.Bounds.Height,primary=s.Primary}).ToArray(),
            level3Ready=counts["Fail"]==0&&counts["Blocked"]==0&&counts["Not Run"]==0,
            limitations=new[]{"Only Pass rows were executed successfully through the external desktop driver.",
                "Reference fixture creation is not UI workflow coverage.","No genuine messages, physical printing, production records or live upgrades.",
                "Only the current desktop resolution and DPI are exercised unless an explicit matrix is recorded."}
        },options));
        static string Cell(string x)=>x.Replace("|","/").Replace("\r"," ").Replace("\n"," ");
        var lines=new List<string>{"# FMSC_SMS Level 2 desktop evidence","",
            "Commit: "+Environment.GetEnvironmentVariable("FMSC_LEVEL2_COMMIT"),"",
            "Total: "+Scenarios.Count+"; "+string.Join("; ",counts.Select(x=>x.Key+": "+x.Value)),"",
            "| Test ID | Module | Scenario | Expected | Actual | Status | Evidence | Defect | Database verification |",
            "|---|---|---|---|---|---|---|---|---|"};
        lines.AddRange(Scenarios.Select(s=>"| "+string.Join(" | ",new[]{s.Id,s.Module,s.ScenarioName,s.Expected,s.Actual,s.Status,s.Evidence,s.Defect,s.DatabaseVerification}.Select(Cell))+" |"));
        lines.AddRange(new[]{"","Readiness for Level 3: "+(counts["Fail"]+counts["Blocked"]+counts["Not Run"]==0?"Ready":"Not established"),
            "","Screenshots cover only this fictional test app. Unexecuted scenarios are never counted as passes."});
        File.WriteAllLines(Path.Combine(Output,"summary.md"),lines);
    }
}
