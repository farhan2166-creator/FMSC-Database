using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Collection;

internal static partial class Program
{
    private static void CheckCollectionSiblingNavigation()
    {
        using var page = new FeeCollectionPage(false);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(FeeCollectionPage);
        void Set(string name, object? value) => type.GetField(name, flags)!.SetValue(page, value);
        T Get<T>(string name) => (T)type.GetField(name, flags)!.GetValue(page)!;
        Task Open(int id) => (Task)type.GetMethod("OpenSiblingStudentAsync", flags)!.Invoke(page, new object[] { id })!;
        var siblings = Get<CheckedListBox>("siblings");
        Set("suppressSiblingEvents", true);
        void Add(int id, int placement, bool outstanding) => type.GetMethod("AddSiblingChoice", flags)!
            .Invoke(page, new object[] { id, placement, "Child", "Child", outstanding });
        Add(20, 200, true);
        Add(30, 300, false);
        Add(40, 0, true);
        Check(siblings.GetItemChecked(0), "A sibling with outstanding fees is included by default");
        Check(!siblings.GetItemChecked(1) && !siblings.GetItemChecked(2),
            "Debt-free siblings and children without a collectible placement are not included by default");
        Check(((FamilyStudentList)siblings).CheckBoxOnly,
            "Clicking a sibling's name preserves their payment checkbox");
        Set("counterIds", new[] { 10 });
        Add(50, 500, true);
        Check(!siblings.GetItemChecked(3), "Default inclusion cannot add children to a fixed counter visit");
        Set("counterIds", null);
        siblings.Items.Clear();
        Set("suppressSiblingEvents", false);
        void Finish(Task task)
        {
            var until = DateTime.UtcNow.AddSeconds(3);
            while (!task.IsCompleted && DateTime.UtcNow < until) Application.DoEvents();
            Check(task.IsCompleted, "Sibling navigation completes after its pending read finishes");
            task.GetAwaiter().GetResult();
        }
        Set("admissionId", 10);
        Get<Dictionary<int, (int EnrollmentId, string Name)>>("payingFor")[20] = (200, "Sibling");
        Check(Open(0).IsCompleted && Open(10).IsCompleted && Open(99).IsCompleted,
            "Invalid, current and unlisted students cannot start sibling navigation");
        Set("transactionBusy", true);
        Check(Open(20).IsCompleted && !Get<bool>("switchingSibling"), "Recording a payment blocks sibling navigation");
        Set("transactionBusy", false);
        Set("billBusy", true);
        Check(Open(20).IsCompleted && !Get<bool>("switchingSibling"), "A bill workflow blocks sibling navigation");
        Set("billBusy", false);
        Set("counterIds", new[] { 10, 20 });
        Check(Open(20).IsCompleted && !Get<bool>("switchingSibling"), "A fixed counter visit keeps its selected account");
        Set("counterIds", null);
        var pending = new TaskCompletionSource();
        Set("siblingLoadTask", pending.Task);
        Set("siblingBusy", true);
        var navigation = Open(20);
        Application.DoEvents();
        Check(!navigation.IsCompleted && Get<int>("admissionId") == 10,
            "Double-click waits for the single-click fee read before changing accounts");
        Check(Open(20).IsCompleted, "Repeated double-clicks cannot start overlapping student loads");
        Set("siblingBusy", false);
        Set("loadVersion", Get<int>("loadVersion") + 1);
        pending.SetResult();
        Finish(navigation);
        Check(Get<int>("admissionId") == 10 && !Get<bool>("switchingSibling"),
            "A newer student or placement selection cancels the queued sibling navigation");

        pending = new TaskCompletionSource();
        Set("siblingLoadTask", pending.Task);
        navigation = Open(20);
        Application.DoEvents();
        Set("admissionId", 30);
        pending.SetResult();
        Finish(navigation);
        Check(Get<int>("admissionId") == 30, "A queued sibling navigation cannot overwrite another student's account");
    }
}
