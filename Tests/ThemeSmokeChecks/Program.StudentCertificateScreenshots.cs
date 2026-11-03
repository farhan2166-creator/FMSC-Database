using System.Data;
using System.Drawing.Imaging;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using FMSC_SMS.Pages.Students.Certificates;

internal static partial class Program
{
    /// <summary>
    /// Bounded noninteractive screenshots of the real current WinForms controls.
    /// All person/certificate data in these captures is fictional and injected
    /// directly into read-only presentation controls, never persisted.
    /// </summary>
    private static void CaptureStudentCertificateScreenshots()
    {
        const BindingFlags privateFields = BindingFlags.Instance | BindingFlags.NonPublic;
        var previous = (CurrentUser.UserId, CurrentUser.Username, CurrentUser.RoleName,
            CurrentUser.MustChangePassword, Permissions: CurrentUser.GetPermissions().ToArray());
        string root = Path.GetFullPath("artifacts/student-certificates-preview");
        Directory.CreateDirectory(root);
        try
        {
            CurrentUser.SignIn(991, "CI screenshot fixture", "Administrator", false,
                Array.Empty<string>());

            using var students = new DataTable();
            students.Columns.Add("AdmissionId", typeof(int));
            students.Columns.Add("StudentName");
            students.Columns.Add("AdmissionNumber");
            students.Columns.Add("FatherName");
            students.Columns.Add("CampusName");
            students.Columns.Add("ClassName");
            students.Columns.Add("SectionName");
            students.Columns.Add("Status");
            students.Columns.Add("DateOfBirth", typeof(DateTime));
            students.Columns.Add("EnrollmentId", typeof(int));
            students.Columns.Add("ClassId", typeof(int));
            students.Columns.Add("ProgramType");
            DataRow sample = students.Rows.Add(41, "Ayesha Khan", "SYN-2026-041",
                "Imran Khan", "Main Campus", "Class 10", "A", "Active",
                new DateTime(2010, 3, 14), 101, 10, "School");

            using (var workspace = new StudentCertificatesWorkspacePage(false))
            using (var host = FixtureHost(workspace, new Size(1510, 910), "Student Certificates Workspace"))
            {
                // Populate the actual WinForms presentation with synthetic rows.
                // SetStudent deliberately does not fire a load event or query SQL.
                var picker = (StudentPicker)typeof(StudentCertificatesWorkspacePage)
                    .GetField("student", privateFields)!.GetValue(workspace)!;
                picker.SetSource(students);
                picker.SetStudent(sample);
                var header = (Label)typeof(StudentCertificatesWorkspacePage)
                    .GetField("identity", privateFields)!.GetValue(workspace)!;
                header.Text = "Ayesha Khan  ·  SYN-2026-041" + Environment.NewLine +
                    "Father: Imran Khan  ·  Main Campus / Class 10 / A  ·  Active";
                using var records = StudentCertificateRegisterReader.CreateTable();
                records.Rows.Add("Character", "Character", "12 Oct 2026", "CHAR-102",
                    "Issue logged", "Collected", "13 Oct 2026", "12 Oct 2026 09:30",
                    102, 902, new DateTime(2026,10,12,9,30,0));
                records.Rows.Add("Regular", "Regular Student", "15 Sep 2026", "REG-341",
                    "Issue logged", "Not Collected", "—", "15 Sep 2026 11:00",
                    341, 441, new DateTime(2026,9,15,11,0,0));
                records.Rows.Add("Sports", "Sports", "09 Aug 2026", "SPORT-77",
                    "Issue logged", "Collected", "10 Aug 2026", "09 Aug 2026 14:10",
                    77, 0, new DateTime(2026,8,9,14,10,0));
                records.Rows.Add("DOB", "Date of Birth", "20 Jun 2026", "DOB-29",
                    "Issue logged", "Not Collected", "—", "20 Jun 2026 10:15",
                    29, 329, new DateTime(2026,6,20,10,15,0));
                records.Rows.Add("NOC", "No Objection", "01 May 2026", "NOC-58",
                    "Issue logged", "Collected", "03 May 2026", "01 May 2026 12:40",
                    58, 0, new DateTime(2026,5,1,12,40,0));
                var grid = (DataGridView)typeof(V2Page)
                    .GetField("Grid", privateFields)!.GetValue(workspace)!;
                grid.DataSource = records.DefaultView;
                host.Show(); Application.DoEvents();
                if (grid.Rows.Count > 0)
                {
                    grid.ClearSelection();
                    grid.CurrentCell = grid.Rows[0].Cells["Type"];
                    grid.Rows[0].Selected = true;
                }
                Capture(host, root, "01-workspace-student-issued-certificates.png");
                host.ClientSize = new Size(1180, 750);
                Capture(host, root, "02-workspace-compact-window.png");
                host.Close();
            }

            CaptureCompactEditor("03-character-certificate-details-dialog.png", "Character",
                () => new CharacterCertificatePage(false), root, sample);
            CaptureCompactEditor("04-regular-student-details-dialog.png", "Regular Student",
                () => new RegularStudentCertificatePage(false), root, sample);
            CaptureCompactEditor("05-leaving-certificate-details-dialog.png", "Leave",
                () => new LeaveCertificatePage(null, false), root, sample);
            CaptureCompactEditor("06-provisional-certificate-details-dialog.png", "Provisional",
                () => new ProvisionalCertificatePage(false), root, sample);

            var files = Directory.GetFiles(root, "*.png");
            Check(files.Length == 6 && files.All(f => new FileInfo(f).Length > 4000),
                "Six actual WinForms workspace and compact-dialog images were rendered with fictional fixture data.");
            Console.WriteLine("SCREENSHOTS: " + string.Join(", ", files.Select(Path.GetFileName)));
            Console.WriteLine("Noninteractive control render only; no database, operator input, printer, or installation was exercised.");
        }
        finally
        {
            CurrentUser.SignIn(previous.UserId, previous.Username, previous.RoleName,
                previous.MustChangePassword, previous.Permissions);
        }
    }

