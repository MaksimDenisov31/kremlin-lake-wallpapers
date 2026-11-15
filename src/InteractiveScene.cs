using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace RussiaFlag
{
    internal sealed partial class SceneCanvas
    {
        internal bool IsLake { get; private set; }
        internal InteractionState State = new InteractionState();
        private Bitmap still, windowView, tackleAtlas, screenPicture, boxClosed, boxOpen;
        private Bitmap projectedMonitor;
        internal MonitorProjection MonitorPlane;
        private GraphicsPath monitorMask;
        private int monitorCacheSecond = -1;
        private string monitorMode = "clock";
        private double flagCacheTime = double.NaN;
        private GraphicsPath curtainMask;
        private GraphicsPath physicalMonitorOutline;
        private Region curtainVisible;
        private PointF[] curtainPoints;
        private Bitmap curtainFabric;
        private float sourceWidth, sourceHeight, cropLeft, cropTop, zoom;
        private bool ai;
        private float lastCurtain = -1, drawer;
        private int lastSignature = -1, lastSecond = -1;
        private float dragCurtainStart;
        internal RectangleF CurtainArea, LampArea, ScreenArea, PocketArea, SoundArea;
        internal bool Hotspots;
        private void SetProjection(int w, int h, float left, float top, double scale)
        { sourceWidth = w; sourceHeight = h; cropLeft = left; cropTop = top; zoom = (float)scale; }
        private RectangleF SourceRect(float x, float y, float w, float h)
        { return new RectangleF((x * sourceWidth - cropLeft) * zoom, (y * sourceHeight - cropTop) * zoom, w * sourceWidth * zoom, h * sourceHeight * zoom); }
        internal PointF SourcePixel(float x, float y) { return new PointF((x - cropLeft) * zoom, (y - cropTop) * zoom); }
        private RectangleF NormalRect(float x, float y, float w, float h)
        { return new RectangleF(x * frame.Width, y * frame.Height, w * frame.Width, h * frame.Height); }
        private static Bitmap LoadAsset(string name)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", name);
            if (!File.Exists(path)) return null;
            using (Image image = Image.FromFile(path)) return new Bitmap(image);
        }
        private void InitializeInteractive(string asset)
        {
            IsLake = asset == "lake-rest.png"; ai = asset == "kremlin-office.png";
            still = (Bitmap)frame.Clone();
            if (IsLake)
            {
                tackleAtlas = LoadAsset("fishing-box.png");
                PocketArea = NormalRect(.02f, .61f, .22f, .36f);
                SoundArea = NormalRect(.035f, .535f, .038f, .056f);
                if (tackleAtlas != null)
                {
                    boxClosed = PrepareBox(0); boxOpen = PrepareBox(tackleAtlas.Width / 2);
                }
            }
            else
            {
                windowView = LoadAsset("window-view.png");
                InitializeMonitorPlane();
                InitializeCurtain();
                LampArea = ai ? SourceRect(.566f, .396f, .065f, .15f) : SourceRect(.526f, .348f, .080f, .17f);
                PocketArea = ai ? SourceRect(.655f, .720f, .055f, .10f) : SourceRect(.620f, .720f, .060f, .10f);
            }
        }
        private void InitializeCurtain()
        {
            // Trace only the white sheer fabric, below the fixed scalloped gold valance
            // and between the fixed side drapes. No rectangular background replacement.
            curtainPoints = ai ? new PointF[] {
                SourcePixel(1237,124), SourcePixel(1254,132), SourcePixel(1272,137), SourcePixel(1290,136),
                SourcePixel(1308,131), SourcePixel(1326,124), SourcePixel(1342,114), SourcePixel(1355,102),
                SourcePixel(1366,87), SourcePixel(1369,145), SourcePixel(1374,213), SourcePixel(1382,290),
                SourcePixel(1386,320), SourcePixel(1393,360), SourcePixel(1404,400), SourcePixel(1413,435),
                SourcePixel(1226,435), SourcePixel(1226,300), SourcePixel(1229,260), SourcePixel(1233,195) } : new PointF[] {
                SourcePixel(1090,95), SourcePixel(1110,105), SourcePixel(1131,108), SourcePixel(1150,105),
                SourcePixel(1172,97), SourcePixel(1190,85), SourcePixel(1208,69), SourcePixel(1224,49),
                SourcePixel(1240,125), SourcePixel(1244,195), SourcePixel(1251,250), SourcePixel(1257,300),
                SourcePixel(1262,360), SourcePixel(1269,400), SourcePixel(1270,427), SourcePixel(1086,450),
                SourcePixel(1084,270), SourcePixel(1090,195) };
            curtainMask = new GraphicsPath(); curtainMask.AddPolygon(curtainPoints); CurtainArea = curtainMask.GetBounds();
            curtainVisible = new Region(curtainMask);
            // Continue the same window behind the whole photographed monitor body;
            // expose only the remaining white strips around its bezel and chair.
            curtainVisible.Exclude(physicalMonitorOutline);
            curtainFabric = new Bitmap(Math.Max(1, (int)Math.Ceiling(CurtainArea.Width)), Math.Max(1, (int)Math.Ceiling(CurtainArea.Height)), PixelFormat.Format32bppPArgb);
            using (Graphics fabric = Graphics.FromImage(curtainFabric))
            {
                fabric.TranslateTransform(-CurtainArea.Left, -CurtainArea.Top); fabric.SetClip(curtainVisible, CombineMode.Replace);
                fabric.DrawImage(still, 0, 0);
            }
        }
        internal bool CurtainContains(Point point) { return curtainVisible != null && curtainVisible.IsVisible(point); }
        private void InitializeMonitorPlane()
        {
            // Coordinates measured on each unmodified source image, before monitor scaling/crop.
            PointF[] corners = ai ? new PointF[] { SourcePixel(1161, 306), SourcePixel(1390, 301), SourcePixel(1384, 481), SourcePixel(1163, 456) } :
                new PointF[] { SourcePixel(1020, 281), SourcePixel(1257, 277), SourcePixel(1250, 476), SourcePixel(1020, 451) };
            MonitorPlane = new MonitorProjection(corners); ScreenArea = MonitorPlane.Bounds;
            monitorMask = new GraphicsPath(FillMode.Alternate); monitorMask.AddPolygon(corners);
            physicalMonitorOutline = new GraphicsPath();
            physicalMonitorOutline.AddPolygon(ai ? new PointF[] { SourcePixel(1154,298), SourcePixel(1399,294), SourcePixel(1390,501), SourcePixel(1153,471) } :
                new PointF[] { SourcePixel(1013,274), SourcePixel(1263,270), SourcePixel(1254,482), SourcePixel(1013,454) });
        }
        private Bitmap PrepareBox(int offset)
        {
            Bitmap box = new Bitmap((int)PocketArea.Width, (int)PocketArea.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(box))
            { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(tackleAtlas, new Rectangle(0, 0, box.Width, box.Height), new Rectangle(offset, 0, tackleAtlas.Width / 2, tackleAtlas.Height), GraphicsUnit.Pixel); }
            return box;
        }
        internal void Bind(InteractionState state, string picture, string mode = null)
        {
            State = state;
            monitorMode = mode ?? (String.IsNullOrEmpty(picture) ? "clock" : "image");
            if (monitorMode == "image" && !String.IsNullOrEmpty(picture) && File.Exists(picture))
                using (Image image = Image.FromFile(picture)) screenPicture = new Bitmap(image);
        }
        internal InputTarget[] Targets(Rectangle screen)
        {
            List<InputTarget> result = new List<InputTarget>();
            if (IsLake) { AddTarget(result, screen, PocketArea, "lake-pocket"); AddTarget(result, screen, SoundArea, "nature"); }
            else
            {
                // Only the exposed curtain above the physical monitor receives input.
                RectangleF curtainHit = CurtainArea; curtainHit.Height = Math.Max(12, ScreenArea.Top - curtainHit.Top);
                AddTarget(result, screen, curtainHit, "curtain"); AddTarget(result, screen, LampArea, "lamp");
                AddTarget(result, screen, ScreenArea, "monitor"); AddTarget(result, screen, PocketArea, "office-pocket");
            }
            return result.ToArray();
        }
        private void AddTarget(List<InputTarget> result, Rectangle screen, RectangleF area, string action)
        {
            float sx = (float)screen.Width / frame.Width, sy = (float)screen.Height / frame.Height;
            Rectangle mapped = Rectangle.Round(new RectangleF(screen.X + area.X * sx, screen.Y + area.Y * sy, area.Width * sx, area.Height * sy));
            mapped.Intersect(screen);
            if (mapped.Width > 0 && mapped.Height > 0) result.Add(new InputTarget { Bounds = mapped, Action = action, Scene = this });
            if (action == "curtain" && result.Count > 0)
            {
                PointF[] shape = new PointF[curtainPoints.Length];
                for (int i = 0; i < shape.Length; i++) shape[i] = new PointF(screen.X + curtainPoints[i].X * sx, screen.Y + curtainPoints[i].Y * sy);
                result[result.Count - 1].Shape = shape;
            }
        }
        internal void CurtainGesture(SceneGesture gesture)
        {
            if (gesture.Kind == 0) dragCurtainStart = State.Curtain;
            else
            {
                int dx = gesture.Point.X - gesture.Start.X;
                if (gesture.Kind == 2 && Math.Abs(dx) < 6) State.CurtainTarget = State.CurtainTarget > .5f ? 0 : 1;
                else
                {
                    State.Curtain = Math.Max(0, Math.Min(1, dragCurtainStart + dx / (float)Math.Max(30, gesture.Target.Bounds.Width)));
                    State.CurtainTarget = gesture.Kind == 2 ? (State.Curtain > .4 ? 1 : 0) : State.Curtain;
                }
            }
            Refresh();
        }
        internal bool Tick(double seconds)
        {
            float targetDrawer = (IsLake ? State.LakePocket : State.OfficePocket) ? 1 : 0;
            float next = drawer + Math.Sign(targetDrawer - drawer) * (float)Math.Min(Math.Abs(targetDrawer - drawer), seconds * 3.5);
            bool moving = Math.Abs(next - drawer) > .001; drawer = next;
            int signature = (State.Lamp ? 1 : 0) | (State.Monitor ? 2 : 0) | (State.OfficePocket ? 4 : 0) | (State.LakePocket ? 8 : 0) | (State.Nature ? 16 : 0) | (Hotspots ? 32 : 0);
            int second = DateTime.Now.Second;
            bool changed = signature != lastSignature || Math.Abs(lastCurtain - State.Curtain) > .0001f || moving || (!IsLake && State.Monitor && second != lastSecond);
            if (!IsLake && State.Monitor && monitorMode == "flag" && !State.Paused) changed = true;
            if (IsLake && !State.Paused) { WarpWater(State.Time); changed = true; }
            lastSignature = signature; lastCurtain = State.Curtain; lastSecond = second;
            if (changed) Refresh();
            return changed;
        }
        private unsafe void WarpWater(double time)
        {
            time *= 2.4;
            // Animate clear water on the left; the person and distant shore stay still.
            Rectangle area = Rectangle.Round(NormalRect(0, .45f, .49f, .55f));
            BitmapData original = still.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            BitmapData output = frame.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppRgb);
            try
            {
                for (int y = area.Top; y < area.Bottom; y++)
                {
                    double depth = (double)(y - area.Top) / area.Height;
                    double wave = (Math.Sin(depth * 45 - time * 1.4) + .5 * Math.Sin(depth * 91 + time * 1.1)) * Math.Sin(depth * Math.PI) * frame.Width / 800.0;
                    int shift = (int)Math.Round(wave), leftEdge = Math.Min(48, area.Width / 4), rightEdge = Math.Min(68, area.Width / 4);
                    uint* dest = (uint*)((byte*)output.Scan0 + y * output.Stride);
                    uint* source = (uint*)((byte*)original.Scan0 + y * original.Stride);
                    for (int x = area.Left; x < area.Left + leftEdge; x++) dest[x] = source[x + shift * (x - area.Left) / leftEdge];
                    for (int x = area.Left + leftEdge; x < area.Right - rightEdge; x++) dest[x] = source[x + shift];
                    for (int x = area.Right - rightEdge; x < area.Right; x++) dest[x] = source[x + shift * (area.Right - 1 - x) / rightEdge];
                }
            }
            finally { frame.UnlockBits(output); still.UnlockBits(original); }
        }
        private void PaintOverlays(Graphics graphics, Rectangle bounds)
        {
            GraphicsState saved = graphics.Save();
            graphics.TranslateTransform(bounds.Left, bounds.Top); graphics.ScaleTransform((float)bounds.Width / frame.Width, (float)bounds.Height / frame.Height);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (IsLake) { DrawTackleBox(graphics); DrawSoundButton(graphics); }
            else
            {
                DrawCurtain(graphics);
                if (State.Lamp) DrawLamp(graphics);
                if (State.Monitor) DrawMonitor(graphics);
                DrawDrawer(graphics);
            }
            if (Hotspots) DrawHotspots(graphics);
            graphics.Restore(saved);
        }
        private void DrawCurtain(Graphics g)
        {
            float p = State.Curtain;
            if (p <= .001f || windowView == null) return;
            GraphicsState save = g.Save();
            using (Region clip = curtainVisible.Clone())
            {
                g.SetClip(clip, CombineMode.Intersect);
                g.DrawImage(windowView, CurtainArea);
                float remaining = CurtainArea.Width * (1 - p);
                if (remaining > .5f)
                {
                    RectangleF fabric = new RectangleF(CurtainArea.Right - remaining, CurtainArea.Top, remaining, CurtainArea.Height);
                    g.DrawImage(curtainFabric, fabric);
                    using (LinearGradientBrush shadow = new LinearGradientBrush(new RectangleF(fabric.Left - 9, fabric.Top, 10, fabric.Height), Color.Transparent, Color.FromArgb(50, 25, 20, 15), LinearGradientMode.Horizontal))
                        g.FillRectangle(shadow, fabric.Left - 9, fabric.Top, 9, fabric.Height);
                }
            }
            g.Restore(save);
        }
        private void DrawLamp(Graphics g)
        {
            RectangleF glow = new RectangleF(LampArea.X - LampArea.Width * 1.0f, LampArea.Y + LampArea.Height * .5f, LampArea.Width * 3.8f, LampArea.Height * 1.65f);
            using (GraphicsPath shape = new GraphicsPath())
            {
                shape.AddEllipse(glow);
                using (PathGradientBrush brush = new PathGradientBrush(shape))
                { brush.CenterColor = Color.FromArgb(95, 255, 219, 132); brush.SurroundColors = new Color[] { Color.FromArgb(0, 255, 220, 140) }; g.FillPath(brush, shape); }
            }
            using (SolidBrush bulb = new SolidBrush(Color.FromArgb(145, 255, 242, 185)))
                g.FillEllipse(bulb, LampArea.X + LampArea.Width * .32f, LampArea.Y + LampArea.Height * .02f, LampArea.Width * .38f, LampArea.Height * .09f);
        }
        private void DrawMonitor(Graphics g)
        {
            int second = DateTime.Now.Second;
            if (monitorMode == "flag")
            {
                if (projectedMonitor == null || flagCacheTime != State.Time) { projectedMonitor = MonitorPlane.Flag(State.Time, projectedMonitor); flagCacheTime = State.Time; }
            }
            else if (projectedMonitor == null || (screenPicture == null && monitorCacheSecond != second))
            {
                if (projectedMonitor != null) projectedMonitor.Dispose();
                using (Bitmap content = new Bitmap(640, 480, PixelFormat.Format32bppArgb))
                {
                    using (Graphics display = Graphics.FromImage(content)) DrawMonitorContent(display, new RectangleF(0, 0, 640, 480));
                    projectedMonitor = MonitorPlane.Warp(content);
                }
                monitorCacheSecond = second;
            }
            GraphicsState save = g.Save(); g.SetClip(monitorMask, CombineMode.Intersect);
            // Restore the photographed objects that stand in front of the physical screen.
            using (GraphicsPath foreground = new GraphicsPath())
            {
                foreground.AddPolygon(ai ? new PointF[] { SourcePixel(1174, 368), SourcePixel(1200, 374), SourcePixel(1200, 401), SourcePixel(1229, 399), SourcePixel(1234, 493), SourcePixel(1174, 493) } :
                    new PointF[] { SourcePixel(1041, 348), SourcePixel(1067, 354), SourcePixel(1067, 382), SourcePixel(1098, 382), SourcePixel(1102, 489), SourcePixel(1041, 489) });
                foreground.AddPolygon(ai ? new PointF[] { SourcePixel(1340, 464), SourcePixel(1353, 450), SourcePixel(1378, 441), SourcePixel(1394, 446), SourcePixel(1394, 500), SourcePixel(1340, 500) } :
                    new PointF[] { SourcePixel(1203, 449), SourcePixel(1219, 437), SourcePixel(1247, 426), SourcePixel(1267, 436), SourcePixel(1267, 500), SourcePixel(1203, 500) });
                using (Region visible = new Region(monitorMask)) { visible.Exclude(foreground); g.SetClip(visible, CombineMode.Intersect); }
            }
            g.DrawImage(projectedMonitor, MonitorPlane.Bounds);
            g.Restore(save);
        }
        private void DrawMonitorContent(Graphics g, RectangleF screenArea)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (LinearGradientBrush screen = new LinearGradientBrush(screenArea, Color.FromArgb(15, 42, 55), Color.FromArgb(3, 13, 23), LinearGradientMode.ForwardDiagonal)) g.FillRectangle(screen, screenArea);
            if (screenPicture != null)
            {
                double fit = Math.Min(screenArea.Width / screenPicture.Width, screenArea.Height / screenPicture.Height);
                SizeF size = new SizeF((float)(screenPicture.Width * fit), (float)(screenPicture.Height * fit));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(screenPicture, new RectangleF((screenArea.Width - size.Width) / 2, (screenArea.Height - size.Height) / 2, size.Width, size.Height));
            }
            else
            {
                using (Font clock = new Font("Segoe UI Light", screenArea.Height * .20f, GraphicsUnit.Pixel))
                using (Font date = new Font("Segoe UI", screenArea.Height * .07f, GraphicsUnit.Pixel))
                using (SolidBrush ink = new SolidBrush(Color.FromArgb(230, 240, 247)))
                using (StringFormat centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(DateTime.Now.ToString("HH:mm:ss"), clock, ink, new RectangleF(0, screenArea.Height * .22f, screenArea.Width, screenArea.Height * .35f), centered);
                    g.DrawString(DateTime.Now.ToString("dd MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")), date, ink, new RectangleF(0, screenArea.Height * .56f, screenArea.Width, screenArea.Height * .2f), centered);
                }
            }
            using (LinearGradientBrush reflection = new LinearGradientBrush(screenArea, Color.FromArgb(18, 230, 240, 246), Color.Transparent, LinearGradientMode.ForwardDiagonal)) g.FillRectangle(reflection, screenArea);
        }
        private void DrawDrawer(Graphics g)
        {
            if (drawer <= .001f) return;
            RectangleF hole = PocketArea;
            using (SolidBrush dark = new SolidBrush(Color.FromArgb((int)(230 * drawer), 18, 12, 8))) g.FillRectangle(dark, hole);
            RectangleF front = new RectangleF(hole.X, hole.Y + hole.Height * drawer * .65f, hole.Width, hole.Height);
            g.DrawImage(still, front, hole, GraphicsUnit.Pixel);
            using (Pen trim = new Pen(Color.FromArgb(165, 171, 132, 67), Math.Max(1, frame.Width / 1100f))) g.DrawRectangle(trim, front.X, front.Y, front.Width, front.Height);
        }
        private void DrawTackleBox(Graphics g)
        {
            RectangleF box = PocketArea; box.Y -= drawer * frame.Height * .025f;
            if (boxClosed != null)
            {
                g.DrawImage(drawer > .5f ? boxOpen : boxClosed, box);
            }
            else
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(54, 68, 42))) g.FillRectangle(brush, box);
            }
        }
        private void DrawSoundButton(Graphics g)
        {
            using (SolidBrush button = new SolidBrush(State.Nature ? Color.FromArgb(210, 52, 100, 77) : Color.FromArgb(145, 15, 32, 38))) g.FillEllipse(button, SoundArea);
            using (Font symbol = new Font("Segoe UI Symbol", SoundArea.Height * .48f, GraphicsUnit.Pixel))
            using (SolidBrush white = new SolidBrush(Color.FromArgb(235, 244, 235)))
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString("♪", symbol, white, SoundArea, center);
        }
        private void DrawHotspots(Graphics g)
        {
            Dictionary<string, string> labels = new Dictionary<string, string> { { "curtain", "Штора: потяните вправо / влево" }, { "lamp", "Лампа" }, { "monitor", "Экран: включить / выключить" }, { "office-pocket", "Тайник для ярлыков" }, { "lake-pocket", "Рыболовный ящик" }, { "nature", "Звуки природы" } };
            foreach (InputTarget target in Targets(new Rectangle(0, 0, frame.Width, frame.Height)))
            {
                using (Pen pen = new Pen(Color.FromArgb(220, 244, 211, 126), 2)) g.DrawRectangle(pen, target.Bounds);
                using (Font font = new Font("Segoe UI", Math.Max(12, frame.Width / 110f), GraphicsUnit.Pixel))
                {
                    SizeF size = g.MeasureString(labels[target.Action], font);
                    float x = Math.Min(frame.Width - size.Width - 12, target.Bounds.Left), y = Math.Max(4, target.Bounds.Top - size.Height - 8);
                    using (SolidBrush black = new SolidBrush(Color.FromArgb(220, 15, 22, 25))) g.FillRectangle(black, x, y, size.Width + 8, size.Height + 4);
                    g.DrawString(labels[target.Action], font, Brushes.White, x + 4, y + 2);
                }
            }
        }
        private void DisposeInteractive()
        {
            if (still != null) still.Dispose(); if (windowView != null) windowView.Dispose();
            if (tackleAtlas != null) tackleAtlas.Dispose(); if (screenPicture != null) screenPicture.Dispose();
            if (boxClosed != null) boxClosed.Dispose(); if (boxOpen != null) boxOpen.Dispose();
            if (projectedMonitor != null) projectedMonitor.Dispose(); if (monitorMask != null) monitorMask.Dispose();
            if (curtainMask != null) curtainMask.Dispose(); if (curtainFabric != null) curtainFabric.Dispose();
            if (curtainVisible != null) curtainVisible.Dispose(); if (physicalMonitorOutline != null) physicalMonitorOutline.Dispose();
        }
    }
}
