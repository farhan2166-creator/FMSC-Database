using System.Data;
using FMSC_SMS.Core;
using System.Xml.Linq;
using Microsoft.Reporting.WinForms;

internal static partial class Program
{
    private static void CheckReportCaptions(string[]? selectedReports = null)
    {
        string output=Path.GetFullPath("artifacts/report-captions");Directory.CreateDirectory(output);
        using var profile=new DataTable();profile.Columns.Add("SchoolName");
        var school=profile.Rows.Add("Fictional School");
        foreach(string name in selectedReports ?? new[]{"AdmissionForm","ClassMarksSheet","ClassAttendanceSheet",
            "ClassMonthlyAttendanceSheet","ClassOutstandingCharges","ClassSectionSummary","FeeAgingReport",
            "BankAccountOpeningCertificate","CharacterCertificate","CollegeLeaveCertificate","DateOfBirthCertificate",
            "LeaveCertificate","NoObjectionCertificate","ProvisionalCertificate","RegularLecturerCertificate",
            "RegularStudentCertificate","SportsCertificate"})
        {
            string path=Path.Combine(AppContext.BaseDirectory,"Reports",name+".rdlc");
            var document=XDocument.Load(path);XNamespace ns=document.Root!.Name.Namespace;
            bool certificate=name.EndsWith("Certificate",StringComparison.Ordinal);
            if(certificate && !CertificateLetterhead.UsesPreprintedPad(path))
                document=XDocument.Parse(CertificateLetterhead.Definition(path,school));
            if(name=="CharacterCertificate")
            {
                var image=document.Descendants(ns+"Image").Single(element=>(string?)element.Attribute("Name")=="CertificateHeaderImage");
                Check(image.Element(ns+"Height")!.Value=="3in"&&image.Element(ns+"Sizing")!.Value=="FitProportional",
                    "The character certificate reserves a three-inch header without stretching the artwork.");
                Check(double.Parse(image.Element(ns+"Width")!.Value[..^2],System.Globalization.CultureInfo.InvariantCulture)>7.4,
                    "The new character header fills the existing printable report width.");
                Check(document.Descendants(ns+"LeftMargin").Single().Value=="0.41in"&&document.Descendants(ns+"RightMargin").Single().Value=="0.41in",
                    "The character certificate preserves its original paper margins.");
            }
            if(CertificateLetterhead.UsesPreprintedPad(path))
                Check(document.Descendants(ns+"TopMargin").Single().Value=="2in",name+" reserves two inches for preprinted letterhead.");
            using var report=new LocalReport();report.LoadReportDefinition(new StringReader(document.ToString()));
            report.EnableExternalImages=true;
            foreach(var dataset in document.Descendants(ns+"DataSet"))
            {
                var data=new DataTable();
                foreach(var field in dataset.Element(ns+"Fields")!.Elements(ns+"Field"))
                {
                    string typeName=field.Elements().FirstOrDefault(element=>element.Name.LocalName=="TypeName")?.Value??"System.String";
                    data.Columns.Add(field.Element(ns+"DataField")!.Value,Type.GetType(typeName,true)!);
                }
                var row=data.NewRow();
                foreach(DataColumn column in data.Columns)
                    row[column]=column.DataType==typeof(string)?column.ColumnName=="DateOfBirthInWords"?"24 October 2026":
                        column.ColumnName=="DateOfBirthInFigures"?"24-10-2026":
                        column.ColumnName=="ClassPromotedTo"?"Class 8 — 8":
                        column.ColumnName=="InstitutionLine"?"Fictional School Boys Campus Fictional City":"Example":column.DataType==typeof(DateTime)?new DateTime(2026,10,24):
                        column.DataType==typeof(bool)?true:Convert.ChangeType(1,column.DataType);
                data.Rows.Add(row);
                using var printable=CertificateUi.PrintData(path,data);
                if(CertificateLetterhead.UsesPreprintedPad(path))
                    Check(printable.Rows[0].Field<string>("ClassPromotedTo")=="Class 8"
                        && data.Rows[0].Field<string>("ClassPromotedTo")=="Class 8 — 8",
                        name+" prints the class once without rewriting the saved certificate.");
                report.DataSources.Add(new ReportDataSource((string)dataset.Attribute("Name")!,printable.Copy()));
            }
            byte[] pdf=report.Render("PDF");File.WriteAllBytes(Path.Combine(output,name+".pdf"),pdf);
            if(name=="CharacterCertificate")
                Check(System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(pdf),@"/Type\s*/Page\b").Count==1,
                    "The taller character header and certificate body still fit on one PDF page.");
            byte[] png=report.Render("IMAGE","<DeviceInfo><OutputFormat>PNG</OutputFormat><DpiX>110</DpiX><DpiY>110</DpiY></DeviceInfo>");
            File.WriteAllBytes(Path.Combine(output,name+".png"),png);
            Check(pdf.Length>1000&&png.Length>1000,name+" renders its split text/data runs with representative typed fixture values.");
            if(certificate)
            {
                string html=System.Text.Encoding.UTF8.GetString(report.Render("HTML4.0"));
                Check(html.Contains("24-10-2026",StringComparison.Ordinal),name+" prints its standard date as day-month-year.");
                if(name is "CollegeLeaveCertificate" or "LeaveCertificate" or "ProvisionalCertificate" or "DateOfBirthCertificate")
                    Check(html.Contains("24 October 2026",StringComparison.Ordinal),name+" retains the full month in its written birth date.");
                if(name=="RegularStudentCertificate")
                    Check(html.Contains("Fictional School",StringComparison.Ordinal) && !html.Contains("Boys Campus",StringComparison.Ordinal),
                        "Regular student certificate prints the school name without the campus.");
            }
        }
    }
}
