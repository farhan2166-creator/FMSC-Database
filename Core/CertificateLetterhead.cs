using System.Xml.Linq;

namespace FMSC_SMS.Core;

internal static class CertificateLetterhead
{
    internal static bool UsesPreprintedPad(string path) =>
        Path.GetFileName(path) is "LeaveCertificate.rdlc" or "CollegeLeaveCertificate.rdlc";

    internal static string Definition(string path, DataRow? profile, DataRow? certificate = null, IReadOnlyList<string>? provisionalSubjects = null)
    {
        var document = XDocument.Load(path);
        XNamespace ns = document.Root!.Name.Namespace;
        string assetDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");
        foreach (var item in new[] { ("CertificateHeaderImage", "header.png") })
        {
            string imagePath = Path.Combine(assetDirectory, item.Item2);
            if (!File.Exists(imagePath))
                throw new FileNotFoundException($"The certificate image is missing: Assets/{item.Item2}.", imagePath);
            var image = document.Descendants(ns + "Image").Single(element => (string?)element.Attribute("Name") == item.Item1);
            image.Element(ns + "Value")!.Value = new Uri(imagePath).AbsoluteUri;
            using var bitmap = new Bitmap(imagePath);
            double width = Math.Min(7.45, 2.0 * bitmap.Width / bitmap.Height);
            image.Element(ns + "Width")!.Value = width.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "in";
            image.SetElementValue(ns + "Left", ((7.45 - width) / 2).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "in");
        }
        string Text(string column) => profile is null ? string.Empty : CertificateUi.GetString(profile, column);
        string address = string.Join(" ", new[] { Text("AddressLine"), Text("City") }.Where(value => !string.IsNullOrWhiteSpace(value)));
        string contact = Text("ContactNumber");
        string email = Text("Email");
        string details = string.Join("   |   ", new[]
        {
            string.IsNullOrWhiteSpace(address) ? string.Empty : "Address: " + address,
            string.IsNullOrWhiteSpace(contact) ? string.Empty : "Cell: " + contact
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        foreach (var item in new[] { ("CertificateFooterContact", details), ("CertificateFooterEmail", string.IsNullOrWhiteSpace(email) ? string.Empty : "Email: " + email) })
        {
            var textbox = document.Descendants(ns + "Textbox").Single(element => (string?)element.Attribute("Name") == item.Item1);
            textbox.Descendants(ns + "Value").Single().Value = item.Item2;
        }
        if (Path.GetFileName(path) == "ProvisionalCertificate.rdlc" && certificate is not null)
        {
            if (provisionalSubjects is not null) ProvisionalSubjectLayout.Apply(document, provisionalSubjects);
            else ProvisionalSubjectLayout.Apply(document, CertificateUi.GetString(certificate, "SubjectsPassedLine"));
        }
        return document.ToString();
    }
}
