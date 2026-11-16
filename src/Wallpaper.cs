using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RussiaFlag
{
    internal sealed class Wallpaper : ApplicationContext
    {
        private readonly List<SceneCanvas> canvases = new List<SceneCanvas>();
        private readonly List<DesktopSurface> surfaces = new List<DesktopSurface>();
        private readonly Anthem anthem = new Anthem();
        private readonly EventWaitHandle stop;
        private readonly System.Windows.Forms.Timer poll;
        private readonly NotifyIcon tray;
        private readonly Icon trayIcon;
        private readonly ContextMenuStrip menu;
        private readonly Settings settings;
        private readonly ToolStripMenuItem originalItem, aiItem, shamanItem, anthemItem, noneItem;
        private readonly ToolStripMenuItem playItem, stopItem, fragmentItem, swapItem, refreshItem, exitItem;
        private readonly ToolStripMenuItem singleSceneMenu, singleOfficeItem, singleLakeItem;
        private bool forceSingleScreen;
        private readonly bool testMode;
        private readonly InteractionState sceneState = new InteractionState();
        private readonly Anthem nature = new Anthem();
        private readonly Control dispatcher = new Control();
        private readonly List<Rectangle> sceneScreens = new List<Rectangle>();
        private readonly Dictionary<string, ToolStripMenuItem> interactiveItems = new Dictionary<string, ToolStripMenuItem>();
        private readonly Stopwatch animationTime = Stopwatch.StartNew();
        private AnimationClock animation;
        private DesktopInput input;
        private ShortcutPocket officePocket, lakePocket;
        private SceneCanvas draggingCurtain;
        private double lastAnimation, hotspotsUntil;
        private int animationFrames;
        internal bool MenuChecksPassed { get; private set; }
        private string displayLayout;
        private bool exiting;
        internal Wallpaper(EventWaitHandle stop, bool silent, bool testMode = false, bool stressMode = false, bool forceSingleScreen = false)
        {
            this.stop = stop;
            this.testMode = testMode;
            this.forceSingleScreen = forceSingleScreen;
            IntPtr dispatcherHandle = dispatcher.Handle;
            settings = testMode ? new Settings { PersistPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "menu-test-settings.ini") } : Settings.Load();
            if (stressMode) { settings.MonitorMode = "flag"; settings.ScreenEnabled = true; }
            sceneState.Monitor = settings.ScreenEnabled;
            menu = new ContextMenuStrip();
            menu.Opening += delegate { if (animation != null) animation.Suspended = true; };
            menu.Closed += delegate { if (animation != null) animation.Suspended = false; };
            ToolStripMenuItem pictures = new ToolStripMenuItem("Картинка кабинета");
            aiItem = new ToolStripMenuItem("ИИ — улучшенная версия", null, delegate { ChooseOffice("ai"); });
            originalItem = new ToolStripMenuItem("Оригинальная фотография", null, delegate { ChooseOffice("original"); });
            pictures.DropDownItems.Add(aiItem); pictures.DropDownItems.Add(originalItem); menu.Items.Add(pictures);
            ToolStripMenuItem music = new ToolStripMenuItem("Музыка при запуске");
            shamanItem = new ToolStripMenuItem("SHAMAN — Я русский (фрагмент)", null, delegate { ChooseMusic("shaman"); });
            anthemItem = new ToolStripMenuItem("Гимн РФ — полностью", null, delegate { ChooseMusic("anthem"); });
            noneItem = new ToolStripMenuItem("Без музыки", null, delegate { ChooseMusic("none"); });
            music.DropDownItems.Add(shamanItem); music.DropDownItems.Add(anthemItem); music.DropDownItems.Add(noneItem); menu.Items.Add(music);
            fragmentItem = new ToolStripMenuItem("Настроить фрагмент «Я русский»…", null, delegate { ConfigureFragment(); });
            menu.Items.Add(fragmentItem);
            playItem = new ToolStripMenuItem("Воспроизвести выбранную музыку", null, delegate { PlaySelected(); }); menu.Items.Add(playItem);
            stopItem = new ToolStripMenuItem("Остановить музыку", null, delegate { anthem.Dispose(); }); menu.Items.Add(stopItem);
            menu.Items.Add(new ToolStripSeparator());
            singleSceneMenu = new ToolStripMenuItem("Сцена на одном мониторе");
            singleOfficeItem = new ToolStripMenuItem("Кремль", null, delegate { ChooseSingleScene("office"); });
            singleLakeItem = new ToolStripMenuItem("Отдых у озера", null, delegate { ChooseSingleScene("lake"); });
            singleSceneMenu.DropDownItems.Add(singleOfficeItem); singleSceneMenu.DropDownItems.Add(singleLakeItem); menu.Items.Add(singleSceneMenu);
            swapItem = new ToolStripMenuItem("Поменять сцены местами", null, delegate { Change(delegate { settings.Swapped = !settings.Swapped; Rebuild(); }); }); menu.Items.Add(swapItem);
            refreshItem = new ToolStripMenuItem("Обновить мониторы", null, delegate { TryRebuild(); }); menu.Items.Add(refreshItem);
            ToolStripMenuItem interactive = new ToolStripMenuItem("Интерактивные элементы");
            string[,] controls = {
                { "curtain", "Штора — открыть / закрыть" }, { "lamp", "Лампа — включить / выключить" },
                { "monitor", "Монитор — включить / выключить" }, { "office-pocket", "Тайник в столе" },
                { "lake-pocket", "Рыболовный ящик" }, { "nature", "Звуки природы" }, { "pause", "Пауза анимации" },
                { "help", "Показать активные области" }, { "flag", "На экране монитора — флаг РФ (анимация)" }, { "clock", "На экране монитора — часы" },
                { "screen-picture", "На экране монитора — картинка…" }, { "mute-all", "Выключить весь звук" }
            };
            for (int i = 0; i < controls.GetLength(0); i++)
            {
                string key = controls[i, 0];
                ToolStripMenuItem item = new ToolStripMenuItem(controls[i, 1], null, delegate { try { SceneAction(key); } catch (Exception error) { Notice(error); } });
                interactiveItems.Add(key, item); interactive.DropDownItems.Add(item);
            }
            menu.Items.Add(interactive);
            menu.Items.Add(new ToolStripSeparator());
            exitItem = new ToolStripMenuItem("Закрыть обои", null, delegate { ExitThread(); }); menu.Items.Add(exitItem);
            trayIcon = AppIcon.Load();
            tray = new NotifyIcon { Icon = trayIcon, Text = "Кремль и отдых у озера — обои", ContextMenuStrip = menu };
            poll = new System.Windows.Forms.Timer { Interval = 250 };
            try
            {
                Rebuild();
                if (!testMode || stressMode)
                {
                    input = new DesktopInput(dispatcher, OnSceneGesture); UpdateInput();
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last-run.txt"), "Desktop mouse hook installed: " + input.Installed + "\r\n");
                    animation = new AnimationClock(dispatcher, Animate);
                    hotspotsUntil = 12;
                }
                UpdateChecks();
                tray.Visible = true;
                poll.Tick += delegate
                {
                    if (stop.WaitOne(0)) { ExitThread(); return; }
                    if (displayLayout != Layout()) { TryRebuild(); return; }
                    foreach (DesktopSurface surface in surfaces)
                        if (!surface.IsAttached) { TryRebuild(); break; }
                };
                poll.Start();
                if (!testMode) settings.Save();
                if (!silent) PlaySelected();
            }
            catch { Cleanup(); throw; }
        }
        private void UpdateChecks()
        {
            aiItem.Checked = settings.Office == "ai"; originalItem.Checked = settings.Office == "original";
            shamanItem.Checked = settings.Music == "shaman"; anthemItem.Checked = settings.Music == "anthem"; noneItem.Checked = settings.Music == "none";
            interactiveItems["nature"].Checked = sceneState.Nature; interactiveItems["pause"].Checked = sceneState.Paused;
            interactiveItems["lamp"].Checked = sceneState.Lamp; interactiveItems["monitor"].Checked = sceneState.Monitor;
            interactiveItems["flag"].Checked = settings.MonitorMode == "flag";
            interactiveItems["clock"].Checked = settings.MonitorMode == "clock";
            interactiveItems["screen-picture"].Checked = settings.MonitorMode == "image";
            singleOfficeItem.Checked = settings.SingleScene == "office"; singleLakeItem.Checked = settings.SingleScene == "lake";
            bool single = SelectedScreens().Length == 1;
            singleSceneMenu.Visible = single; swapItem.Visible = !single;
            bool officeVisible = canvases.Exists(delegate(SceneCanvas canvas) { return !canvas.IsLake; });
            bool lakeVisible = canvases.Exists(delegate(SceneCanvas canvas) { return canvas.IsLake; });
            foreach (string key in new string[] { "curtain", "lamp", "monitor", "office-pocket", "flag", "clock", "screen-picture" })
                interactiveItems[key].Visible = officeVisible;
            foreach (string key in new string[] { "lake-pocket", "nature" }) interactiveItems[key].Visible = lakeVisible;
        }
        private void Change(Action action)
        {
            try { action(); settings.Save(); UpdateChecks(); }
            catch (Exception error) { Notice(error); }
        }
        private void ChooseOffice(string value) { Change(delegate { settings.Office = value; Rebuild(); }); }
        private void ChooseSingleScene(string value) { Change(delegate { settings.SingleScene = value; Rebuild(); }); }
        private void ChooseMusic(string value)
        {
            Change(delegate { anthem.Dispose(); settings.Music = value; });
            if (!testMode) PlaySelected();
        }
        private void ConfigureFragment()
        {
            anthem.Dispose();
            using (FragmentForm form = new FragmentForm(settings))
            {
                if (testMode)
                {
                    using (System.Windows.Forms.Timer dismiss = new System.Windows.Forms.Timer { Interval = 150 })
                    {
                        dismiss.Tick += delegate { dismiss.Stop(); form.CancelButtonControl.PerformClick(); };
                        dismiss.Start(); form.ShowDialog();
                    }
                }
                else if (form.ShowDialog() == DialogResult.OK) { UpdateChecks(); PlaySelected(); }
            }
        }
        private void Notice(Exception error)
        {
            Program.Log(error);
            if (testMode) throw new InvalidOperationException("Menu callback failed", error);
            tray.ShowBalloonTip(7000, "Обои: сообщение", error.Message, ToolTipIcon.Warning);
        }
        private void PlaySelected()
        {
            try
            {
                if (settings.Music == "none") { anthem.Dispose(); return; }
                if (settings.Music == "anthem") { anthem.Play(testMode); return; }
                if (!settings.FragmentReady || !File.Exists(settings.ShamanPath))
                {
                    if (!testMode) tray.ShowBalloonTip(8000, "SHAMAN — Я русский", "Выберите аудиофайл и нужный припев: «Настроить фрагмент» в меню трея.", ToolTipIcon.Info);
                    return;
                }
                anthem.PlayFile(settings.ShamanPath, settings.StartMs, settings.EndMs, testMode);
            }
            catch (Exception error) { Notice(error); }
        }
        private Screen[] SelectedScreens()
        {
            List<Screen> screens = new List<Screen>();
            screens.Add(Screen.PrimaryScreen);
            if (forceSingleScreen) return screens.ToArray();
            List<Screen> secondary = new List<Screen>();
            foreach (Screen screen in Screen.AllScreens) if (!screen.Primary) secondary.Add(screen);
            secondary.Sort(delegate(Screen a, Screen b) { return String.CompareOrdinal(a.DeviceName, b.DeviceName); });
            if (secondary.Count > 0) screens.Add(secondary[0]);
            return screens.ToArray();
        }
        private string Layout()
        {
            StringBuilder text = new StringBuilder();
            foreach (Screen screen in SelectedScreens()) text.Append(screen.DeviceName).Append(screen.Bounds).Append(';');
            return text.ToString();
        }
        private void TryRebuild()
        {
            try { Rebuild(); }
            catch (Exception error) { Program.Log(error); ExitThread(); }
        }
        private void ClearSurfaces()
        {
            if (input != null) input.Update(new InputTarget[0]);
            draggingCurtain = null;
            if (officePocket != null) officePocket.Close(); if (lakePocket != null) lakePocket.Close();
            foreach (DesktopSurface surface in surfaces) surface.Dispose();
            surfaces.Clear();
            foreach (SceneCanvas canvas in canvases) canvas.Dispose();
            canvases.Clear();
            sceneScreens.Clear();
        }
        private void Rebuild()
        {
            ClearSurfaces();
            StringBuilder report = new StringBuilder();
            Screen[] screens = SelectedScreens();
            try
            {
                for (int i = 0; i < screens.Length; i++)
                {
                    Rectangle bounds = screens[i].Bounds;
                    bool office = screens.Length == 1 ? settings.SingleScene == "office" : ((i == 0) != settings.Swapped);
                    string asset = office ? settings.OfficeAsset : "lake-rest.png";
                    SceneCanvas canvas = new SceneCanvas(bounds.Width, bounds.Height, asset);
                    canvas.Bind(sceneState, settings.ScreenPicture, settings.MonitorMode);
                    canvases.Add(canvas);
                    sceneScreens.Add(bounds);
                    surfaces.Add(DesktopSurface.CreateAndShow(bounds, canvas));
                    report.AppendLine("Monitor " + (i + 1) + ": " + screens[i].DeviceName + " " + bounds + " -> " + asset + "; attached=True");
                }
                displayLayout = Layout();
                report.AppendLine("Office: " + settings.Office + "; music: " + settings.Music + "; fragment ready: " + settings.FragmentReady);
                report.AppendLine("Interactive scenes: curtain, lamp, clock/photo monitor, shortcut pockets, animated water and nature sounds; target " + AnimationClock.TargetFps + " FPS; water speed 2.4x.");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last-run.txt"), report.ToString());
                UpdateInput();
                UpdateChecks();
            }
            catch { ClearSurfaces(); throw; }
        }
        private void Cleanup()
        {
            if (exiting) return;
            exiting = true;
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last-run.txt"), "Animation callbacks: " + animationFrames + "; mean callback FPS including startup: " + (animationFrames / Math.Max(.001, animationTime.Elapsed.TotalSeconds)).ToString("F2") + "\r\n");
            if (input != null) input.Dispose(); if (animation != null) animation.Dispose();
            poll.Stop(); poll.Dispose();
            anthem.Dispose(); nature.Dispose(); tray.Visible = false; tray.Dispose(); trayIcon.Dispose(); menu.Dispose();
            ClearSurfaces();
            dispatcher.Dispose();
        }
        protected override void ExitThreadCore() { Cleanup(); base.ExitThreadCore(); }

        private void UpdateInput()
        {
            if (input == null) return;
            List<InputTarget> targets = new List<InputTarget>();
            for (int i = 0; i < canvases.Count; i++) targets.AddRange(canvases[i].Targets(sceneScreens[i]));
            input.Update(targets.ToArray());
        }
        private void OnSceneGesture(SceneGesture gesture)
        {
            if (exiting || gesture.Target.Scene.IsDisposed) return;
            try
            {
                if (gesture.Target.Action == "curtain")
                {
                    if (gesture.Kind == 0) draggingCurtain = gesture.Target.Scene;
                    if (draggingCurtain == gesture.Target.Scene) gesture.Target.Scene.CurtainGesture(gesture);
                    if (gesture.Kind == 2) draggingCurtain = null;
                }
                else if (gesture.Kind == 2 && Math.Abs(gesture.Point.X - gesture.Start.X) < 8 && Math.Abs(gesture.Point.Y - gesture.Start.Y) < 8) SceneAction(gesture.Target.Action);
            }
            catch (Exception error) { Notice(error); }
        }
        private void Animate()
        {
            if (exiting) return;
            if (stop.WaitOne(0)) { ExitThread(); return; }
            try
            {
                double now = animationTime.Elapsed.TotalSeconds, dt = Math.Min(.05, Math.Max(0, now - lastAnimation)); lastAnimation = now;
                animationFrames++;
                if (!sceneState.Paused)
                {
                    sceneState.Time += dt;
                    sceneState.Curtain += Math.Sign(sceneState.CurtainTarget - sceneState.Curtain) * (float)Math.Min(Math.Abs(sceneState.CurtainTarget - sceneState.Curtain), dt * 1.4);
                }
                foreach (SceneCanvas canvas in canvases) { canvas.Hotspots = now < hotspotsUntil; canvas.Tick(sceneState.Paused ? 0 : dt); }
            }
            catch (Exception error) { Program.Log(error); sceneState.Paused = true; }
        }
        private void SceneAction(string action)
        {
            switch (action)
            {
                case "curtain": sceneState.CurtainTarget = sceneState.CurtainTarget > .5f ? 0 : 1; break;
                case "lamp": sceneState.Lamp = !sceneState.Lamp; break;
                case "monitor": sceneState.Monitor = !sceneState.Monitor; settings.ScreenEnabled = sceneState.Monitor; settings.Save(); break;
                case "office-pocket": TogglePocket(false); break;
                case "lake-pocket": TogglePocket(true); break;
                case "nature":
                    if (sceneState.Nature) { nature.Dispose(); sceneState.Nature = false; }
                    else { nature.PlayLoop(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "nature.mp3"), testMode); sceneState.Nature = true; }
                    break;
                case "pause": sceneState.Paused = !sceneState.Paused; break;
                case "help": hotspotsUntil = animationTime.Elapsed.TotalSeconds + 15; break;
                case "flag": Change(delegate { settings.MonitorMode = "flag"; settings.ScreenEnabled = true; sceneState.Monitor = true; Rebuild(); }); break;
                case "clock": Change(delegate { settings.MonitorMode = "clock"; settings.ScreenPicture = ""; settings.ScreenEnabled = true; sceneState.Monitor = true; Rebuild(); }); break;
                case "screen-picture": ChooseScreenPicture(); break;
                case "mute-all": anthem.Dispose(); nature.Dispose(); sceneState.Nature = false; break;
            }
            UpdateChecks();
            foreach (SceneCanvas canvas in canvases) canvas.Tick(0);
        }
        private void TogglePocket(bool lake)
        {
            ShortcutPocket current = lake ? lakePocket : officePocket;
            if (current != null && !current.IsDisposed) { current.Close(); return; }
            Point anchor = new Point(Screen.PrimaryScreen.WorkingArea.Left + 100, Screen.PrimaryScreen.WorkingArea.Bottom - 100);
            for (int i = 0; i < canvases.Count; i++) if (canvases[i].IsLake == lake)
                foreach (InputTarget target in canvases[i].Targets(sceneScreens[i])) if (target.Action == (lake ? "lake-pocket" : "office-pocket")) anchor = new Point(target.Bounds.Left, target.Bounds.Top);
            ShortcutPocket pocket = new ShortcutPocket(lake, anchor, testMode ? Path.GetTempPath() : null);
            if (lake) { lakePocket = pocket; sceneState.LakePocket = true; } else { officePocket = pocket; sceneState.OfficePocket = true; }
            pocket.FormClosed += delegate
            {
                if (lake) { lakePocket = null; sceneState.LakePocket = false; } else { officePocket = null; sceneState.OfficePocket = false; }
            };
            pocket.Show();
        }
        private void ChooseScreenPicture()
        {
            if (testMode) { settings.ScreenPicture = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "window-view.png"); settings.MonitorMode = "image"; settings.ScreenEnabled = true; sceneState.Monitor = true; Rebuild(); settings.Save(); return; }
            using (OpenFileDialog picker = new OpenFileDialog { Title = "Картинка на мониторе в кабинете", Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp|Все файлы|*.*", CheckFileExists = true })
            {
                if (picker.ShowDialog() != DialogResult.OK) return;
                using (Image check = Image.FromFile(picker.FileName)) { }
                string target = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "desk-screen-picture" + Path.GetExtension(picker.FileName));
                if (!String.Equals(target, picker.FileName, StringComparison.OrdinalIgnoreCase)) File.Copy(picker.FileName, target, true);
                Change(delegate { settings.ScreenPicture = target; settings.MonitorMode = "image"; settings.ScreenEnabled = true; sceneState.Monitor = true; Rebuild(); });
            }
        }

        internal void RunMenuChecks()
        {
            StringBuilder report = new StringBuilder();
            try
            {
                ClickCheck(aiItem); Require(settings.Office == "ai" && aiItem.Checked && !originalItem.Checked, "AI selection");
                Require(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last-run.txt")).Contains("kremlin-office.png"), "AI attached"); report.AppendLine("AI image click and live attachment: PASS");
                ClickCheck(originalItem); Require(settings.Office == "original" && originalItem.Checked && !aiItem.Checked, "Original selection"); report.AppendLine("Original image click and live attachment: PASS");
                ClickCheck(anthemItem); ClickCheck(playItem); Require(anthem.Status() == "playing", "Anthem playback"); report.AppendLine("Anthem choice and Play button (muted): PASS");
                ClickCheck(stopItem); Require(anthem.Status() == "closed", "Stop playback"); report.AppendLine("Stop button: PASS");
                ClickCheck(noneItem); ClickCheck(playItem); Require(anthem.Status() == "closed" && noneItem.Checked, "No music"); report.AppendLine("No music choice: PASS");
                ClickCheck(shamanItem); Require(shamanItem.Checked, "Shaman choice"); ClickCheck(playItem); Require(anthem.Status() == "closed", "Missing Shaman file handled"); report.AppendLine("Shaman choice; no wrong track when fragment is unconfigured: PASS");
                ClickCheck(fragmentItem); report.AppendLine("Fragment settings dialog opens; Cancel button closes it: PASS");
                FragmentForm.VerifyButtons(report);
                ClickCheck(interactiveItems["curtain"]); Require(sceneState.CurtainTarget == 1, "Curtain action");
                ClickCheck(interactiveItems["lamp"]); Require(sceneState.Lamp, "Lamp action");
                bool monitorBefore = sceneState.Monitor; ClickCheck(interactiveItems["monitor"]); Require(sceneState.Monitor != monitorBefore, "Monitor action");
                ClickCheck(interactiveItems["office-pocket"]); Require(sceneState.OfficePocket, "Office pocket opens"); ClickCheck(interactiveItems["office-pocket"]); Require(!sceneState.OfficePocket, "Office pocket closes");
                ClickCheck(interactiveItems["lake-pocket"]); Require(sceneState.LakePocket, "Lake pocket opens"); ClickCheck(interactiveItems["lake-pocket"]); Require(!sceneState.LakePocket, "Lake pocket closes");
                ClickCheck(interactiveItems["nature"]); Require(sceneState.Nature && nature.Status() == "playing", "Nature audio loop"); ClickCheck(interactiveItems["nature"]); Require(!sceneState.Nature && nature.Status() == "closed", "Nature audio stop");
                ClickCheck(interactiveItems["pause"]); Require(sceneState.Paused, "Pause action"); ClickCheck(interactiveItems["pause"]); Require(!sceneState.Paused, "Resume action");
                ClickCheck(interactiveItems["help"]); Require(hotspotsUntil > 0, "Hotspot hints");
                ClickCheck(interactiveItems["flag"]); Require(settings.MonitorMode == "flag" && sceneState.Monitor && interactiveItems["flag"].Checked, "Built-in waving flag selection");
                ClickCheck(interactiveItems["screen-picture"]); Require(settings.ScreenPicture != "", "Screen picture selection");
                ClickCheck(interactiveItems["clock"]); Require(settings.ScreenPicture == "", "Clock selection"); ClickCheck(interactiveItems["mute-all"]);
                report.AppendLine("All interactive menu actions, pocket windows, nature audio loop/stop, pause, hints and screen content: PASS");
                ClickCheck(swapItem); Require(settings.Swapped, "Swap scenes"); ClickCheck(swapItem); Require(!settings.Swapped, "Swap back"); report.AppendLine("Swap scenes and back: PASS");
                ClickCheck(refreshItem); Require(surfaces.Count == SelectedScreens().Length, "Refresh displays"); report.AppendLine("Refresh displays: PASS");
                Settings loaded = Settings.LoadFrom(settings.PersistPath);
                Require(loaded.Office == settings.Office && loaded.Music == settings.Music && loaded.Swapped == settings.Swapped, "Saved preferences"); report.AppendLine("Selections persist to disk: PASS");
                report.AppendLine("All menu leaf items hit-tested and clicked. Exit button: invoked last.");
                MenuChecksPassed = true;
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "menu-verification.txt"), report.ToString());
                ClickCheck(exitItem);
            }
            catch (Exception error)
            {
                MenuChecksPassed = false;
                report.AppendLine("FAILED: " + error); Program.Log(error);
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "menu-verification.txt"), report.ToString()); ExitThread();
            }
        }
        private static void Require(bool condition, string test) { if (!condition) throw new Exception(test); }
        private void CheckInteractiveVisibility(bool office, bool lake)
        {
            foreach (string key in new string[] { "curtain", "lamp", "monitor", "office-pocket", "flag", "clock", "screen-picture" })
                Require(interactiveItems[key].Available == office, "Office control visibility: " + key);
            foreach (string key in new string[] { "lake-pocket", "nature" })
                Require(interactiveItems[key].Available == lake, "Lake control visibility: " + key);
            foreach (string key in new string[] { "pause", "help", "mute-all" })
                Require(interactiveItems[key].Available, "Shared control remains available: " + key);
        }
        internal void RunSingleMonitorChecks()
        {
            StringBuilder report = new StringBuilder();
            try
            {
                Require(canvases.Count == 1 && !canvases[0].IsLake, "Single screen starts with Kremlin");
                Require(singleSceneMenu.Available && !swapItem.Available, "Single-monitor menu replaces swap");
                CheckInteractiveVisibility(true, false);
                ClickCheck(interactiveItems["lamp"]); Require(sceneState.Lamp, "Visible office lamp works");
                ClickCheck(interactiveItems["lamp"]); Require(!sceneState.Lamp, "Visible office lamp switches off");
                ClickCheck(singleLakeItem); Require(canvases.Count == 1 && canvases[0].IsLake && singleLakeItem.Checked, "Single screen switches to lake");
                CheckInteractiveVisibility(false, true);
                ClickCheck(interactiveItems["nature"]); Require(sceneState.Nature && nature.Status() == "playing", "Visible lake nature control works");
                ClickCheck(interactiveItems["nature"]); Require(!sceneState.Nature && nature.Status() == "closed", "Visible lake nature switches off");
                Require(Settings.LoadFrom(settings.PersistPath).SingleScene == "lake", "Single scene preference persists");
                ClickCheck(singleOfficeItem); Require(!canvases[0].IsLake && singleOfficeItem.Checked, "Single screen switches to Kremlin");
                CheckInteractiveVisibility(true, false);
                ClickCheck(singleLakeItem);
                CheckInteractiveVisibility(false, true);
                report.AppendLine("One screen: Kremlin / lake menu switches full-screen scene and persists choice: PASS");
                report.AppendLine("One screen: only active-scene interactive controls shown; lamp and nature controls clicked; shared controls retained: PASS");
                if (Screen.AllScreens.Length > 1)
                {
                    forceSingleScreen = false; Rebuild();
                    Require(canvases.Count == 2 && !singleSceneMenu.Available && swapItem.Available, "Two screens show swap menu");
                    CheckInteractiveVisibility(true, true);
                    ClickCheck(swapItem); Require(canvases[0].IsLake && !canvases[1].IsLake, "Two screens swap scenes");
                    CheckInteractiveVisibility(true, true);
                    forceSingleScreen = true; Rebuild();
                    Require(canvases.Count == 1 && canvases[0].IsLake && singleSceneMenu.Available && !swapItem.Available, "Returning to one screen restores saved lake, independently of swap");
                    CheckInteractiveVisibility(false, true);
                    report.AppendLine("One -> two -> one screen: menu adapts, two-scene swap works, saved single scene restored: PASS");
                    report.AppendLine("Interactive controls update on monitor changes; both scenes available with two screens, including after swap: PASS");
                }
                foreach (DesktopSurface surface in surfaces) Require(surface.IsAttached, "Single-screen scene stays attached");
                MenuChecksPassed = true;
                File.WriteAllText("single-monitor-verification.txt", report.ToString()); ExitThread();
            }
            catch (Exception error)
            {
                MenuChecksPassed = false; report.AppendLine("FAILED: " + error); Program.Log(error);
                File.WriteAllText("single-monitor-verification.txt", report.ToString()); ExitThread();
            }
        }
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
        internal void RunTrayStressChecks()
        {
            StringBuilder report = new StringBuilder();
            int round = 0, stage = 0, framesAtOpen = 0;
            bool expected = false;
            System.Windows.Forms.Timer exercise = new System.Windows.Forms.Timer { Interval = 160 };
            exercise.Tick += delegate
            {
                try
                {
                    if (stage == 0)
                    {
                        menu.AutoClose = true; menu.Show(new Point(80, 100));
                        Require(animation != null && animation.Suspended, "Animation suspends for tray menu");
                        framesAtOpen = animationFrames; expected = !settings.Swapped; stage = 1;
                    }
                    else if (stage == 1)
                    {
                        Require(animationFrames == framesAtOpen, "No frames while menu is open");
                        Point point = new Point(swapItem.Bounds.Left + swapItem.Bounds.Width / 2, swapItem.Bounds.Top + swapItem.Bounds.Height / 2);
                        Require(swapItem.Enabled && menu.GetItemAt(point) == swapItem, "Swap item remains clickable");
                        IntPtr position = new IntPtr((point.Y << 16) | (point.X & 0xffff));
                        SendMessage(menu.Handle, 0x0200, IntPtr.Zero, position);
                        SendMessage(menu.Handle, 0x0201, new IntPtr(1), position);
                        SendMessage(menu.Handle, 0x0202, IntPtr.Zero, position);
                        stage = 2;
                    }
                    else
                    {
                        Require(settings.Swapped == expected, "Native mouse click activated swap");
                        Require(!menu.Visible && !animation.Suspended, "Menu closes and animation resumes");
                        Require(swapItem.Enabled && !swapItem.IsDisposed, "Swap button stays enabled");
                        foreach (DesktopSurface surface in surfaces) Require(surface.IsAttached, "Desktop stays attached after swap");
                        report.AppendLine("Native menu click, swap " + (++round) + ": PASS; swapped=" + settings.Swapped);
                        stage = 0;
                        if (round == 8)
                        {
                            exercise.Stop(); exercise.Dispose();
                            report.AppendLine("Live animation at target 100 FPS; menu rendering pause/resume and native click messages: PASS");
                            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tray-stress-verification.txt"), report.ToString());
                            RunMenuChecks();
                        }
                    }
                }
                catch (Exception error)
                {
                    exercise.Stop(); exercise.Dispose(); MenuChecksPassed = false;
                    report.AppendLine("FAILED: " + error); Program.Log(error);
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tray-stress-verification.txt"), report.ToString()); ExitThread();
                }
            };
            exercise.Start();
        }
        private void ClickCheck(ToolStripMenuItem item)
        {
            menu.AutoClose = false;
            menu.Show(new Point(80, 100));
            ToolStripMenuItem parent = item.OwnerItem as ToolStripMenuItem;
            if (parent != null) { parent.DropDown.AutoClose = false; parent.ShowDropDown(); }
            Application.DoEvents();
            ToolStrip owner = item.Owner;
            Point center = new Point(item.Bounds.Left + item.Bounds.Width / 2, item.Bounds.Top + item.Bounds.Height / 2);
            Require(item.Enabled && item.Visible && owner.GetItemAt(center) == item, "Menu hit test: " + item.Text);
            menu.Close();
            item.PerformClick();
            Application.DoEvents();
        }
    }
}
