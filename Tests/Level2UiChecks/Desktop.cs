using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

internal sealed class Desktop : IDisposable
{
    internal readonly UIA3Automation Automation=new();
    internal Process? Process;
    internal string EvidenceDirectory {get;}
    internal Desktop(string evidenceDirectory)=>EvidenceDirectory=evidenceDirectory;
    [DllImport("user32.dll")] private static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd,IntPtr hdc,uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
    [StructLayout(LayoutKind.Sequential)] private struct RECT {public int Left,Top,Right,Bottom;}
    internal static void RequireInteractive()
    {
        if(!Environment.UserInteractive || System.Diagnostics.Process.GetCurrentProcess().SessionId==0)
            throw new InvalidOperationException("An interactive unlocked Windows session is required.");
        IntPtr desktop=OpenInputDesktop(0,false,0x0100); // DESKTOP_SWITCHDESKTOP
        if(desktop==IntPtr.Zero) throw new InvalidOperationException("The input desktop is unavailable or locked.");
        CloseDesktop(desktop);
    }
    internal void Launch(string exe)
    {
        RequireInteractive(); using var c=Identity.Open();
        var start=new ProcessStartInfo(exe) {UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(exe)!};
        Process=System.Diagnostics.Process.Start(start)??throw new InvalidOperationException("App did not start.");
        File.WriteAllText(Path.Combine(Identity.Root,"app-process.json"),System.Text.Json.JsonSerializer.Serialize(new {pid=Process.Id,executable=Path.GetFullPath(exe)}));
    }
    internal Window? TryWindow(string? title=null)=>Automation.GetDesktop().FindAllChildren(cf=>cf.ByProcessId(Process!.Id))
        .SelectMany(root=>new[]{root}.Concat(root.FindAllDescendants(cf=>cf.ByControlType(ControlType.Window))))
        .Where(e=>e.ControlType==ControlType.Window && !e.IsOffscreen && (title==null || e.Name.Contains(title,StringComparison.OrdinalIgnoreCase)))
        .Select(e=>e.AsWindow()).LastOrDefault();
    internal Window Window(string? title=null)=>Wait(()=>TryWindow(title),"application window "+title);
    internal static T Wait<T>(Func<T?> lookup,string description,int seconds=20) where T:class
    {
        var timer=Stopwatch.StartNew();
        while(timer.Elapsed.TotalSeconds<seconds)
        {
            RequireInteractive();
            try {var found=lookup(); if(found!=null)return found;}
            catch(InvalidOperationException) { }
            catch(COMException) { } // Startup closes its connection form before login opens.
            Thread.Sleep(150);
        }
        throw new TimeoutException("Timed out waiting for "+description);
    }
    internal AutomationElement Find(string name,ControlType? type=null,AutomationElement? scope=null)=>Wait(()=>
        TryFind(name,type,scope),"control "+name);
    internal AutomationElement? TryFind(string name,ControlType? type=null,AutomationElement? scope=null)
    {
        AutomationElement? Search(AutomationElement root)=>root.FindAllDescendants(cf=>type.HasValue?
            cf.ByName(name).Or(cf.ByAutomationId("Level2Input_"+System.Text.RegularExpressions.Regex.Replace(name,@"[^A-Za-z0-9]+","_"))).And(cf.ByControlType(type.Value)):
            cf.ByName(name)).FirstOrDefault(e=>!e.IsOffscreen);
        var match=Search(scope??Window());
        // Date selectors and native menus are owned top-level popup surfaces.
        return match??(scope==null?Automation.GetDesktop().FindAllChildren(cf=>cf.ByProcessId(Process!.Id))
            .Select(Search).FirstOrDefault(found=>found!=null):null);
    }
    internal void Click(string name,ControlType? type=null,AutomationElement? scope=null,bool twice=false)
    {
        using var c=Identity.Open(); RequireInteractive();
        var e=Find(name,type,scope);
        Identity.Check(e.IsEnabled && !e.IsOffscreen,"Control is unavailable: "+name);
        if(twice)e.DoubleClick(); else e.Click();
    }
    internal void ClickElement(AutomationElement e,bool twice=false)
    {
        using var c=Identity.Open();RequireInteractive();
        Identity.Check(e.Properties.ProcessId.Value==Process!.Id&&e.IsEnabled&&!e.IsOffscreen,"Grid input must belong to this visible test application.");
        if(twice)e.DoubleClick();else e.Click();
    }
    internal void Fill(string name,string value,AutomationElement? scope=null)
    {
        using var c=Identity.Open(); RequireInteractive();
        var e=Wait(()=>{
            var candidate=Find(name,ControlType.Edit,scope);
            return candidate.IsEnabled&&!candidate.IsOffscreen?candidate:null;
        },"editable "+name); e.Click();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL,VirtualKeyShort.KEY_A);
        if(value.Length==0)Keyboard.Type(VirtualKeyShort.BACK);
        else Keyboard.Type(value);
    }
    internal void Select(string name,string text)
    {
        using var c=Identity.Open(); RequireInteractive();
        var e=Wait(()=>Find(name,ControlType.ComboBox) is {IsEnabled:true} found?found:null,"enabled combo "+name);
        // Click the observed dropdown item: programmatic Select and incremental
        // search are unreliable for owner-drawn WinForms lists with shared prefixes.
        e.Click();
        try
        {
            var item=Wait(()=>e.FindFirstDescendant(cf=>cf.ByName(text).And(cf.ByControlType(ControlType.ListItem)))
                ??Automation.GetDesktop().FindAllChildren(cf=>cf.ByProcessId(Process!.Id))
                    .Select(root=>root.FindFirstDescendant(cf=>cf.ByName(text).And(cf.ByControlType(ControlType.ListItem))))
                    .FirstOrDefault(found=>found!=null&&!found.IsOffscreen),"dropdown item "+text);
            item.Click();
        }
        catch {Key(VirtualKeyShort.ESCAPE);throw;}
        Identity.Check(e.AsComboBox().Value==text,"Combo selection did not persist: "+name);
    }
    internal void PickStudent(string field="Student (Admission Number / Name)")
    {
        Fill(field,"Fictional Example Student");
        Wait(()=>Window().FindFirstDescendant(cf=>cf.ByControlType(ControlType.ListItem)),"student search match",10);
        Key(VirtualKeyShort.DOWN);Key(VirtualKeyShort.RETURN);
    }
    internal void OpenStudent()
    {
        Click("Find Student",ControlType.Button);
        var dialog=Window("Find Student");
        Fill("Search","Fictional Example Student",dialog);
        Click("Search",ControlType.Button,dialog);
        Wait(()=>TryFind("Open Student",ControlType.Button,dialog) is {IsEnabled:true} e?e:null,"student search result");
        Click("Open Student",ControlType.Button,dialog);
        Wait(()=>Find("Student Name",ControlType.Edit) is {IsEnabled:true} e?e:null,"admission load completed");
    }
    internal void Navigate(string page,string group="Daily Work")
    {
        if(TryFind(page,ControlType.Button)==null)Click(group,ControlType.Button);
        Click(page,ControlType.Button);
    }
    internal void Date(string name,DateTime date)
    {
        using var c=Identity.Open();RequireInteractive();
        Find(name,ControlType.ComboBox).Focus();Key(VirtualKeyShort.F4);
        Fill("Year",date.Year.ToString());
        Select("Month",System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(date.Month));
        Find("Day calendar").Focus();Key(VirtualKeyShort.HOME);
        // The fictional workflow dates are deliberately the first of the month.
        Identity.Check(date.Day==1,"Date helper requires an explicit first-of-month fixture date.");
        Click("Use Date",ControlType.Button);
        string actual=Find(name,ControlType.ComboBox).AsComboBox().Value;
        Identity.Check(DateTime.TryParse(actual,out var chosen)&&chosen.Date==date.Date,"Calendar choice did not persist: "+name);
    }
    internal void AssertMissingChoice(string name,string prefix)
    {
        using var c=Identity.Open();RequireInteractive();
        var combo=Find(name,ControlType.ComboBox);combo.Click();
        var items=Automation.GetDesktop().FindAllChildren(cf=>cf.ByProcessId(Process!.Id))
            .SelectMany(root=>root.FindAllDescendants(cf=>cf.ByControlType(ControlType.ListItem)));
        Identity.Check(!items.Any(e=>(e.Properties.Name.ValueOrDefault??"").StartsWith(prefix,StringComparison.Ordinal)),"An ineligible dropdown choice remains: "+prefix);
        Key(VirtualKeyShort.ESCAPE);
    }
    internal void DashboardAfterLogin()
    {
        Wait(()=>{
            var window=Window();
            if(window.Name.Contains("Backup Reminder",StringComparison.Ordinal))
            {
                Screenshot("startup-backup-reminder");Click("OK",ControlType.Button,window);return null;
            }
            var nav=TryFind("Daily Work",ControlType.Button,window);
            return nav is {IsEnabled:true}?nav:null;
        },"dashboard after any first-run backup reminder",60);
    }
    internal void Key(params VirtualKeyShort[] keys)
    {
        using var c=Identity.Open(); RequireInteractive();
        GetWindowThreadProcessId(GetForegroundWindow(),out uint pid);
        if(pid!=Process!.Id)Window().SetForeground();
        Keyboard.TypeSimultaneously(keys);
    }
    internal string Screenshot(string id)
    {
        RequireInteractive(); var window=Window(); window.SetForeground();
        GetWindowThreadProcessId(GetForegroundWindow(),out uint pid);
        if(pid!=Process!.Id)throw new InvalidOperationException("Evidence capture refused: app is not foreground.");
        string path=Path.Combine(EvidenceDirectory,id+".png");
        var handle=new IntPtr(window.Properties.NativeWindowHandle.Value);
        if(!GetWindowRect(handle,out var rect))throw new InvalidOperationException("Window bounds unavailable.");
        // Render only this HWND; screen-region capture can include unrelated windows
        // through rounded corners or occluding overlays.
        using var bitmap=new System.Drawing.Bitmap(rect.Right-rect.Left,rect.Bottom-rect.Top);
        using(var graphics=System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.Black);
            var dc=graphics.GetHdc();
            try {if(!PrintWindow(handle,dc,2))throw new InvalidOperationException("Window render failed.");}
            finally {graphics.ReleaseHdc(dc);}
        }
        bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png); return Path.GetFileName(path);
    }
    internal void Dump(string id)
    {
        // Names/ids/types only; never ValuePattern, passwords, clipboard or other processes.
        var items=Window().FindAllDescendants().Where(e=>!e.IsOffscreen)
            .Select(e=>$"{e.ControlType}\t{e.Properties.AutomationId.ValueOrDefault}\t{(e.Properties.IsPassword.ValueOrDefault?"[masked]":e.Properties.Name.ValueOrDefault)}");
        File.WriteAllLines(Path.Combine(EvidenceDirectory,id+"-controls.txt"),items);
    }
    public void Dispose()
    {
        if(Process is {HasExited:false}) {Process.Kill(entireProcessTree:true); Process.WaitForExit(5000);}
        Process?.Dispose(); Automation.Dispose();
    }
}
