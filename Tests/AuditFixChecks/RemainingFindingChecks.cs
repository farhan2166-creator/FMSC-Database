using FMSC_SMS.Core;
using System.Data;
using System.Drawing;

internal static class RemainingFindingChecks
{
    internal static int Run()
    {
        int count=0;
        void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
        foreach(var pair in new[]{(false,false),(true,false),(false,true),(true,true)}){
            bool granted=QuickRegistrationAccess.CanUse(p=>p=="Students.Admit"?pair.Item1:p=="Students.View"&&pair.Item2);
            Check(granted==(pair.Item1&&pair.Item2),"QR requires both existing permissions");
        }
        Check(QuickRegistrationAccess.CanUse(_=>true),"Administrator bypass from HasPermission remains supported");
        Check(!QuickRegistrationAccess.CanUse(p=>p is "Students.View" or "Students.Administer"),"Administer does not implicitly grant Admit");

        var table=new DataTable{CaseSensitive=false,Locale=System.Globalization.CultureInfo.InvariantCulture};
        table.Columns.Add("Id",typeof(int));
        foreach(string column in new[]{"Reference","StudentName","FatherName","ContactNumber","Status","Verification"})table.Columns.Add(column,typeof(string));
        table.Rows.Add(1,"QR-1","Alice","O'Neil","0300","Pending","Needs Review");
        table.Rows.Add(2,"A-2","Ali [100%]*",DBNull.Value,"0302","Active","Verified");
        table.Rows.Add(3,"L-3","علی","محمد",DBNull.Value,"Left","Needs Review");
        table.Rows.Add(4,"A-4","Zain","Prakash","0304567","Active","Needs Review");
        using var view=new DataView(table);
        void Match(bool pending,bool active,bool needs,string term,params int[] ids){
            view.RowFilter=AdmissionReviewFilter.Create(pending,active,needs,term);
            Check(view.Cast<DataRowView>().Select(r=>(int)r["Id"]).SequenceEqual(ids),"Literal filter: "+term);
            Check(ReferenceEquals(view.Table,table)&&table.Rows.Count==4,"Filtering retains the source table/rows");
        }
        Match(true,true,true,"",1,2,3,4);
        Match(false,true,false,"",2,4);
        Match(false,false,true,"",1,3,4);
        Match(false,true,true,"",4);
        Match(true,false,false," alice ",1);
        Match(true,false,false,"o'neil",1);
        Match(true,false,false,"0304567",4);
        foreach(string literal in new[]{"%","*","[","]","[100%]","[100%]*"})Match(true,false,false,literal,2);
        Match(true,false,false,"علی",3);
        Match(true,false,false,"محمد",3);
        Match(true,false,false,"missing");
        Match(true,false,false,"%' OR [Status]='Active");
        view.Sort="StudentName DESC";
        view.RowFilter=AdmissionReviewFilter.Create(true,false,false,"a");
        Check(view.Sort=="StudentName DESC","Filtering preserves operator sorting");

        foreach(var work in new[]{new Rectangle(0,0,1024,728),new Rectangle(0,0,1366,728),new Rectangle(0,0,1920,1040),new Rectangle(-1280,20,1280,680),new Rectangle(1920,-100,1024,728)})
        foreach(float scale in new[]{.82f,1f,1.25f,1.5f}){
            var wanted=new Rectangle(work.Left-50,work.Top-50,(int)(1280*scale),(int)(900*scale));
            var minimum=new Size((int)(1000*scale),(int)(700*scale));
            var result=WorkingAreaLayout.Fit(wanted,minimum,work);
            Check(work.Contains(result.Bounds),"Scaled dialog fits the monitor working area");
            Check(result.MinimumSize.Width<=result.Bounds.Width&&result.MinimumSize.Height<=result.Bounds.Height,"Minimum cannot force clipping");
            Check(result.Bounds.Width<=work.Width-16&&result.Bounds.Height<=work.Height-16,"Working-area inset retained");
        }
        var normal=WorkingAreaLayout.Fit(new Rectangle(20,20,800,600),new Size(400,300),new Rectangle(0,0,1920,1040));
        Check(normal.Bounds==new Rectangle(20,20,800,600),"A fitting window stays unchanged");
        return count;
    }
}
