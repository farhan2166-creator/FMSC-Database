using FMSC_SMS.Core;

internal static class ConnectionRecoveryChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new Exception(label);
            checks++;
        }
        foreach (var (reason, restart, repair) in new[] {
            ("EngineMissing", false, true), ("EngineUnavailable", true, true),
            ("EngineBusy", true, false), ("WrongEngine", false, false), ("AttachFailed", true, false)
        })
        {
            var failure = new DatabaseRecoveryException(reason, "Specific operator explanation", "Engine output: code 123");
            var guidance = DatabaseConnectionGuidance.Create("Generic outer message", failure, true);
            Check(guidance.Message.Contains("Specific operator explanation"), "Recovery must retain its operator explanation: " + reason);
            Check(guidance.CanRestart == restart && guidance.CanRepair == repair, "Recovery actions must match the confirmed reason: " + reason);
            Check(failure.ToString().Contains(reason) && failure.ToString().Contains("Engine output: code 123"), "Recovery diagnostics must reach the log/details: " + reason);
        }
        var wrapped = new InvalidOperationException("Outer error", new DatabaseRecoveryException("WrongEngine", "Different engine"));
        var nested = DatabaseConnectionGuidance.Create("Generic", wrapped, true);
        Check(!nested.CanRestart && !nested.CanRepair && nested.Message.Contains("Different engine"), "Wrapped recovery failures retain safe actions");
        var missing = DatabaseConnectionGuidance.Create("Generic", new FileNotFoundException(), false);
        Check(!missing.CanRestart && !missing.CanRepair && missing.Message.Contains("file could not be found"), "Missing school files require file recovery, not SQL repair");
        var permission = DatabaseConnectionGuidance.Create("Access denied to LocalDB (Error 229).", new InvalidOperationException(), true);
        Check(permission.Message.Contains("Access denied to LocalDB (Error 229).") && !permission.CanRepair, "Error/LocalDB wording must not hide permissions or offer an unrelated reinstall");
        var timeout = DatabaseConnectionGuidance.Create("The operation took too long.", new TimeoutException(), true);
        Check(timeout.Message.Contains("took too long") && timeout.CanRestart && !timeout.CanRepair, "Ordinary errors retain the explanation and connection recovery");
        foreach (string version in new[] { "1", "3" })
        {
            var failure = new DatabaseCompatibilityException(new(DatabaseCompatibilityIssue.SchemaVersion, "2", version));
            var guidance = DatabaseConnectionGuidance.Create("Generic", new InvalidOperationException("Wrapper", failure), true);
            Check(!guidance.CanRepair && !guidance.CanRestart && guidance.Message.Contains($"version: {version}"), "Wrapped version failures retain version-specific guidance");
        }
        return checks;
    }
}
