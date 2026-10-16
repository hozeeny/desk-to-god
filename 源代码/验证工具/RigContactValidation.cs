using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
namespace NetBunny
{
    internal static class RigContactValidation
    {
        [STAThread] private static int Main(string[] args)
        {
            try
            {
                string assets = args[0], output = args[1]; Directory.CreateDirectory(output);
                Console.WriteLine(HeroPoseRig.SelfTest());
                using (AdventureRenderer renderer = new AdventureRenderer(assets)) using (RasterActorRenderer rig = new RasterActorRenderer(assets))
                {
                    ExportContact(renderer, rig, Path.Combine(output, "步态八相位.png"));
                    ExportHands(renderer, Path.Combine(output, "双向七武器握持.png"));
                    ExportThemes(renderer, rig, Path.Combine(output, "六角色脚手接触.png"));
                    int checkedFrames = 0;
                    foreach (string theme in RasterActorRenderer.ThemeCodes) for (int n = 0; n < 60; n++) foreach (bool right in new[] { false, true })
                        using (Bitmap frame = new Bitmap(220, 220, PixelFormat.Format32bppPArgb)) using (Graphics g = Graphics.FromImage(frame))
                        {
                            g.SmoothingMode = SmoothingMode.AntiAlias; rig.DrawHero(g, new AdventureRenderState { ThemeCode = theme, Phase = "run", Cycle = n / 60.0, Time = 0, FacingRight = right });
                            for (int y = 194; y < 220; y++) for (int x = 0; x < 220; x++) if (frame.GetPixel(x, y).A > 40) throw new InvalidOperationException(theme + " sole sinks below plane at frame " + n + " / facing " + right);
                            checkedFrames++;
                        }
                    Console.WriteLine("Raster contact checks: " + checkedFrames + " real hero frames keep all feet above the floor, both facings and all six characters.");
                    Console.WriteLine(AdventureRenderer.AssetsSelfTest(assets));
                }
                AdventureRenderer.ExportRunGif(assets, Path.Combine(output, "骑士脚步修复60帧.gif"));
                Console.WriteLine("Contact sheets and 60-frame motion GIF exported. No generated PNG asset was changed."); return 0;
            }
            catch(Exception e) { Console.Error.WriteLine(e); return 1; }
        }
        private static void ExportContact(AdventureRenderer renderer, RasterActorRenderer rig, string path)
        {
            using (Bitmap sheet = new Bitmap(1320, 820, PixelFormat.Format32bppArgb)) using (Graphics g = Graphics.FromImage(sheet)) using (Font font = new Font("Microsoft YaHei UI", 15, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.Clear(Color.FromArgb(20, 27, 43)); g.SmoothingMode = SmoothingMode.AntiAlias;
                for (int n = 0; n < 8; n++)
                {
                    int x = n % 4 * 330, y = n / 4 * 410; AdventureRenderState state = new AdventureRenderState { ThemeCode = "OTHER", Phase = "run", Cycle = n / 8.0, Time = 0, Size = new Size(330, 330) };
                    using (Pen floor = new Pen(Color.FromArgb(85, 108, 126), 1)) g.DrawLine(floor, x + 15, y + 191 * 1.5f, x + 315, y + 191 * 1.5f);
                    using (Bitmap frame = renderer.Render(state)) g.DrawImageUnscaled(frame, x, y);
                    HeroPose pose = HeroPoseRig.Evaluate(state, rig.WeaponGrip(state.WeaponId));
                    GraphicsState save = g.Save(); g.TranslateTransform(x, y); g.ScaleTransform(1.5f, 1.5f); FootGuide(g, pose.NearFoot, Color.FromArgb(220, 121, 255, 182)); FootGuide(g, pose.FarFoot, Color.FromArgb(220, 106, 192, 255)); Mark(g, pose.BodyPoint(pose.NearHand), Color.Orange); Mark(g, pose.BodyPoint(pose.FarHand), Color.Orange); g.Restore(save);
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(221, 233, 245))) { g.DrawString("相位 " + n + "/8  近脚" + (pose.NearFoot.Planted ? "支撑" : "回收") + " / 远脚" + (pose.FarFoot.Planted ? "支撑" : "回收"), font, brush, x + 12, y + 346); g.DrawString("绿=近腿 蓝=远腿 橙=握柄中心", font, brush, x + 12, y + 373); }
                }
                sheet.Save(path, ImageFormat.Png);
            }
        }
        private static void FootGuide(Graphics g, HeroFootPose foot, Color color)
        {
            using (Pen pen = new Pen(color, 1)) g.DrawLines(pen, new[] { foot.Hip, foot.Knee, foot.Ankle });
            Mark(g, foot.Hip, color); Mark(g, foot.Knee, color); Mark(g, foot.Ankle, color);
            using (Pen line = new Pen(foot.Planted ? Color.LimeGreen : color, 1.2f)) g.DrawLine(line, foot.Ankle.X - 7, (float)foot.SoleY, foot.Ankle.X + 12, (float)foot.SoleY);
        }
        private static void Mark(Graphics g, PointF p, Color c) { using (Pen pen = new Pen(c, 1.1f)) g.DrawEllipse(pen, p.X - 2, p.Y - 2, 4, 4); }
        private static void ExportHands(AdventureRenderer renderer, string path)
        {
            using (Bitmap sheet = new Bitmap(1960, 860, PixelFormat.Format32bppArgb)) using (Graphics g = Graphics.FromImage(sheet)) using (Font font = new Font("Microsoft YaHei UI", 13, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.Clear(Color.FromArgb(20, 27, 43)); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                for (int row = 0; row < 2; row++) for (int n = 0; n < 7; n++)
                {
                    int x = n * 280, y = row * 430;
                    using (Bitmap frame = renderer.Render(new AdventureRenderState { ThemeCode = "OTHER", Phase = "run", Cycle = .2, Time = 0, FacingRight = row == 0, WeaponId = RasterActorRenderer.WeaponIds[n], Size = new Size(440, 440) }))
                    {
                        g.DrawImage(frame, new Rectangle(x, y, 280, 280));
                        g.DrawImage(frame, new Rectangle(x + 10, y + 285, 260, 120), new Rectangle(120, 205, 260, 120), GraphicsUnit.Pixel);
                    }
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(221,233,245))) g.DrawString(RasterActorRenderer.WeaponIds[n] + (row == 0 ? " · 向右" : " · 向左"), font, brush, x + 10, y + 407);
                }
                sheet.Save(path, ImageFormat.Png);
            }
        }
        private static void ExportThemes(AdventureRenderer renderer, RasterActorRenderer rig, string path)
        {
            using (Bitmap sheet = new Bitmap(1800, 700, PixelFormat.Format32bppArgb)) using (Graphics g = Graphics.FromImage(sheet)) using (Font font = new Font("Microsoft YaHei UI", 15, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.Clear(Color.FromArgb(20,27,43));
                for (int row = 0; row < 2; row++) for (int n = 0; n < 6; n++)
                {
                    int x = n * 300, y = row * 350;
                    using (Pen floor = new Pen(Color.FromArgb(100,120,130),1)) g.DrawLine(floor,x+12,y+191*300f/220,x+288,y+191*300f/220);
                    using (Bitmap frame = renderer.Render(new AdventureRenderState { ThemeCode = RasterActorRenderer.ThemeCodes[n], Phase = row == 0 ? "run" : "idle", Cycle = .125, Time = 0, FacingRight = row == 0, Size = new Size(300,300) })) g.DrawImageUnscaled(frame,x,y);
                    using (SolidBrush brush = new SolidBrush(Color.White)) g.DrawString(RasterActorRenderer.ThemeCodes[n] + (row == 0 ? " · 向右跑" : " · 向左站立"),font,brush,x+15,y+316);
                }
                sheet.Save(path,ImageFormat.Png);
            }
        }
    }
}
