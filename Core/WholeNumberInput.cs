using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FMSC_SMS.Core;

// Reject an invalid edit as a whole. In particular, never turn a pasted 12.5 into 125.
internal static class WholeNumberInput
{
    private static readonly ConditionalWeakTable<TextBox, Entry> Entries=new();
    private static readonly ConditionalWeakTable<DataGridView, object> Grids=new();
    internal static Func<string?> ReadPasteText=()=>Clipboard.ContainsText()?Clipboard.GetText():null;
    internal static void Attach(TextBox field,Func<bool>? signed=null)=>
        Configure(field,()=>field.Enabled&&!field.ReadOnly,signed??(()=>false));
    internal static void Attach(ModernTextBox field,Func<bool>? signed=null,Func<bool>? active=null)
    {
        var editor=field.Controls.OfType<TextBox>().Single();
        Configure(editor,()=>field.Enabled&&!field.ReadOnly&&(active?.Invoke()??true),signed??(()=>false));
    }
    internal static void WireField(Control field,string caption)
    {
        if(field is not ModernTextBox text || text is ModernNumericTextBox)return;
        string key=Key(caption);
        if(caption.Contains("(Rs",StringComparison.OrdinalIgnoreCase) || key is
            "amount" or "discount" or "newamount" or "monthlyincome" or "marksobtained" or "marksoutof" or
            "outof" or "examyear" or "displayorder" or "daysoverduefrom" or "daysoverdueto")Attach(text);
    }
    private static string Key(string value)=>new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    internal static bool IsNumericCell(DataGridView grid,DataGridViewCell? cell)
    {
        if(grid.ReadOnly || cell is not DataGridViewTextBoxCell || cell.ReadOnly)return false;
        string key=Key(cell.OwningColumn.DataPropertyName.Length>0?cell.OwningColumn.DataPropertyName:cell.OwningColumn.Name);
        var type=Nullable.GetUnderlyingType(cell.ValueType??typeof(string))??cell.ValueType;
        return cell.OwningColumn.Name.StartsWith("CL_",StringComparison.Ordinal) || key is
            "amount" or "discount" or "discountamount" or "applieddiscount" or "newamount" or "actualamount" or "proposed" or "paynow" ||
            type==typeof(decimal)||type==typeof(int)||type==typeof(long)||type==typeof(short)||type==typeof(byte)||type==typeof(double)||type==typeof(float);
    }
    private static bool IsSigned(DataGridViewCell? cell)=>cell is not null && Key(cell.OwningColumn.Name)=="actualamount";
    internal static void WireGrid(DataGridView grid)
    {
        if(Grids.TryGetValue(grid,out _))return;
        Grids.Add(grid,new object());
        grid.CellFormatting+=(_,e)=>{
            if(e.RowIndex<0 || e.ColumnIndex<0 || !IsNumericCell(grid,grid.Rows[e.RowIndex].Cells[e.ColumnIndex]) || e.Value is not decimal amount)return;
            // SQL decimal values such as 500.00 are still whole numbers. Keep any historical fraction visible.
            string format=WholeRupees.IsWhole(amount)?"0":"0.############################";
            e.Value=amount.ToString(format,System.Globalization.CultureInfo.InvariantCulture);e.FormattingApplied=true;
        };
        grid.EditingControlShowing+=(_,e)=>{
            if(e.Control is TextBox editor)Configure(editor,()=>IsNumericCell(grid,grid.CurrentCell),()=>IsSigned(grid.CurrentCell));
        };
        grid.CellValidating+=(_,e)=>{
            if(e.RowIndex<0 || e.ColumnIndex<0 || !grid.IsCurrentCellInEditMode)return;
            var cell=grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            if(!IsNumericCell(grid,cell))return;
            string text=Convert.ToString(e.FormattedValue)?.Trim()??"";
            bool valid=text.Length==0 || InputParse.TryWholeAmount(text,out decimal number)&&(number>=0||IsSigned(cell));
            const string error="Enter a whole number using digits only; decimals and letters are not allowed.";
            if(!valid){e.Cancel=true;cell.ErrorText=error;}
            else if(cell.ErrorText==error)cell.ErrorText="";
        };
    }
    internal static bool AllowsInsertion(string text,int start,int length,string insertion,bool signed)
    {
        if(insertion.Length==0)return true;
        // Pasted grouping is accepted only when it forms a complete, valid whole number.
        string candidate=text.Remove(start,length).Insert(start,insertion);
        return InputParse.TryWholeAmount(candidate,out decimal number)&&(signed||number>=0);
    }
    private static void Configure(TextBox editor,Func<bool> active,Func<bool> signed)
    {
        var entry=Entries.GetValue(editor,key=>new Entry(key));
        entry.Active=active;entry.Signed=signed;
    }
    private sealed class Entry : NativeWindow
    {
        private readonly TextBox editor;
        internal Func<bool> Active=()=>false,Signed=()=>false;
        internal Entry(TextBox editor)
        {
            this.editor=editor;
            editor.KeyPress+=(_,e)=>{
                if(!Active()||char.IsControl(e.KeyChar)||char.IsAsciiDigit(e.KeyChar))return;
                if(e.KeyChar=='-'&&Signed()&&editor.SelectionStart==0&&
                    !editor.Text.Remove(editor.SelectionStart,editor.SelectionLength).Contains('-'))return;
                e.Handled=true;
            };
            editor.HandleCreated+=(_,_)=>AssignHandle(editor.Handle);
            editor.HandleDestroyed+=(_,_)=>ReleaseHandle();
            if(editor.IsHandleCreated)AssignHandle(editor.Handle);
        }
        protected override void WndProc(ref Message message)
        {
            const int Paste=0x0302;
            if(message.Msg==Paste&&Active()&&!editor.ReadOnly)
            {
                try
                {
                    string? insertion=ReadPasteText();
                    if(insertion is null)return;
                    if(editor.Text.Length-editor.SelectionLength+insertion.Length>editor.MaxLength ||
                        !AllowsInsertion(editor.Text,editor.SelectionStart,editor.SelectionLength,insertion,Signed()))return;
                }
                catch(ExternalException){return;}
            }
            base.WndProc(ref message);
        }
    }
}
