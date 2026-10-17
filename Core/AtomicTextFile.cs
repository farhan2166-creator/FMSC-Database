using System.Text;
using System.Security.Cryptography;

namespace FMSC_SMS.Core;

/// <summary>Publish a fully flushed settings file without truncating the prior copy.</summary>
internal static class AtomicTextFile
{
    internal static void Write(string path, string text, Encoding? encoding = null)
    {
        string fullPath = Path.GetFullPath(path);
        string folder = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, encoding ?? new UTF8Encoding(false), 1024, leaveOpen: true);
                writer.Write(text);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            // Windows can refuse overlapping MoveFileEx replacements of one target.
            // Serialize only publication, across windows of the same user/session;
            // every temporary file is still flushed before acquiring this gate.
            string key=OperatingSystem.IsWindows()?fullPath.ToUpperInvariant():fullPath;
            string name=@"Local\FMSC_SMS.AtomicText."+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            using var gate=new Mutex(false,name);
            bool held=false;
            try
            {
                try{held=gate.WaitOne(TimeSpan.FromSeconds(10));}
                catch(AbandonedMutexException){held=true;}
                if(!held)throw new IOException("Another settings save is still finishing. Try saving again.");
                File.Move(temporary,fullPath,overwrite:true);
            }
            finally{if(held)gate.ReleaseMutex();}
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Trace.TraceWarning("Could not remove temporary settings file: {0}", ex.Message); }
        }
    }
}
