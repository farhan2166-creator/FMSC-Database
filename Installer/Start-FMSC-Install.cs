using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

// GUI-subsystem bootstrap: launches the already-reviewed PowerShell installer
// under UAC without displaying its console. core.ps1 retains all SQL, backup,
// migration, recovery, validation and failure decisions.
internal static class FmscSetupLauncher
{
    [STAThread]
    private static void Main()
    {
        try
        {
            string root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string script = Path.Combine(root, "core.ps1");
            if (!File.Exists(script))
                throw new FileNotFoundException("The FMSC installer script is missing. Re-extract the complete installer package.");

            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = "-NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File \"" +
                    script + "\" -GraphicalInstall",
                WorkingDirectory = root,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (Process process = Process.Start(start)) { }
        }
        catch (Win32Exception error)
        {
            // UAC was declined: do not attempt an unelevated installation.
            if (error.NativeErrorCode != 1223)
                MessageBox.Show(error.Message, "FMSC - Setup Could Not Start",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "FMSC - Setup Could Not Start",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
