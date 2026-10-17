namespace FMSC_SMS.Core;

internal static class StudentPickerGeometry
{
    internal static Rectangle TextBounds(
        Size controlSize,
        int preferredTextHeight,
        int leftInset,
        int rightInset,
        int dropDownWidth)
    {
        int width = Math.Max(0, controlSize.Width - leftInset - rightInset - dropDownWidth);
        int height = Math.Min(preferredTextHeight, Math.Max(0, controlSize.Height - 2));
        int top = Math.Max(0, (controlSize.Height - height) / 2);
        return new Rectangle(leftInset, top, width, height);
    }
}
