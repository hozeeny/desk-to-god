using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace NetBunny
{
    internal sealed class AdventureRenderState
    {
        internal string Kind = "Hero", ThemeCode = "OTHER", Phase = "run", WeaponId = "spear", Label = "", MonsterKind = "skeleton";
        internal double Time, PhaseTime, MoveSpeed = 100, HealthRatio = 1, GlowProgress = -1, AttackProgress, Opacity = 1, Cycle = Double.NaN, DeathProgress, HitFlash;
        internal bool FacingRight = true;
        internal int Level = 1, QueueNumber;
        internal int HealthCurrent = -1, HealthMaximum = -1;
        internal Size Size;
        internal double DiskFreeRatio = -1;
        internal string DiskLabel;
        internal bool DiskCritical;
        internal string GrowthLabel;
        internal double GrowthRatio = -1;
    }

    // Every joint is evaluated from a continuous cycle. There is no finite running sprite frame list.
    // The caller owns each returned PArgb bitmap; Present transfers that ownership to the layered window.
    internal sealed class AdventureRenderer : IDisposable
    {
        private static readonly Color Ink = Color.FromArgb(33, 39, 59), Gold = Color.FromArgb(255, 206, 108);
        private readonly Font tiny = new Font("Microsoft YaHei UI", 8.0f, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font caption = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font big = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel);
        private Bitmap buildings;
        private readonly RasterActorRenderer raster;
        private readonly Rectangle[] buildingCells = new Rectangle[6];
        private readonly Bitmap[] buildingCache = new Bitmap[6];
        private bool disposed;
        internal AdventureRenderer(string assetDirectory)
        {
            raster = new RasterActorRenderer(assetDirectory);
            if (!String.IsNullOrEmpty(assetDirectory))
            {
                try { raster.Validate(delegate(bool ok, string detail) { if (!ok) throw new InvalidDataException("角色素材不完整，请重新解压完整安装包。" + Environment.NewLine + detail); }); }
                catch { raster.Dispose(); tiny.Dispose(); caption.Dispose(); big.Dispose(); throw; }
            }
            string path = Path.Combine(assetDirectory ?? "", "world-buildings-v2.png");
            if (!File.Exists(path)) path = Path.Combine(assetDirectory ?? "", "world-buildings.png");
            try
            {
                if (File.Exists(path))
                {
                    using (Image input = Image.FromFile(path)) buildings = new Bitmap(input);
                    for (int i = 0; i < 6; i++)
                    {
                        Rectangle source = buildingCells[i] = AlphaBounds(buildings, new Rectangle(i % 3 * buildings.Width / 3, i / 3 * buildings.Height / 2, buildings.Width / 3, buildings.Height / 2));
                        float scale = Math.Min(1, 320f / Math.Max(source.Width, source.Height));
                        Bitmap cell = new Bitmap(Math.Max(1, (int)Math.Ceiling(source.Width * scale)), Math.Max(1, (int)Math.Ceiling(source.Height * scale)), PixelFormat.Format32bppPArgb); buildingCache[i] = cell;
                        using (Graphics g = Graphics.FromImage(cell)) { g.CompositingMode = CompositingMode.SourceCopy; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(buildings, new Rectangle(0, 0, cell.Width, cell.Height), source, GraphicsUnit.Pixel); }
                    }
                }
            }
            catch { if (buildings != null) buildings.Dispose(); buildings = null; for (int i = 0; i < buildingCache.Length; i++) { if (buildingCache[i] != null) buildingCache[i].Dispose(); buildingCache[i] = null; } }
        }
        internal Bitmap Render(AdventureRenderState s)
        {
            if (disposed) throw new ObjectDisposedException("AdventureRenderer");
            if (s == null) throw new ArgumentNullException("s");
            Size size = s.Size.Width > 0 && s.Size.Height > 0 ? s.Size : RecommendedSize(s.Kind);
            size = new Size(Math.Max(32, Math.Min(1024, size.Width)), Math.Max(32, Math.Min(1024, size.Height)));
            Bitmap frame = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(frame))
            {
                g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias; g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                string kind = (s.Kind ?? "Hero").ToLowerInvariant();
                bool diskLandmark = (kind == "landmark" || kind == "castle") && !String.IsNullOrEmpty(s.DiskLabel);
                bool growthLandmark = (kind == "landmark" || kind == "castle") && !String.IsNullOrEmpty(s.GrowthLabel);
                g.ScaleTransform(size.Width / 220f, size.Height / (growthLandmark ? 280f : diskLandmark ? 250f : 220f));
                if (kind == "landmark" || kind == "castle") DrawLandmark(g, s);
                else if (kind == "evilboss") DrawEvilBoss(g, s);
                else if (kind == "skeleton" || kind == "monster" || kind == "boss") DrawSkeleton(g, s, kind == "boss");
                else if (kind == "effect") DrawEffect(g, s);
                else if (kind == "loot") DrawLoot(g, s);
                else DrawHero(g, s);
            }
            if (s.Opacity >= .999) return frame;
            Bitmap faded = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(faded)) using (ImageAttributes attrs = new ImageAttributes())
            { ColorMatrix matrix = new ColorMatrix(); matrix.Matrix33 = (float)Clamp(s.Opacity); attrs.SetColorMatrix(matrix); g.DrawImage(frame, new Rectangle(Point.Empty, size), 0, 0, size.Width, size.Height, GraphicsUnit.Pixel, attrs); }
            frame.Dispose(); return faded;
        }
        internal static Size RecommendedSize(string kind)
        { switch ((kind ?? "hero").ToLowerInvariant()) { case "skeleton": return new Size(130, 154); case "boss": return new Size(200, 220); case "landmark": case "castle": return new Size(220, 240); case "effect": return new Size(260, 220); default: return new Size(220, 220); } }
        private static Rectangle AlphaBounds(Bitmap image, Rectangle cell)
        {
            int minX = cell.Right, minY = cell.Bottom, maxX = cell.Left, maxY = cell.Top;
            for (int y = cell.Top; y < cell.Bottom; y += 2) for (int x = cell.Left; x < cell.Right; x += 2)
                if (image.GetPixel(x, y).A > 20) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
            return minX >= maxX || minY >= maxY ? cell : Rectangle.Intersect(cell, Rectangle.FromLTRB(minX - 3, minY - 3, maxX + 4, maxY + 4));
        }
        private void DrawHero(Graphics g, AdventureRenderState s)
        {
            string phase = (s.Phase ?? "run").ToLowerInvariant();
            double cycle = Double.IsNaN(s.Cycle) ? s.Time * (.65 + Math.Min(2.6, Math.Abs(s.MoveSpeed) / 90)) : s.Cycle;
            double a = cycle * Math.PI * 2, wave = Math.Sin(a), opposite = Math.Sin(a + Math.PI);
            bool running = phase == "run" || phase == "running";
            bool pant = phase == "pant" || phase == "panting" || phase == "idle" || phase == "rest";
            bool attack = phase == "attack" || s.AttackProgress > .03;
            bool grave = phase == "grave" || phase == "tombstone" || phase == "dead";
            bool dying = phase == "dying" || phase == "death";
            bool revive = phase == "revive" || phase == "reviving";
            double death = Clamp(s.DeathProgress > 0 ? s.DeathProgress : s.PhaseTime / 3.4);
            if (grave) { DrawGrave(g, s, 1); return; }
            if (dying && death > .7) { DrawGrave(g, s, (death - .7) / .3); return; }
            if (raster.HasRig(s.ThemeCode))
            {
                Shadow(g, 57, 182, 113, 13, 45);
                double rasterGlow = Clamp(s.GlowProgress > 1 ? s.GlowProgress / 4.5 : s.GlowProgress);
                if (s.Level > 1 || rasterGlow > 0) DrawAura(g, s, rasterGlow);
                raster.DrawHero(g, s);
                if (pant)
                {
                    GraphicsState breathState = g.Save(); if (!s.FacingRight) { g.TranslateTransform(220, 0); g.ScaleTransform(-1, 1); } DrawPant(g, s); g.Restore(breathState);
                }
                if (attack) DrawAttack(g, s); DrawHeroStatus(g, s); return;
            }
            double bob = running ? -3.5 * Math.Cos(a * 2) : pant ? Math.Sin(s.Time * 6) * 2 : 0;
            double lean = running ? 10 + wave * 1.3 : pant ? 18 : attack ? -9 + Math.Sin(s.AttackProgress * Math.PI) * 22 : 0;
            Shadow(g, 63, 175, 104, 15, 45);
            double glow = Clamp(s.GlowProgress > 1 ? s.GlowProgress / 4.5 : s.GlowProgress);
            if (s.Level > 1 || glow > 0) DrawAura(g, s, glow);
            GraphicsState original = g.Save();
            if (!s.FacingRight) { g.TranslateTransform(220, 0); g.ScaleTransform(-1, 1); }
            if (dying) { g.TranslateTransform(111, 175); g.RotateTransform((float)(-82 * death / .7)); g.TranslateTransform(-111, -175); }
            if (revive)
            {
                double p = Clamp(s.DeathProgress > 0 ? s.DeathProgress : s.PhaseTime / 2.4);
                DrawEarth(g, 110, 184); g.SetClip(new RectangleF(0, 0, 220, 184)); g.TranslateTransform(0, (float)(60 * (1 - p)));
            }
            GraphicsState body = g.Save();
            if (attack) g.TranslateTransform(-8, 0);
            g.TranslateTransform(110, (float)(136 + bob)); g.RotateTransform((float)lean); g.TranslateTransform(-110, -136);
            DrawCape(g, s, Math.Sin(a - .6), running, pant);
            Color outfit = ThemeColor(s.ThemeCode), light = Light(outfit, .36);
            double rear = running ? opposite : pant ? -.15 : -.08, front = running ? wave : pant ? .2 : .1;
            DrawLeg(g, s, 104, 139, rear, running ? Math.Max(0, Math.Cos(a)) : 0, true, outfit);
            DrawArm(g, s, new PointF(98, 110), new PointF((float)(88 - rear * 14), (float)(132 + Math.Abs(rear) * 3)), new PointF((float)(91 - rear * 17), (float)(147 - rear * 8)), true);
            DrawTorso(g, s, outfit, light, pant);
            DrawLeg(g, s, 117, 140, front, running ? Math.Max(0, -Math.Cos(a)) : 0, false, outfit);
            GraphicsState head = g.Save(); g.TranslateTransform(109, 89); g.RotateTransform((float)(running ? -lean * .5 + wave : pant ? 7 + Math.Sin(s.Time * 6) * 2 : -lean * .6)); g.TranslateTransform(-109, -89);
            DrawHead(g, s, wave); g.Restore(head);
            PointF elbow = new PointF((float)(130 + (attack ? Math.Sin(s.AttackProgress * Math.PI) * 11 : -front * 5)), (float)(128 - (attack ? 9 : front * 6)));
            PointF hand = new PointF((float)(146 + (attack ? Math.Sin(s.AttackProgress * Math.PI) * 19 : -front * 6)), (float)(126 + (pant ? 10 : 0) - (attack ? 9 : front * 4)));
            GraphicsState heldWeapon = g.Save(); g.TranslateTransform(hand.X, hand.Y); g.ScaleTransform(.78f, .78f);
            DrawWeapon(g, s.WeaponId, 0, 0, (float)(attack ? -35 + 60 * s.AttackProgress : running ? -9 + wave * 3 : pant ? 17 : -12), s.Time, s.AttackProgress); g.Restore(heldWeapon);
            DrawArm(g, s, new PointF(123, 109), elbow, hand, false);
            if (s.HitFlash > 0) { using (Pen hit = new Pen(Color.FromArgb((int)(Clamp(s.HitFlash * 4) * 170), Color.White), 4)) g.DrawEllipse(hit, 75, 53, 73, 109); }
            if (pant) DrawPant(g, s);
            g.Restore(body); g.Restore(original);
            if (attack) DrawAttack(g, s);
            DrawHeroStatus(g, s);
        }
        private static Color ThemeColor(string theme)
        { switch ((theme ?? "OTHER").ToUpperInvariant()) { case "CN": return Color.FromArgb(85, 177, 184); case "JP": return Color.FromArgb(181, 67, 80); case "US": return Color.FromArgb(167, 109, 65); case "TW": return Color.FromArgb(100, 180, 78); case "HK": return Color.FromArgb(54, 66, 89); default: return Color.FromArgb(118, 156, 184); } }
        private static Color Light(Color c, double mix) { return Color.FromArgb((int)(c.R + (255 - c.R) * mix), (int)(c.G + (255 - c.G) * mix), (int)(c.B + (255 - c.B) * mix)); }
        private static Color Dark(Color c, double mix) { return Color.FromArgb((int)(c.R * mix), (int)(c.G * mix), (int)(c.B * mix)); }
        private void DrawCape(Graphics g, AdventureRenderState s, double wave, bool run, bool pant)
        {
            string theme = (s.ThemeCode ?? "OTHER").ToUpperInvariant(); if (theme == "TW" || theme == "HK") return;
            Color cape = theme == "CN" ? Color.FromArgb(224, 250, 246) : theme == "JP" ? Color.FromArgb(65, 44, 70) : theme == "US" ? Color.FromArgb(194, 80, 53) : Color.FromArgb(199, 62, 95);
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddBezier(97, 103, 73, 110, (float)(57 - wave * 7), (float)(142 + wave * 6), run ? 42 : 66, run ? 133 : 155);
                p.AddBezier(run ? 42 : 66, run ? 133 : 155, 63, 163, 76, 158, 90, 151); p.AddLine(90, 151, 109, 111); p.CloseFigure(); FillPath(g, p, cape, Dark(cape, .66));
                using (Pen fold = new Pen(Color.FromArgb(100, Color.White), 2)) g.DrawBezier(fold, 96, 109, 82, 122, 76, 144, run ? 51 : 74, run ? 137 : 153);
            }
        }
        private void DrawLeg(Graphics g, AdventureRenderState s, float hipX, float hipY, double phase, double lift, bool back, Color outfit)
        {
            string theme = (s.ThemeCode ?? "OTHER").ToUpperInvariant();
            PointF hip = new PointF(hipX, hipY), knee = new PointF((float)(hipX + phase * 20), (float)(hipY + 18 - lift * 5));
            PointF ankle = new PointF((float)(hipX - phase * 20), (float)(hipY + 35 - lift * 18));
            Color cloth = theme == "OTHER" ? Color.FromArgb(50, 66, 90) : Dark(outfit, .7);
            Limb(g, hip, knee, back ? 13 : 15, Dark(cloth, back ? .78 : 1)); Limb(g, knee, ankle, back ? 12 : 13, back ? Dark(outfit, .78) : outfit);
            if (theme == "OTHER" || theme == "JP")
            { Ellipse(g, knee.X - 7, knee.Y - 6, 14, 13, back ? Dark(outfit, .85) : Light(outfit, .4), Ink, 2); Line(g, Light(outfit, .55), 2, knee.X - 3, knee.Y + 7, ankle.X - 2, ankle.Y - 4); }
            if (theme == "TW")
            { Shape(g, new[] { new PointF(ankle.X - 6, ankle.Y - 3), new PointF(ankle.X + 8, ankle.Y - 4), new PointF(ankle.X + 20, ankle.Y + 5), new PointF(ankle.X + 6, ankle.Y + 8), new PointF(ankle.X - 7, ankle.Y + 6) }, Color.FromArgb(148, 204, 83), Ink, 2); }
            else
            {
                using (GraphicsPath boot = new GraphicsPath()) { boot.AddBezier(ankle.X - 7, ankle.Y - 8, ankle.X + 5, ankle.Y - 10, ankle.X + 5, ankle.Y - 1, ankle.X + 11, ankle.Y); boot.AddBezier(ankle.X + 11, ankle.Y, ankle.X + 19, ankle.Y + 5, ankle.X + 15, ankle.Y + 9, ankle.X - 8, ankle.Y + 6); boot.CloseFigure(); FillPath(g, boot, theme == "OTHER" ? Light(outfit, .12) : Color.FromArgb(46, 45, 55), Ink); }
                Line(g, Color.FromArgb(220, 229, 227), 2, ankle.X - 5, ankle.Y + 4, ankle.X + 10, ankle.Y + 5);
            }
        }
        private void DrawTorso(Graphics g, AdventureRenderState s, Color outfit, Color light, bool pant)
        {
            string theme = (s.ThemeCode ?? "OTHER").ToUpperInvariant();
            using (GraphicsPath body = new GraphicsPath()) { body.AddBezier(97, 103, 109, 99, 121, 101, 128, 110); body.AddLine(128, 110, 125, 143); body.AddBezier(125, 143, 110, 150, 94, 144, 94, 141); body.CloseFigure(); FillPath(g, body, light, Dark(outfit, .65)); }
            if (theme == "OTHER")
            {
                Shape(g, new[] { new PointF(99, 112), new PointF(111, 117), new PointF(124, 110), new PointF(119, 134), new PointF(111, 139), new PointF(100, 132) }, Light(outfit, .55), Ink, 2);
                Line(g, Gold, 3, 110, 117, 111, 134); Ellipse(g, 106, 119, 10, 10, Color.FromArgb(91, 224, 235), Dark(outfit, .45), 2);
                Line(g, Gold, 3, 96, 140, 125, 140);
            }
            else if (theme == "CN")
            {
                Shape(g, new[] { new PointF(99, 104), new PointF(115, 122), new PointF(121, 105), new PointF(126, 110), new PointF(113, 136), new PointF(94, 112) }, Color.FromArgb(226, 250, 246), Ink, 1.5f);
                Line(g, Color.FromArgb(255, 211, 112), 5, 96, 137, 124, 138); Line(g, Color.FromArgb(48, 118, 129), 4, 116, 139, 130, 153);
                Ellipse(g, 119, 137, 7, 10, Color.FromArgb(93, 235, 195), Ink, 1);
            }
            else if (theme == "JP")
            { for (int i = 0; i < 4; i++) { RectangleFill(g, 96, 112 + i * 7, 30, 5, i % 2 == 0 ? Color.FromArgb(88, 44, 60) : outfit, Ink, 1); } Line(g, Gold, 3, 100, 110, 103, 137); Line(g, Gold, 3, 119, 110, 121, 138); }
            else if (theme == "US")
            {
                Shape(g, new[] { new PointF(99, 104), new PointF(108, 110), new PointF(108, 137), new PointF(95, 140) }, Color.FromArgb(93, 65, 54), Ink, 1);
                Shape(g, new[] { new PointF(120, 104), new PointF(112, 110), new PointF(112, 137), new PointF(127, 140) }, Color.FromArgb(93, 65, 54), Ink, 1);
                DrawStar(g, 120, 118, 5, Gold); Shape(g, new[] { new PointF(103, 105), new PointF(120, 105), new PointF(115, 119) }, Color.FromArgb(231, 90, 72), Ink, 1);
            }
            else if (theme == "TW")
            { Ellipse(g, 99, 111, 24, 31, Color.FromArgb(219, 229, 132), Dark(outfit, .8), 1.5f); Ellipse(g, 113, 122, 3, 3, Color.FromArgb(129, 155, 66), Color.Transparent, 0); }
            else
            {
                Shape(g, new[] { new PointF(99, 103), new PointF(110, 110), new PointF(121, 104), new PointF(115, 139), new PointF(109, 138) }, Color.FromArgb(230, 232, 234), Ink, 1);
                Line(g, Gold, 2, 105, 112, 116, 117); Ellipse(g, 110, 115, 6, 6, Gold, Ink, 1);
                Line(g, Color.FromArgb(73, 87, 111), 3, 97, 112, 104, 121); Line(g, Color.FromArgb(73, 87, 111), 3, 125, 112, 117, 123);
            }
            RectangleFill(g, 95, 138, 31, 6, theme == "TW" ? Color.FromArgb(117, 85, 55) : Color.FromArgb(47, 44, 55), Ink, 1.5f); RectangleFill(g, 108, 138, 8, 6, Gold, Ink, 1);
        }
        private void DrawArm(Graphics g, AdventureRenderState s, PointF shoulder, PointF elbow, PointF hand, bool back)
        {
            string theme = (s.ThemeCode ?? "OTHER").ToUpperInvariant(); Color outfit = ThemeColor(theme), skin = theme == "TW" ? Light(outfit, .13) : Color.FromArgb(249, 197, 155);
            Color arm = back ? Dark(outfit, .76) : outfit;
            Limb(g, shoulder, elbow, theme == "CN" ? 16 : 12, arm); Limb(g, elbow, hand, theme == "CN" ? 13 : 10, theme == "US" || theme == "HK" ? skin : Light(arm, .12));
            if (theme == "OTHER" || theme == "JP") { Ellipse(g, shoulder.X - 10, shoulder.Y - 8, 20, 16, Light(arm, .4), Ink, 2); Line(g, Gold, 2, shoulder.X - 6, shoulder.Y - 4, shoulder.X + 5, shoulder.Y - 4); }
            Ellipse(g, hand.X - 6, hand.Y - 6, 12, 12, theme == "OTHER" ? Light(outfit, .35) : skin, Ink, 2);
        }
        private void DrawHead(Graphics g, AdventureRenderState s, double wave)
        {
            string theme = (s.ThemeCode ?? "OTHER").ToUpperInvariant(); Color skin = Color.FromArgb(255, 208, 165);
            if (theme == "OTHER")
            {
                using (GraphicsPath plume = new GraphicsPath()) { plume.AddBezier(107, 60, 92, 35, (float)(65 - wave * 4), (float)(43 + wave * 5), 66, 60); plume.AddBezier(66, 60, 78, 51, 92, 72, 107, 68); plume.CloseFigure(); FillPath(g, plume, Color.FromArgb(250, 100, 119), Color.FromArgb(132, 43, 80)); }
                using (GraphicsPath helmet = new GraphicsPath()) { helmet.AddBezier(84, 83, 82, 48, 119, 45, 133, 66); helmet.AddBezier(133, 66, 141, 83, 135, 97, 117, 105); helmet.AddLine(117, 105, 88, 96); helmet.CloseFigure(); FillPath(g, helmet, Color.FromArgb(222, 241, 251), Color.FromArgb(94, 129, 162)); }
                Shape(g, new[] { new PointF(88, 77), new PointF(137, 75), new PointF(132, 91), new PointF(111, 96), new PointF(88, 88) }, Color.FromArgb(33, 51, 73), Ink, 2);
                Line(g, Color.FromArgb(86, 234, 248), 3.5f, 95, 83, 108, 84); Line(g, Color.FromArgb(86, 234, 248), 3.5f, 119, 84, 131, 81);
                Line(g, Gold, 3, 114, 58, 114, 97); Ellipse(g, 110, 67, 8, 8, Gold, Ink, 1);
                Line(g, Color.FromArgb(245, 252, 255), 2, 93, 65, 103, 58); Line(g, Color.FromArgb(169, 194, 211), 2, 92, 91, 102, 95);
            }
            else if (theme == "TW")
            {
                Ellipse(g, 83, 65, 55, 39, Color.FromArgb(132, 202, 93), Ink, 2.8f); Ellipse(g, 88, 54, 20, 23, Color.FromArgb(143, 216, 102), Ink, 2.5f); Ellipse(g, 116, 54, 20, 23, Color.FromArgb(143, 216, 102), Ink, 2.5f);
                Ellipse(g, 94, 60, 10, 13, Color.FromArgb(255, 246, 199), Ink, 1.5f); Ellipse(g, 122, 60, 10, 13, Color.FromArgb(255, 246, 199), Ink, 1.5f);
                Ellipse(g, 99, 63, 3, 7, Ink, Ink, 0); Ellipse(g, 127, 63, 3, 7, Ink, Ink, 0);
                using (Pen p = new Pen(Ink, 2)) g.DrawBezier(p, 92, 85, 102, 97, 126, 98, 133, 82);
                Ellipse(g, 86, 82, 10, 6, Color.FromArgb(236, 150, 129), Color.Transparent, 0); Ellipse(g, 126, 82, 10, 6, Color.FromArgb(236, 150, 129), Color.Transparent, 0);
                Shape(g, new[] { new PointF(90, 101), new PointF(116, 103), new PointF(103, 114) }, Color.FromArgb(217, 93, 69), Ink, 1.5f);
            }
            else
            {
                Ellipse(g, 86, 59, 49, 47, skin, Ink, 2.7f); Ellipse(g, 83, 82, 9, 13, skin, Ink, 1.8f);
                Ellipse(g, 101, 82, 4, 6, Ink, Ink, 0); Ellipse(g, 123, 80, 4, 6, Ink, Ink, 0); Line(g, Ink, 2, 111, 95, 122, 93);
                Line(g, Ink, 2, 98, 77, 106, 76); Line(g, Ink, 2, 120, 74, 128, 75);
                if (theme == "CN")
                {
                    using (GraphicsPath hair = new GraphicsPath()) { hair.AddBezier(87, 81, 76, 48, 112, 40, 130, 56); hair.AddBezier(130, 56, 148, 69, 130, 86, 127, 78); hair.AddLine(127, 78, 118, 62); hair.AddBezier(118, 62, 103, 77, 90, 68, 87, 81); FillPath(g, hair, Color.FromArgb(48, 64, 79), Ink); }
                    Ellipse(g, 94, 42, 21, 14, Color.FromArgb(49, 67, 82), Ink, 2); Line(g, Gold, 4, 97, 54, 113, 53); Line(g, Color.FromArgb(101, 224, 212), 2.5f, 99, 52, 79, 61); Line(g, Color.FromArgb(101, 224, 212), 2.5f, 99, 52, 82, 51);
                    Line(g, Color.FromArgb(243, 241, 214), 3, 91, 71, 127, 68); Ellipse(g, 108, 67, 6, 6, Color.FromArgb(80, 212, 201), Ink, 1);
                }
                else if (theme == "JP")
                {
                    using (GraphicsPath helm = new GraphicsPath()) { helm.AddBezier(83, 77, 82, 44, 125, 42, 137, 76); helm.AddLine(137, 76, 83, 77); FillPath(g, helm, Color.FromArgb(128, 51, 69), Ink); }
                    Shape(g, new[] { new PointF(85, 77), new PointF(79, 99), new PointF(94, 101), new PointF(97, 80) }, Color.FromArgb(180, 70, 84), Ink, 2); Shape(g, new[] { new PointF(128, 78), new PointF(129, 101), new PointF(141, 98), new PointF(137, 78) }, Color.FromArgb(180, 70, 84), Ink, 2);
                    Line(g, Gold, 3, 86, 74, 136, 74);
                    using (Pen horn = new Pen(Gold, 5)) { horn.StartCap = horn.EndCap = LineCap.Round; g.DrawBezier(horn, 110, 64, 99, 60, 95, 49, 96, 38); g.DrawBezier(horn, 111, 64, 124, 58, 128, 47, 127, 37); }
                    Ellipse(g, 105, 57, 11, 11, Gold, Ink, 1.5f);
                }
                else if (theme == "US")
                {
                    Shape(g, new[] { new PointF(88, 67), new PointF(90, 44), new PointF(103, 49), new PointF(118, 42), new PointF(132, 65) }, Color.FromArgb(163, 109, 65), Ink, 2.5f);
                    using (GraphicsPath brim = new GraphicsPath()) { brim.AddBezier(73, 65, 94, 70, 129, 55, 149, 62); brim.AddBezier(149, 62, 147, 76, 87, 85, 73, 65); FillPath(g, brim, Color.FromArgb(207, 155, 93), Ink); }
                    Line(g, Color.FromArgb(94, 62, 47), 4, 92, 63, 129, 58); DrawStar(g, 110, 59, 5, Gold);
                    Shape(g, new[] { new PointF(107, 90), new PointF(116, 86), new PointF(126, 90), new PointF(117, 92), new PointF(108, 94) }, Color.FromArgb(102, 64, 44), Color.Transparent, 0);
                }
                else
                {
                    using (GraphicsPath hair = new GraphicsPath()) { hair.AddBezier(86, 80, 76, 53, 97, 44, 110, 51); hair.AddBezier(110, 51, 125, 37, 140, 57, 133, 73); hair.AddBezier(133, 73, 116, 57, 100, 76, 86, 80); FillPath(g, hair, Color.FromArgb(38, 44, 57), Ink); }
                    RectangleFill(g, 95, 78, 15, 10, Color.FromArgb(32, 40, 52), Ink, 1.5f); RectangleFill(g, 116, 76, 17, 10, Color.FromArgb(32, 40, 52), Ink, 1.5f); Line(g, Ink, 2, 109, 80, 117, 79); Line(g, Color.FromArgb(139, 192, 207), 1.5f, 99, 80, 106, 80); Line(g, Color.FromArgb(139, 192, 207), 1.5f, 120, 78, 128, 78);
                }
            }
        }
        private void DrawWeapon(Graphics g, string id, float x, float y, float angle, double time, double attack)
        {
            if (raster.DrawWeapon(g, id, x, y, angle, time, attack)) return;
            GraphicsState save = g.Save(); g.TranslateTransform(x, y); g.RotateTransform(angle);
            string weapon = (id ?? "spear").ToLowerInvariant();
            if (weapon == "greatsword")
            {
                Line(g, Ink, 8, -15, 14, 10, -8); Line(g, Color.FromArgb(120, 78, 73), 4, -15, 14, 10, -8);
                Shape(g, new[] { new PointF(8, -12), new PointF(47, -59), new PointF(64, -67), new PointF(60, -48), new PointF(23, -1) }, Color.FromArgb(173, 224, 245), Ink, 3);
                Line(g, Color.White, 3, 20, -10, 58, -58); Line(g, Color.FromArgb(125, 99, 237), 4, 24, -13, 51, -48); Line(g, Gold, 7, 1, -14, 26, 9);
            }
            else if (weapon == "laser" || weapon == "automaton" || weapon == "auto")
            {
                RectangleFill(g, -8, -13, 55, 22, Color.FromArgb(71, 86, 119), Ink, 2.5f); RectangleFill(g, 40, -8, 18, 12, Color.FromArgb(164, 206, 217), Ink, 2); RectangleFill(g, 0, 4, 14, 18, Color.FromArgb(64, 66, 90), Ink, 2);
                Ellipse(g, 5, -10, 17, 14, weapon == "laser" ? Color.FromArgb(104, 240, 241) : Color.FromArgb(255, 201, 99), Ink, 2);
                Line(g, Color.FromArgb(228, 250, 255), 2, 27, -7, 39, -7); Line(g, Color.FromArgb(228, 250, 255), 2, 27, -2, 39, -2);
                if (weapon != "laser") { RectangleFill(g, 21, -24, 16, 10, Color.FromArgb(139, 165, 190), Ink, 2); Ellipse(g, 26, -22, 6, 6, Color.FromArgb(245, 105, 98), Ink, 1); }
            }
            else if (weapon == "boomerang")
            {
                Shape(g, new[] { new PointF(-10, 3), new PointF(18, -36), new PointF(47, -14), new PointF(23, -20), new PointF(2, 10) }, Color.FromArgb(255, 205, 101), Ink, 3);
                Line(g, Color.FromArgb(242, 251, 249), 3, 0, 0, 19, -29); Line(g, Color.FromArgb(122, 100, 225), 4, 13, -15, 20, -25);
            }
            else if (weapon == "blackhole")
            {
                Line(g, Ink, 7, -13, 28, 21, -21); Line(g, Color.FromArgb(104, 87, 149), 4, -13, 28, 21, -21);
                for (int n = 4; n >= 1; n--) Ellipse(g, 25 - n * 7, -35 - n * 5, n * 14, n * 10, Color.FromArgb(17, 150, 104, 255), Color.Transparent, 0);
                Ellipse(g, 10, -49, 32, 28, Color.FromArgb(16, 12, 38), Color.FromArgb(184, 125, 251), 4);
                using (Pen orbit = new Pen(Color.FromArgb(241, 188, 255), 2)) g.DrawArc(orbit, 2, -40, 46, 11, (float)(time * 130), 270);
                DrawStar(g, 22, -35, 4, Color.FromArgb(180, 131, 249));
            }
            else if (weapon == "wave" || weapon == "shockwave")
            {
                Line(g, Ink, 8, -17, 21, 38, -37); Line(g, Color.FromArgb(113, 112, 178), 4, -17, 21, 38, -37);
                Shape(g, new[] { new PointF(26, -39), new PointF(50, -52), new PointF(53, -28), new PointF(38, -19) }, Color.FromArgb(117, 236, 230), Ink, 2.5f);
                DrawStar(g, 42, -36, 8, Color.FromArgb(230, 255, 253));
            }
            else
            {
                Line(g, Ink, 6, -60, 31, 57, -39); Line(g, Color.FromArgb(165, 118, 79), 3.5f, -60, 31, 57, -39);
                Shape(g, new[] { new PointF(48, -42), new PointF(78, -55), new PointF(66, -29), new PointF(57, -39) }, Color.FromArgb(220, 248, 249), Ink, 2.3f);
                Line(g, Color.FromArgb(81, 215, 238), 2, 57, -40, 72, -49); Line(g, Gold, 5, 48, -34, 54, -27);
                using (GraphicsPath pennant = new GraphicsPath()) { pennant.AddBezier(46, -30, 38, -14, 20, -22, 24, -2); pennant.AddBezier(24, -2, 42, -8, 50, -9, 51, -27); pennant.CloseFigure(); FillPath(g, pennant, Color.FromArgb(232, 84, 113), Color.FromArgb(142, 43, 71)); }
            }
            g.Restore(save);
        }
        private void DrawAttack(Graphics g, AdventureRenderState s)
        {
            double p = Clamp(s.AttackProgress), wave = Math.Sin(p * Math.PI); if (wave <= .01) return;
            GraphicsState save = g.Save(); if (!s.FacingRight) { g.TranslateTransform(220, 0); g.ScaleTransform(-1, 1); }
            string w = s.WeaponId ?? "spear"; Color color = w == "blackhole" ? Color.FromArgb(174, 120, 255) : w == "greatsword" ? Color.FromArgb(249, 198, 105) : Color.FromArgb(111, 233, 247);
            if (w == "laser" || w == "automaton")
            { Line(g, Color.FromArgb((int)(wave * 100), color), 13, 167, 113, 207, 106); Line(g, Color.FromArgb((int)(wave * 240), Color.White), 3, 167, 113, 207, 106); }
            else if (w == "blackhole")
            { for (int i = 0; i < 4; i++) using (Pen pen = new Pen(Color.FromArgb((int)(wave * (130 - i * 20)), color), 3)) g.DrawArc(pen, 125 - i * 6, 69 - i * 7, 62 + i * 11, 63 + i * 11, (float)(s.Time * 250 + i * 60), 230); }
            else if (w == "boomerang")
            { using (Pen trail = new Pen(Color.FromArgb((int)(wave * 180), Gold), 4)) g.DrawArc(trail, 78, 65, 125, 110, -80, (float)(wave * 280)); }
            else
            { for (int i = 2; i >= 0; i--) using (Pen pen = new Pen(Color.FromArgb((int)(wave * (i == 0 ? 220 : 70)), i == 0 ? Color.White : color), 3 + i * 5)) g.DrawArc(pen, 101 - i * 3, 47 - i * 3, 100 + i * 6, 125 + i * 4, -80, (float)(wave * 155)); }
            DrawStar(g, 192, 113, (float)(7 * wave), Color.White); g.Restore(save);
        }
        private void DrawPant(Graphics g, AdventureRenderState s)
        {
            double pulse = (Math.Sin(s.Time * 6) + 1) * .5;
            using (Pen breath = new Pen(Color.FromArgb(160, 227, 246, 250), 2))
            { g.DrawBezier(breath, 145, 95, 151, 88, 156, 101, (float)(161 + pulse * 6), 94); g.DrawBezier(breath, 149, 104, 158, 99, 161, 111, (float)(169 + pulse * 7), 104); }
            using (GraphicsPath drop = new GraphicsPath()) { drop.AddBezier(78, 72, 66, 87, 79, 95, 83, 84); drop.AddBezier(83, 84, 81, 79, 78, 72, 78, 72); FillPath(g, drop, Color.FromArgb(132, 226, 245), Color.FromArgb(54, 119, 155)); }
        }
        private void DrawAura(Graphics g, AdventureRenderState s, double glow)
        {
            float strength = (float)(.12 + Math.Min(15, s.Level) * .012 + glow * .55);
            for (int i = 0; i < 7; i++)
            {
                Color c = Hue(i / 7.0 + s.Time * .09); float w = 72 + i * 13, h = 118 + i * 9;
                using (Pen aura = new Pen(Color.FromArgb(Math.Min(150, (int)(strength * (110 - i * 8))), c), 5 - i * .35f)) g.DrawEllipse(aura, 110 - w / 2, 117 - h / 2, w, h);
                double a = s.Time * (1 + i * .05) + i * Math.PI * 2 / 7;
                if (glow > .02) DrawStar(g, (float)(110 + Math.Cos(a) * (55 + i * 2)), (float)(113 + Math.Sin(a) * 76), (float)(2.5 + glow * 2), Color.FromArgb((int)(90 + glow * 165), c));
            }
            if (glow > .05) DrawLabel(g, "LEVEL UP!", 110, 19, Color.FromArgb(255, 229, 137), big);
        }
        private void DrawHeroStatus(Graphics g, AdventureRenderState s)
        {
            if (s.HealthRatio < .999 || s.QueueNumber > 0) HealthBar(g, 78, 193, 66, 5, s.HealthRatio, Color.FromArgb(103, 230, 172));
            if (!String.IsNullOrEmpty(s.Label)) DrawLabel(g, s.Label, 110, 202, Color.White, caption);
        }
        private void DrawGrave(Graphics g, AdventureRenderState s, double fade)
        {
            if (raster.DrawGrave(g, s, fade)) { DrawLabel(g, String.IsNullOrEmpty(s.Label) ? "右键 · 复活" : s.Label, 110, 205, Color.FromArgb(235, 239, 249), caption); return; }
            Shadow(g, 62, 178, 111, 17, 55); DrawEarth(g, 110, 185);
            using (GraphicsPath stone = new GraphicsPath()) { stone.AddLine(78, 179, 78, 112); stone.AddBezier(78, 112, 78, 79, 138, 77, 143, 110); stone.AddLine(143, 110, 146, 179); stone.CloseFigure(); FillPath(g, stone, Color.FromArgb(157, 178, 196), Color.FromArgb(79, 94, 120)); }
            Line(g, Color.FromArgb(205, 220, 231), 3, 85, 113, 85, 167); Line(g, Color.FromArgb(80, 92, 118), 6, 112, 109, 112, 145); Line(g, Color.FromArgb(80, 92, 118), 6, 98, 123, 127, 123);
            Line(g, Color.FromArgb(79, 94, 120), 2, 129, 151, 122, 162); Line(g, Color.FromArgb(79, 94, 120), 2, 122, 162, 129, 168);
            RectangleFill(g, 70, 176, 83, 10, Color.FromArgb(91, 112, 133), Ink, 2.5f);
            double floatY = Math.Sin(s.Time * 2.5) * 5; float ghostY = (float)(40 + floatY - Math.Min(22, s.PhaseTime * 7));
            using (GraphicsPath ghost = new GraphicsPath()) { ghost.AddBezier(89, ghostY + 23, 82, ghostY - 6, 127, ghostY - 8, 130, ghostY + 17); ghost.AddLine(130, ghostY + 17, 135, ghostY + 37); ghost.AddLine(135, ghostY + 37, 123, ghostY + 32); ghost.AddLine(123, ghostY + 32, 112, ghostY + 40); ghost.AddLine(112, ghostY + 40, 101, ghostY + 32); ghost.AddLine(101, ghostY + 32, 88, ghostY + 38); ghost.CloseFigure(); using (SolidBrush b = new SolidBrush(Color.FromArgb(190, 216, 248, 255))) g.FillPath(b, ghost); using (Pen p = new Pen(Color.FromArgb(200, 124, 194, 218), 2)) g.DrawPath(p, ghost); }
            Ellipse(g, 100, ghostY + 12, 5, 8, Color.FromArgb(83, 130, 159), Color.Transparent, 0); Ellipse(g, 117, ghostY + 12, 5, 8, Color.FromArgb(83, 130, 159), Color.Transparent, 0);
            using (Pen halo = new Pen(Color.FromArgb(214, 255, 221, 118), 2)) g.DrawEllipse(halo, 97, ghostY - 13, 26, 7);
            DrawStar(g, 72, (float)(75 - Math.Sin(s.Time) * 14), 4, Color.FromArgb(180, 203, 243, 255));
            DrawLabel(g, String.IsNullOrEmpty(s.Label) ? "右键 · 复活" : s.Label, 110, 200, Color.FromArgb(235, 239, 249), caption);
        }
        private static void DrawEarth(Graphics g, float x, float y)
        { Ellipse(g, x - 50, y - 11, 100, 15, Color.FromArgb(84, 65, 72), Ink, 2); for (int i = 0; i < 7; i++) Ellipse(g, x - 45 + i * 13, y - 6 - i % 2 * 3, 14, 8, i % 2 == 0 ? Color.FromArgb(138, 110, 103) : Color.FromArgb(106, 85, 90), Ink, 1); }
        private void DrawEvilBoss(Graphics g, AdventureRenderState s)
        {
            bool dying = s.Phase == "shatter" || s.Phase == "defeated";
            float alpha = (float)(dying ? 1-Clamp(s.DeathProgress) : 1);
            if (alpha <= 0) return;
            using (Bitmap body = new Bitmap(220,220,PixelFormat.Format32bppPArgb))
            {
                using (Graphics pose = Graphics.FromImage(body))
                {
                    pose.SmoothingMode=SmoothingMode.AntiAlias; pose.InterpolationMode=InterpolationMode.HighQualityBicubic;
                    raster.DrawHero(pose,s);
                }
                using (Pen aura = new Pen(Color.FromArgb((int)(alpha*145),203,43,91), 4))
                { g.DrawEllipse(aura,35,36,151,158); g.DrawArc(aura,24,100,172,83,(float)(s.Time*80),245); }
                using (ImageAttributes tint = new ImageAttributes())
                {
                    ColorMatrix matrix = new ColorMatrix(new float[][] {
                        new float[]{.63f,0,0,0,0}, new float[]{0,.36f,0,0,0}, new float[]{0,0,.70f,0,0},
                        new float[]{0,0,0,alpha,0},new float[]{.11f,.015f,.08f,0,1}});
                    tint.SetColorMatrix(matrix);
                    g.DrawImage(body,new Rectangle(0,(int)(dying?s.DeathProgress*16:0),220,220),0,0,220,220,GraphicsUnit.Pixel,tint);
                }
            }
            if (!dying)
            {
                DrawStar(g,110,37,8,Color.FromArgb(235,255,67,104));
                MonsterHealth(g,s,true,26);
                DrawLabel(g,String.IsNullOrEmpty(s.Label)?"邪恶分身 BOSS":s.Label,110,13,Color.FromArgb(255,144,173),caption);
                if(s.AttackProgress>0) DrawAttack(g,s);
            }
        }
        private void DrawSkeleton(Graphics g, AdventureRenderState s, bool boss)
        {
            string phase = (s.Phase ?? "walk").ToLowerInvariant();
            bool shatter = phase == "defeated" || phase == "shatter" || phase == "dead" || phase == "vanish";
            double death = Clamp(s.DeathProgress);
            if (shatter && death >= 1) return;
            Shadow(g, 54, 181, 109, 14, (int)(44 * (shatter ? 1 - death : 1)));
            if (raster.DrawMonster(g, s, boss))
            {
                string monsterPhase = (s.Phase ?? "walk").ToLowerInvariant();
                if (monsterPhase != "defeated" && monsterPhase != "shatter" && monsterPhase != "dead" && monsterPhase != "vanish")
                {
                    MonsterHealth(g, s, boss, 26);
                    if (!String.IsNullOrEmpty(s.Label)) DrawLabel(g, s.Label, 110, 13, Color.FromArgb(255, 235, 186), caption);
                }
                return;
            }
            double t = s.Time, walk = Math.Sin(t * (boss ? 4 : 6)), bounce = Math.Cos(t * 12) * 2;
            GraphicsState save = g.Save();
            if (!s.FacingRight) { g.TranslateTransform(220, 0); g.ScaleTransform(-1, 1); }
            if (!boss) { g.TranslateTransform(13, 20); g.ScaleTransform(.88f, .88f); }
            else g.TranslateTransform(0, 14);
            g.TranslateTransform(0, (float)bounce);
            Color bone = s.HitFlash > 0 ? Color.White : Color.FromArgb(239, 230, 205), shadowBone = Color.FromArgb(185, 178, 165);
            if (shatter)
            {
                double p = death;
                for (int i = 0; i < 12; i++) { double angle = i * 2.399; float x = (float)(110 + Math.Cos(angle) * (15 + p * 80)), y = (float)(120 + Math.Sin(angle) * (20 + p * 55) + p * p * 22); Limb(g, new PointF(x, y), new PointF(x + (float)Math.Cos(angle) * 13, y + (float)Math.Sin(angle) * 13), 6, bone); }
                DrawStar(g, 110, 111, 12, Gold); g.Restore(save); return;
            }
            if (boss)
            {
                using (GraphicsPath cloak = new GraphicsPath()) { cloak.AddBezier(79, 92, 54, 126, 67, 155, 48, 178); cloak.AddLine(48, 178, 159, 178); cloak.AddBezier(159, 178, 143, 131, 147, 108, 130, 92); cloak.CloseFigure(); FillPath(g, cloak, Color.FromArgb(98, 55, 112), Color.FromArgb(48, 33, 65)); }
            }
            PointF hip = new PointF(108, 144), kneeA = new PointF((float)(94 + walk * 11), 161), footA = new PointF((float)(91 - walk * 14), (float)(183 - Math.Max(0, -walk) * 9));
            PointF kneeB = new PointF((float)(124 - walk * 11), 160), footB = new PointF((float)(129 + walk * 14), (float)(182 - Math.Max(0, walk) * 9));
            Limb(g, hip, kneeA, 7, shadowBone); Limb(g, kneeA, footA, 7, shadowBone); Limb(g, hip, kneeB, 8, bone); Limb(g, kneeB, footB, 8, bone);
            Line(g, Ink, 9, footA.X - 5, footA.Y, footA.X + 13, footA.Y); Line(g, bone, 5, footA.X - 5, footA.Y, footA.X + 13, footA.Y);
            Line(g, Ink, 10, footB.X - 4, footB.Y, footB.X + 15, footB.Y); Line(g, bone, 6, footB.X - 4, footB.Y, footB.X + 15, footB.Y);
            Limb(g, new PointF(92, 106), new PointF((float)(78 + walk * 6), 128), 7, shadowBone); Limb(g, new PointF((float)(78 + walk * 6), 128), new PointF((float)(86 + walk * 9), 143), 7, shadowBone);
            Line(g, Ink, 10, 108, 105, 108, 144); Line(g, bone, 6, 108, 105, 108, 144);
            for (int i = 0; i < 4; i++)
            {
                using (Pen edge = new Pen(Ink, 7)) g.DrawArc(edge, 91 + i, 108 + i * 6, 36 - i * 2, 14, 4, 172);
                using (Pen rib = new Pen(bone, 4)) g.DrawArc(rib, 91 + i, 108 + i * 6, 36 - i * 2, 14, 4, 172);
            }
            Shape(g, new[] { new PointF(96, 137), new PointF(107, 141), new PointF(121, 137), new PointF(117, 152), new PointF(109, 147), new PointF(100, 153) }, bone, Ink, 2);
            DrawSkull(g, 83, 52, boss ? 59 : 56, boss ? 55 : 51, bone, boss);
            if (boss)
            {
                Shape(g, new[] { new PointF(83, 61), new PointF(75, 38), new PointF(90, 50), new PointF(104, 38), new PointF(119, 48), new PointF(140, 34), new PointF(136, 64) }, Color.FromArgb(91, 74, 118), Ink, 3);
                Ellipse(g, 106, 48, 11, 14, Color.FromArgb(239, 103, 189), Ink, 2);
                Shape(g, new[] { new PointF(83, 102), new PointF(71, 105), new PointF(66, 119), new PointF(88, 122), new PointF(96, 108) }, Color.FromArgb(97, 90, 124), Ink, 2.5f);
                Shape(g, new[] { new PointF(129, 102), new PointF(144, 103), new PointF(152, 118), new PointF(130, 124), new PointF(120, 111) }, Color.FromArgb(97, 90, 124), Ink, 2.5f);
                Line(g, Gold, 2, 73, 114, 85, 116); Line(g, Gold, 2, 133, 116, 145, 113);
            }
            PointF elbow = new PointF((float)(138 - walk * 8), 125), hand = new PointF((float)(151 - walk * 5), 131);
            Limb(g, new PointF(124, 107), elbow, 8, bone); Limb(g, elbow, hand, 8, bone);
            GraphicsState weapon = g.Save(); g.TranslateTransform(hand.X, hand.Y); g.RotateTransform((float)(walk * 10));
            Line(g, Ink, 7, -3, 21, 11, -31); Line(g, Color.FromArgb(144, 114, 102), 4, -3, 21, 11, -31);
            if (boss) Shape(g, new[] { new PointF(9, -29), new PointF(12, -55), new PointF(40, -43), new PointF(37, -20) }, Color.FromArgb(187, 141, 199), Ink, 3);
            else { Limb(g, new PointF(10, -29), new PointF(20, -43), 10, bone); Ellipse(g, 7, -36, 11, 9, bone, Ink, 2); Ellipse(g, 14, -48, 13, 10, bone, Ink, 2); }
            g.Restore(weapon); g.Restore(save);
            if (s.QueueNumber > 0)
            { Ellipse(g, 176, 41, 27, 27, Color.FromArgb(255, 210, 112), Ink, 2); DrawTextCenter(g, s.QueueNumber.ToString(), new RectangleF(176, 42, 27, 25), Ink, big); }
            MonsterHealth(g, s, boss, 35);
            if (boss) DrawLabel(g, String.IsNullOrEmpty(s.Label) ? "内存压力 BOSS" : s.Label, 110, 13, Color.FromArgb(255, 213, 231), caption);
            else if (!String.IsNullOrEmpty(s.Label)) DrawLabel(g, s.Label, 110, 13, Color.White, big);
        }
        private static void DrawSkull(Graphics g, float x, float y, float w, float h, Color bone, bool boss)
        {
            using (GraphicsPath skull = new GraphicsPath()) { skull.AddBezier(x, y + h * .55f, x - 5, y - 9, x + w + 6, y - 9, x + w, y + h * .55f); skull.AddLine(x + w, y + h * .55f, x + w - 9, y + h * .73f); skull.AddLine(x + w - 9, y + h * .73f, x + w - 11, y + h); skull.AddLine(x + w - 11, y + h, x + 14, y + h); skull.AddLine(x + 14, y + h, x + 10, y + h * .76f); skull.CloseFigure(); FillPath(g, skull, bone, Color.FromArgb(179, 174, 157)); }
            Ellipse(g, x + 8, y + h * .34f, 16, 17, Ink, Ink, 1); Ellipse(g, x + w - 24, y + h * .31f, 16, 17, Ink, Ink, 1);
            Ellipse(g, x + 13, y + h * .43f, 5, 6, boss ? Color.FromArgb(255, 97, 171) : Color.FromArgb(133, 232, 195), Color.Transparent, 0); Ellipse(g, x + w - 18, y + h * .4f, 5, 6, boss ? Color.FromArgb(255, 97, 171) : Color.FromArgb(133, 232, 195), Color.Transparent, 0);
            Shape(g, new[] { new PointF(x + w * .5f, y + h * .58f), new PointF(x + w * .43f, y + h * .73f), new PointF(x + w * .59f, y + h * .72f) }, Ink, Ink, 1);
            for (int i = 0; i < 3; i++) Line(g, Ink, 1.8f, x + 20 + i * 8, y + h - 10, x + 20 + i * 8, y + h);
            if (boss) { Line(g, Ink, 4, x + 6, y + 14, x + 23, y + 21); Line(g, Ink, 4, x + w - 6, y + 14, x + w - 22, y + 21); }
        }
        private void DrawLandmark(Graphics g, AdventureRenderState s)
        {
            string theme = (s.ThemeCode ?? "OTHER").ToUpperInvariant(); int index = Array.IndexOf(new[] { "OTHER", "CN", "JP", "US", "TW", "HK" }, theme); if (index < 0) index = 0;
            Shadow(g, 24, 184, 170, 18, 58);
            if (buildings != null)
            {
                Rectangle cell = buildingCells[index]; float scale = Math.Min(202f / cell.Width, 184f / cell.Height); float w = cell.Width * scale, h = cell.Height * scale;
                Bitmap cached = buildingCache[index]; g.DrawImage(cached, new RectangleF(110 - w / 2, 195 - h, w, h), new Rectangle(0, 0, cached.Width, cached.Height), GraphicsUnit.Pixel);
            }
            else DrawVectorLandmark(g, theme);
            for (int i = 0; i < 3; i++) { double a = s.Time * .7 + i * 2.1; DrawStar(g, (float)(110 + Math.Cos(a) * 77), (float)(89 + Math.Sin(a) * 54), 2.8f, Color.FromArgb(160, theme == "JP" ? Color.FromArgb(255, 184, 197) : Color.FromArgb(185, 157, 242))); }
            DrawLabel(g, String.IsNullOrEmpty(s.Label) ? new[] { "幽影古堡", "九霄妖塔", "月影神龛", "自由之光", "旧日小屋", "九龙城寨" }[index] : s.Label, 110, 208, Color.FromArgb(249, 230, 185), caption);
            bool hasGrowth = !String.IsNullOrEmpty(s.GrowthLabel);
            if (hasGrowth)
            {
                Color growthColor = s.GrowthRatio >= 1 ? Color.FromArgb(255, 201, 113) : Color.FromArgb(193, 172, 255);
                DrawLabel(g, s.GrowthLabel, 110, 225, growthColor, caption);
                HealthBar(g, 29, 241, 162, 5, s.GrowthRatio < 0 ? 0 : s.GrowthRatio, growthColor);
            }
            if (!String.IsNullOrEmpty(s.DiskLabel))
            {
                Color diskColor = s.DiskCritical ? Color.FromArgb(255, 113, 128) : Color.FromArgb(133, 235, 196);
                DrawLabel(g, s.DiskLabel, 110, hasGrowth ? 255 : 225, diskColor, caption);
                HealthBar(g, 29, hasGrowth ? 271 : 241, 162, 5, s.DiskFreeRatio < 0 ? 0 : s.DiskFreeRatio, diskColor);
            }
        }
        private void DrawVectorLandmark(Graphics g, string theme)
        {
            // Complete vector landmarks are available offline and while the optional illustrated atlas is loading.
            Ellipse(g, 28, 172, 162, 27, Color.FromArgb(91, 77, 101), Ink, 2);
            if (theme == "CN")
            {
                for (int tier = 0; tier < 4; tier++)
                {
                    float y = 143 - tier * 32, w = 115 - tier * 17, x = 110 - w / 2;
                    RectangleFill(g, x + 13, y - 8, w - 26, 39, Color.FromArgb(108, 74, 105), Ink, 2);
                    Shape(g, new[] { new PointF(x - 13, y + 4), new PointF(x + 5, y - 6), new PointF(110, y - 20), new PointF(x + w - 5, y - 6), new PointF(x + w + 13, y + 4), new PointF(x + w - 3, y + 8), new PointF(x + 3, y + 8) }, Color.FromArgb(63, 97, 105), Ink, 2.5f);
                    RectangleFill(g, 103, y + 11, 15, 16, Color.FromArgb(255, 191, 101), Ink, 1.5f); Line(g, Gold, 2, x - 8, y + 4, x + w + 8, y + 4);
                }
                Line(g, Gold, 3, 110, 23, 110, 8); Ellipse(g, 106, 6, 8, 8, Gold, Ink, 1);
            }
            else if (theme == "JP")
            {
                RectangleFill(g, 69, 101, 86, 69, Color.FromArgb(134, 88, 93), Ink, 2); Shape(g, new[] { new PointF(53, 110), new PointF(109, 61), new PointF(169, 110), new PointF(152, 118), new PointF(109, 84), new PointF(69, 117) }, Color.FromArgb(70, 81, 97), Ink, 3);
                RectangleFill(g, 93, 128, 36, 44, Color.FromArgb(80, 52, 67), Ink, 2); Line(g, Gold, 2, 111, 133, 111, 169);
                RectangleFill(g, 46, 88, 13, 98, Color.FromArgb(213, 80, 72), Ink, 2); RectangleFill(g, 164, 88, 13, 98, Color.FromArgb(213, 80, 72), Ink, 2);
                RectangleFill(g, 39, 102, 145, 11, Color.FromArgb(219, 84, 72), Ink, 2);
                Shape(g, new[] { new PointF(28, 72), new PointF(61, 80), new PointF(159, 80), new PointF(193, 72), new PointF(184, 88), new PointF(36, 88) }, Color.FromArgb(222, 86, 74), Ink, 3);
                RectangleFill(g, 29, 69, 163, 7, Color.FromArgb(64, 66, 83), Ink, 2); Ellipse(g, 105, 112, 11, 15, Gold, Ink, 1);
            }
            else if (theme == "US")
            {
                RectangleFill(g, 66, 163, 92, 22, Color.FromArgb(176, 161, 151), Ink, 2); RectangleFill(g, 78, 139, 68, 25, Color.FromArgb(195, 181, 165), Ink, 2);
                Shape(g, new[] { new PointF(91, 143), new PointF(96, 83), new PointF(125, 81), new PointF(137, 145) }, Color.FromArgb(111, 185, 165), Ink, 2.5f);
                Line(g, Color.FromArgb(174, 221, 195), 4, 108, 92, 102, 139); Line(g, Color.FromArgb(71, 143, 133), 3, 119, 98, 127, 137);
                Limb(g, new PointF(100, 86), new PointF(79, 57), 10, Color.FromArgb(114, 187, 167)); Limb(g, new PointF(79, 57), new PointF(76, 33), 9, Color.FromArgb(114, 187, 167));
                RectangleFill(g, 69, 27, 15, 12, Color.FromArgb(135, 181, 158), Ink, 2); Shape(g, new[] { new PointF(71, 25), new PointF(75, 7), new PointF(82, 17), new PointF(80, 27) }, Gold, Ink, 2);
                Ellipse(g, 99, 55, 27, 30, Color.FromArgb(132, 199, 175), Ink, 2); Shape(g, new[] { new PointF(99, 63), new PointF(93, 46), new PointF(104, 54), new PointF(108, 39), new PointF(114, 54), new PointF(126, 43), new PointF(126, 64) }, Color.FromArgb(124, 190, 167), Ink, 2);
                RectangleFill(g, 125, 92, 16, 24, Color.FromArgb(143, 203, 178), Ink, 2); Line(g, Ink, 2, 109, 68, 112, 68); Line(g, Ink, 2, 119, 68, 121, 68);
            }
            else if (theme == "TW")
            {
                Shape(g, new[] { new PointF(53, 183), new PointF(58, 99), new PointF(159, 101), new PointF(169, 184) }, Color.FromArgb(162, 139, 118), Ink, 3);
                Shape(g, new[] { new PointF(39, 110), new PointF(101, 56), new PointF(174, 103), new PointF(169, 116), new PointF(107, 77), new PointF(53, 123) }, Color.FromArgb(100, 87, 92), Ink, 3);
                for (int i = 0; i < 6; i++) Line(g, Color.FromArgb(119, 103, 103), 2, 57 + i * 16, 104 - i * 3, 70 + i * 14, 112);
                RectangleFill(g, 115, 127, 29, 56, Color.FromArgb(73, 66, 69), Ink, 2); RectangleFill(g, 70, 128, 28, 27, Color.FromArgb(65, 71, 79), Ink, 2);
                Line(g, Color.FromArgb(164, 127, 91), 7, 67, 149, 101, 133); Line(g, Ink, 2, 152, 119, 146, 134); Line(g, Ink, 2, 146, 134, 155, 146); Line(g, Ink, 2, 155, 146, 151, 163);
                Ellipse(g, 41, 169, 35, 21, Color.FromArgb(91, 141, 85), Ink, 2); Ellipse(g, 152, 169, 34, 21, Color.FromArgb(96, 150, 89), Ink, 2);
                Line(g, Color.FromArgb(193, 174, 145), 7, 126, 136, 126, 177);
            }
            else if (theme == "HK")
            {
                int[] heights = { 99, 140, 119, 156, 108 }; Color[] tones = { Color.FromArgb(131, 139, 139), Color.FromArgb(161, 130, 120), Color.FromArgb(116, 138, 144), Color.FromArgb(151, 145, 126), Color.FromArgb(119, 133, 133) };
                for (int col = 0; col < 5; col++)
                {
                    float x = 39 + col * 29, top = 182 - heights[col]; RectangleFill(g, x, top, 34, heights[col], tones[col], Ink, 2);
                    for (int row = 0; row < heights[col] / 19 - 1; row++) for (int win = 0; win < 2; win++) RectangleFill(g, x + 6 + win * 14, top + 10 + row * 18, 8, 11, (row + col + win) % 3 == 0 ? Color.FromArgb(247, 201, 130) : Color.FromArgb(63, 78, 88), Ink, .7f);
                    Line(g, Ink, 2, x + 27, top + 8, x + 27, 179); if (col % 2 == 0) RectangleFill(g, x + 3, top - 8, 19, 9, Color.FromArgb(130, 149, 150), Ink, 2);
                }
                using (Pen wire = new Pen(Ink, 1.4f)) { g.DrawBezier(wire, 38, 103, 91, 130, 143, 102, 181, 128); g.DrawBezier(wire, 49, 143, 96, 157, 147, 140, 178, 152); }
                RectangleFill(g, 91, 87, 13, 49, Color.FromArgb(204, 79, 95), Ink, 1.5f); Line(g, Gold, 2, 97, 95, 97, 127);
            }
            else
            {
                RectangleFill(g, 65, 110, 93, 72, Color.FromArgb(89, 83, 117), Ink, 3);
                RectangleFill(g, 43, 87, 39, 96, Color.FromArgb(110, 101, 139), Ink, 2.5f); RectangleFill(g, 145, 87, 33, 96, Color.FromArgb(110, 101, 139), Ink, 2.5f); RectangleFill(g, 94, 64, 41, 104, Color.FromArgb(115, 102, 141), Ink, 2.5f);
                Shape(g, new[] { new PointF(35, 92), new PointF(62, 39), new PointF(87, 92) }, Color.FromArgb(87, 57, 103), Ink, 3); Shape(g, new[] { new PointF(136, 91), new PointF(159, 46), new PointF(185, 91) }, Color.FromArgb(87, 57, 103), Ink, 3); Shape(g, new[] { new PointF(83, 67), new PointF(113, 17), new PointF(144, 67) }, Color.FromArgb(105, 61, 115), Ink, 3);
                Line(g, Gold, 3, 113, 16, 113, 6); Shape(g, new[] { new PointF(114, 7), new PointF(138, 12), new PointF(115, 20) }, Color.FromArgb(216, 85, 123), Ink, 1);
                for (int i = 0; i < 3; i++) RectangleFill(g, 101, 79 + i * 22, 20, 12, Color.FromArgb(255, 190, 107), Ink, 1.5f);
                RectangleFill(g, 54, 111, 15, 24, Color.FromArgb(239, 168, 113), Ink, 1.5f); RectangleFill(g, 154, 113, 14, 23, Color.FromArgb(239, 168, 113), Ink, 1.5f);
                using (GraphicsPath arch = new GraphicsPath()) { arch.AddArc(94, 140, 35, 33, 180, 180); arch.AddLine(129, 156, 129, 183); arch.AddLine(129, 183, 94, 183); arch.CloseFigure(); FillPath(g, arch, Color.FromArgb(44, 39, 65), Ink); }
                for (int i = 0; i < 4; i++) Line(g, Color.FromArgb(161, 129, 119), 2, 99 + i * 8, 155, 99 + i * 8, 181);
            }
        }
        private void DrawLoot(Graphics g, AdventureRenderState s)
        {
            double t = s.Time;
            for (int i = 4; i > 0; i--) Ellipse(g, 110 - i * 17, 108 - i * 15, i * 34, i * 30, Color.FromArgb(15 + i * 3, 255, 207, 112), Color.Transparent, 0);
            using (Pen orbit = new Pen(Color.FromArgb(130, 255, 221, 141), 2)) g.DrawEllipse(orbit, 53, 156, 113, 14);
            DrawWeapon(g, s.WeaponId, 101, (float)(107 + Math.Sin(t * 3) * 7), -12, t, 0);
            for (int i = 0; i < 6; i++) { double a = t * 1.2 + i * Math.PI / 3; DrawStar(g, (float)(110 + Math.Cos(a) * 65), (float)(108 + Math.Sin(a) * 56), 3 + i % 2, Gold); }
            DrawLabel(g, String.IsNullOrEmpty(s.Label) ? "获得新装备" : s.Label, 110, 192, Gold, caption);
        }
        private void DrawEffect(Graphics g, AdventureRenderState s)
        {
            double p = Clamp(s.AttackProgress); if (p <= 0) p = .5;
            if (s.WeaponId == "blackhole")
            {
                for (int i = 5; i > 0; i--) Ellipse(g, 110 - i * 16, 110 - i * 11, i * 32, i * 22, Color.FromArgb(15, 150, 100, 235), Color.Transparent, 0);
                Ellipse(g, 72, 82, 76, 56, Color.FromArgb(15, 12, 30), Color.FromArgb(173, 124, 238), 5);
                for (int i = 0; i < 7; i++) using (Pen ring = new Pen(Hue(i / 7.0 + .6), 2)) g.DrawArc(ring, 52 - i * 3, 91 - i * 6, 116 + i * 6, 37 + i * 12, (float)(s.Time * 150 + i * 47), 135);
            }
            else if (s.WeaponId == "laser" || s.WeaponId == "automaton")
            { Line(g, Color.FromArgb(65, 104, 226, 244), 22, 15, 111, 204, 111); Line(g, Color.FromArgb(203, 117, 234, 250), 8, 15, 111, 204, 111); Line(g, Color.White, 3, 17, 111, 204, 111); DrawStar(g, 196, 111, 12, Color.White); }
            else if (s.WeaponId == "boomerang")
            { using (Pen trail = new Pen(Color.FromArgb(100, Gold), 6)) g.DrawArc(trail, 22, 43, 173, 132, 20, 280); DrawWeapon(g, "boomerang", 116, 111, (float)(s.Time * 500), s.Time, p); }
            else
            { for (int i = 0; i < 4; i++) using (Pen wave = new Pen(Color.FromArgb(220 - i * 43, i == 0 ? Color.White : Color.FromArgb(104, 230, 239)), 5 - i * .7f)) g.DrawArc(wave, 17 + i * 21, 25 + i * 12, 122, 161 - i * 20, -74, 148); }
        }
        internal Bitmap RenderWeaponIcon(string id, int size)
        { Bitmap image = new Bitmap(size, size, PixelFormat.Format32bppPArgb); using (Graphics g = Graphics.FromImage(image)) { g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic; if (!raster.DrawWeaponIcon(g, id, new RectangleF(size * .08f, size * .08f, size * .84f, size * .84f))) { g.ScaleTransform(size / 110f, size / 110f); g.TranslateTransform(55, 59); g.ScaleTransform(.68f, .68f); DrawWeapon(g, id, 0, 0, 12, .4, 0); } } return image; }
        private static void HealthBar(Graphics g, float x, float y, float w, float h, double health, Color fill)
        {
            using (GraphicsPath bg = KnightBrand.Rounded(x, y, w, h, h / 2)) using (SolidBrush b = new SolidBrush(Color.FromArgb(210, 32, 37, 54))) g.FillPath(b, bg);
            float width = (float)(Math.Max(0, Math.Min(1, health)) * (w - 2));
            if (width <= 0) return;
            // The final window can be smaller than this 220-unit canvas. Preserve at
            // least one opaque device pixel for any positive HP, including 1/900.
            float scale; using (Matrix transform = g.Transform) { float[] m = transform.Elements; scale = (float)Math.Sqrt(m[0] * m[0] + m[1] * m[1]); }
            width = Math.Min(w - 2, Math.Max(width, 1.5f / Math.Max(.01f, scale)));
            using (SolidBrush b = new SolidBrush(fill)) g.FillRectangle(b, x + 1, y + 1, width, Math.Max(1, h - 2));
        }
        private void MonsterHealth(Graphics g, AdventureRenderState s, bool boss, float y)
        {
            bool exact = s.HealthCurrent >= 0 && s.HealthMaximum > 0;
            double ratio = exact ? s.HealthCurrent / (double)s.HealthMaximum : s.HealthRatio;
            HealthBar(g, boss ? 56 : 72, y, boss ? 108 : 77, boss ? 7 : 5, ratio, boss ? Color.FromArgb(237, 98, 169) : Color.FromArgb(240, 176, 102));
            if (exact && ratio <= .25)
                DrawTextCenter(g, "HP " + Math.Max(0, s.HealthCurrent) + "/" + s.HealthMaximum, new RectangleF(49, y + (boss ? 8 : 6), 122, 14), Color.FromArgb(255, 238, 200), caption);
        }
        private void DrawLabel(Graphics g, string text, float centerX, float y, Color color, Font font)
        {
            if (String.IsNullOrEmpty(text)) return; if (text.Length > 25) text = text.Substring(0, 24) + "…";
            SizeF measured = g.MeasureString(text, font); float width = Math.Min(213, measured.Width + 12), height = measured.Height + 4;
            using (GraphicsPath p = KnightBrand.Rounded(centerX - width / 2, y - height / 2, width, height, 6)) using (SolidBrush bg = new SolidBrush(Color.FromArgb(204, 33, 39, 59))) g.FillPath(bg, p);
            DrawTextCenter(g, text, new RectangleF(centerX - width / 2, y - height / 2, width, height), color, font);
        }
        private static void DrawTextCenter(Graphics g, string text, RectangleF rect, Color color, Font font)
        { using (SolidBrush brush = new SolidBrush(color)) using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap }) g.DrawString(text, font, brush, rect, format); }
        private static void Shadow(Graphics g, float x, float y, float w, float h, int opacity)
        { Ellipse(g, x, y, w, h, Color.FromArgb(opacity, 24, 29, 46), Color.Transparent, 0); }
        private static void Limb(Graphics g, PointF a, PointF b, float width, Color color)
        { using (Pen edge = new Pen(Ink, width + 4)) { edge.StartCap = edge.EndCap = LineCap.Round; g.DrawLine(edge, a, b); } using (Pen limb = new Pen(color, width)) { limb.StartCap = limb.EndCap = LineCap.Round; g.DrawLine(limb, a, b); } }
        private static void FillPath(Graphics g, GraphicsPath path, Color top, Color bottom)
        { RectangleF bounds = path.GetBounds(); if (bounds.Width <= 0 || bounds.Height <= 0) return; using (LinearGradientBrush b = new LinearGradientBrush(bounds, top, bottom, 70)) g.FillPath(b, path); using (Pen p = new Pen(Ink, 2.5f)) { p.LineJoin = LineJoin.Round; g.DrawPath(p, path); } }
        private static void Ellipse(Graphics g, float x, float y, float w, float h, Color fill, Color stroke, float width)
        { using (SolidBrush b = new SolidBrush(fill)) g.FillEllipse(b, x, y, w, h); if (width > 0) using (Pen p = new Pen(stroke, width)) g.DrawEllipse(p, x, y, w, h); }
        private static void RectangleFill(Graphics g, float x, float y, float w, float h, Color fill, Color stroke, float width)
        { using (SolidBrush b = new SolidBrush(fill)) g.FillRectangle(b, x, y, w, h); if (width > 0) using (Pen p = new Pen(stroke, width)) g.DrawRectangle(p, x, y, w, h); }
        private static void Shape(Graphics g, PointF[] points, Color fill, Color stroke, float width)
        { using (SolidBrush b = new SolidBrush(fill)) g.FillPolygon(b, points); if (width > 0) using (Pen p = new Pen(stroke, width)) { p.LineJoin = LineJoin.Round; g.DrawPolygon(p, points); } }
        private static void Line(Graphics g, Color c, float w, float x1, float y1, float x2, float y2)
        { using (Pen p = new Pen(c, w)) { p.StartCap = p.EndCap = LineCap.Round; g.DrawLine(p, x1, y1, x2, y2); } }
        private static void DrawStar(Graphics g, float cx, float cy, float radius, Color color)
        { if (radius <= 0) return; PointF[] p = new PointF[8]; for (int i = 0; i < 8; i++) { double a = -Math.PI / 2 + i * Math.PI / 4, r = i % 2 == 0 ? radius : radius * .28; p[i] = new PointF(cx + (float)(Math.Cos(a) * r), cy + (float)(Math.Sin(a) * r)); } Shape(g, p, color, Color.Transparent, 0); }
        private static Color Hue(double h)
        { h = (h % 1 + 1) % 1 * 6; int i = (int)h; double f = h - i; int down = (int)(255 * (1 - f)), up = (int)(255 * f); switch (i) { case 0: return Color.FromArgb(255, up, 95); case 1: return Color.FromArgb(down, 255, 95); case 2: return Color.FromArgb(95, 255, up); case 3: return Color.FromArgb(95, down, 255); case 4: return Color.FromArgb(up, 95, 255); default: return Color.FromArgb(255, 95, down); } }
        private static double Clamp(double value) { return Double.IsNaN(value) ? 0 : Math.Max(0, Math.Min(1, value)); }
        public void Dispose() { if (disposed) return; disposed = true; if (buildings != null) buildings.Dispose(); foreach (Bitmap cell in buildingCache) if (cell != null) cell.Dispose(); raster.Dispose(); tiny.Dispose(); caption.Dispose(); big.Dispose(); }
        internal static void ExportPreview(string assetDirectory, string path)
        {
            string[] themes = { "OTHER", "CN", "JP", "US", "TW", "HK" }, names = { "铠甲骑士", "剑修", "日本武士", "牛仔", "青蛙", "黑帮小弟" };
            using (AdventureRenderer renderer = new AdventureRenderer(assetDirectory)) using (Bitmap sheet = new Bitmap(1440, 1330, PixelFormat.Format32bppArgb)) using (Graphics g = Graphics.FromImage(sheet)) using (Font title = new Font("Microsoft YaHei UI", 24, FontStyle.Bold, GraphicsUnit.Pixel)) using (Font label = new Font("Microsoft YaHei UI", 16, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.Clear(Color.FromArgb(21, 28, 46)); g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush white = new SolidBrush(Color.FromArgb(239, 244, 251))) g.DrawString("骑士助手  /  桌面冒险图鉴", title, white, 34, 20);
                using (SolidBrush sub = new SolidBrush(Color.FromArgb(151, 174, 198))) g.DrawString("手绘质感分件 · 连续关节动画 · 六种角色 · 五种怪物随机出没 · 自主规划 / 一键清场", label, sub, 36, 59);
                for (int i = 0; i < 6; i++)
                {
                    int x = i * 240;
                    using (GraphicsPath card = KnightBrand.Rounded(x + 12, 100, 216, 465, 18)) using (SolidBrush b = new SolidBrush(Color.FromArgb(31, 42, 64))) g.FillPath(b, card);
                    DrawTextCenter(g, names[i], new RectangleF(x + 10, 110, 220, 28), Color.FromArgb(240, 224, 190), label);
                    using (Bitmap hero = renderer.Render(new AdventureRenderState { ThemeCode = themes[i], Phase = "run", Cycle = .13 + i * .13, Time = .5, WeaponId = "spear", Size = new Size(220, 220) })) g.DrawImage(hero, x + 10, 145);
                    using (Bitmap castle = renderer.Render(new AdventureRenderState { Kind = "Landmark", ThemeCode = themes[i], Time = 1, Size = new Size(210, 210) })) g.DrawImage(castle, x + 15, 340);
                }
                string[] ids = { "spear", "greatsword", "wave", "laser", "boomerang", "blackhole", "automaton" }, weaponNames = { "星辉长枪", "虹光巨剑", "破空冲击波", "极光镭射枪", "月轮回旋镖", "星渊黑洞", "自动攻击装置" };
                for (int i = 0; i < ids.Length; i++) { int x = 20 + i * 200; using (Bitmap icon = renderer.RenderWeaponIcon(ids[i], 106)) g.DrawImage(icon, x + 47, 592); DrawTextCenter(g, weaponNames[i], new RectangleF(x, 694, 200, 28), Color.FromArgb(220, 232, 246), label); }
                string[] kinds = { "Hero", "Hero", "Hero", "Hero", "Hero", "Hero" }, phases = { "pant", "attack", "grave", "revive", "run", "attack" }, poses = { "低速喘气", "七彩升级 / 攻击", "墓碑与灵魂", "破土复活", "连续跑步", "远程装备" };
                for (int i = 0; i < 6; i++)
                { int x = i * 240; using (Bitmap pose = renderer.Render(new AdventureRenderState { Kind = kinds[i], Phase = phases[i], ThemeCode = "OTHER", Time = 1.2, WeaponId = i == 5 ? "laser" : "greatsword", AttackProgress = i == 1 || i == 5 ? .7 : 0, DeathProgress = .5, GlowProgress = i == 1 ? .8 : 0, Level = i == 1 ? 6 : 1, Size = new Size(220, 220) })) g.DrawImage(pose, x + 10, 755); DrawTextCenter(g, poses[i], new RectangleF(x, 979, 240, 30), Color.FromArgb(220, 232, 246), label); }
                string[] monsterNames = { "骷髅卫兵", "史莱姆", "小恶魔", "夜翼蝙蝠", "蘑菇精", "内存压力 Boss" };
                for (int i = 0; i < RasterActorRenderer.MonsterKinds.Length; i++)
                {
                    int x = i * 240;
                    using (GraphicsPath card = KnightBrand.Rounded(x + 12, 1043, 216, 260, 18)) using (SolidBrush b = new SolidBrush(Color.FromArgb(31, 42, 64))) g.FillPath(b, card);
                    using (Bitmap monster = renderer.Render(new AdventureRenderState { Kind = i == 5 ? "Boss" : "Skeleton", MonsterKind = RasterActorRenderer.MonsterKinds[i], Phase = "walk", Time = .5, Label = i == 5 ? "RAM 88%" : "128 MiB", Size = new Size(220, 220) })) g.DrawImage(monster, x + 10, 1048);
                    DrawTextCenter(g, monsterNames[i], new RectangleF(x, 1267, 240, 30), Color.FromArgb(220, 232, 246), label);
                }
                sheet.Save(path, ImageFormat.Png);
            }
        }
        internal static void ExportAnimationFrames(string assetDirectory, string directory)
        {
            Directory.CreateDirectory(directory);
            using (AdventureRenderer renderer = new AdventureRenderer(assetDirectory))
                for (int i = 0; i < 60; i++) using (Bitmap frame = renderer.Render(new AdventureRenderState { Phase = "run", Cycle = i / 60.0, Time = i / 60.0, Size = new Size(220, 220) })) frame.Save(Path.Combine(directory, "run-" + i.ToString("D3") + ".png"), ImageFormat.Png);
        }
        internal static void ExportRunGif(string assetDirectory, string path)
        {
            // GIF is a preview of runtime-rendered frames. Source illustration PNGs are never rewritten.
            // A 20 ms delay avoids browser clamping of 10 ms GIF delays. Runtime requests
            // 15 ms timer ticks and uses actual elapsed time; displayed FPS is not guaranteed.
            using (AdventureRenderer renderer = new AdventureRenderer(assetDirectory)) using (FileStream file = File.Create(path)) using (BinaryWriter writer = new BinaryWriter(file))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a")); writer.Write((ushort)320); writer.Write((ushort)340); writer.Write((byte)0x70); writer.Write((byte)0); writer.Write((byte)0);
                writer.Write(new byte[] { 0x21, 0xff, 0x0b }); writer.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0")); writer.Write(new byte[] { 3, 1, 0, 0, 0 });
                for (int n = 0; n < 60; n++)
                {
                    using (Bitmap frame = renderer.Render(new AdventureRenderState { ThemeCode = "OTHER", Phase = "run", Cycle = n / 60.0, Time = 0, Size = new Size(320, 320) }))
                    using (Bitmap matte = new Bitmap(320, 340, PixelFormat.Format24bppRgb)) using (Graphics g = Graphics.FromImage(matte)) using (Font font = new Font("Microsoft YaHei UI", 13, FontStyle.Regular, GraphicsUnit.Pixel))
                    {
                        g.Clear(Color.FromArgb(51, 43, 51)); g.DrawImageUnscaled(frame, 0, 0); DrawTextCenter(g, "连续关节跑步 · 60 个不同姿势", new RectangleF(0, 309, 320, 25), Color.FromArgb(190, 211, 234), font); WriteGifFrame(writer, matte);
                    }
                }
                writer.Write((byte)0x3b);
            }
        }
        private static void WriteGifFrame(BinaryWriter writer, Bitmap frame)
        {
            byte[] bytes; using (MemoryStream stream = new MemoryStream()) { frame.Save(stream, ImageFormat.Gif); bytes = stream.ToArray(); }
            int globalSize = (bytes[10] & 0x80) != 0 ? 3 * (1 << ((bytes[10] & 7) + 1)) : 0, offset = 13 + globalSize;
            while (bytes[offset] == 0x21) { offset += 2; while (bytes[offset] != 0) offset += bytes[offset] + 1; offset++; }
            if (bytes[offset] != 0x2c) throw new InvalidDataException("GIF frame image descriptor is missing.");
            int packed = bytes[offset + 9], localSize = (packed & 0x80) != 0 ? 3 * (1 << ((packed & 7) + 1)) : 0;
            writer.Write(new byte[] { 0x21, 0xf9, 4, 4, 2, 0, 0, 0 });
            writer.Write(bytes, offset, 9); writer.Write((byte)((packed & 0x78) | 0x80 | (localSize != 0 ? packed & 7 : bytes[10] & 7)));
            if (localSize != 0) writer.Write(bytes, offset + 10, localSize); else writer.Write(bytes, 13, globalSize);
            int imageData = offset + 10 + localSize, end = imageData + 1;
            while (bytes[end] != 0) end += bytes[end] + 1; end++;
            writer.Write(bytes, imageData, end - imageData);
        }
        private static ulong Hash(Bitmap bitmap)
        { ulong value = 1469598103934665603UL; for (int y = 0; y < bitmap.Height; y += 2) for (int x = 0; x < bitmap.Width; x += 2) unchecked { value ^= (uint)bitmap.GetPixel(x, y).ToArgb(); value *= 1099511628211UL; } return value; }
        internal static string SelfTest()
        {
            int checks = 0; Action<bool, string> check = delegate(bool ok, string name) { if (!ok) throw new InvalidOperationException("Adventure renderer: " + name); checks++; };
            using (AdventureRenderer renderer = new AdventureRenderer(""))
            {
                HashSet<ulong> hashes = new HashSet<ulong>();
                for (int i = 0; i < 60; i++) using (Bitmap b = renderer.Render(new AdventureRenderState { Phase = "run", Cycle = i / 60.0, Time = 0, Size = new Size(220, 220) })) { hashes.Add(Hash(b)); check(b.GetPixel(0, 0).A == 0 && b.PixelFormat == PixelFormat.Format32bppPArgb, "transparent premultiplied frame"); }
                check(hashes.Count == 60, "60 genuinely distinct articulated run poses");
                using (Bitmap zero = renderer.Render(new AdventureRenderState { Cycle = 0, Time = 0 })) using (Bitmap loop = renderer.Render(new AdventureRenderState { Cycle = 1, Time = 0 })) check(Hash(zero) == Hash(loop), "seamless continuous cycle endpoint");
                HashSet<ulong> skins = new HashSet<ulong>(); foreach (string theme in new[] { "OTHER", "CN", "JP", "US", "TW", "HK" }) using (Bitmap b = renderer.Render(new AdventureRenderState { ThemeCode = theme, Time = .2, Cycle = .3 })) skins.Add(Hash(b)); check(skins.Count == 6, "six distinct characters");
                HashSet<ulong> landmarks = new HashSet<ulong>(); foreach (string theme in new[] { "OTHER", "CN", "JP", "US", "TW", "HK" }) using (Bitmap b = renderer.Render(new AdventureRenderState { Kind = "Landmark", ThemeCode = theme, Time = .2 })) landmarks.Add(Hash(b)); check(landmarks.Count == 6, "six distinct offline landmarks");
                HashSet<ulong> weapons = new HashSet<ulong>(); foreach (string id in new[] { "spear", "greatsword", "wave", "laser", "boomerang", "blackhole", "automaton" }) using (Bitmap b = renderer.RenderWeaponIcon(id, 110)) weapons.Add(Hash(b)); check(weapons.Count == 7, "seven distinct equipment silhouettes");
                foreach (string phase in new[] { "run", "pant", "attack", "dying", "grave", "revive" }) using (Bitmap b = renderer.Render(new AdventureRenderState { Phase = phase, Time = 1, DeathProgress = .4, AttackProgress = .7, GlowProgress = .5 })) check(b.Width == 220 && b.Height == 220, "state render " + phase);
                using (Bitmap faded = renderer.Render(new AdventureRenderState { Opacity = 0 })) check(Hash(faded) == HashEmpty(220, 220), "zero opacity is transparent");
                int before = GetGuiResources(Process.GetCurrentProcess().Handle, 0); for (int i = 0; i < 120; i++) using (Bitmap b = renderer.Render(new AdventureRenderState { Kind = i % 2 == 0 ? "Boss" : "Hero", Cycle = i / 60.0, Time = i / 60.0, GlowProgress = .8 })) { }
                int after = GetGuiResources(Process.GetCurrentProcess().Handle, 0); check(after <= before + 3, "render loop releases GDI handles");
            }
            return "Adventure art: " + checks + " assertions passed; 60 distinct smooth run poses, seamless cycle, 6 skins, 6 buildings, 7 weapons, no GDI handle growth.";
        }
        private static ulong HashEmpty(int w, int h) { using (Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppPArgb)) return Hash(b); }
        internal static string AssetsSelfTest(string assetDirectory)
        {
            int checks = 0; Action<bool, string> check = delegate(bool ok, string name) { if (!ok) throw new InvalidOperationException("V5 raster assets: " + name); checks++; };
            using (AdventureRenderer renderer = new AdventureRenderer(assetDirectory))
            {
                renderer.raster.Validate(check);
                HashSet<ulong> skinHashes = new HashSet<ulong>();
                foreach (string theme in RasterActorRenderer.ThemeCodes)
                {
                    HashSet<ulong> poses = new HashSet<ulong>();
                    for (int n = 0; n < 60; n++) using (Bitmap frame = renderer.Render(new AdventureRenderState { ThemeCode = theme, Cycle = n / 60.0, Time = 0 }))
                    { poses.Add(Hash(frame)); check(frame.GetPixel(0, 0).A == 0, theme + " run has transparent border " + n); }
                    check(poses.Count == 60, theme + " has 60 distinct articulated run poses");
                    using (Bitmap begin = renderer.Render(new AdventureRenderState { ThemeCode = theme, Cycle = 0, Time = 0 })) using (Bitmap end = renderer.Render(new AdventureRenderState { ThemeCode = theme, Cycle = 1, Time = 0 })) check(Hash(begin) == Hash(end), theme + " has an exactly seamless run loop");
                    using (Bitmap skin = renderer.Render(new AdventureRenderState { ThemeCode = theme, Cycle = .3, Time = 0 })) skinHashes.Add(Hash(skin));
                    foreach (string weapon in RasterActorRenderer.WeaponIds) foreach (bool facing in new[] { false, true }) foreach (string phase in new[] { "run", "attack", "dying" })
                        using (Bitmap frame = renderer.Render(new AdventureRenderState { ThemeCode = theme, WeaponId = weapon, FacingRight = facing, Phase = phase, Cycle = .25, Time = 0, DeathProgress = .4, AttackProgress = phase == "attack" ? .5 : 0 }))
                            check(EdgesEmpty(frame), theme + " " + weapon + " " + phase + " full silhouette stays inside overlay in " + (facing ? "right" : "left") + " facing");
                }
                check(skinHashes.Count == 6, "six genuinely different raster characters");
                HashSet<ulong> monsterHashes = new HashSet<ulong>();
                foreach (string kind in RasterActorRenderer.MonsterKinds)
                {
                    using (Bitmap frame = renderer.Render(new AdventureRenderState { Kind = kind == "boss" ? "Boss" : "Skeleton", MonsterKind = kind, Time = .2 })) monsterHashes.Add(Hash(frame));
                    using (Bitmap first = renderer.Render(new AdventureRenderState { Kind = "Skeleton", MonsterKind = kind, Time = 0 })) using (Bitmap next = renderer.Render(new AdventureRenderState { Kind = "Skeleton", MonsterKind = kind, Time = .17 })) check(Hash(first) != Hash(next), kind + " animation moves between samples");
                    foreach (string phase in new[] { "walk", "attack", "shatter" }) using (Bitmap frame = renderer.Render(new AdventureRenderState { Kind = kind == "boss" ? "Boss" : "Skeleton", MonsterKind = kind, Phase = phase, Time = .6, DeathProgress = .45, HitFlash = .2 })) check(frame.Width > 100 && frame.Height > 100, kind + " " + phase + " renders");
                    foreach (bool facing in new[] { false, true })
                    using (Bitmap direction = renderer.Render(new AdventureRenderState { Kind = kind == "boss" ? "Boss" : "Skeleton", MonsterKind = kind, FacingRight = facing, Phase = "walk", Time = 0, Size = new Size(220, 220) }))
                    {
                        // Inspect actual eye pixels at the neutral pose, independently of
                        // the atlas-facing lookup. Faces must lead in the travel direction.
                        double eyes = MonsterEyeCenter(direction, kind);
                        check(facing ? eyes > 110.5 : eyes < 108.5, kind + " facial features face " + (facing ? "right" : "left"));
                    }
                    Size healthSize = kind == "boss" ? new Size(190, 220) : new Size(130, 170);
                    using (Bitmap positive = renderer.Render(new AdventureRenderState { Kind = kind == "boss" ? "Boss" : "Skeleton", MonsterKind = kind, HealthRatio = 0, HealthCurrent = 1, HealthMaximum = 900, Size = healthSize }))
                    using (Bitmap zero = renderer.Render(new AdventureRenderState { Kind = kind == "boss" ? "Boss" : "Skeleton", MonsterKind = kind, HealthRatio = 1, HealthCurrent = 0, HealthMaximum = 900, Size = healthSize }))
                    {
                        check(HasMonsterHealthPixel(positive, kind == "boss"), kind + " one HP remains visible at the actual small window size");
                        check(!HasMonsterHealthPixel(zero, kind == "boss"), kind + " zero HP has no positive fill even with a stale ratio");
                    }
                    using (Bitmap initial = renderer.Render(new AdventureRenderState { Kind = kind == "boss" ? "Boss" : "Skeleton", MonsterKind = kind, Phase = "shatter", DeathProgress = 0, AttackProgress = 0 }))
                    using (Bitmap hit = renderer.Render(new AdventureRenderState { Kind = kind == "boss" ? "Boss" : "Skeleton", MonsterKind = kind, Phase = "shatter", DeathProgress = 0, AttackProgress = .95 }))
                    using (Bitmap finished = renderer.Render(new AdventureRenderState { Kind = kind == "boss" ? "Boss" : "Skeleton", MonsterKind = kind, Phase = "shatter", DeathProgress = 1, AttackProgress = .95, Label = "must disappear" }))
                    {
                        check(Hash(initial) != HashEmpty(initial.Width, initial.Height), kind + " shatter begins at an intact visible frame");
                        check(Hash(initial) == Hash(hit), kind + " shatter progress does not reuse the hit flash timer");
                        check(Hash(finished) == HashEmpty(finished.Width, finished.Height), kind + " completed shatter removes body, label and shadow");
                    }
                }
                check(monsterHashes.Count == 6, "five distinct common monsters and different boss");
                foreach (string phase in new[] { "pant", "attack", "dying", "grave", "revive" }) foreach (string theme in RasterActorRenderer.ThemeCodes)
                    using (Bitmap frame = renderer.Render(new AdventureRenderState { ThemeCode = theme, Phase = phase, Time = .3, DeathProgress = .4, AttackProgress = phase == "attack" ? .5 : 0 })) check(Hash(frame) != HashEmpty(frame.Width, frame.Height), theme + " " + phase + " is visible");
                int before = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                for (int i = 0; i < 180; i++) using (Bitmap frame = renderer.Render(new AdventureRenderState { ThemeCode = RasterActorRenderer.ThemeCodes[i % 6], Kind = i % 3 == 0 ? "Skeleton" : "Hero", MonsterKind = RasterActorRenderer.MonsterKinds[i % 6], Cycle = i / 60.0, Time = i / 60.0, WeaponId = RasterActorRenderer.WeaponIds[i % 7] })) { }
                int after = GetGuiResources(Process.GetCurrentProcess().Handle, 0); check(after <= before + 3, "raster rendering releases native GDI handles");
            }
            return "V5 raster art: " + checks + " assertions passed; source atlas transparency, 6 full raster rigs x 60 poses, seamless loops, 5 monsters + boss, 7 textured weapons, both facings, resurrection and GDI ownership.";
        }
        private static bool EdgesEmpty(Bitmap bitmap)
        {
            for (int x = 0; x < bitmap.Width; x++) if (bitmap.GetPixel(x, 0).A > 12 || bitmap.GetPixel(x, bitmap.Height - 1).A > 12) return false;
            for (int y = 0; y < bitmap.Height; y++) if (bitmap.GetPixel(0, y).A > 12 || bitmap.GetPixel(bitmap.Width - 1, y).A > 12) return false; return true;
        }
        private static double MonsterEyeCenter(Bitmap bitmap, string kind)
        {
            int index = Array.IndexOf(RasterActorRenderer.MonsterKinds, kind);
            int[] firstRows = { 110, 125, 95, 120, 115, 80 }, lastRows = { 118, 150, 123, 146, 139, 109 };
            double sum = 0, count = 0;
            for (int y = firstRows[index]; y <= lastRows[index]; y++) for (int x = 20; x < 200; x++)
            {
                Color c = bitmap.GetPixel(x, y);
                bool eye = index == 5 ? c.R > 200 && c.B > 100 && c.G < 120 && c.R > c.B * .95 : c.R > 215 && c.G > 120 && c.B < 120;
                if (c.A > 180 && eye) { sum += x; count++; }
            }
            if (count == 0) throw new InvalidOperationException("No authored eye pixels found for " + kind);
            return sum / count;
        }
        private static bool HasMonsterHealthPixel(Bitmap bitmap, bool boss)
        {
            int x0 = (int)((boss ? 56 : 72) * bitmap.Width / 220.0), x1 = (int)((boss ? 164 : 149) * bitmap.Width / 220.0);
            int y0 = (int)(27 * bitmap.Height / 220.0), y1 = (int)((boss ? 32 : 30) * bitmap.Height / 220.0);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                Color c = bitmap.GetPixel(x, y);
                if (c.A > 150 && c.R > 190 && (boss ? c.B > 125 && c.G < 130 : c.G > 120 && c.B < 140)) return true;
            }
            return false;
        }
        [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int flags);
    }
}
