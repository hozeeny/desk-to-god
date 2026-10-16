using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace NetBunny
{
    internal static class MonsterDirectionValidation
    {
        [STAThread] private static int Main(string[] args)
        {
            try
            {
                string assets = args[0], output = args[1]; Directory.CreateDirectory(output);
                using (AdventureRenderer renderer = new AdventureRenderer(assets))
                using (RasterActorRenderer raster = new RasterActorRenderer(assets))
                using (Bitmap sheet = new Bitmap(1200, 1600))
                using (Graphics g = Graphics.FromImage(sheet))
                using (Font heading = new Font("Microsoft YaHei UI", 18, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Font note = new Font("Microsoft YaHei UI", 14, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    g.Clear(Color.FromArgb(21, 29, 45)); g.SmoothingMode = SmoothingMode.AntiAlias;
                    string[] titles = { "原始素材朝向", "向左移动 ←", "向右移动 →", "仅剩 1 HP（实际窗口）" };
                    for (int col = 0; col < 4; col++) g.DrawString(titles[col], heading, Brushes.White, 15 + col * 300, 12);
                    for (int row = 0; row < 6; row++)
                    {
                        string kind = RasterActorRenderer.MonsterKinds[row]; bool boss = row == 5; int y = 50 + row * 255;
                        raster.Monsters.DrawFit(g, row, new RectangleF(40, y + 18, 210, 200));
                        g.DrawString(kind, note, Brushes.LightSteelBlue, 15, y + 225);
                        for (int col = 1; col <= 3; col++)
                        {
                            Size size = col == 3 ? boss ? new Size(190, 220) : new Size(130, 170) : new Size(220, 220);
                            AdventureRenderState state = new AdventureRenderState { Kind = boss ? "Boss" : "Skeleton", MonsterKind = kind, Phase = "walk", Time = 0, FacingRight = col != 1, Size = size, HealthRatio = col == 3 ? 0 : 1, HealthCurrent = col == 3 ? 1 : -1, HealthMaximum = col == 3 ? 900 : -1 };
                            using (Bitmap actor = renderer.Render(state)) g.DrawImageUnscaled(actor, col * 300 + (300 - actor.Width) / 2, y + 8);
                            if (col < 3)
                            {
                                using (Pen arrow = new Pen(Color.FromArgb(139, 217, 234), 3))
                                {
                                    arrow.CustomEndCap = new AdjustableArrowCap(4, 5);
                                    g.DrawLine(arrow, col * 300 + (col == 1 ? 215 : 85), y + 237, col * 300 + (col == 1 ? 85 : 215), y + 237);
                                }
                            }
                            else g.DrawString("HP 1/900 · 血条仍有可见填充", note, Brushes.LightSteelBlue, col * 300 + 35, y + 235);
                        }
                    }
                    sheet.Save(Path.Combine(output, "六种怪物左右移动与残血对照.png"), ImageFormat.Png);
                }
                using (AdventureRenderer renderer = new AdventureRenderer(assets))
                {
                    System.Reflection.MethodInfo eyeCenter = typeof(AdventureRenderer).GetMethod("MonsterEyeCenter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    foreach (string kind in RasterActorRenderer.MonsterKinds) foreach (bool right in new[] { false, true })
                    using (Bitmap frame = renderer.Render(new AdventureRenderState { Kind = kind == "boss" ? "Boss" : "Skeleton", MonsterKind = kind, FacingRight = right, Phase = "walk", Time = 0, Size = new Size(220, 220) }))
                        Console.WriteLine(kind + " " + (right ? "right" : "left") + " eye center X=" + eyeCenter.Invoke(null, new object[] { frame, kind }));
                }
                Console.WriteLine(AdventureRenderer.AssetsSelfTest(assets));
                Console.WriteLine("Six authored source silhouettes, both world facings and exact 1/900 HP exported. Source PNG files only read.");
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
