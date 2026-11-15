using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace RussiaFlag
{
    internal static class PocketElevation
    {
        internal static string Token(PocketEntry entry) { return Path.GetFileName(Path.GetDirectoryName(entry.Stored)); }
        internal static void Validate(string zone, string token)
        {
            Guid id;
            if ((zone != "lake" && zone != "office") || !Guid.TryParseExact(token, "N", out id))
                throw new ArgumentException("Недопустимый файл для возврата из ящика.");
        }
        internal static void Execute(string zone, string token, string root = null)
        {
            Validate(zone, token);
            PocketStore store = new PocketStore(zone, root);
            PocketEntry entry = store.Items.Find(delegate(PocketEntry item) { return Token(item) == token; });
            // A completed return may already have removed the entry before folder cleanup failed.
            if (entry != null) store.Restore(entry);
        }
        internal static bool Request(string zone, string token, Func<ProcessStartInfo, int> launch = null)
        {
            Validate(zone, token);
            ProcessStartInfo info = new ProcessStartInfo(Application.ExecutablePath,
                "--restore-pocket " + zone + " " + token) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory };
            try
            {
                int result;
                if (launch != null) result = launch(info);
                else using (Process helper = Process.Start(info)) { helper.WaitForExit(); result = helper.ExitCode; }
                return result == 0;
            }
            catch (Win32Exception error) { if (error.NativeErrorCode == 1223) return false; throw; }
        }
    }
}
