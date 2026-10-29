using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Staff.Certificates;
using FMSC_SMS.Pages.Students.Certificates;

internal static partial class Program
{
    private static void CheckCertificateNavigation()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (Func<V2Page> create in new Func<V2Page>[] {
            () => new RegularStudentCertificatePage(false), () => new CharacterCertificatePage(false),
            () => new LeaveCertificatePage(null,false), () => new CollegeLeaveCertificatePage(false),
            () => new DateOfBirthCertificatePage(false), () => new SportsCertificatePage(false),
            () => new NoObjectionCertificatePage(false), () => new ProvisionalCertificatePage(false),
            () => new BankAccountOpeningCertificatePage(false), () => new RegularLecturerCertificatePage() })
        {
            using var page = create();
            string name = page.GetType().Name;
            var picker = Descendants(page).OfType<StudentPicker>().Single();
            var next = (ModernButton)page.GetType().GetField("clearButton",flags)!.GetValue(page)!;
            var grid = (DataGridView)typeof(V2Page).GetField("Grid",flags)!.GetValue(page)!;
            Control card = picker;
            while (card is not HumanCard) card = card.Parent!;
            Control root = card.Parent!;
            root.Parent!.Controls.Remove(root);
            using var form = new Form { ClientSize=new Size(1280,800), ShowInTaskbar=false,
                StartPosition=FormStartPosition.Manual, Location=new Point(-20000,-20000) };
            form.Controls.Add(root); root.Dock=DockStyle.Fill;
            form.Show(); Application.DoEvents();
            next.Focus();
            Check(page.FocusEntryField()&&picker.ContainsFocus,name+": page activation focuses the search text");
            next.Enabled=true; next.Focus(); next.PerformClick(); Application.DoEvents();
            Check(picker.ContainsFocus, name+": New/Next returns focus to the search text");
            Check(card.Controls.Count>0 && grid.TabIndex==0, name+": history starts at the grid");
            foreach (var flow in Descendants(root).OfType<FlowLayoutPanel>().Where(flow=>flow.Controls.OfType<ModernButton>().Any()))
                Check(flow.Controls.Cast<Control>().Select(control=>control.TabIndex).SequenceEqual(Enumerable.Range(0,flow.Controls.Count)),
                    name+": action Tab order matches the visible button order");
            Check(!page.IsHandleCreated, name+": navigation inspection starts no database loaders");
            form.Close();
        }

        using var host = new Form { ClientSize=new Size(900,600), ShowInTaskbar=false,
            StartPosition=FormStartPosition.Manual, Location=new Point(-20000,-20000) };
        using var search = new StudentPicker { Dock=DockStyle.Top };
        using var date = HumanUi.Date(); date.Dock=DockStyle.Top;
        using var editor = new Panel { Dock=DockStyle.Top, Height=100 };
        editor.Controls.Add(date); editor.Controls.Add(search);
        using var gridHistory = HumanUi.Grid();
        using var nextButton = new ModernButton { Text="New" };
        using var editorActions = CertificateButtonRow.Create(nextButton);
        using var historyActions = CertificateButtonRow.Create(new ModernButton { Text="Print" });
        host.Controls.Add(gridHistory); host.Controls.Add(editor);
        nextButton.Click+=(_,_)=>search.Clear();
        int loadedIndex=-1;
        CertificateUi.WireNavigation(host,search,gridHistory,nextButton,editor,editorActions,historyActions,index=>loadedIndex=index);
        host.Show(); Application.DoEvents();
        Check(search.ContainsFocus,"Launching a certificate starts in admission search");

