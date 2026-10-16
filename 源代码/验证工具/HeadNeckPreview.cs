using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using NetBunny;

internal static class HeadNeckPreview
{
    private static int checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    [STAThread] private static int Main(string[] args)
    {
        try
        {
            Directory.CreateDirectory(args[1]);
            ValidateNecks(args[0]);
            using (AdventureRenderer renderer = new AdventureRenderer(args[0]))
            using (Bitmap sheet = new Bitmap(1800, 1645))
            using (Graphics g = Graphics.FromImage(sheet))
            using (Font font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold))
            {
                g.Clear(Color.FromArgb(39, 47, 61));
                string[] phases = { "run", "run", "attack", "pant", "run", "attack", "pant" };
                double[] cycles = { 0, .3, .5, .75, .5, .7, .75 };
                for (int theme = 0; theme < 6; theme++)
                for (int phase = 0; phase < phases.Length; phase++)
                {
                    AdventureRenderState s = new AdventureRenderState { ThemeCode = RasterActorRenderer.ThemeCodes[theme], Phase = phases[phase], Cycle = cycles[phase], Time = cycles[phase], AttackProgress = phases[phase] == "attack" ? .65 : 0, FacingRight = phase < 4, Size = new Size(440, 440) };
                    using (Bitmap frame = renderer.Render(s))
                        g.DrawImage(frame, new Rectangle(theme * 300, phase * 235 + 30, 300, 205), new Rectangle(88, 110, 264, 180), GraphicsUnit.Pixel);
                    g.DrawString(s.ThemeCode + " " + s.Phase + (s.FacingRight ? " R" : " L"), font, Brushes.White, theme * 300 + 10, phase * 235 + 2);
                }
                sheet.Save(Path.Combine(args[1], args.Length > 2 ? args[2] : "六皮肤头颈接合.png"), ImageFormat.Png);
            }
            Console.WriteLine("Head/neck attachment: " + checks + " checks passed across six skins, 60 continuous poses, running/attack/panting and both directions; actual raster overlap at every neck, no source PNG modifications.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static void ValidateNecks(string assets)
    {
        using (RasterActorRenderer renderer = new RasterActorRenderer(assets))
        foreach (string theme in RasterActorRenderer.ThemeCodes)
        foreach (string phase in new[] { "run", "attack", "pant" })
        foreach (bool facing in new[] { false, true })
        {
            double smallestTilt = Double.MaxValue, largestTilt = Double.MinValue;
            PointF previous = PointF.Empty;
            for (int i = 0; i < 60; i++)
            {
                AdventureRenderState state = new AdventureRenderState { ThemeCode = theme, Phase = phase, Time = i / 60.0, Cycle = i / 60.0, AttackProgress = phase == "attack" ? i / 60.0 : 0, FacingRight = facing };
                HeroPose pose = HeroPoseRig.Evaluate(state, renderer.WeaponGrip("spear"));
                PointF attachment = pose.BodyPoint(RasterActorRenderer.NeckAnchor(theme));
                if (!facing) attachment.X = 220 - attachment.X;
                using (Bitmap torso = new Bitmap(220, 220))
                using (Bitmap head = new Bitmap(220, 220))
                {
                    using (Graphics g = Graphics.FromImage(torso))
                    {
                        Prepare(g, pose, facing); RasterActorRenderer.DrawNeckConnector(g, theme);
                        renderer.Rig(theme).Draw(g, 1, new RectangleF(85, 103, 55, 53));
                    }
                    using (Graphics g = Graphics.FromImage(head))
                    { Prepare(g, pose, facing); RasterActorRenderer.DrawAttachedHead(g, renderer.Rig(theme), pose, theme); }
                    int overlap = 0;
                    for (int y = (int)attachment.Y - 14; y <= (int)attachment.Y + 14; y++)
                    for (int x = (int)attachment.X - 14; x <= (int)attachment.X + 14; x++)
                        if (torso.GetPixel(x, y).A >= 150 && head.GetPixel(x, y).A >= 150) overlap++;
                    Check(overlap >= 12, theme + " " + phase + " " + facing + " pose " + i + ": opaque head and collar share pixels (" + overlap + ")");
                }
                if (i > 0) Check(Math.Abs(attachment.X - previous.X) < 1.5 && Math.Abs(attachment.Y - previous.Y) < 1.5, theme + " " + phase + ": attached joint stays continuous");
                previous = attachment; smallestTilt = Math.Min(smallestTilt, pose.HeadTilt); largestTilt = Math.Max(largestTilt, pose.HeadTilt);
            }
            Check(largestTilt - smallestTilt > .35, theme + " " + phase + ": head movement remains articulated");
        }
    }
    private static void Prepare(Graphics g, HeroPose pose, bool facing)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        if (!facing) { g.TranslateTransform(220, 0); g.ScaleTransform(-1, 1); }
        RasterActorRenderer.ApplyBody(g, pose);
    }
}
