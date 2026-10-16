using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;

namespace NetBunny
{
    // Source regions are cached in memory at suitable display resolutions; PNG assets stay intact.
    internal sealed class RasterAtlas : IDisposable
    {
        internal readonly Bitmap Image;
        internal readonly Rectangle[] Cells;
        internal readonly Rectangle[] Bounds;
        internal readonly int[] InkPixels;
        internal readonly string PathName;
        internal readonly double TransparentRatio;
        private Bitmap[] cached;
        internal RasterAtlas(string path, int columns, int rows)
        {
            PathName = path;
            using (Image input = System.Drawing.Image.FromFile(path))
            {
                Image = new Bitmap(input.Width, input.Height, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(Image)) g.DrawImageUnscaled(input, 0, 0);
            }
            Cells = new Rectangle[columns * rows]; Bounds = new Rectangle[Cells.Length]; InkPixels = new int[Cells.Length];
            BitmapData data = Image.LockBits(new Rectangle(0, 0, Image.Width, Image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            bool complete = false; try
            {
                byte[] pixels = new byte[Math.Abs(data.Stride) * data.Height]; Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); long transparent = 0;
                for (int y = 0; y < Image.Height; y++) for (int x = 0; x < Image.Width; x++) if (pixels[(data.Stride >= 0 ? y : Image.Height - 1 - y) * Math.Abs(data.Stride) + x * 4 + 3] < 16) transparent++;
                Rectangle[] authored = ReadRegions(System.IO.Path.ChangeExtension(path, ".rects"), Cells.Length, Image.Size);
                for (int i = 0; i < Cells.Length; i++)
                {
                    Rectangle c = Rectangle.FromLTRB(i % columns * Image.Width / columns, i / columns * Image.Height / rows, (i % columns + 1) * Image.Width / columns, (i / columns + 1) * Image.Height / rows);
                    if (authored != null) c = authored[i];
                    Cells[i] = c; int left = c.Right, top = c.Bottom, right = c.Left, bottom = c.Top;
                    for (int y = c.Top; y < c.Bottom; y++) for (int x = c.Left; x < c.Right; x++)
                    {
                        int alpha = pixels[(data.Stride >= 0 ? y : Image.Height - 1 - y) * Math.Abs(data.Stride) + x * 4 + 3];
                        if (alpha <= 24) continue;
                        InkPixels[i]++; left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                    }
                    Bounds[i] = InkPixels[i] == 0 ? Rectangle.Empty : Rectangle.Intersect(c, Rectangle.FromLTRB(left - 2, top - 2, right + 3, bottom + 3));
                }
                TransparentRatio = transparent / (double)(Image.Width * Image.Height); complete = true;
            }
            finally { Image.UnlockBits(data); if (!complete) Image.Dispose(); }
            try
            {
                cached = new Bitmap[Bounds.Length];
                for (int i = 0; i < Bounds.Length; i++)
                {
                    if (!HasCell(i)) continue; Rectangle source = Bounds[i];
                    int maximum = 256;
                    if (System.IO.Path.GetFileName(path).StartsWith("rig-", StringComparison.OrdinalIgnoreCase)) maximum = i == 0 ? 192 : i == 1 ? 128 : i == 10 ? 160 : i == 11 ? 288 : 96;
                    else if (System.IO.Path.GetFileName(path).StartsWith("monsters-", StringComparison.OrdinalIgnoreCase)) maximum = i == 5 ? 288 : 208;
                    float scale = Math.Min(1, maximum / (float)Math.Max(source.Width, source.Height));
                    Bitmap cell = new Bitmap(Math.Max(1, (int)Math.Ceiling(source.Width * scale)), Math.Max(1, (int)Math.Ceiling(source.Height * scale)), PixelFormat.Format32bppPArgb); cached[i] = cell;
                    using (Graphics g = Graphics.FromImage(cell)) { g.CompositingMode = CompositingMode.SourceCopy; g.CompositingQuality = CompositingQuality.HighQuality; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(Image, new Rectangle(0, 0, cell.Width, cell.Height), source, GraphicsUnit.Pixel); }
                }
            }
            catch { Dispose(); throw; }
        }
        private static Rectangle[] ReadRegions(string path, int count, Size size)
        {
            if (!File.Exists(path)) return null; System.Collections.Generic.List<Rectangle> regions = new System.Collections.Generic.List<Rectangle>();
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] fields = line.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length != 4) throw new InvalidDataException("Atlas region must contain x, y, width, height: " + path);
                Rectangle r = new Rectangle(Int32.Parse(fields[0]), Int32.Parse(fields[1]), Int32.Parse(fields[2]), Int32.Parse(fields[3]));
                if (r.Width <= 0 || r.Height <= 0 || r.X < 0 || r.Y < 0 || r.Right > size.Width || r.Bottom > size.Height) throw new InvalidDataException("Atlas region outside image: " + path);
                regions.Add(r);
            }
            if (regions.Count != count) throw new InvalidDataException("Atlas region count mismatch: " + path); return regions.ToArray();
        }
        internal static RasterAtlas TryLoad(string directory, string file, int columns, int rows)
        {
            string path = System.IO.Path.Combine(directory ?? "", file); if (!File.Exists(path)) return null;
            try { return new RasterAtlas(path, columns, rows); } catch { return null; }
        }
        internal bool HasCell(int index) { return index >= 0 && index < Bounds.Length && !Bounds[index].IsEmpty && InkPixels[index] >= 64; }
        internal Bitmap CellImage(int index) { return cached[index]; }
        internal void Draw(Graphics g, int index, RectangleF destination)
        {
            if (!HasCell(index)) return;
            Bitmap cell = cached[index]; InterpolationMode interpolation = g.InterpolationMode;
            // Cells already went through high-quality prefiltering once. Bilinear transforms
            // at their display resolution avoid repeating the large bicubic kernel every tick.
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(cell, destination, new Rectangle(0, 0, cell.Width, cell.Height), GraphicsUnit.Pixel); g.InterpolationMode = interpolation;
        }
        internal void DrawFit(Graphics g, int index, RectangleF box)
        {
            if (!HasCell(index)) return; Rectangle source = Bounds[index];
            float scale = Math.Min(box.Width / source.Width, box.Height / source.Height);
            RectangleF destination = new RectangleF(box.X + (box.Width - source.Width * scale) * .5f, box.Y + (box.Height - source.Height * scale) * .5f, source.Width * scale, source.Height * scale);
            Draw(g, index, destination);
        }
        internal void DrawFade(Graphics g, int index, RectangleF destination, float opacity, float flash)
        {
            if (!HasCell(index)) return;
            if (opacity >= .999f && flash <= 0) { Draw(g, index, destination); return; }
            using (ImageAttributes attributes = new ImageAttributes())
            {
                ColorMatrix matrix = new ColorMatrix(); matrix.Matrix33 = Math.Max(0, Math.Min(1, opacity)); matrix.Matrix40 = matrix.Matrix41 = matrix.Matrix42 = Math.Max(0, Math.Min(.7f, flash)); attributes.SetColorMatrix(matrix);
                Bitmap cell = cached[index]; Rectangle b = new Rectangle(0, 0, cell.Width, cell.Height); PointF[] points = { new PointF(destination.Left, destination.Top), new PointF(destination.Right, destination.Top), new PointF(destination.Left, destination.Bottom) };
                InterpolationMode interpolation = g.InterpolationMode; g.InterpolationMode = InterpolationMode.Bilinear;
                g.DrawImage(cell, points, b, GraphicsUnit.Pixel, attributes); g.InterpolationMode = interpolation;
            }
        }
        public void Dispose() { if (cached != null) { foreach (Bitmap cell in cached) if (cell != null) cell.Dispose(); cached = null; } Image.Dispose(); }
    }
}
