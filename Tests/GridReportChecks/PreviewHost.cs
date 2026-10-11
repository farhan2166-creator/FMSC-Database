// Definition generation and rendering run here; opening the preview needs Windows.
public interface IWin32Window { }
namespace FMSC_SMS.Core
{
    internal static class RdlcReportPreview
    {
        internal static void Show(IWin32Window? owner, string title, string path, string dataSet,
            System.Data.DataTable data, string reportDefinition) => throw new NotSupportedException();
    }
}
