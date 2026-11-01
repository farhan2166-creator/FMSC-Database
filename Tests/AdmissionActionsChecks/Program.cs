using FMSC_SMS.Core;

int checks=0;
void Check(bool value,string message) { if (!value) throw new Exception(message);checks++; }
AdmissionWorkflowActions State(string status="Active",bool legacy=false,int draft=0,bool held=false,bool busy=false,
    bool admin=true,bool enroll=true,int student=1,bool registration=false,bool official=true) => AdmissionWorkflowActions.For(student,status,registration,legacy,draft,held,busy,admin,enroll,officialAdmission:official);
var active=State();Check(active.Hold&&active.Departure&&active.Transfer&&active.History,"A confirmed current student offers hold, departure, transfer and history");
Check(!active.Readmit&&!active.Resume&&!active.Setup&&!active.CompleteDraft&&!active.ReviewLeft,"Current students have no competing return or setup actions");
var left=State("Left");Check(left.History&&left.Readmit&&left.ReviewLeft,"A former student retains history, readmission and the explicit correction route");
Check(!left.Setup&&!left.Hold&&!left.Departure&&!left.Transfer&&!left.Resume,"Adding former-student history cannot route through current activation or departure");
var held=State(held:true);Check(held.Resume&&held.History,"Held students retain resume and history");
Check(!held.Hold&&!held.Departure&&!held.Readmit&&!held.Transfer&&!held.Setup&&!held.CompleteDraft,"Hold presents one return path");
var adopted=State("Left",held:true);Check(adopted.Resume&&!adopted.Readmit&&!adopted.ReviewLeft,"Adopted Left records return through resume, not readmission or another adoption");
var legacy=State(legacy:true);Check(legacy.Setup&&legacy.History&&!legacy.CompleteDraft,"Legacy activation and history are separate choices");
Check(!legacy.Hold&&!legacy.Departure&&!legacy.Readmit&&!legacy.Transfer,"Legacy setup cannot be mistaken for ordinary movement");
var draft=State(draft:5);Check(draft.CompleteDraft&&draft.History&&!draft.Setup,"An existing draft opens completion rather than legacy activation");
Check(!draft.Hold&&!draft.Departure&&!draft.Readmit&&!draft.Transfer,"Drafts retain the original movement restriction");
Check(!State(draft:-1).Enabled,"Unresolved confirmation metadata fails closed");
foreach(var state in new[]{State(student:0),State(registration:true)})
    Check(!(state.Setup||state.CompleteDraft||state.History||state.Hold||state.Resume||state.Departure||state.Readmit||state.Transfer||state.ReviewLeft),"Unsaved or pending registrations expose no saved-student lifecycle actions");
foreach(var state in new[]{State(),State("Left"),State(held:true),State(legacy:true),State(draft:5)})
    Check(state.Enabled,"Stable loaded records permit their relevant actions");
Check(!State(busy:true).Enabled,"Busy state blocks all action invocations");
var viewer=State(admin:false,enroll:false);Check(!(viewer.History||viewer.Hold||viewer.Departure||viewer.Transfer),"Viewing access grants no lifecycle permissions");
var transferOnly=State(admin:false);Check(transferOnly.Transfer&&!transferOnly.History&&!transferOnly.Hold,"Enrollment managers retain transfer without administrative actions");
var adminOnly=State(enroll:false);Check(adminOnly.History&&adminOnly.Hold&&!adminOnly.Transfer,"Student administrators do not gain transfer permission implicitly");
Check(State("active").Hold&&State("LEFT").Readmit,"Status casing cannot create misleading alternatives");
foreach(var status in new[]{"Left","Withdrawn","Inactive"})
    Check(State(status).Readmit,$"{status} is an eligible former-student state");
foreach(var status in new[]{"Active","Pending","Cancelled","Promoted"})
    Check(!State(status).Readmit,$"{status} cannot be readmitted");
Check(!State("Left",official:false).Readmit,"An unofficial or cancelled registration cannot use readmission");
Check(!AdmissionWorkflowActions.CanReadmit("Left",true,activeEnrollment:true),"An active enrollment blocks readmission");
Check(!AdmissionWorkflowActions.CanReadmit("Left",true,held:true),"An unresolved hold requires resume");
Check(!State(legacy:true,held:true).Setup,"An existing hold cannot be bypassed through legacy activation");
var historical=AdmissionWorkflowActions.For(1,"Active",false,false,0,false,false,true,true,false);
Check(historical.History&&historical.Transfer&&historical.Departure,"Historical students retain history-building transfers and departure");
Check(!historical.Hold&&!historical.Resume,"Historical placement cannot enter routine hold or resume");
var historicalHeld=AdmissionWorkflowActions.For(1,"Left",false,false,0,true,false,true,true,false);
Check(!historicalHeld.Resume&&!historicalHeld.ReviewLeft&&historicalHeld.History,"Historical hold needs a current placement before return; history stays available");
Console.WriteLine($"PASS: {checks} focused admission workflow action checks; no database or financial writes.");
