namespace FMSC_SMS.Core;

/// <summary>Uses the bounded shared cache so later layout passes can reuse widths.</summary>
internal sealed class TextWidthMeasurementSession
{
    internal int Measure(string text, Font font) => UiTextWidthCache.Measure(text, font);
}
