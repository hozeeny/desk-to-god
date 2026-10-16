using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace NetBunny
{
    internal static class KnightBrand
    {
        private static RasterAtlas helmetAtlas;
        private static bool helmetLoaded;
        private static void LoadHelmet(string directory)
        {
            if (helmetLoaded) return;
            helmetLoaded = true;
            helmetAtlas = RasterAtlas.TryLoad(directory, "rig-OTHER.png", 4, 3);
            AppDomain.CurrentDomain.ProcessExit += delegate { if (helmetAtlas != null) helmetAtlas.Dispose(); };
        }
        internal static void DrawHelmet(Graphics g, Rectangle bounds)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(bounds.X, bounds.Y); g.ScaleTransform(bounds.Width / 128f, bounds.Height / 128f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath plate = Rounded(4, 4, 120, 120, 31))
            using (LinearGradientBrush bg = new LinearGradientBrush(new Rectangle(4, 4, 120, 120), Color.FromArgb(43, 45, 78), Color.FromArgb(15, 23, 43), 55))
            { g.FillPath(bg, plate); using (Pen rim = new Pen(Color.FromArgb(83, 101, 143), 2)) g.DrawPath(rim, plate); }
            LoadHelmet(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets"));
            if (helmetAtlas != null && helmetAtlas.HasCell(0))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                helmetAtlas.DrawFit(g, 0, new RectangleF(8, 5, 112, 118));
                g.Restore(state); return;
            }
            using (GraphicsPath plume = new GraphicsPath())
            { plume.AddBezier(63, 40, 46, 15, 86, 7, 107, 27); plume.AddBezier(107, 27, 88, 22, 87, 52, 64, 51); plume.CloseFigure(); using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(50, 12, 60, 41), Color.FromArgb(255, 111, 116), Color.FromArgb(178, 39, 90), 30)) g.FillPath(b, plume); using (Pen p = new Pen(Color.FromArgb(23, 27, 49), 4)) g.DrawPath(p, plume); }
            using (GraphicsPath helmet = new GraphicsPath())
            { helmet.AddBezier(31, 71, 29, 34, 58, 24, 82, 38); helmet.AddBezier(82, 38, 98, 46, 98, 70, 93, 88); helmet.AddLine(93, 88, 66, 111); helmet.AddLine(66, 111, 34, 92); helmet.CloseFigure(); using (LinearGradientBrush steel = new LinearGradientBrush(new Rectangle(30, 32, 66, 80), Color.FromArgb(239, 250, 255), Color.FromArgb(94, 130, 166), 35)) g.FillPath(steel, helmet); using (Pen p = new Pen(Color.FromArgb(14, 22, 42), 5)) g.DrawPath(p, helmet); }
            PointF[] visor = { new PointF(37, 62), new PointF(93, 62), new PointF(89, 83), new PointF(66, 91), new PointF(36, 79) };
            using (SolidBrush b = new SolidBrush(Color.FromArgb(24, 37, 59))) g.FillPolygon(b, visor);
            using (Pen glow = new Pen(Color.FromArgb(67, 224, 247), 5)) { glow.StartCap = glow.EndCap = LineCap.Round; g.DrawLine(glow, 44, 70, 59, 72); g.DrawLine(glow, 74, 72, 87, 69); }
            using (Pen light = new Pen(Color.FromArgb(240, 204, 103), 5)) { light.LineJoin = LineJoin.Round; g.DrawLines(light, new[] { new PointF(64, 38), new PointF(66, 59), new PointF(66, 100) }); }
            using (Pen p = new Pen(Color.FromArgb(205, 226, 240), 3)) { g.DrawLine(p, 45, 87, 55, 94); g.DrawLine(p, 78, 95, 87, 88); }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 230, 147))) g.FillEllipse(b, 59, 47, 13, 13);
            g.Restore(state);
        }
        internal static GraphicsPath Rounded(float x, float y, float w, float h, float r)
        { GraphicsPath p = new GraphicsPath(); float d = r * 2; p.AddArc(x, y, d, d, 180, 90); p.AddArc(x + w - d, y, d, d, 270, 90); p.AddArc(x + w - d, y + h - d, d, d, 0, 90); p.AddArc(x, y + h - d, d, d, 90, 90); p.CloseFigure(); return p; }
        internal static void ExportIcon(string path)
        {
            LoadHelmet(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), "Assets"));
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 }; List<byte[]> images = new List<byte[]>();
            foreach (int size in sizes)
                using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(bmp))
                using (MemoryStream stream = new MemoryStream())
                { DrawHelmet(g, new Rectangle(0, 0, size, size)); bmp.Save(stream, ImageFormat.Png); images.Add(stream.ToArray()); }
            using (FileStream file = File.Create(path)) using (BinaryWriter writer = new BinaryWriter(file))
            {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length); int offset = 6 + sizes.Length * 16;
                for (int i = 0; i < sizes.Length; i++) { writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length; }
                foreach (byte[] image in images) writer.Write(image);
            }
        }
    }
}
