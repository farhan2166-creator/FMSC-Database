namespace FMSC_SMS.Core;

/// <summary>Maps a confirmed failure to explanation and appropriate recovery actions.</summary>
internal sealed record DatabaseConnectionGuidance(string Status, string Message, bool CanRestart, bool CanRepair)
{
    internal static DatabaseConnectionGuidance Create(string message, Exception? failure, bool databaseFileExists)
    {
        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            if (current is DatabaseCompatibilityException compatibility)
                return new(compatibility.Failure.Title, compatibility.Failure.ConnectionMessage, false, false);
            if (current is DatabaseRecoveryException recovery)
                return recovery.Reason switch {
                    "EngineMissing" => new("SQL connection software needs attention", recovery.Message +
                        "\r\n\r\nChoose Repair Connection to reinstall SQL Server LocalDB. Internet access and Windows administrator approval are required. Restart Connection cannot install missing software.", false, true),
                    "EngineUnavailable" => new("Database engine could not start", recovery.Message +
                        "\r\n\r\nIf restarting does not help, Repair Connection can reinstall SQL Server LocalDB. Internet access and Windows administrator approval are required.", true, true),
                    "EngineBusy" => new("Database engine is busy", recovery.Message, true, false),
                    "WrongEngine" => new("Different database engine connected", recovery.Message +
                        "\r\n\r\nAsk the administrator to connect the correct FMSC LocalDB instance, then choose Reconnect. Restart and repair are unavailable for this connection.", false, false),
                    "AttachFailed" => new("School data file could not be opened", recovery.Message, true, false),
                    _ => new("Connection needs attention", recovery.Message, true, false)
                };
        }
        if (!databaseFileExists)
            return new("School data file is missing",
                "Your saved school data file could not be found. Ask the administrator to restore access to the school records, then choose Reconnect. Restart and SQL repair cannot restore a missing data file.", false, false);
        // Retain permission, timeout and other operator-facing explanations instead of
        // guessing the cause from words such as 'Error' or 'LocalDB'.
        return new("Connection needs attention", message +
            "\r\n\r\nChoose Reconnect. If the connection still fails, try Restart Connection or contact the administrator.", true, false);
    }
}
