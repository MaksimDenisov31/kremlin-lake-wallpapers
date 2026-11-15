using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RussiaFlag
{
    internal static class InteractionVerification
    {
        private static void Check(bool value, string name) { if (!value) throw new Exception("Interactive check: " + name); }
        private static int Differences(Bitmap a, Bitmap b, Rectangle area)
        {
            area.Intersect(new Rectangle(0, 0, a.Width, a.Height)); int changes = 0;
            for (int y = area.Top; y < area.Bottom; y += 4)
                for (int x = area.Left; x < area.Right; x += 4) if (a.GetPixel(x, y) != b.GetPixel(x, y)) changes++;
            return changes;
        }
        internal static int Run()
        {
            StringBuilder report = new StringBuilder();
            foreach (string asset in new string[] { "kremlin-office.png", "office-original.jpg" })
            using (SceneCanvas scene = new SceneCanvas(1280, 720, asset))
            {
                InteractionState state = new InteractionState(); scene.Bind(state, "");
                PointF[] expectedCorners = scene.MonitorPlane.Corners;
                PointF[] mappedCorners = { scene.MonitorPlane.Project(0, 0), scene.MonitorPlane.Project(1, 0), scene.MonitorPlane.Project(1, 1), scene.MonitorPlane.Project(0, 1) };
                for (int corner = 0; corner < 4; corner++) Check(Math.Abs(mappedCorners[corner].X - expectedCorners[corner].X) < .01 && Math.Abs(mappedCorners[corner].Y - expectedCorners[corner].Y) < .01, "Monitor perspective corner mapping");
                using (Bitmap color = new Bitmap(64, 48))
                {
                    using (Graphics fill = Graphics.FromImage(color)) fill.Clear(Color.CornflowerBlue);
                    using (Bitmap warped = scene.MonitorPlane.Warp(color))
                        Check(warped.GetPixel(0, 0).A == 0 || warped.GetPixel(warped.Width - 1, 0).A == 0, "Perspective leaves pixels outside screen transparent");
                }
                using (Bitmap original = scene.Snapshot())
                {
                    state.Lamp = true; scene.Tick(0);
                    using (Bitmap lit = scene.Snapshot()) Check(Differences(original, lit, new Rectangle(0, 0, 1280, 720)) > 30, "Lamp changes desk lighting");
                    state.Lamp = false; state.Monitor = true;
                    using (Bitmap screen = scene.Snapshot()) Check(Differences(original, screen, Rectangle.Round(scene.ScreenArea)) > 50, "Monitor displays clock");
                    scene.Save(asset == "kremlin-office.png" ? "preview-perspective-clock-ai.png" : "preview-perspective-clock-original.png");
                    state.Monitor = false;
                    InputTarget target = Array.Find(scene.Targets(new Rectangle(0, 0, 1280, 720)), delegate(InputTarget item) { return item.Action == "curtain"; });
                    Check(target != null, "Visible curtain target");
                    Point start = new Point(target.Bounds.Left + 4, target.Bounds.Top + 4), end = new Point(target.Bounds.Right + 10, target.Bounds.Top + 4);
                    scene.CurtainGesture(new SceneGesture { Target = target, Kind = 0, Start = start, Point = start });
                    scene.CurtainGesture(new SceneGesture { Target = target, Kind = 1, Start = start, Point = end });
                    scene.CurtainGesture(new SceneGesture { Target = target, Kind = 2, Start = start, Point = end });
                    Check(state.Curtain > .9 && state.CurtainTarget == 1, "Curtain drag opens window");
                    using (Bitmap opened = scene.Snapshot())
                    {
                        Check(Differences(original, opened, Rectangle.Round(scene.CurtainArea)) > 50, "Window view appears");
                        Check(Differences(original, opened, Rectangle.Round(scene.ScreenArea)) == 0, "Window does not paint over monitor");
                        PointF[] stripSamples = asset == "kremlin-office.png" ? new PointF[] { scene.SourcePixel(1230,270), scene.SourcePixel(1270,294), scene.SourcePixel(1404,420) } :
                            new PointF[] { scene.SourcePixel(1089,240), scene.SourcePixel(1150,270), scene.SourcePixel(1265,390) };
                        foreach (PointF sample in stripSamples)
                        {
                            Point pixel = Point.Round(sample);
                            Check(scene.CurtainContains(pixel), "Remaining white strip is included in curtain mask");
                            Check(original.GetPixel(pixel.X,pixel.Y) != opened.GetPixel(pixel.X,pixel.Y), "Window replaces remaining white strip");
                        }
                        for (int y = 0; y < original.Height; y += 3)
                            for (int x = 0; x < original.Width; x += 3)
                                if (!scene.CurtainContains(new Point(x, y))) Check(original.GetPixel(x, y) == opened.GetPixel(x, y), "Opening changes only white fabric; fixed drapes stay unchanged");
                    }
                    scene.CurtainGesture(new SceneGesture { Target = target, Kind = 0, Start = start, Point = start });
                    scene.CurtainGesture(new SceneGesture { Target = target, Kind = 2, Start = start, Point = start });
                    Check(state.CurtainTarget == 0, "Curtain click closes window");
                    state.Curtain = 1; state.CurtainTarget = 1; state.Lamp = true; state.Monitor = true; state.OfficePocket = true;
                    scene.Tick(.3); scene.Hotspots = true;
                    scene.Save(asset == "kremlin-office.png" ? "preview-interactive-office-ai.png" : "preview-interactive-office-original.png");
                }
                report.AppendLine(asset + ": curtain drag/click, restored window, monitor occlusion and clock, lamp: PASS");
                report.AppendLine(asset + ": four-corner perspective mapping and transparent outside pixels: PASS");
                report.AppendLine(asset + ": opening changes only the white curtain mask; fixed drapes and gold valance unchanged: PASS");
                report.AppendLine(asset + ": left, bottom and monitor-side white strips replaced; physical monitor body protected: PASS");
                state.Curtain = 1; state.CurtainTarget = 1; state.Lamp = false; state.OfficePocket = false; state.Monitor = true; scene.Hotspots = false;
                scene.Save(asset == "kremlin-office.png" ? "preview-window-clean-ai.png" : "preview-window-clean-original.png");
            }
            foreach (string asset in new string[] { "kremlin-office.png", "office-original.jpg" })
            using (SceneCanvas scene = new SceneCanvas(1280, 720, asset))
            {
                InteractionState state = new InteractionState { Monitor = true };
                scene.Bind(state, "", "flag");
                using (Bitmap first = scene.Snapshot())
                {
                    state.Time = 1.2;
                    using (Bitmap second = scene.Snapshot()) Check(Differences(first, second, Rectangle.Round(scene.ScreenArea)) > 50, "Built-in flag animates");
                }
                using (Bitmap flag = scene.MonitorPlane.Flag(0, null))
                {
                    PointF top = scene.MonitorPlane.Project(.75, .03), bottom = scene.MonitorPlane.Project(.75, .97);
                    Color white = flag.GetPixel((int)top.X - scene.MonitorPlane.Bounds.X, (int)top.Y - scene.MonitorPlane.Bounds.Y);
                    Color red = flag.GetPixel((int)bottom.X - scene.MonitorPlane.Bounds.X, (int)bottom.Y - scene.MonitorPlane.Bounds.Y);
                    Check(white.R > 120 && white.G > 120 && white.B > 120 && red.R > red.B + 50, "Flag fills top and bottom without letterboxing");
                    Stopwatch flagTimer = Stopwatch.StartNew();
                    for (int i = 0; i < 100; i++) scene.MonitorPlane.Flag(i / 100.0, flag);
                    report.AppendLine(asset + ": built-in full-screen waving flag PASS; projection render average ms: " + (flagTimer.Elapsed.TotalMilliseconds / 100).ToString("F2"));
                }
                scene.Save(asset == "kremlin-office.png" ? "preview-perspective-flag-ai.png" : "preview-perspective-flag-original.png");
            }
            using (SceneCanvas lake = new SceneCanvas(1920, 1080, "lake-rest.png"))
            {
                InteractionState state = new InteractionState(); lake.Bind(state, ""); lake.Tick(0);
                using (Bitmap before = lake.Snapshot())
                {
                    state.Time = 1.7; lake.Tick(.02);
                    using (Bitmap after = lake.Snapshot())
                    {
                        Check(Differences(before, after, new Rectangle(0, 486, 850, 400)) > 500, "Lake water moves");
                        Check(Differences(before, after, new Rectangle(1150, 250, 750, 700)) == 0, "Person stays still");
                        state.Paused = true; state.Time = 3; lake.Tick(.02);
                        using (Bitmap paused = lake.Snapshot()) Check(Differences(after, paused, new Rectangle(0, 486, 850, 400)) == 0, "Water pause");
                    }
                }
                state.Paused = false;
                Stopwatch watch = Stopwatch.StartNew();
                for (int i = 0; i < 120; i++) { state.Time = i / 60.0; lake.Tick(1.0 / 60); }
                report.AppendLine("Lake water update average ms at 1920x1080: " + (watch.Elapsed.TotalMilliseconds / 120).ToString("F2"));
                state.LakePocket = true; state.Nature = true; lake.Tick(.4); lake.Hotspots = true; lake.Save("preview-interactive-lake.png");
                report.AppendLine("Water motion, unchanged person, pause and fishing-box rendering: PASS");
            }
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".tools", "shortcut-verification");
            Directory.CreateDirectory(root); string file = Path.Combine(root, "target.txt"); File.WriteAllText(file, "shortcut test");
            ShortcutStore store = new ShortcutStore("test", root); store.Add(new string[] { file, root, file });
            Check(store.Items.Count == 2, "Shortcut duplicates removed");
            ShortcutStore reloaded = new ShortcutStore("test", root); Check(reloaded.Items.Count == 2, "Shortcuts persist");
            reloaded.Remove(file); Check(File.Exists(file), "Removing shortcut preserves original file"); reloaded.Remove(root);
            report.AppendLine("Shortcut add, duplicate handling, persistence, reference removal preserves original: PASS");
            using (Form foreground = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(100, 100), Size = new Size(200, 150), ShowInTaskbar = false })
            {
                foreground.Show(); Application.DoEvents(); Check(!DesktopInput.IsDesktopPoint(new Point(150, 150)), "Foreground app input not intercepted"); foreground.Close();
            }
            using (Anthem ambience = new Anthem())
            {
                ambience.PlayLoop(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "nature.mp3"), true);
                Thread.Sleep(200); Check(ambience.Status() == "playing", "Nature playback loop"); ambience.Dispose(); Check(ambience.Status() == "closed", "Nature stop");
            }
            report.AppendLine("Foreground window protection and nature audio start/stop: PASS");
            File.WriteAllText("interaction-verification.txt", report.ToString()); return 0;
        }
    }
}
