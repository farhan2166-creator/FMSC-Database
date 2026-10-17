namespace FMSC_SMS.Core;

internal readonly record struct ReportFilterArrangement(int Columns, int Rows)
{
    internal static ReportFilterArrangement For(int fieldCount, int availableWidth, int minimumFieldWidth)
    {
        if (fieldCount < 0) throw new ArgumentOutOfRangeException(nameof(fieldCount));
        if (minimumFieldWidth <= 0) throw new ArgumentOutOfRangeException(nameof(minimumFieldWidth));
        int columns = Math.Clamp(availableWidth / minimumFieldWidth, 1, Math.Max(1, fieldCount));
        return new(columns, Math.Max(1, (fieldCount + columns - 1) / columns));
    }
}
