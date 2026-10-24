using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Enrollment;

internal static partial class Program
{
    private static void CheckTransferReview()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var types=new DataTable();types.Columns.Add("FeeTypeId",typeof(int));types.Columns.Add("DisplayOrder",typeof(int));
        types.Columns.Add("FeeTypeCode");types.Columns.Add("IsTransportLinked",typeof(bool));
        types.Rows.Add(1,1,"TUITION",false);types.Rows.Add(2,2,"TRANSPORT",true);types.Rows.Add(3,3,"ADMISSION",false);
        types.Rows.Add(4,4,"BOOKS",false);types.Rows.Add(5,5,"LAB",false);
        using var current=new DataTable();
        foreach(string name in new[]{"FeeTypeId","StudentFeeAssignmentId"})current.Columns.Add(name,typeof(int));
        foreach(string name in new[]{"FeeTypeName","FeeTypeCode","Frequency"})current.Columns.Add(name);
        current.Columns.Add("StandardAmount",typeof(decimal));current.Columns.Add("IsMandatory",typeof(bool));
        foreach(string name in new[]{"CancelledAt","EffectiveFrom","EffectiveTo","LastBilledMonth"})current.Columns.Add(name,typeof(DateTime));
        var start=new DateTime(2026,4,1);
        current.Rows.Add(1,11,"Tuition Fee","TUITION","Monthly",3000m,true,DBNull.Value,start,DBNull.Value,new DateTime(2026,10,1));
        current.Rows.Add(2,12,"Transport Fee","TRANSPORT","Monthly",500m,false,DBNull.Value,start,DBNull.Value,DBNull.Value);
        current.Rows.Add(3,13,"Admission Fee","ADMISSION","Yearly",5000m,true,DBNull.Value,start,DBNull.Value,DBNull.Value);
        current.Rows.Add(4,14,"Books Fee","BOOKS","Yearly",200m,true,DBNull.Value,start,DBNull.Value,DBNull.Value);
        current.Rows.Add(5,15,"Lab Fee","LAB","Monthly",200m,true,DBNull.Value,start,DBNull.Value,DBNull.Value);
        using var destination=new DataTable();destination.Columns.Add("FeeTypeId",typeof(int));destination.Columns.Add("FeeTypeName");destination.Columns.Add("FeeTypeCode");
        destination.Columns.Add("Frequency");destination.Columns.Add("Amount",typeof(decimal));destination.Columns.Add("IsMandatory",typeof(bool));destination.Columns.Add("IsTransportLinked",typeof(bool));
        destination.Rows.Add(1,"Tuition Fee","TUITION","Monthly",3500m,true,false);
        destination.Rows.Add(2,"Transport Fee","TRANSPORT","Monthly",600m,false,true);
        destination.Rows.Add(3,"Admission Fee","ADMISSION","Yearly",6000m,true,false);
        destination.Rows.Add(4,"Books Fee","BOOKS","Yearly",300m,true,false);
        destination.Rows.Add(5,"Lab Fee","LAB","Monthly",500m,true,false);
        TransferFeeSyncDialog Create()=>new("Example Student","Girls Campus — Nine","Girls Campus — Ten",new DateTime(2026,10,24),1,1,2,
            new TransferFeeSyncDialog.ComparisonData(types,current,destination,new DateTime(2026,11,1)));
        string output=Path.GetFullPath("artifacts/transfer-review");Directory.CreateDirectory(output);
        var themeField=typeof(AppTheme).GetField("_currentTheme",BindingFlags.Static|BindingFlags.NonPublic)!;
        var previous=AppTheme.CurrentTheme;
        double Light(Color c)
        {
            double Channel(int v){double n=v/255d;return n<=.04045?n/12.92:Math.Pow((n+.055)/1.055,2.4);}
            return .2126*Channel(c.R)+.7152*Channel(c.G)+.0722*Channel(c.B);
        }
        try
        {
            foreach(var theme in AppTheme.SelectableThemes)
            {
                themeField.SetValue(null,theme);
                double ratio=(Light(AppTheme.SelectionText)+.05)/(Light(AppTheme.SelectionBackground)+.05);
                Check(ratio>=4.5,"Selected text remains readable in "+theme);
                Check(AppTheme.SelectionBackground.ToArgb()!=AppTheme.Primary.ToArgb(),"Selection uses a distinct colour in "+theme);
                using var dialog=Create();
                dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(-30000,-30000);dialog.ShowInTaskbar=false;
                dialog.Show();Application.DoEvents();
                var grid=(DataGridView)typeof(TransferFeeSyncDialog).GetField("grid",flags)!.GetValue(dialog)!;
                Check(grid.Rows.Count==2 && grid.Rows.Cast<DataGridViewRow>().All(row=>new[]{"Tuition Fee","Transport Fee"}.Contains(Convert.ToString(row.Cells[0].Value))),
                    "The review includes only tuition and used transport");
                Check(grid.Columns["New"].HeaderText=="From Nov 2026","The comparison uses the actual school cutoff month");
                Check(grid.Height>=220 && grid.Width>=850,"The default dialog gives the fee grid substantial room");
                Check(grid.DefaultCellStyle.SelectionBackColor==AppTheme.SelectionBackground && grid.DefaultCellStyle.SelectionForeColor==AppTheme.SelectionText,
                    "Transfer selections use the contrasting shared palette");
                foreach(var size in new[]{new Size(900,680),new Size(1040,760)})
                {
                    dialog.ClientSize=size;dialog.PerformLayout();Application.DoEvents();
                    Check(grid.Height>=140 && grid.Parent!.ClientRectangle.Contains(grid.Bounds),"The grid remains usable when the dialog is resized");
                }
                if(theme.ToString() is "Violet" or "OceanBlue")
                {
                    grid.Rows[0].Selected=true;
                    using var image=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
                    image.Save(Path.Combine(output,theme+".png"));
                }
                dialog.Close();
            }
            current.Rows.RemoveAt(1);
            using var noTransport=Create();
            var only=(DataGridView)typeof(TransferFeeSyncDialog).GetField("grid",flags)!.GetValue(noTransport)!;
            Check(only.Rows.Count==1,"A configured destination transport rate does not add transport to a non-user");
        }
        finally{themeField.SetValue(null,previous);}
    }
}
