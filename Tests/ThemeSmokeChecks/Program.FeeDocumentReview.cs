using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FMSC_SMS.Core;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

internal static partial class Program
{
    private sealed record FeeDocumentReviewSample(string Name,string Document,string Paper,int Pages,string Template);

    private static void CheckFeeDocumentReviewPack()
    {
        var samples=new List<FeeDocumentReviewSample>();
        bool passed=false;
        int assertions=0;
        try
        {
            CheckFeeDocuments(samples);
            assertions=RegistrationBillChecks.Run();
            passed=true;
        }
        catch(Exception ex)
        {
            Console.WriteLine("::error::"+ex.Message.Replace("%","%25").Replace("\r","%0D").Replace("\n","%0A"));
            throw;
        }
        finally { WriteFeeDocumentReviewPack(samples,passed); }
        Console.WriteLine($"PASS: {checks} document checks and {assertions} bill-data assertions; {samples.Count} review scenarios.");
    }

    private static byte[] FeeReviewFirstPage(FeeBillRollDocument.Page page)
    {
        using var source=new MemoryStream(page.Image.ToArray());
        using var image=Image.FromStream(source);
        using var bitmap=new Bitmap((int)Math.Ceiling(page.Settings.PaperSize.Width*1.3),(int)Math.Ceiling(page.Settings.PaperSize.Height*1.3));
        bitmap.SetResolution(130,130);
        using(var graphics=Graphics.FromImage(bitmap)) { graphics.Clear(Color.White);graphics.DrawImage(image,new Rectangle(0,0,bitmap.Width,bitmap.Height)); }
        using var output=new MemoryStream();bitmap.Save(output,System.Drawing.Imaging.ImageFormat.Png);return output.ToArray();
    }

    private static void CheckFeeReviewReceiptCases(DataTable source,Func<string,DataTable,bool,int> render)
    {
        using var bank=source.Copy();
        foreach(DataRow row in bank.Rows)
        {
            row["ReceiptNumber"]="R-FICTIONAL-BANK-001";row["PaymentMethod"]="Bank";
            row["CashReceived"]=row["TotalPaid"];row["CreditApplied"]=0m;row["CreditRemaining"]=0m;
            row["CreditRetained"]=0m;row["ShowCreditRemaining"]=false;
        }
        Check(render("bank-payment-receipt",bank,false)==1,"A fictional bank payment renders with the normal receipt settlement");
        Check(render("bank-payment-receipt-a4",bank,true)>=1,"Bank receipt data renders in the annual A4 layout");
        using var advance=bank.Copy();
        foreach(DataRow row in advance.Rows)
        {
            row["PaymentMethod"]="Cash";row["CashReceived"]=Convert.ToDecimal(row["TotalPaid"])+500m;
            row["CreditRetained"]=500m;row["CreditRemaining"]=500m;row["ShowCreditRemaining"]=true;
        }
        Check(render("overpayment-retained-as-advance-receipt",advance,false)==1,"Overpayment prints the retained advance without adding it to fee settlement");
        using var longNames=bank.Copy();
        foreach(DataRow row in longNames.Rows)
        {
            row["StudentName"]="Muhammad Abdul Rahman Example Student With A Long Recorded Name";
            row["FatherName"]="Abdul Hameed Example Parent With A Long Recorded Name";
            row["FeeDescription"]="Annual examination preparation and supplementary learning materials fee";
        }
        Check(render("long-names-and-fees-receipt",longNames,false)>=1,"Long receipt identities and fee labels render without changing the template");
    }

    private static void WriteFeeDocumentReviewPack(IReadOnlyList<FeeDocumentReviewSample> samples,bool passed)
    {
        string folder=Path.GetFullPath("artifacts/fee-documents");Directory.CreateDirectory(folder);
        string status=passed?"PASSED":"FAILED / PARTIAL OUTPUT";
        Check(samples.Select(sample=>sample.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()==samples.Count,"Scenario names cannot overwrite another review PDF");
        string commit=Environment.GetEnvironmentVariable("GITHUB_SHA")??"Local build";
        string Title(string name)=>CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('-', ' '));
        string H(string text)=>WebUtility.HtmlEncode(text);
        var index=new StringBuilder("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>Bill and receipt scenario review</title><style>body{font:16px sans-serif;max-width:1100px;margin:24px auto;padding:0 12px;color:#000;background:#fff}table{border-collapse:collapse;width:100%}td,th{border:1px solid #999;padding:8px;text-align:left}img{max-width:200px;max-height:260px}summary{cursor:pointer}</style><h1>Bill and receipt scenario review</h1>");
        index.Append($"<p>Checks: <b>{status}</b>. Source commit: <code>{H(commit)}</code>.</p><p>Static fictional fixtures; no school database, payment posting or physical printer is used. PNG previews show the first page only; PDFs and the combined review file retain every page.</p>");
        index.Append("<p>Open <a href=\"REVIEW-ALL.pdf\">REVIEW-ALL.pdf</a> for all scenarios, with a bookmark for each. The individual PDFs below retain their original paper sizes.</p><table><thead><tr><th>Scenario</th><th>Paper / pages</th><th>PDF</th><th>First page</th></tr></thead><tbody>");
        using var combined=new PdfDocument();
        var metadata=new List<object>();
        foreach(var sample in samples)
        {
            string name=sample.Name;
            string pdfPath=Path.Combine(folder,name+".pdf");
            using var input=PdfReader.Open(pdfPath,PdfDocumentOpenMode.Import);
            int first=combined.PageCount;
            foreach(var page in input.Pages)combined.AddPage(page);
            combined.Outlines.Add(Title(name),combined.Pages[first],false);
            index.Append($"<tr><td>{H(Title(name))}<br><small>{H(sample.Document)} · {H(sample.Template)}</small></td><td>{H(sample.Paper)}<br>{sample.Pages} page(s)<br>Combined: {first+1}–{combined.PageCount}</td><td><a href=\"{H(name)}.pdf\">Open PDF</a></td><td><details><summary>Show preview</summary><a href=\"{H(name)}.png\"><img loading=\"lazy\" src=\"{H(name)}.png\" alt=\"First page: {H(Title(name))}\"></a></details></td></tr>");
            metadata.Add(new {sample.Name,sample.Document,sample.Paper,sample.Pages,sample.Template,CombinedFirstPage=first+1,
                Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pdfPath)))});
        }
        index.Append("</tbody></table><p>Review names and references, fee/month wrapping, discounts and credit, payment versus balance, child/family totals, legacy notices, page breaks and unused paper. Automated checks do not replace visual review or testing your own printer.</p></html>");
        int pageCount=combined.PageCount;
        Check(combined.Outlines.Count==samples.Count,"Each review scenario has a PDF bookmark");
        if(pageCount>0)
        {
            string path=Path.Combine(folder,"REVIEW-ALL.pdf");combined.Save(path);
            using var verify=PdfReader.Open(path,PdfDocumentOpenMode.Import);
            Check(verify.PageCount==samples.Sum(sample=>sample.Pages),"The combined review PDF preserves every scenario page");
        }
        File.WriteAllText(Path.Combine(folder,"REVIEW-INDEX.html"),index.ToString());
        File.WriteAllText(Path.Combine(folder,"provenance.json"),JsonSerializer.Serialize(new {SourceCommit=commit,Checks=status,ScenarioCount=samples.Count,PageCount=pageCount,Samples=metadata},new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine($"::notice title=Bill and receipt review pack::{samples.Count} scenarios, {pageCount} PDF pages. Checks: {status}.");
    }
}
