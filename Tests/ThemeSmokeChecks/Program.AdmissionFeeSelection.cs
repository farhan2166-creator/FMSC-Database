using System.Collections;
using System.Reflection;
using FMSC_SMS.Pages.Students.Admissions;

internal static partial class Program
{
    private static void CheckAdmissionFeeSelection()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var page=new StudentAdmissionPage();var type=page.GetType();
        type.GetMethod("EnsureDetailsTabBuilt",flags)!.Invoke(page,null);
        var grid=(DataGridView)type.GetField("feeGrid",flags)!.GetValue(page)!;
        var selected=(IList)type.GetField("_selectedFees",flags)!.GetValue(page)!;
        var rowType=selected.GetType().GetGenericArguments()[0];
        grid.Parent!.Controls.Remove(grid);
        using var form=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000),ClientSize=new Size(1100,400)};
        form.Controls.Add(grid);grid.Dock=DockStyle.Fill;grid.DataSource=selected;form.Show();Application.DoEvents();
        object Add(string code,bool hasPayments=false)
        {
            var row=Activator.CreateInstance(rowType,true)!;
            foreach(var (name,value) in new (string,object)[]{("FeeTypeCode",code),("FeeTypeName",code),("IsMandatory",true),("HasPayments",hasPayments)})
                rowType.GetProperty(name)!.SetValue(row,value);
            selected.Add(row);Application.DoEvents();return row;
        }
        void Remove()=>type.GetMethod("FeeGrid_CellContentClick",flags)!.Invoke(page,new object[]{grid,new DataGridViewCellEventArgs(grid.Columns["Remove"].Index,0)});
        Add("TUITION");Remove();Check(selected.Count==0,"Auto-loaded tuition can be removed from a new admission.");
        Add("ADMISSION");Remove();Check(selected.Count==0,"Auto-loaded admission fee can be removed, including the last selected fee.");
        Add("TUITION",true);Remove();Check(selected.Count==1,"A fee with financial history remains protected.");selected.Clear();
        type.GetField("currentAdmissionId",flags)!.SetValue(page,21);
        Add("TUITION");Remove();Check(selected.Count==1,"Draft fee removal does not delete an existing student's saved tuition assignment.");
        Check(!page.IsHandleCreated,"Fee removal checks start no student lookups or database writes.");
    }
}
