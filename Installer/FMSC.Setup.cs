using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;

// Single GUI-subsystem entry point for fresh installs and prepared updates.
// core.ps1 remains the only installation, SQL, backup and recovery engine.
// It displays the same existing WinForms progress window in both modes.
internal static class FmscSetup
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(directory)) throw new IOException("Cannot locate FMSC setup files.");
            string installer = Path.Combine(directory, "core.ps1");
            if (!File.Exists(installer))
                throw new FileNotFoundException("FMSC installation files are missing. Extract the full release before installing.");

            string installerArguments;
            if (args.Length == 0)
            {
                // The only first-install entry point; never fall back to update mode.
                installerArguments = "-ShowInstallerWindow";
            }
            else if (args.Length == 5 && string.Equals(args[0], "--update", StringComparison.Ordinal))
            {
                string root = Path.GetFullPath(args[1]);
                string expected = Path.GetFullPath(Path.Combine(root, "application", "UpdateSupport"));
                if (!string.Equals(Path.GetFullPath(directory).TrimEnd('\\'),
                    expected.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("FMSC update files are not in the approved prepared location.");

                int processId;
                long startTicks;
                if (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out processId) ||
                    !long.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out startTicks) ||
                    processId <= 0 || startTicks <= 0 ||
                    !Regex.IsMatch(args[4], @"^S-1-(?:[0-9]+-)+[0-9]+$"))
                    throw new InvalidOperationException("FMSC update process identity is invalid.");

                installerArguments = "-PreparedRoot " + Quote(root) +
                    " -OriginalProcessId " + processId.ToString(CultureInfo.InvariantCulture) +
                    " -OriginalStartTicks " + startTicks.ToString(CultureInfo.InvariantCulture) +
                    " -OriginalUserSid " + args[4];
            }
            else
            {
                throw new InvalidOperationException("Invalid FMSC setup arguments.");
            }

            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = "-NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File " +
                    Quote(installer) + " " + installerArguments,
                WorkingDirectory = directory,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process launched = Process.Start(start);
            if (launched == null) throw new IOException("FMSC setup could not start.");
            launched.Dispose();
        }
        catch (Win32Exception error)
        {
            if (error.NativeErrorCode != 1223)
                MessageBox.Show(error.Message, "FMSC Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            // 1223 means Windows administrator approval was cancelled.
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "FMSC Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOf('"') >= 0 ||
            value.IndexOfAny(new char[] { '\r', '\n' }) >= 0)
            throw new InvalidOperationException("Invalid FMSC setup path.");
        return "\"" + value + "\"";
    }
}
