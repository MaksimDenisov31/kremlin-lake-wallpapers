using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace RussiaFlag
{
    // Prepare each photograph once at monitor resolution; idle scenes need no frame loop.
    internal sealed partial class SceneCanvas : IDisposable
    {
        internal event EventHandler SceneInvalidated;
        internal bool IsDisposed { get; private set; }
        private readonly Bitmap frame;
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
        {
            internal uint Size;
            internal int Width, Height;
            internal ushort Planes, BitCount;
            internal uint Compression, ImageSize;
            internal int XPels, YPels;
            internal uint Used, Important;
        }
        [DllImport("gdi32.dll")] private static extern int StretchDIBits(IntPtr hdc, int x, int y, int w, int h,
            int sx, int sy, int sw, int sh, IntPtr bits, ref BitmapInfo info, uint usage, uint operation);
        [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr hdc, int mode);

        internal SceneCanvas(int width, int height, string asset)
        {
            frame = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            try
            {
                using (Image source = Image.FromFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", asset)))
                using (Graphics graphics = Graphics.FromImage(frame))
                {
                    double scale = Math.Max((double)width / source.Width, (double)height / source.Height);
                    float sw = (float)(width / scale), sh = (float)(height / scale);
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    // A lower crop keeps the standing man's feet in the widescreen frame.
                    float cropY = (source.Height - sh) *
                        (String.Equals(asset, "office-original.jpg", StringComparison.OrdinalIgnoreCase) ? 1f : .5f);
                    SetProjection(source.Width, source.Height, (source.Width - sw) / 2, cropY, scale);
                    using (ImageAttributes attributes = new ImageAttributes())
                    {
                        attributes.SetWrapMode(WrapMode.TileFlipXY);
                        graphics.DrawImage(source, new Rectangle(0, 0, width, height),
                            (source.Width - sw) / 2, cropY, sw, sh, GraphicsUnit.Pixel, attributes);
                    }
                }
                InitializeInteractive(asset);
            }
            catch { frame.Dispose(); throw; }
        }
        internal void PaintScene(Graphics graphics, Rectangle bounds)
        {
            BitmapData pixels = frame.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            IntPtr dc = IntPtr.Zero;
            try
            {
                dc = graphics.GetHdc();
                BitmapInfo info = new BitmapInfo { Size = 40, Width = frame.Width, Height = -frame.Height, Planes = 1, BitCount = 32 };
                int previous = SetStretchBltMode(dc, 3);
                StretchDIBits(dc, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0, 0, frame.Width, frame.Height, pixels.Scan0, ref info, 0, 0x00CC0020);
                SetStretchBltMode(dc, previous);
            }
            finally
            {
                if (dc != IntPtr.Zero) graphics.ReleaseHdc(dc);
                frame.UnlockBits(pixels);
            }
            PaintOverlays(graphics, bounds);
        }
        internal void Refresh() { if (SceneInvalidated != null) SceneInvalidated(this, EventArgs.Empty); }
        internal void Save(string path)
        {
            using (Bitmap result = Snapshot()) result.Save(path, ImageFormat.Png);
        }
        internal Bitmap Snapshot()
        {
            Bitmap result = (Bitmap)frame.Clone();
            using (Graphics graphics = Graphics.FromImage(result)) PaintOverlays(graphics, new Rectangle(0, 0, result.Width, result.Height));
            return result;
        }
        public void Dispose() { if (!IsDisposed) { IsDisposed = true; DisposeInteractive(); frame.Dispose(); } }
    }
}
