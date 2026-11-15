using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace RussiaFlag
{
    internal sealed class Settings
    {
        internal string Office = "original", Music = "shaman", ShamanPath = "";
        internal int StartMs, EndMs = 30000;
        internal bool FragmentReady, Swapped;
        internal string SingleScene = "office";
        internal string ScreenPicture = "";
        internal string MonitorMode = "clock";
        internal bool ScreenEnabled;
        internal string PersistPath = FilePath;
        internal static string FilePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini"); } }
        internal static Settings Load()
        {
            Settings value = LoadFrom(FilePath);
            string bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "shaman-chorus.mp3");
            if (String.IsNullOrWhiteSpace(value.ShamanPath) && File.Exists(bundled))
            {
                value.ShamanPath = bundled; value.StartMs = 0; value.EndMs = 33750; value.FragmentReady = true;
            }
            return value;
        }
        internal static Settings LoadFrom(string path)
        {
            Settings value = new Settings();
            value.PersistPath = path;
            if (!File.Exists(path)) return value;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int equals = line.IndexOf('=');
                if (equals < 0) continue;
                string key = line.Substring(0, equals), data = line.Substring(equals + 1);
                int number;
                switch (key)
                {
                    case "office": if (data == "ai" || data == "original") value.Office = data; break;
                    case "music": if (data == "shaman" || data == "anthem" || data == "none") value.Music = data; break;
                    case "shamanPath": value.ShamanPath = data; break;
                    case "startMs": if (int.TryParse(data, out number)) value.StartMs = number; break;
                    case "endMs": if (int.TryParse(data, out number)) value.EndMs = number; break;
                    case "fragmentReady": value.FragmentReady = data == "true"; break;
                    case "swapped": value.Swapped = data == "true"; break;
                    case "singleScene": if (data == "office" || data == "lake") value.SingleScene = data; break;
                    case "screenPicture": value.ScreenPicture = data; break;
                    case "monitorMode": if (data == "clock" || data == "image" || data == "flag") value.MonitorMode = data; break;
                    case "screenEnabled": value.ScreenEnabled = data == "true"; break;
                }
            }
            if (value.StartMs < 0 || value.EndMs <= value.StartMs) value.FragmentReady = false;
            if (!Array.Exists(File.ReadAllLines(path, Encoding.UTF8), delegate(string line) { return line.StartsWith("monitorMode="); }) && !String.IsNullOrEmpty(value.ScreenPicture)) value.MonitorMode = "image";
            return value;
        }
        internal string OfficeAsset { get { return Office == "ai" ? "kremlin-office.png" : "office-original.jpg"; } }
        internal void Save()
        {
            string text = "office=" + Office + "\r\nmusic=" + Music + "\r\nshamanPath=" + ShamanPath +
                "\r\nstartMs=" + StartMs.ToString(CultureInfo.InvariantCulture) + "\r\nendMs=" + EndMs.ToString(CultureInfo.InvariantCulture) +
                "\r\nfragmentReady=" + (FragmentReady ? "true" : "false") + "\r\nswapped=" + (Swapped ? "true" : "false") + "\r\nscreenPicture=" + ScreenPicture + "\r\nmonitorMode=" + MonitorMode + "\r\nscreenEnabled=" + (ScreenEnabled ? "true" : "false") + "\r\nsingleScene=" + SingleScene + "\r\n";
            File.WriteAllText(PersistPath + ".tmp", text, Encoding.UTF8);
            if (File.Exists(PersistPath)) File.Replace(PersistPath + ".tmp", PersistPath, null);
            else File.Move(PersistPath + ".tmp", PersistPath);
        }
    }
}
