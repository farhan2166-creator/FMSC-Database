using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Configuration;
using FMSC_SMS.Pages.Finance.Collection;

internal static partial class Program
{
    private static void CheckWholeNumberInputs()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        foreach(string text in new[]{"abc","12a","1.5","12.00","1e3","Rs. 100","PKR 100","+12","12,34","١٢"})
            Check(!InputParse.TryWholeAmount(text,out _),"Whole-number input rejects "+text);
        foreach(string text in new[]{"0","125","1,250","-25"," 125 "})
            Check(InputParse.TryWholeAmount(text,out _),"Whole-number input accepts "+text);
        Check(InputParse.TryWholeValue(500.00m,out decimal stored)&&stored==500m && !InputParse.TryWholeValue(500.5m,out _),
            "SQL decimal storage accepts exact whole values and preserves rejection of historical fractions");
        bool KeyBlocked(TextBox editor,char key)
        {
            var e=new KeyPressEventArgs(key);
            typeof(Control).GetMethod("OnKeyPress",flags)!.Invoke(editor,new object[]{e});
            return e.Handled;
        }
        TextBox Editor(ModernTextBox field)=>field.Controls.OfType<TextBox>().Single();
        using var number=new ModernNumericTextBox { DecimalPlaces=2,Value=450 };
        Check(number.DecimalPlaces==0&&number.Text=="450","Shared numeric fields display only whole numbers, even under older decimal configuration");
        foreach(char key in new[]{'a','.','+','١'})Check(KeyBlocked(Editor(number),key),"Numeric typing blocks "+key);
        Check(!KeyBlocked(Editor(number),'5')&&!KeyBlocked(Editor(number),'\b'),"Digits and correction keys remain available");
        using var signed=new ModernNumericTextBox {Minimum=-100};signed.Text="";
        Check(!KeyBlocked(Editor(signed),'-')&&KeyBlocked(Editor(number),'-'),"Only existing signed numeric fields allow a leading minus");
        number.Text="12.5";
        Check(number.Value==12.5m&&number.Text=="12.5"&&!FormValidator.HasValue(number),
            "A fractional value supplied by code stays visible for rejection and is never rounded into a valid amount");
        number.Value=450;
        using var amount=HumanUi.TextBox(false,20);
        using var amountField=HumanUi.Field("Amount (Rs.)",amount);
        using var name=HumanUi.TextBox(false,100);
        using var nameField=HumanUi.Field("Father Name",name);
        using var identifier=HumanUi.TextBox(false,40);
        using var identifierField=HumanUi.Field("Admission Number",identifier);
        Check(KeyBlocked(Editor(amount),'a')&&KeyBlocked(Editor(amount),'.'),"Plain monetary textboxes receive the same numeric entry rules");
        Check(!KeyBlocked(Editor(name),'A')&&!KeyBlocked(Editor(identifier),'a'),"Human names and admission-number suffixes retain their text formats");
        Check(!WholeNumberInput.AllowsInsertion("100",0,3,"12.5",false)&&!WholeNumberInput.AllowsInsertion("100",0,3,"abc",false),
            "Pasted invalid values are rejected in full, never converted to a different number");
        Check(WholeNumberInput.AllowsInsertion("125",1,1,"0",false)&&WholeNumberInput.AllowsInsertion("100",0,3,"1,250",false),
            "Whole-number paste respects selected text and accepts valid thousands formatting");
        Check(!WholeNumberInput.AllowsInsertion("100",0,3,"-20",false)&&WholeNumberInput.AllowsInsertion("100",0,3,"-20",true),
            "Signed pastes are limited to fields that already support signed amounts");
        using var host=new Form {ClientSize=new Size(900,500),ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000)};
        number.Dock=DockStyle.Top;host.Controls.Add(number);host.Show();Application.DoEvents();
        var originalPasteReader=WholeNumberInput.ReadPasteText;
        try
        {
            foreach(string paste in new[]{"abc","12.5","100abc","1e3","Rs. 125"})
            {
                WholeNumberInput.ReadPasteText=()=>paste;
                Editor(number).SelectAll();Editor(number).Paste();
                Check(number.Text=="450","The native paste message rejects "+paste+" without changing the previous amount");
            }
        }
        finally{WholeNumberInput.ReadPasteText=originalPasteReader;}

        using var matrix=new FeeRateMatrixForm(null,false);
        Check((string)typeof(FeeRateMatrixForm).GetMethod("FormatAmount",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{0.001m})! =="0.001",
            "Loading an existing matrix fraction never rounds it into an accepted whole fee");
        var grid=(DataGridView)typeof(FeeRateMatrixForm).GetField("grid",flags)!.GetValue(matrix)!;
        grid.Parent!.Controls.Remove(grid);host.Controls.Add(grid);grid.BringToFront();grid.Columns.Clear();
        grid.Columns.Add("CL_1","Class 1 fee");grid.Columns.Add("Remarks","Remarks");grid.Rows.Add("100","Optional note");
        grid.Visible=true;grid.Focus();Application.DoEvents();
        grid.CurrentCell=grid.Rows[0].Cells[0];Check(grid.BeginEdit(true),"The actual fee matrix starts a numeric edit without loading SQL");
        var editing=(TextBox)grid.EditingControl!;
        Check(KeyBlocked(editing,'x')&&KeyBlocked(editing,'.')&&!KeyBlocked(editing,'7'),"Fee matrix editors accept digits and refuse characters and decimal points");
        var readPaste=WholeNumberInput.ReadPasteText;
        try
        {
            editing.Text="100";editing.SelectAll();WholeNumberInput.ReadPasteText=()=>"12.5";editing.Paste();
            Check(editing.Text=="100","The matrix also rejects a fractional paste before the value reaches the cell");
        }
        finally{WholeNumberInput.ReadPasteText=readPaste;}
        var invalidEdit=(DataGridViewCellValidatingEventArgs)Activator.CreateInstance(typeof(DataGridViewCellValidatingEventArgs),flags,null,new object[]{0,0,"12.5"},null)!;
        typeof(DataGridView).GetMethod("OnCellValidating",flags,null,new[]{typeof(DataGridViewCellValidatingEventArgs)},null)!.Invoke(grid,new object[]{invalidEdit});
        Check(invalidEdit.Cancel&&Convert.ToString(grid.Rows[0].Cells[0].Value)=="100",
            "Grid validation rejects a fractional draft without replacing the saved cell value");
        editing.Text="125";Check(grid.EndEdit()&&Convert.ToString(grid.Rows[0].Cells[0].Value)=="125","A corrected whole-number fee commits normally");
        grid.CurrentCell=grid.Rows[0].Cells[1];grid.BeginEdit(true);
        Check(!KeyBlocked((TextBox)grid.EditingControl!,'x')&&!KeyBlocked((TextBox)grid.EditingControl!,'.'),
            "A reused grid editor releases numeric restrictions when editing a text column");
        grid.EndEdit();
        grid.Rows[0].Cells[0].Value=500.00m;
        Check(Convert.ToString(grid.Rows[0].Cells[0].FormattedValue)=="500","A stored whole decimal opens without an editable .00 suffix");
        grid.Rows[0].Cells[0].Value=500.5m;
        Check(Convert.ToString(grid.Rows[0].Cells[0].FormattedValue)=="500.5","An existing fractional amount is displayed exactly instead of rounded");
        using var page=new FeeCollectionPage(false);
        foreach(string field in new[]{"paymentAmount","discountAmount"})
        {
            var box=(ModernTextBox)typeof(FeeCollectionPage).GetField(field,flags)!.GetValue(page)!;box.Enabled=true;box.ReadOnly=false;
            Check(KeyBlocked(Editor(box),'a')&&KeyBlocked(Editor(box),'.'),"Collection "+field+" follows the shared whole-number policy");
        }
        Check(!matrix.IsHandleCreated&&!page.IsHandleCreated,"Input checks never load or change the school database");
    }
}
