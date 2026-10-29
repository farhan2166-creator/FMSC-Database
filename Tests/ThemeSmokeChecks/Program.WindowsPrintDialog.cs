using System.Drawing.Printing;
using System.Reflection;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckWindowsPrintDialog(bool inspectDialog)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var document=new RdlcPrintDocument(Enumerable.Range(0,3).Select(_=>new MemoryStream()).ToList());
        document.PrinterSettings.Copies=2;
        document.PrinterSettings.Collate=true;
        using(var dialog=ReportPrintDialog.Create(document,2,false))
        {
            Check(!dialog.UseEXDialog&&dialog.AllowSomePages&&!dialog.AllowSelection,
                "The shared Windows dialog retains the established native range-based print choices");
            Check(document.PrinterSettings.PrintRange==PrintRange.AllPages
                &&document.PrinterSettings.FromPage==2&&document.PrinterSettings.ToPage==2,
                "Normal Print defaults to all pages and prefills the range with the preview's current page");
            Check(document.PrinterSettings.Copies==2&&document.PrinterSettings.Collate,
                "Opening print choices retains the existing copies and collation settings");
        }
        void Begin()=>typeof(RdlcPrintDocument).GetMethod("OnBeginPrint",flags)!.Invoke(document,new object[]{new PrintEventArgs()});
        int Field(string name)=>(int)typeof(RdlcPrintDocument).GetField(name,flags)!.GetValue(document)!;
        document.PrinterSettings.PrintRange=PrintRange.SomePages;Begin();
        Check(Field("pageIndex")==1&&Field("lastPage")==2,
            "Selecting Pages targets only preview page 2 without editing either page number");
        document.PrinterSettings.PrintRange=PrintRange.SomePages;
        document.PrinterSettings.FromPage=2;document.PrinterSettings.ToPage=3;Begin();
        Check(Field("pageIndex")==1&&Field("lastPage")==3,"Explicit page ranges retain their original limits");
        document.PrinterSettings.PrintRange=PrintRange.AllPages;Begin();
        Check(Field("pageIndex")==0&&Field("lastPage")==3,"All Pages ignores a previously selected partial range");
        document.PrinterSettings.FromPage=4;document.PrinterSettings.ToPage=4;document.PrinterSettings.PrintRange=PrintRange.SomePages;
        bool rejected=false;
        try{Begin();}catch(TargetInvocationException ex)when(ex.InnerException is InvalidOperationException){rejected=true;}
        Check(rejected,"An invalid page range is refused instead of printing the whole document");
        using(var dialog=ReportPrintDialog.Create(document,2,true))
            Check(document.PrinterSettings.PrintRange==PrintRange.SomePages&&document.PrinterSettings.FromPage==2&&document.PrinterSettings.ToPage==2,
                "Print This Page opens the same Windows dialog with the current page's range selected");

        if(!inspectDialog)return;
        using var owner=new Form {Text="Windows print dialog check — no pages will be printed",ClientSize=new Size(720,220),StartPosition=FormStartPosition.CenterScreen};
        owner.Show();Application.DoEvents();
        foreach(bool current in new[]{false,true})
        {
            using var dialog=ReportPrintDialog.Create(document,2,current);
            var result=dialog.ShowDialog(owner);
            Console.WriteLine($"NATIVE_PRINT: current-page shortcut={current}; result={result}; range={document.PrinterSettings.PrintRange}; from={document.PrinterSettings.FromPage}; to={document.PrinterSettings.ToPage}");
            Check(result is DialogResult.Cancel or DialogResult.OK && document.PagesPrinted==0,
                "Inspecting or dismissing native print choices never sends a print job from this test");
        }
        owner.Close();
    }
}
