using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Collection;

internal static partial class Program
{
    private static void CheckCollectionAllocationPolicy()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var page=new FeeCollectionPage(false);
        var type=typeof(FeeCollectionPage);
        T Field<T>(string name)=>(T)type.GetField(name,flags)!.GetValue(page)!;
        var grid=Field<DataGridView>("charges");
        var discount=Field<ModernTextBox>("discountAmount");
        var credit=Field<Dictionary<int,decimal>>("availableCreditByStudent");
        var useCredit=Field<CheckBox>("useAvailableCredit");
        var numberField=typeof(AdmissionNumbers).GetField("_numbers",BindingFlags.Static|BindingFlags.NonPublic)!;
        var stampField=typeof(AdmissionNumbers).GetField("_loadedAt",BindingFlags.Static|BindingFlags.NonPublic)!;
        var oldNumbers=numberField.GetValue(null);var oldStamp=stampField.GetValue(null);
        numberField.SetValue(null,new Dictionary<int,string>{{1,"9000"},{2,"1000"},{3,"1"},{10,"10"},{20,"2"},{30,"2a"}});
        stampField.SetValue(null,DateTime.UtcNow);
        void Reset()
        {
            type.GetField("suppressPaymentEvents",flags)!.SetValue(page,true);
            type.GetField("suppressDiscountEvents",flags)!.SetValue(page,true);
            grid.Rows.Clear();credit.Clear();discount.Text="0";useCredit.Checked=true;
        }
        void Bill(long id,int child,int grade,string number,bool monthly,int month,decimal due,bool tuition=false,decimal? original=null)
        {
            var row=grid.Rows[grid.Rows.Add()];
            row.Cells["BillingChargeId"].Value=id;row.Cells["BillingChargeId"].Tag=tuition?0:1;
            row.Cells["AdmissionId"].Value=child;row.Cells["EnrollmentId"].Value=child+100;
            row.Cells["StudentName"].Value="Child "+child;row.Cells["StudentName"].Tag=(grade,number);
            row.Cells["FeeTypeName"].Value=tuition?"Tuition":"Other fee";row.Cells["FeeTypeName"].Tag=monthly;
            row.Cells["BillingMonth"].Tag=new DateTime(2026,month,1);
            row.Cells["Balance"].Value=due;row.Cells["Balance"].Tag=due;
            row.Cells["GrossAmount"].Value=original??due;row.Cells["GrossAmount"].Tag=0m;
            row.Cells["NetAmount"].Value=original??due;row.Cells["AppliedDiscount"].Value=0m;
        }
        decimal Cash(long id)=>Convert.ToDecimal(grid.Rows.Cast<DataGridViewRow>().Single(row=>Convert.ToInt64(row.Cells["BillingChargeId"].Value)==id).Cells["Amount"].Value);
        decimal Allocate(decimal money)=>(decimal)type.GetMethod("FillFromReceivedAmount",flags)!.Invoke(page,new object[]{money})!;
        Dictionary<long,decimal> CreditPlan()=>(Dictionary<long,decimal>)type.GetMethod("GetCreditAllocations",flags)!.Invoke(page,new object?[]{null})!;
        void Family()
        {
            Reset();
            // Deliberately load the senior's old tuition first and give the junior a higher admission number.
            Bill(31,3,10,"1",true,1,150,true);Bill(32,3,10,"1",false,1,200);Bill(33,3,10,"1",false,2,50);
            Bill(21,2,3,"1000",true,3,200,true);Bill(22,2,3,"1000",false,2,200);
            Bill(11,1,0,"9000",true,5,100,true);Bill(12,1,0,"9000",false,5,400);
        }
        try
        {
            Reset();
            Bill(1,1,0,"9000",true,2,300,true);
            Bill(2,1,0,"9000",true,1,100); // Transport predates tuition.
            Bill(3,1,0,"9000",false,1,500);
            Bill(4,1,0,"9000",false,2,200,original:1000);
            Bill(5,1,0,"9000",false,3,50);
            Allocate(250);
            Check(Cash(2)==100 && Cash(1)==150 && Cash(3)==0 && Cash(4)==0 && Cash(5)==0,
                "All monthly fees use oldest-month priority, including transport before newer tuition.");
            Allocate(700);
            Check(Cash(1)==300 && Cash(2)==100 && Cash(5)==50 && Cash(4)==200 && Cash(3)==50,
                "After monthly fees, other fees use the smallest unpaid balance, rather than their original amount or date.");
            credit[1]=450;Allocate(250);
            var plan=CreditPlan();
            Check(plan[2]==100 && plan[1]==300 && plan[5]==50 && Cash(4)==200 && Cash(3)==50,
                "The student's advance follows the agreed order before cash is split over the remaining payable balances.");

            Family();Allocate(550);
            Check(Cash(11)==100 && Cash(12)==400 && Cash(21)==50 && Cash(22)==0 && Cash(31)==0,
                "The junior is settled in full before the next class, even when a senior has older monthly dues.");
            Allocate(50);
            Check(Cash(11)==50 && Cash(12)==0 && Cash(21)==0 && Cash(31)==0,
                "An amount too small for the junior stays with that child and starts with monthly fees.");
            Allocate(1200);
            Check(Cash(11)==100 && Cash(12)==400 && Cash(21)==200 && Cash(22)==200 &&
                Cash(31)==150 && Cash(33)==50 && Cash(32)==100,
                "The final senior receives the remainder on monthly fees, then smaller other fees before larger fees.");
            foreach(decimal money in new[]{1m,99m,100m,499m,500m,501m,899m,900m,901m,1049m,1050m,1300m})
            {
                decimal placed=Allocate(money);
                Check(placed==money && grid.Rows.Cast<DataGridViewRow>().Sum(row=>Convert.ToDecimal(row.Cells["Amount"].Value))==money,
                    "Every proposed rupee is allocated exactly once across class and fee boundaries.");
            }
            credit[3]=100;Allocate(500);
            Check(Cash(11)==100 && Cash(12)==400 && Cash(31)==0 && CreditPlan()[31]==100,
                "A senior's own advance stays with the senior and cannot fund the junior's charges.");

            Reset();Bill(10,10,2,"10",true,1,200,true);Bill(20,20,2,"2",false,5,200);
            Allocate(100);
            Check(Cash(20)==100 && Cash(10)==0,"Same-class siblings use the numeric admission number, not student ID or charge age.");
            Bill(30,30,2,"2a",true,1,200,true);Allocate(300);
            Check(Cash(20)==200 && Cash(30)==100 && Cash(10)==0,"Admission suffixes retain their natural position before the next admission number.");

            Reset();Bill(40,1,0,"9000",true,5,500,true);Bill(41,1,0,"9000",true,1,500);
            discount.Text="100";
            var discounts=(Dictionary<long,decimal>)type.GetMethod("GetDiscountAllocations",flags)!.Invoke(page,null)!;
            Check(discounts[40]==100 && !discounts.ContainsKey(41),"The separate discount allocation retains its existing largest-fee and tuition tie policy.");
            Allocate(100);
            Check(Cash(41)==100 && Cash(40)==0,"Payment still follows oldest monthly charges after the independent discount calculation.");

            Family();Allocate(100);
            type.GetMethod("ApplyProposedAmountForChild",flags)!.Invoke(page,new object[]{3,75m});
            Check(Cash(31)==75 && Cash(11)==100,"An operator can still override a child's Pay amount without replacing other children's chosen amounts.");
        }
        finally{numberField.SetValue(null,oldNumbers);stampField.SetValue(null,oldStamp);}
    }
}
