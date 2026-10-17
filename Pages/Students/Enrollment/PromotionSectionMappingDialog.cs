using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Students.Enrollment;

internal sealed class PromotionSectionMappingDialog : ApplicationWindowForm
{
    private readonly DataGridView grid = HumanUi.Grid(true);
    internal Dictionary<int,int> Mapping { get; } = new();
    internal string Summary { get; private set; } = "";

    internal PromotionSectionMappingDialog(IEnumerable<DataRow> selected, DataTable destinations, bool suggestMatches)
    {
        Text = "Choose Destination Sections";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(AppScale.Scale(760), AppScale.Scale(430));
        MinimizeBox = false;
        ShowInTaskbar = false;
        grid.Dock = DockStyle.Fill;
        grid.AutoGenerateColumns = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name="Source",HeaderText="Source section",ReadOnly=true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name="Count",HeaderText="Students to promote",ReadOnly=true });
        var choices=new DataTable();
        choices.Columns.Add("ClassSectionId",typeof(int)); choices.Columns.Add("Display",typeof(string));
        choices.Rows.Add(0,"Choose a destination section");
        foreach(DataRow row in destinations.Rows) choices.Rows.Add(row["ClassSectionId"],row["Display"]);
        grid.Columns.Add(new DataGridViewComboBoxColumn {
            Name="Destination",HeaderText="Destination section",DataSource=choices,
            ValueMember="ClassSectionId",DisplayMember="Display",DisplayStyle=DataGridViewComboBoxDisplayStyle.DropDownButton
        });
        foreach(var group in selected.Where(row=>Convert.ToString(row["Decision"])=="PROMOTE")
            .GroupBy(row=>Convert.ToInt32(row["SectionId"])).OrderBy(group=>Convert.ToString(group.First()["SectionName"])))
        {
            int destination=0;
            if(suggestMatches)
            {
                var matches=destinations.AsEnumerable().Where(row=>Convert.ToInt32(row["SectionId"])==group.Key).ToArray();
                if(matches.Length==1) destination=Convert.ToInt32(matches[0]["ClassSectionId"]);
            }
            int index=grid.Rows.Add(Convert.ToString(group.First()["SectionName"]),group.Count(),destination);
            grid.Rows[index].Tag=group.Key;
        }
        var continueButton=new ModernButton { Text="Review Promotion",ThemeRole="Primary" };
        var cancel=new ModernButton { Text=UiText.Actions.Cancel,DialogResult=DialogResult.Cancel };
        continueButton.Click+=(_,_)=> {
            grid.EndEdit();
            Mapping.Clear();
            var summary=new List<string>();
            foreach(DataGridViewRow row in grid.Rows)
            {
                int id=Convert.ToInt32(row.Cells["Destination"].Value ?? 0);
                var destination=destinations.AsEnumerable().SingleOrDefault(option=>Convert.ToInt32(option["ClassSectionId"])==id);
                if(destination is null)
                {
                    ModernMessageDialog.Show(this,"Choose a destination for every source section before continuing.","Destination Sections",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                    grid.CurrentCell=row.Cells["Destination"]; return;
                }
                Mapping.Add((int)row.Tag!,id);
                summary.Add($"{row.Cells["Source"].Value} → {destination["Display"]}: {row.Cells["Count"].Value} student(s)");
            }
            Summary=string.Join("\n",summary);
            DialogResult=DialogResult.OK;
        };
        var root=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(AppMetrics.SpacingMd) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppScale.Scale(55)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.ButtonRowHeight));
        root.Controls.Add(new Label { Dock=DockStyle.Fill,Text="Map each source section to a destination; multiple sections may share one. Held-back students keep their class and section." },0,0);
        root.Controls.Add(grid,0,1);
        var actions=HumanUi.ButtonRow(continueButton,cancel); actions.Dock=DockStyle.Fill; actions.Margin=Padding.Empty;
        root.Controls.Add(actions,0,2);
        Controls.Add(root);
        CancelButton=cancel;
    }
}
