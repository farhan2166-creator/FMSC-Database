namespace FMSC_SMS.Core;

/// <summary>Bounded, session-wide cache of default-padded text widths.</summary>
internal static class UiTextWidthCache
{
    private const int EntryLimit = 8192;
    private const int MaximumTextLength = 256;
    private static readonly object Sync = new();
    private static readonly Dictionary<(string Text, Font Font), LinkedListNode<Entry>> Entries = new();
    private static readonly LinkedList<Entry> Recent = new();
    private readonly record struct Entry((string Text, Font Font) Key, int Width);

    static UiTextWidthCache()
    {
        AppScale.ScaleChanged += (_, _) => Clear();
        Application.ApplicationExit += (_, _) => Clear();
    }

    internal static int Measure(string text, Font font)
    {
        // Long values render normally; neither pages nor data tables are retained.
        if (text.Length > MaximumTextLength)
            return TextRenderer.MeasureText(text, font).Width;
        var key = (text, font);
        lock (Sync)
        {
            if (Entries.TryGetValue(key, out var node))
            {
                Recent.Remove(node);
                Recent.AddFirst(node);
                return node.Value.Width;
            }
            // Preserve the default padding used by grids; AppMetrics.TextWidth
            // uses NoPadding and intentionally remains a separate cache.
            int width = TextRenderer.MeasureText(text, font).Width;
            if (Entries.Count >= EntryLimit)
            {
                var oldest = Recent.Last!;
                Entries.Remove(oldest.Value.Key);
                Recent.RemoveLast();
            }
            Entries.Add(key, Recent.AddFirst(new Entry(key, width)));
            return width;
        }
    }

    internal static void Clear()
    {
        lock (Sync)
        {
            Entries.Clear();
            Recent.Clear();
        }
    }
}
