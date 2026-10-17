namespace FMSC_SMS.Core;

internal static class ActionBusyMessage
{
    internal static string? For(string text)
    {
        string label = (text ?? string.Empty).TrimStart();
        while (label.Length > 0 && !char.IsLetter(label[0]))
            label = label[1..];
        bool Starts(string word) => label.StartsWith(word, StringComparison.OrdinalIgnoreCase);
        if (Starts("Save")) return "Saving…";
        if (Starts("Collect")) return "Collecting payment…";
        if (Starts("Search") || Starts("Find")) return "Searching…";
        if (Starts("Load") || Starts("View") || Starts("Scan")) return "Loading…";
        if (Starts("Refresh") || Starts("Reload")) return "Refreshing…";
        if (Starts("Generate") || Starts("Run")) return "Generating…";
        if (Starts("Preview")) return "Loading preview…";
        if (Starts("Print")) return "Preparing printout…";
        if (Starts("Export")) return "Exporting…";
        if (Starts("Backup")) return "Creating backup…";
        if (Starts("Confirm")) return "Confirming…";
        if (Starts("Apply")) return "Applying…";
        if (Starts("Post")) return "Posting…";
        if (Starts("Reverse")) return "Reversing…";
        if (Starts("Refund")) return "Recording refund…";
        if (Starts("Promote")) return "Promoting students…";
        if (Starts("Restore")) return "Restoring…";
        if (Starts("Issue")) return "Issuing…";
        if (Starts("Delete")) return "Deleting…";
        if (Starts("Remove")) return "Removing…";
        if (Starts("Cancel")) return "Cancelling…";
        if (Starts("Execute") || Starts("Process")) return "Processing…";
        if (Starts("Set")) return "Updating…";
        return null;
    }
}
