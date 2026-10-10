using FMSC_SMS.Core;
int count=0;
void Check(bool result,string name){if(!result)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
ModernButton Button(Control root,string text,bool enabled=true){var b=new ModernButton{Text=text,Enabled=enabled};root.Add(b);return b;}
var actions=AppHotKeys.PageActions;
Check(actions.All(a=>AppHotKeys.IsValidShortcut(a.KeyData)),"Every page command uses at most two keys");
Check(actions.Select(a=>a.KeyData).Distinct().Count()==actions.Length,"Page commands have unique keys");
Check(AppHotKeys.DashboardNavigation.All(a=>AppHotKeys.IsValidShortcut(a.Keys)),"Navigation uses at most two keys");
Check(!AppHotKeys.DashboardNavigation.Any(n=>actions.Any(a=>a.KeyData==n.Keys)),"Navigation does not collide with page commands");
Check(!AppHotKeys.IsValidShortcut(Keys.Control|Keys.Shift|Keys.S) && !AppHotKeys.IsValidShortcut(Keys.ControlKey),"Three keys and modifier-only bindings rejected");
Check(actions.All(a=>!string.IsNullOrWhiteSpace(a.Description)),"Every page command has a description");
var root=new Control();var controller=new PageActionShortcutController(root,new());
var left=new Control();var right=new Control();root.Add(left);root.Add(right);
var input=new TextBox();left.Add(input);input.Focus();
var leftNew=Button(left,"New Record");var rightNew=Button(right,"New Record");
Check(controller.TryHandle(Keys.Control|Keys.N)&&leftNew.Clicks==1&&rightNew.Clicks==0,"New follows focused editor");
var second=Button(left,"New Certificate");
Check(!controller.TryHandle(Keys.Control|Keys.N)&&leftNew.Clicks==1&&second.Clicks==0,"Ambiguous actions do not execute");
second.Visible=false;leftNew.Enabled=false;
Check(!controller.TryHandle(Keys.Control|Keys.N)&&rightNew.Clicks==0,"Disabled focused action cannot run action in another editor");
Check(!controller.AvailableHints().Any(h=>h.Key=="Ctrl+N"),"Disabled actions have no key badge");
var run=Button(root,"Runner Settings");
Check(!controller.TryHandle(Keys.Control|Keys.R)&&run.Clicks==0,"Caption prefixes require a word boundary");
var search=new ModernTextBox{Name="studentSearch"};left.Add(search);
Check(controller.TryHandle(Keys.Control|Keys.F)&&search.ContainsFocus&&search.SelectedAll,"Find focuses and selects search text");
foreach(var key in new[]{Keys.A,Keys.C,Keys.X,Keys.V,Keys.Z,Keys.Y,Keys.Delete,Keys.Back})
 Check(!controller.TryHandle(Keys.Control|key),"Native edit key preserved: Ctrl+"+key);
var reload=Button(root,"Reload");
Check(controller.TryHandle(Keys.F5)&&reload.Clicks==1&&LookupDataCache.Invalidations==1&&StudentSearchCache.Invalidations==1,"Refresh invalidates caches and executes reload");
int calls=0;var local=Button(root,"Custom");controller.RegisterShortcut(local,Keys.Alt|Keys.P,"Alt+P",()=>calls++);
Check(controller.TryHandle(Keys.Alt|Keys.P)&&calls==1,"Registered contextual command executes");
local.Enabled=false;
Check(controller.TryHandle(Keys.Alt|Keys.P)&&calls==1,"Unavailable contextual command consumed without execution");
try{controller.RegisterShortcut(local,Keys.Alt|Keys.P,"Alt+P",()=>{});Check(false,"Duplicate accepted");}catch(ArgumentException){Check(true,"Duplicate local binding rejected");}
try{controller.RegisterShortcut(local,Keys.Control|Keys.Shift|Keys.P,"Ctrl+Shift+P",()=>{});Check(false,"Three keys accepted");}catch(ArgumentException){Check(true,"Three-key registration rejected");}
var tabRoot=new Control();var tabs=new TabControl();tabRoot.Add(tabs);
tabs.TabPages.AddRange(new[]{new Control(),new Control{Enabled=false},new Control()});
Check(AppHotKeys.TrySwitchTab(tabRoot,Keys.Control|Keys.Tab)&&tabs.SelectedIndex==2,"Next tab skips disabled tab");
Check(AppHotKeys.TrySwitchTab(tabRoot,Keys.Control|Keys.PageDown)&&tabs.SelectedIndex==0,"Next tab wraps");
Check(AppHotKeys.TrySwitchTab(tabRoot,Keys.Control|Keys.PageUp)&&tabs.SelectedIndex==2,"Previous tab wraps");
Check(!AppHotKeys.TrySwitchTab(tabRoot,Keys.Control|Keys.D2)&&tabs.SelectedIndex==2,"Direct selection respects disabled tabs");
Check(!AppHotKeys.TrySwitchTab(tabRoot,Keys.Control|Keys.D9),"Nonexistent direct tab ignored");
var nested=new TabControl();tabs.Add(nested);nested.TabPages.AddRange(new[]{new Control(),new Control()});nested.Focus();
Check(AppHotKeys.TrySwitchTab(tabRoot,Keys.Control|Keys.Tab)&&nested.SelectedIndex==1&&tabs.SelectedIndex==2,"Nested focused tabs take precedence");
Console.WriteLine($"{count} logic checks passed (simulated controls; native Windows UI untested).");