    private static Form FixtureHost(Control page, Size size, string title)
    {
        var form = new Form
        {
            Text = title,
            AutoScaleMode = AutoScaleMode.None,
            ClientSize = size,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000),
            BackColor = AppTheme.PageBackground
        };
        page.Dock = DockStyle.Fill;
        form.Controls.Add(page);
        return form;
    }

    private static void CaptureCompactEditor(string name, string type,
        Func<Control> factory, string folder, DataRow sample)
    {
        using var dialog = CertificateWorkspaceDetailsDialog.BuildForScreenshot(
            type, sample, factory);
        dialog.Show(); Application.DoEvents();
        Capture(dialog, folder, name);
        dialog.Close();
    }

    private static void Capture(Form form, string folder, string name)
    {
        form.PerformLayout();
        Application.DoEvents();
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        using (var canvas = Graphics.FromImage(image))
        using (var bg = new SolidBrush(Color.FromArgb(238, 35, 47, 64)))
        using (var fg = new SolidBrush(Color.White))
        using (var font = new Font("Segoe UI", 10, FontStyle.Bold))
        {
            const string note = "CI RENDER · FICTIONAL DATA · NOT A LIVE DESKTOP SESSION";
            var measurement = canvas.MeasureString(note, font);
            var area = new RectangleF(10, image.Height - 31,
                Math.Min(image.Width - 20, measurement.Width + 24), 25);
            canvas.FillRectangle(bg, area);
            canvas.DrawString(note, font, fg, area.Left + 9, area.Top + 2);
        }
        string path = Path.Combine(folder, name);
        image.Save(path, ImageFormat.Png);
        Console.WriteLine($"CAPTURED {path} ({image.Width}x{image.Height})");
    }
}
