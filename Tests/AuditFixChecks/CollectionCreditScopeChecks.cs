using FMSC_SMS.Core;

internal static class CollectionCreditScopeChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var balances=new Dictionary<int,decimal>{{163,5000m},{162,0m}};
        var siblingFee=new[]{(1L,162,2300m)};
        check(CollectionCreditAllocation.Build(siblingFee,balances,163).Count==0,
            "A 5000 main-student advance cannot pay a sibling's 2300 fee");
        check(CollectionCreditAllocation.Build(siblingFee,balances,162).Count==0,
            "Changing the main student does not move the old main student's money");
        var own=CollectionCreditAllocation.Build(new[]{(2L,163,2300m)},balances,163);
        check(own[2]==2300m&&balances[163]-own.Values.Sum()==2700m,
            "The main student's own fee is fully covered with 2700 advance left and zero new cash");
        check(balances[163]==5000m&&balances[162]==0m,"Planning never posts or transfers advance");
        balances[162]=1000m;
        check(CollectionCreditAllocation.Build(siblingFee,balances,163).Count==0,
            "Including a sibling does not silently opt in that sibling's advance");
        check(CollectionCreditAllocation.Build(siblingFee,balances,162)[1]==1000m,
            "Selecting that sibling as main uses only their own 1000, leaving 1300 payable");
        balances[163]=2700m;balances[162]=2300m;
        check(CollectionCreditAllocation.Build(siblingFee,balances,162)[1]==2300m,
            "After an explicit transfer, the recipient selected as main can settle with zero cash");
        check(CollectionCreditAllocation.Build(siblingFee,balances,163).Count==0,
            "A transfer still requires collection under the recipient's own main-student selection");
        var multiple=new[]{(3L,163,400m),(4L,163,2500m),(5L,162,400m)};
        balances[163]=1000m;balances[162]=100m;
        var plan=CollectionCreditAllocation.Build(multiple,balances,163);
        check(plan.Count==2&&plan[3]==400m&&plan[4]==600m,"Ordered fees share the main student's advance without overspending");
        check(!plan.ContainsKey(5),"A sibling's fee never borrows the unused or exhausted main advance");
        plan=CollectionCreditAllocation.Build(new[]{(6L,163,1800m)},new Dictionary<int,decimal>{{163,5000m}},163);
        check(plan[6]==1800m,"Advance is capped at the remaining fee after discount");
        plan=CollectionCreditAllocation.Build(new[]{(0L,163,100m),(7L,163,0m),(8L,163,-100m),(9L,999,100m)},balances,163);
        check(plan.Count==0,"Missing charges, settled fees, negative dues and unowned balances do not create applications");
        check(CollectionCreditAllocation.Build(multiple,new Dictionary<int,decimal>{{163,-5m}},163).Count==0,
            "Negative ledger balances are not spendable advance");
        check(CollectionCreditAllocation.Build(multiple,balances,0).Count==0,"No main student means no ordinary advance application");
        var counter=CollectionCreditAllocation.Build(multiple,balances,null);
        check(counter[3]==400m&&counter[4]==600m&&counter[5]==100m,
            "The fixed family counter retains separate per-child advance accounts");
        check(counter.Values.Sum()==1100m&&balances[163]==1000m&&balances[162]==100m,
            "Counter planning conserves each child's balance without changing the ledger");
    }
}
