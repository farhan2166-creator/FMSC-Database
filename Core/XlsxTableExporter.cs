using System.Globalization;
using ClosedXML.Excel;

namespace FMSC_SMS.Core;

internal static class XlsxTableExporter
{
    internal static void Write(DataTable table,string path)
    {
        if(table.Columns.Count==0)throw new InvalidOperationException("There are no report columns to export.");
        if(table.Rows.Count>1048575 || table.Columns.Count>16384)
            throw new InvalidOperationException("This report exceeds Excel's worksheet size. Narrow the report filters and export again.");
        AtomicExportFile.Publish(path,temporary=>{
            using var workbook=new XLWorkbook();
            workbook.Properties.Author="FMSC SMS";
            var sheet=workbook.Worksheets.Add("Report");
            sheet.Style.Font.FontColor=XLColor.Black;
            for(int col=0;col<table.Columns.Count;col++)
            {
                var source=table.Columns[col];
                bool text=source.DataType==typeof(string) || IsReference(source.ColumnName);
                sheet.Cell(1,col+1).Value=source.ColumnName;
                sheet.Column(col+1).Width=Math.Clamp(source.ColumnName.Length+3,text?24:18,40);
                if(text)sheet.Column(col+1).Style.NumberFormat.Format="@";
                else if(source.DataType==typeof(decimal))sheet.Column(col+1).Style.NumberFormat.Format="#,##0.##";
                for(int row=0;row<table.Rows.Count;row++)
                {
                    object value=table.Rows[row][col];
                    if(value is DBNull)continue;
                    var cell=sheet.Cell(row+2,col+1);
                    if(text)cell.Value=Convert.ToString(value,CultureInfo.InvariantCulture)??"";
                    else if(value is DateTime date)
                    {
                        if(date.Year<1900)cell.Value=date.ToString("dd-MMM-yyyy HH:mm:ss",CultureInfo.InvariantCulture);
                        else
                        {
                            cell.Value=date;
                            cell.Style.NumberFormat.Format=date.TimeOfDay==TimeSpan.Zero?"dd-mmm-yyyy":"dd-mmm-yyyy hh:mm:ss";
                        }
                    }
                    else if(value is bool flag)cell.Value=flag;
                    else if(Type.GetTypeCode(value.GetType()) is TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16
                        or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal)
                        cell.Value=Convert.ToDouble(value,CultureInfo.InvariantCulture);
                    else cell.Value=Convert.ToString(value,CultureInfo.InvariantCulture)??"";
                }
            }
            var header=sheet.Range(1,1,1,table.Columns.Count);
            header.Style.Font.Bold=true;
            header.Style.Alignment.WrapText=true;
            header.Style.Border.BottomBorder=XLBorderStyleValues.Thin;
            sheet.Row(1).Height=30;
            sheet.SheetView.FreezeRows(1);
            sheet.Range(1,1,table.Rows.Count+1,table.Columns.Count).SetAutoFilter();
            // The shared atomic publisher uses a .tmp file. Save through a stream
            // so Excel format selection does not depend on that temporary suffix.
            using var output=File.Create(temporary);
            workbook.SaveAs(output);
        },temporary=>{
            using var input=File.OpenRead(temporary);
            using var verified=new XLWorkbook(input);
            if(verified.Worksheets.Count!=1 || verified.Worksheet(1).Cell(1,1).GetString()!=table.Columns[0].ColumnName)
                throw new InvalidDataException("The Excel workbook could not be verified. The previous file was not replaced.");
        });
    }

    private static bool IsReference(string caption)
    {
        string key=new(caption.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return key is "admissionnumber" or "admissionno" or "admno" or "admqrno" or "qrno" or "quickreference"
            or "studentno" or "studentnumber" or "staffno" or "staffnumber" or "studentstaffno"
            or "receipt" or "receiptno" or "receiptnumber" or "receiptreference" or "originalreceipt"
            or "certificateno" or "certificatenumber" or "contact" or "contactnumber" or "altcontact" or "alternatecontact"
            or "phone" or "phonenumber" or "mobile" or "mobilenumber" || key.EndsWith("cnic",StringComparison.Ordinal);
    }
}
