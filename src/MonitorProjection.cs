using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace RussiaFlag
{
    // Project the complete display texture, including text, onto the four photographed
    // corners. A homography handles keystone distortion as well as rotation and shear.
    internal sealed class MonitorProjection
    {
        internal readonly PointF[] Corners;
        internal readonly Rectangle Bounds;
        private readonly double a, b, c, d, e, f, g, h;
        private readonly float[] mapU, mapV;
        private float[] waveSin, waveCos;
        internal MonitorProjection(PointF[] points)
        {
            Corners = points;
            float left = points[0].X, right = left, top = points[0].Y, bottom = top;
            foreach (PointF p in points) { left = Math.Min(left, p.X); right = Math.Max(right, p.X); top = Math.Min(top, p.Y); bottom = Math.Max(bottom, p.Y); }
            Bounds = Rectangle.FromLTRB((int)Math.Floor(left), (int)Math.Floor(top), (int)Math.Ceiling(right), (int)Math.Ceiling(bottom));
            double dx1 = points[1].X - points[2].X, dx2 = points[3].X - points[2].X;
            double dy1 = points[1].Y - points[2].Y, dy2 = points[3].Y - points[2].Y;
            double dx3 = points[0].X - points[1].X + points[2].X - points[3].X;
            double dy3 = points[0].Y - points[1].Y + points[2].Y - points[3].Y;
            double det = dx1 * dy2 - dx2 * dy1;
            if (Math.Abs(det) < .00001) throw new ArgumentException("Degenerate monitor corners");
            g = (dx3 * dy2 - dx2 * dy3) / det; h = (dx1 * dy3 - dx3 * dy1) / det;
            a = points[1].X - points[0].X + g * points[1].X;
            b = points[3].X - points[0].X + h * points[3].X; c = points[0].X;
            d = points[1].Y - points[0].Y + g * points[1].Y;
            e = points[3].Y - points[0].Y + h * points[3].Y; f = points[0].Y;
            mapU = new float[Bounds.Width * Bounds.Height]; mapV = new float[mapU.Length];
            for (int y = 0; y < Bounds.Height; y++)
                for (int x = 0; x < Bounds.Width; x++)
                {
                    double px = Bounds.Left + x + .5, py = Bounds.Top + y + .5;
                    double aa = a - g * px, bb = b - h * px, dd = d - g * py, ee = e - h * py;
                    double determinant = aa * ee - bb * dd;
                    double u = Math.Abs(determinant) < .00001 ? -1 : ((px - c) * ee - bb * (py - f)) / determinant;
                    double v = Math.Abs(determinant) < .00001 ? -1 : (aa * (py - f) - (px - c) * dd) / determinant;
                    int index = y * Bounds.Width + x;
                    mapU[index] = (float)(u < 0 || v < 0 || u > 1 || v > 1 ? -1 : u); mapV[index] = (float)v;
                }
        }
        internal PointF Project(double u, double v)
        {
            double divisor = g * u + h * v + 1;
            return new PointF((float)((a * u + b * v + c) / divisor), (float)((d * u + e * v + f) / divisor));
        }
        internal unsafe Bitmap Warp(Bitmap source)
        {
            Bitmap image = new Bitmap(Bounds.Width, Bounds.Height, PixelFormat.Format32bppArgb);
            BitmapData input = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData output = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < image.Height; y++)
                {
                    uint* row = (uint*)((byte*)output.Scan0 + y * output.Stride);
                    for (int x = 0; x < image.Width; x++)
                    {
                        int index = y * image.Width + x;
                        double u = mapU[index], v = mapV[index];
                        if (u < 0) { row[x] = 0; continue; }
                        double sx = u * (source.Width - 1), sy = v * (source.Height - 1);
                        int ix = Math.Min(source.Width - 2, (int)sx), iy = Math.Min(source.Height - 2, (int)sy);
                        double fx = sx - ix, fy = sy - iy;
                        uint* first = (uint*)((byte*)input.Scan0 + iy * input.Stride);
                        uint* second = (uint*)((byte*)input.Scan0 + (iy + 1) * input.Stride);
                        uint p00 = first[ix], p10 = first[ix + 1], p01 = second[ix], p11 = second[ix + 1];
                        uint color = 0xff000000;
                        for (int shift = 0; shift <= 16; shift += 8)
                        {
                            double upper = ((p00 >> shift) & 255) * (1 - fx) + ((p10 >> shift) & 255) * fx;
                            double lower = ((p01 >> shift) & 255) * (1 - fx) + ((p11 >> shift) & 255) * fx;
                            color |= (uint)(upper * (1 - fy) + lower * fy) << shift;
                        }
                        row[x] = color;
                    }
                }
            }
            finally { image.UnlockBits(output); source.UnlockBits(input); }
            return image;
        }
        internal unsafe Bitmap Flag(double time, Bitmap image)
        {
            if (image == null) image = new Bitmap(Bounds.Width, Bounds.Height, PixelFormat.Format32bppArgb);
            if (waveSin == null)
            {
                waveSin = new float[mapU.Length]; waveCos = new float[mapU.Length];
                for (int i = 0; i < mapU.Length; i++)
                {
                    double phase = Math.PI * 2 * (mapU[i] * 1.8 + mapV[i] * .11);
                    waveSin[i] = (float)Math.Sin(phase); waveCos[i] = (float)Math.Cos(phase);
                }
            }
            float ct = (float)Math.Cos(time * 2.6), st = (float)Math.Sin(time * 2.6);
            BitmapData output = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < image.Height; y++)
                {
                    uint* row = (uint*)((byte*)output.Scan0 + y * output.Stride);
                    for (int x = 0; x < image.Width; x++)
                    {
                        int i = y * image.Width + x;
                        if (mapU[i] < 0) { row[x] = 0; continue; }
                        float wave = waveSin[i] * ct - waveCos[i] * st;
                        float slope = waveCos[i] * ct + waveSin[i] * st;
                        float v = mapV[i] + (.012f + .025f * mapU[i]) * wave + .006f * wave * slope;
                        float light = .83f + .14f * slope + .025f * (1 - 2 * wave * wave);
                        int r, green, blue;
                        if (v < 1f / 3) { r = 255; green = 255; blue = 255; }
                        else if (v < 2f / 3) { r = 0; green = 57; blue = 166; }
                        else { r = 213; green = 43; blue = 30; }
                        row[x] = 0xff000000 | ((uint)(r * light) << 16) | ((uint)(green * light) << 8) | (uint)(blue * light);
                    }
                }
            }
            finally { image.UnlockBits(output); }
            return image;
        }
    }
}
