using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RussiaFlag
{
    internal static class NativeDesktop
    {
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        internal static void EnableDpi() { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
    }

    internal static class Program
    {
        internal static void Log(Exception error)
        {
            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log"), DateTime.Now + " " + error + Environment.NewLine + error.Data["DesktopSurface"] + Environment.NewLine); } catch { }
        }
        [STAThread] private static int Main(string[] args)
        {
            try
            {
                string mode = args.Length > 0 ? args[0] : "";
                if (mode == "--stop-soviet") return StopSovietRoom();
                if (mode == "--stop")
                {
                    try { using (EventWaitHandle signal = EventWaitHandle.OpenExisting("Local\\RussiaFlagWallpaper.Stop")) signal.Set(); }
                    catch (WaitHandleCannotBeOpenedException) { }
                    return 0;
                }
                NativeDesktop.EnableDpi();
                Application.EnableVisualStyles();
                if (mode == "--interaction-test") return InteractionVerification.Run();
                if (mode == "--music-test")
                {
                    Settings music = Settings.Load();
                    if (music.Music != "shaman" || !music.FragmentReady) throw new Exception("SHAMAN is not configured as startup music");
                    using (Anthem player = new Anthem())
                    {
                        int duration = player.ReadDuration(music.ShamanPath);
                        if (music.StartMs < 0 || music.EndMs > duration || music.EndMs <= music.StartMs) throw new Exception("Invalid configured music bounds");
                        player.PlayFile(music.ShamanPath, music.StartMs, music.EndMs, true);
                        if (player.Status() != "playing") throw new Exception("SHAMAN fragment did not start");
                        Thread.Sleep(music.EndMs - music.StartMs + 700);
                        if (player.Status() != "closed") throw new Exception("SHAMAN fragment did not stop");
                        player.PlayFile(music.ShamanPath, music.StartMs, music.EndMs, true);
                        if (player.Status() != "playing") throw new Exception("SHAMAN fragment replay failed");
                        player.Dispose();
                        File.WriteAllText("shaman-verification.txt", "Configured startup music: SHAMAN\r\nFile: " + music.ShamanPath +
                            "\r\nRange ms: " + music.StartMs + ".." + music.EndMs + "\r\nActual file duration ms: " + duration +
                            "\r\nPlayback start, stop after fragment, replay and manual stop: PASS (muted)\r\n");
                    }
                    return 0;
                }
                if (mode == "--diagnostics")
                {
                    DesktopSurfaceNative.RequestWallpaperHost();
                    StringBuilder dump = new StringBuilder();
                    foreach (DesktopWindowInfo item in DesktopSurfaceNative.Snapshot())
                        dump.AppendLine(item.Handle + " parent=" + item.Parent + " " + item.ClassName + " pid=" + item.ProcessId + " visible=" + item.Visible + " ex=" + item.ExtendedStyle.ToString("X"));
                    File.WriteAllText("desktop-diagnostics.txt", dump.ToString());
                    return 0;
                }
                if (mode == "--self-test" || mode == "--render-test") return SelfTest(mode == "--self-test");
                bool created;
                using (Mutex singleton = new Mutex(true, "Local\\RussiaFlagWallpaper.Instance", out created))
                {
                    if (!created) return 0;
                    using (EventWaitHandle signal = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\RussiaFlagWallpaper.Stop"))
                    using (Wallpaper app = new Wallpaper(signal, mode == "--silent" || mode == "--smoke" || mode == "--menu-test" || mode == "--tray-stress-test" || mode == "--single-monitor-test", mode == "--menu-test" || mode == "--tray-stress-test" || mode == "--single-monitor-test", mode == "--tray-stress-test", mode == "--single-monitor-test"))
                    {
                        if (mode == "--menu-test" || mode == "--tray-stress-test" || mode == "--single-monitor-test")
                        {
                            using (System.Windows.Forms.Timer check = new System.Windows.Forms.Timer { Interval = 300 })
                            {
                                check.Tick += delegate { check.Stop(); if (mode == "--tray-stress-test") app.RunTrayStressChecks(); else if (mode == "--single-monitor-test") app.RunSingleMonitorChecks(); else app.RunMenuChecks(); };
                                check.Start(); Application.Run(app);
                            }
                            if (!app.MenuChecksPassed) throw new Exception("Menu checks failed: menu-verification.txt");
                        }
                        else if (mode == "--smoke")
                        {
                            using (System.Threading.Timer smoke = new System.Threading.Timer(delegate { signal.Set(); }, null, 6500, Timeout.Infinite))
                                Application.Run(app);
                        }
                        else Application.Run(app);
                    }
                    singleton.ReleaseMutex();
                }
                return 0;
            }
            catch (Exception error)
            {
                Log(error);
                if (args.Length == 0) MessageBox.Show(error.Message + "\nПодробности: error.log", "Флаг России", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
        private static int StopSovietRoom()
        {
            // Same graceful shutdown signal used by SovietRoom.exe --stop in all editions.
            try { using (EventWaitHandle signal = EventWaitHandle.OpenExisting("Local\\SovietRoom.Desktop.Restore")) signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { return 0; }
            Mutex instance;
            if (!Mutex.TryOpenExisting("Local\\SovietRoom.Desktop.Instance", out instance)) return 0;
            using (instance)
            {
                bool acquired = false;
                try
                {
                    try { acquired = instance.WaitOne(10000); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new TimeoutException("Советская комната не закрылась за 10 секунд. Закройте её через меню в трее.");
                    return 0;
                }
                finally { if (acquired) instance.ReleaseMutex(); }
            }
        }
        private static int SelfTest(bool testAudio)
        {
            StringBuilder report = new StringBuilder();
            string[] assets = { "office-original.jpg", "lake-rest.png" };
            int[] widths = { 2560, 1920 };
            int[] heights = { 1440, 1080 };
            for (int i = 0; i < assets.Length; i++)
            using (SceneCanvas canvas = new SceneCanvas(widths[i], heights[i], assets[i]))
            {
                string preview = i == 0 ? "preview-office.png" : "preview-lake.png";
                canvas.Save(preview);
                using (Bitmap target = new Bitmap(widths[i], heights[i]))
                using (Graphics graphics = Graphics.FromImage(target))
                {
                    canvas.PaintScene(graphics, new Rectangle(0, 0, target.Width, target.Height));
                    Color center = target.GetPixel(target.Width / 2, target.Height / 2);
                    if (center.R + center.G + center.B == 0) throw new Exception("Empty scene: " + assets[i]);
                }
                report.AppendLine(assets[i] + ": prepared and painted at " + widths[i] + "x" + heights[i] + " PASS");
            }
            if (testAudio)
            using (Anthem audio = new Anthem())
            {
                string fixture = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "anthem.mp3");
                audio.PlayFile(fixture, 1000, 1400, true);
                if (audio.Status() != "playing") throw new Exception("Audio fragment did not start");
                Thread.Sleep(700);
                if (audio.Status() != "closed") throw new Exception("Audio fragment did not stop");
                audio.PlayFile(fixture, 0, 250, true);
                audio.PlayFile(fixture, 2000, 3500, true);
                Thread.Sleep(500);
                if (audio.Status() != "playing") throw new Exception("Previous timer interrupted new playback");
                audio.Dispose();
                bool invalidRejected = false;
                try { audio.PlayFile(fixture, 4000, 1000, true); } catch (InvalidOperationException) { invalidRejected = true; }
                if (!invalidRejected || audio.Status() != "closed") throw new Exception("Invalid fragment accepted");
                report.AppendLine("Audio fragment seek, deadline, replay, invalid range: PASS (muted fixture)");
                int fullLength = audio.ReadDuration(fixture);
                audio.Play(true);
                if (audio.Status() != "playing") throw new Exception("Audio did not start");
                if (audio.PlaybackEndMs != fullLength) throw new Exception("Anthem playback does not cover complete file");
                report.AppendLine("Full anthem playback range: 0.." + fullLength + " ms: PASS (muted)");
                Thread.Sleep(31000);
                string status = audio.Status();
                if (fullLength > 31000 && status != "playing") throw new Exception("Full anthem stopped prematurely: " + status);
                report.AppendLine("Anthem continues beyond 30 seconds: PASS");
                audio.Dispose();
                audio.PlayFile(fixture, fullLength - 400, fullLength, true);
                Thread.Sleep(800);
                if (audio.Status() != "closed") throw new Exception("Anthem did not close at end of file");
                report.AppendLine("End-of-file playback and automatic stop: PASS");
            }
            File.WriteAllText("verification.txt", report.ToString());
            return 0;
        }
    }
}
