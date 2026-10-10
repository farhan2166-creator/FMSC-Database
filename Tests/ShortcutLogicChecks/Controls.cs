namespace FMSC_SMS.Core;
// Minimal controls simulate state/focus only; they do not validate native WinForms events.
[Flags] internal enum Keys {
 None=0, Back=8, Tab=9, Enter=13, ShiftKey=16, ControlKey=17, Menu=18,
 Escape=27, PageUp=33, PageDown=34, Insert=45, Delete=46,
 D0=48,D1,D2,D3,D4,D5,D6,D7,D8,D9,
 A=65,B,C,D,E,F,G,H,I,J,K,L,M,N,O,P,Q,R,S,T,U,V,W,X,Y,Z,
 F1=112,F2=113,F5=116,F8=119, KeyCode=65535,
 Shift=65536,Control=131072,Alt=262144,Modifiers=-65536
}
internal class Control {
 private bool visible=true, enabled=true, focused;
 public List<Control> Controls { get; }=new();
 public Control? Parent {get;private set;}
 public bool Visible {get=>visible && (Parent?.Visible ?? true);set=>visible=value;}
 public bool Enabled {get=>enabled && (Parent?.Enabled ?? true);set=>enabled=value;}
 public bool IsDisposed {get;set;} public bool IsHandleCreated=>false;
 public bool ContainsFocus=>focused || Controls.Any(c=>c.ContainsFocus);
 public bool CanFocus=>Visible && Enabled && !IsDisposed;
 public bool TabStop {get;set;}=true;
 public int TabIndex {get;set;}
 public string Text {get;set;}=""; public string Name {get;set;}="";
 public string? AccessibleName {get;set;} public bool HasChildren=>Controls.Count>0;
 public void Add(Control c){Controls.Add(c);c.Parent=this;}
 public bool Focus(){Control root=this;while(root.Parent!=null)root=root.Parent;Clear(root);focused=true;return true;}
 private static void Clear(Control root){root.focused=false;foreach(var c in root.Controls)Clear(c);}
 public void BeginInvoke(Action action)=>action();
}
internal class ModernButton:Control {
 public string ThemeRole {get;set;}="Primary"; public char? ShortcutLetter {get;set;}
 public int Clicks {get;private set;} public void PerformClick(){if(Enabled && Visible)Clicks++;}
}
internal class ToolTip {
 private readonly Dictionary<Control,string?> tips=new();
 public string? GetToolTip(Control c)=>tips.GetValueOrDefault(c);
 public void SetToolTip(Control c,string? text)=>tips[c]=text;
}
internal class TextBoxBase:Control { public bool SelectedAll;public void SelectAll()=>SelectedAll=true; }
internal class TextBox:TextBoxBase {public string PlaceholderText {get;set;}="";}
internal class ModernTextBox:TextBox { }
internal class StudentPicker:TextBox { }
internal class Label:Control { }
internal class ComboBox:Control { }
internal class DateTimePicker:Control { }
internal class DataGridView:Control { }
internal class ListBox:Control { }
internal class TreeView:Control { }
internal class NumericUpDown:Control { }
internal class CheckBox:Control { }
internal class RadioButton:Control { }
internal class TabControl:Control {public List<Control> TabPages {get;}=new();public int SelectedIndex {get;set;} }
internal class V2Page:Control {
 public PageActionShortcutController ShortcutController {get;}
 public V2Page()=>ShortcutController=new(this,new());
 public bool IsGridReport {get;set;} public bool CanPreviewReport {get;set;} public int Previews;
 public Control ReportShortcutTarget=>this; public void BeginReportPreview()=>Previews++;
}
internal class ApplicationWindowForm:Control {
 public PageActionShortcutController ShortcutController {get;}
 public ApplicationWindowForm()=>ShortcutController=new(this,new());
}
internal static class FormFlow {public static bool FocusFirstInput(Control c)=>false;}
internal static class LookupDataCache {public static int Invalidations;public static void Invalidate()=>Invalidations++;}
internal static class StudentSearchCache {public static int Invalidations;public static void Invalidate()=>Invalidations++;}
