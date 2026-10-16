using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NetBunny;

internal static class ResourceGrowthPreview
{
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    private static int checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    [STAThread] private static int Main(string[] args)
    {
        try
        {
            SetProcessDPIAware(); Application.EnableVisualStyles(); Directory.CreateDirectory(args[1]);
            string[] labels = { "积累 0% · 等待安全文件", "下一波积累 50%", "积累完成 · 等待安全文件", "休整 18 秒 · 积累 100%" };
            double[] ratios = { 0, .5, 1, 1 };
            using (AdventureRenderer renderer = new AdventureRenderer(args[0]))
            using (Bitmap contact = new Bitmap(1100, 1150))
            using (Graphics g = Graphics.FromImage(contact))
            using (Font font = new Font("Microsoft YaHei UI", 11))
            {
                g.Clear(Color.FromArgb(37, 43, 58));
                for (int scale = 0; scale < 3; scale++)
                for (int state = 0; state < labels.Length; state++)
                {
                    double zoom = new[] { .8, 1, 1.2 }[scale];
                    Size size = new Size((int)Math.Round(200 * zoom), (int)Math.Round(280 * zoom));
                    using (Bitmap landmark = renderer.Render(new AdventureRenderState { Kind = "Landmark", ThemeCode = "OTHER", Label = state == 0 ? "缓存新增 0 B" : "缓存新增 512.0 KiB", GrowthLabel = labels[state], GrowthRatio = ratios[state], DiskLabel = "C盘剩余 53.2 GiB", DiskFreeRatio = .34, Size = size }))
                    {
                        Check(landmark.Size == size, "requested landmark scale is retained");
                        Check(AlphaInRow(landmark, 225.0 / 280) > 15, "growth label is visible");
                        Check(AlphaInRow(landmark, 243.0 / 280) > 40, "growth progress track is visible");
                        Check(AlphaInRow(landmark, 255.0 / 280) > 15, "disk label is visible below growth");
                        Check(AlphaInRow(landmark, 273.0 / 280) > 40, "disk progress track remains visible");
                        Check(AlphaInRow(landmark, .999) == 0, "bottom edge has transparent breathing room");
                        g.DrawString((int)(zoom * 100) + "%  " + labels[state], font, Brushes.White, state * 275 + 8, scale * 380 + 8);
                        g.DrawImageUnscaled(landmark, state * 275 + (275 - size.Width) / 2, scale * 380 + 37);
                    }
                }
                contact.Save(Path.Combine(args[1], "城堡资源积累-四状态三种缩放.png"), ImageFormat.Png);
            }
            using (Form owner = new Form())
            {
                IntPtr handle = owner.Handle;
                using (AdventureController controller = new AdventureController(owner, args[0], true))
                foreach (int width in new[] { 760, 650 })
                using (AdventureEquipmentForm panel = new AdventureEquipmentForm(controller))
                {
                    panel.Width = width; panel.StartPosition = FormStartPosition.Manual; panel.Location = new Point(-24000, -24000);
                    panel.Show(); panel.RefreshData(); Application.DoEvents();
                    FlowLayoutPanel content = panel.Controls.OfType<FlowLayoutPanel>().Single();
                    ResourceGrowthCard card = content.Controls.OfType<ResourceGrowthCard>().Single();
                    Check(card.Summary == controller.ResourceGrowthText && card.Ratio == controller.ResourceGrowthRatio, "equipment binds current controller growth values");
                    card.SetStatus("城堡蓄积 · 休整 18 秒 · 积累 100%\r\n临时文件总量 12.50 GiB · 可处理 128.00 MiB · 待重扫 512.00 MiB\r\n新增文件 1.20 MiB / 1 MiB；RAM +2.5 / 5 个百分点\r\n本次普查不完整，保留上次可信总量；任一增长达标且休整结束后生成下一波。", 1);
                    content.AutoScrollPosition = new Point(0, card.Top - 12); Application.DoEvents();
                    Size measured = TextRenderer.MeasureText(card.Summary, card.Font, new Size(card.TextBounds.Width, Int32.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
                    Check(measured.Height <= card.TextBounds.Height, width + ": multiline growth text fits measured card");
                    Rectangle bounds = content.RectangleToClient(card.RectangleToScreen(card.ClientRectangle));
                    Check(bounds.Left >= 0 && bounds.Right <= content.ClientSize.Width && bounds.Top >= 0 && bounds.Bottom <= content.ClientSize.Height, width + ": complete growth card is inside viewport");
                    Label rules = content.Controls.OfType<Label>().Single(label => label.Text.StartsWith("清场后每 15 秒"));
                    Check(rules.Text.Contains("60 秒") && rules.Text.Contains("30 秒") && rules.Text.Contains("24 小时"), "refresh cadence, rest gate and safety-age rule are explicit");
                    Check(content.Controls.OfType<Label>().Any(label => label.Text.Contains("冷却退避") && label.Text.Contains("射程反击")), "automatic attack timing is explained");
                    using (Bitmap screenshot = new Bitmap(panel.Width, panel.Height))
                    {
                        panel.DrawToBitmap(screenshot, new Rectangle(Point.Empty, panel.Size));
                        screenshot.Save(Path.Combine(args[1], "装备栏-城堡积累-" + width + ".png"), ImageFormat.Png);
                    }
                    Check(!controller.IsScanning && controller.Battle.QueueCount == 0, "preview starts no real scan or cleanup");
                }
            }
            Console.WriteLine("Resource growth UI: " + checks + " checks passed for four castle states at 80/100/120 percent and 760/650px equipment; isolated demonstration only.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static int AlphaInRow(Bitmap bitmap, double fraction)
    {
        int y = Math.Min(bitmap.Height - 1, Math.Max(0, (int)Math.Round(bitmap.Height * fraction))), count = 0;
        for (int x = 0; x < bitmap.Width; x++) if (bitmap.GetPixel(x, y).A > 40) count++;
        return count;
    }
}