        using var records = new DataTable();
        records.Columns.Add("Id",typeof(int)); records.Columns.Add("CertificateDate",typeof(DateTime));
        records.Rows.Add(99,DateTime.Today.AddDays(-2)); records.Rows.Add(12,DateTime.Today);
        gridHistory.DataSource=CertificateRecordOrder.NewestFirst(records,"Id"); Application.DoEvents();
        void SelectLatest()=>CertificateRecordOrder.Select(gridHistory,"Id",0,index=>loadedIndex=index);
        CertificateUi.FocusHistory(gridHistory,SelectLatest,date);
        Check(gridHistory.Focused && loadedIndex==0 && Convert.ToInt32(gridHistory.CurrentRow!.Cells["Id"].Value)==12,
            "Student loading selects and focuses the newest/top row even when another row has the highest ID");
        gridHistory.CurrentCell=gridHistory.Rows[1].Cells["CertificateDate"]; gridHistory.Rows[1].Selected=true;
        typeof(Control).GetMethod("OnKeyUp",flags)!.Invoke(gridHistory,new object[]{new KeyEventArgs(Keys.Down)});
        Check(loadedIndex==1,"Keyboard movement loads the newly selected history row's editor");
        CertificateRecordOrder.Select(gridHistory,"Id",12,index=>loadedIndex=index);
        Check(loadedIndex==0,"An explicit saved-copy selection still restores that exact copy");
        gridHistory.DataSource=new DataTable(); Application.DoEvents();
        CertificateUi.FocusHistory(gridHistory,()=>throw new Exception("No saved row to select"),date);
        Check(date.ContainsFocus,"An empty history moves focus to the first certificate field");
        host.Close();
        CheckRegularCertificateHistoryNavigation();
    }

    private static void CheckRegularCertificateHistoryNavigation()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var records=new DataTable();
        foreach(string name in new[]{"RegularStudentCertificateId","RegularStudentCertificateIssueId","AdmissionId","EnrollmentId"})records.Columns.Add(name,typeof(int));
        records.Columns.Add("ClassName");records.Columns.Add("CollectionStatus");
        foreach(string name in new[]{"CertificateDate","DateOfBirth","IssuedAt","CollectedAt"})records.Columns.Add(name,typeof(DateTime));
        records.Rows.Add(1,10,41,100,"Nine","Collected",DateTime.Today.AddDays(-1),new DateTime(2010,1,1),DateTime.Now.AddDays(-1),DateTime.Now);
        using var page=new RegularStudentCertificatePage(false,readCertificates:_=>records);
        T Field<T>(string name)=>(T)typeof(RegularStudentCertificatePage).GetField(name,flags)!.GetValue(page)!;
        var picker=Field<StudentPicker>("student");var create=Field<ModernButton>("createCertificateButton");
        Control card=picker;while(card is not HumanCard)card=card.Parent!;
        Control root=card.Parent!;root.Parent!.Controls.Remove(root);
        using var host=new Form {ClientSize=new Size(1280,800),ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)};
        root.Dock=DockStyle.Fill;host.Controls.Add(root);host.Show();Application.DoEvents();
        using var students=new DataTable();students.Columns.Add("AdmissionId",typeof(int));students.Columns.Add("ClassName");
        students.Columns.Add("Status");students.Columns.Add("DateOfBirth",typeof(DateTime));students.Columns.Add("EnrollmentId",typeof(int));
        students.Rows.Add(41,"Ten","Active",new DateTime(2010,1,1),101);
        picker.SetSource(students);picker.SetStudent(students.Rows[0]);
        typeof(RegularStudentCertificatePage).GetMethod("StudentChanged",flags)!.Invoke(page,null);
        var grid=(DataGridView)typeof(V2Page).GetField("Grid",flags)!.GetValue(page)!;
        Check(grid.Focused&&Field<int>("selectedIssueId")==10,"Regular certificate loading focuses the latest saved copy from an earlier class");
        Check(create.Enabled&&create.Text=="New for Current Class","Reviewing an older class still offers a new certificate for the current placement");
        create.PerformClick();
        Check(Field<int>("selectedIssueId")==0&&Field<ModernTextBox>("className").Text=="Ten"&&create.Enabled&&create.Text==UiText.Certificates.Create,
            "New for Current Class prepares a fresh draft without changing the older copy");
        Check(Field<ModernDatePicker>("certificateDate").ContainsFocus&&grid.SelectedRows.Count==0,
            "A current-class draft starts at its date and clears the historical selection");
        Check(!page.IsHandleCreated,"Current-class draft navigation performs no database operation");
        host.Close();
    }
}
