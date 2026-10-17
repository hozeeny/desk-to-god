using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetBunny
{
    internal static class ArtCaptureProbe
    {
        private static readonly Point HeroAt = new Point(28, 175), MonsterAt = new Point(285, 245), LandmarkAt = new Point(180, 490), PanelAt = new Point(520, 50);
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [STAThread] private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "--selftest") return SelfTest(args[1]);
                if (args.Length < 3 || args[0] != "--capture") throw new ArgumentException("--selftest <assets> | --capture <assets> <new-output-directory> [hold-seconds]");
                int hold = args.Length > 3 ? Int32.Parse(args[3]) : 0;
                if (hold < 0 || hold > 300) throw new ArgumentOutOfRangeException("hold-seconds");
                SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Capture(args[1], args[2], hold);
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }

        private static int SelfTest(string assets)
        {
                using (Bitmap blank = new Bitmap(220, 240, PixelFormat.Format32bppArgb))
                {
                    if (DesktopFrameCapture.HasVisibleDetail(blank)) throw new InvalidOperationException("Transparent blank capture was accepted.");
                    using (Graphics g = Graphics.FromImage(blank)) g.Clear(Color.FromArgb(240, 240, 240));
                    if (DesktopFrameCapture.HasVisibleDetail(blank)) throw new InvalidOperationException("Solid gray capture was accepted.");
                }
                using (AdventureRenderer renderer = new AdventureRenderer(assets))
                {
                    foreach (string kind in new[] { "Hero", "Skeleton", "Landmark" })
                    using (Bitmap frame = renderer.Render(new AdventureRenderState { Kind = kind, Size = new Size(220, 240), Phase = "idle", Label = "旧素材基线", Opacity = 1 }))
                        if (!DesktopFrameCapture.HasVisibleDetail(frame)) throw new InvalidOperationException("Visible production frame rejected: " + kind);
                }
                using (Bitmap actor = new Bitmap(30, 30))
                using (Bitmap desktop = new Bitmap(50, 50))
                {
                    using (Graphics g = Graphics.FromImage(actor)) g.Clear(Color.FromArgb(255, 180, 20, 50));
                    using (Graphics g = Graphics.FromImage(desktop)) g.Clear(Color.White);
                    bool rejected = false;
                    try { CheckComposite(actor, desktop, new Point(10, 10), Color.White, "missing actor"); }
                    catch (InvalidOperationException) { rejected = true; }
                    if (!rejected) throw new InvalidOperationException("Desktop without the layered actor was accepted.");
                    using (Graphics g = Graphics.FromImage(desktop)) g.FillRectangle(Brushes.Crimson, 10, 10, 30, 30);
                    using (Graphics g = Graphics.FromImage(actor)) g.Clear(Color.Crimson);
                    CheckComposite(actor, desktop, new Point(10, 10), Color.White, "visible actor");
                    rejected = false;
                    try { CheckComposite(actor, desktop, new Point(3, 3), Color.White, "wrong coordinates"); }
                    catch (InvalidOperationException) { rejected = true; }
                    if (!rejected) throw new InvalidOperationException("Misregistered desktop region was accepted.");
                }
                using (Bitmap actor = new Bitmap(40, 40, PixelFormat.Format32bppArgb))
                using (Bitmap desktop = new Bitmap(40, 40))
                {
                    using (Graphics g = Graphics.FromImage(actor)) { g.Clear(Color.Transparent); g.FillRectangle(Brushes.Crimson, 10, 10, 20, 20); }
                    using (Graphics g = Graphics.FromImage(desktop)) { g.Clear(Color.White); g.FillRectangle(Brushes.Crimson, 10, 10, 20, 20); }
                    CheckComposite(actor, desktop, Point.Empty, Color.White, "transparent edges");
                    bool rejected = false;
                    try { CheckComposite(actor, desktop, Point.Empty, Color.Black, "wrong transparent edges"); }
                    catch (InvalidOperationException) { rejected = true; }
                    if (!rejected) throw new InvalidOperationException("Wrong transparent background was accepted.");
                }
                Console.WriteLine("Art capture: 10 checks passed (blank/gray, production frames, missing/misregistered layers, visible content and transparent edges).");
                return 0;
        }

        private static void Capture(string assets, string output, int holdSeconds)
        {
            Directory.CreateDirectory(output);
            string marker = Path.Combine(output, "capture-report.json");
            if (File.Exists(marker) || File.Exists(Path.Combine(output, "desktop-light.png"))) throw new IOException("Use a new output directory; captures are never overwritten.");
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            if (work.Width < 1320 || work.Height < 900) throw new InvalidOperationException("采集需要至少1320×900的可用桌面。当前为 " + work);
            Dictionary<string, object> report = new Dictionary<string, object>();
            List<object> samples = new List<object>();
            Dictionary<string, string> hashes = new Dictionary<string, string>();
            report["mode"] = "production renderer + WS_EX_LAYERED + real equipment form; demonstration memory state";
            report["newArt"] = false; report["humanReviewRequired"] = true;
            report["utc"] = DateTime.UtcNow.ToString("o");
            report["processId"] = System.Diagnostics.Process.GetCurrentProcess().Id;
            report["screenWorkingArea"] = new[] { work.X, work.Y, work.Width, work.Height };
            report["assets"] = HashDirectory(assets);
            using (AdventureRenderer renderer = new AdventureRenderer(assets))
            using (Bitmap hero = renderer.Render(new AdventureRenderState { Kind = "Hero", ThemeCode = "OTHER", Size = new Size(220, 240), Phase = "idle", Time = .4, MoveSpeed = 0, WeaponId = "spear", Label = "旧素材 · 炼气一层", Level = 7 }))
            using (Bitmap monster = renderer.Render(new AdventureRenderState { Kind = "Skeleton", ThemeCode = "OTHER", MonsterKind = "skeleton", Size = new Size(130, 170), Phase = "idle", Time = .3, MoveSpeed = 0, Label = "白骨妖兵 · 技术基线", HealthCurrent = 80, HealthMaximum = 100, HealthRatio = .8 }))
            using (Bitmap landmark = renderer.Render(new AdventureRenderState { Kind = "Landmark", ThemeCode = "OTHER", Size = new Size(200, 280), Time = .3, Label = "旧地标", GrowthLabel = "旧素材技术验收", GrowthRatio = .3, DiskLabel = "示例磁盘信息", DiskFreeRatio = .6 }))
            using (Form owner = new Form())
            {
                IntPtr unused = owner.Handle;
                using (AdventureController controller = new AdventureController(owner, assets, true))
                using (Form backdrop = new Form())
                using (AdventureEquipmentForm panel = new AdventureEquipmentForm(controller))
                {
                    StopTimer(controller, "timer"); StopTimer(panel, "refresh");
                    MaintenanceConsentStore consent = (MaintenanceConsentStore)Field(controller, "consent");
                    consent.CleanupEnabled = consent.MemoryEnabled = consent.AutoMaintenanceEnabled = false;
                    controller.Cultivation.Mortal = false; controller.Cultivation.RealmIndex = 0; controller.Cultivation.Layer = 1;
                    controller.Cultivation.DaoTitle = "采集验收修士"; controller.Cultivation.AutoBattle = false;
                    backdrop.Text = "桌面修仙传 · 独立画面采集 · 旧素材";
                    backdrop.StartPosition = FormStartPosition.Manual; backdrop.ClientSize = new Size(1280, 830);
                    backdrop.FormBorderStyle = FormBorderStyle.FixedSingle; backdrop.MaximizeBox = false;
                    backdrop.TopMost = true; backdrop.Location = new Point(work.Left + (work.Width - backdrop.Width) / 2, work.Top + (work.Height - backdrop.Height) / 2);
                    backdrop.Paint += delegate(object sender, PaintEventArgs e) { DrawHeader(e.Graphics, backdrop.BackColor); };
                    // A hidden process startup can suppress the first ShowWindow call.
                    // Re-show our own neutral backdrop before capturing any desktop pixels.
                    backdrop.Show(); backdrop.Hide(); backdrop.Show();
                    panel.StartPosition = FormStartPosition.Manual; panel.Size = new Size(740, 760);
                    panel.Location = backdrop.PointToScreen(PanelAt); panel.TopMost = true;
                    panel.Text = "桌面修仙传 · 修行与装备 · 内存演示档"; panel.Show(backdrop); panel.RefreshData(); Pump(300);
                    using (Bitmap ui = new Bitmap(panel.Width, panel.Height))
                    {
                        panel.DrawToBitmap(ui, new Rectangle(Point.Empty, ui.Size));
                        if (!DesktopFrameCapture.HasVisibleDetail(ui)) throw new InvalidOperationException("真实修行面板静态导出为空。");
                        Save(ui, output, "ui-static.png", hashes);
                        Save(hero, output, "hero-alpha.png", hashes); Save(monster, output, "monster-alpha.png", hashes); Save(landmark, output, "landmark-alpha.png", hashes);
                        foreach (string theme in new[] { "light", "dark" })
                        {
                            Color background = theme == "light" ? Color.FromArgb(241, 244, 247) : Color.FromArgb(29, 33, 44);
                            backdrop.BackColor = background; backdrop.Invalidate(); backdrop.Update(); panel.BringToFront(); Pump(150);
                            using (Bitmap canvas = new Bitmap(1280, 830, PixelFormat.Format24bppRgb))
                            {
                                using (Graphics g = Graphics.FromImage(canvas))
                                {
                                    g.Clear(background); DrawHeader(g, background); g.DrawImageUnscaled(ui, PanelAt);
                                    g.DrawImageUnscaled(hero, HeroAt); g.DrawImageUnscaled(monster, MonsterAt); g.DrawImageUnscaled(landmark, LandmarkAt);
                                }
                                Save(canvas, output, "static-" + theme + ".png", hashes);
                            }
                            using (KnightLayeredWindow heroWindow = Layer(backdrop, hero, HeroAt, "采集样板 · 主角"))
                            using (KnightLayeredWindow monsterWindow = Layer(backdrop, monster, MonsterAt, "采集样板 · 妖物"))
                            using (KnightLayeredWindow landmarkWindow = Layer(backdrop, landmark, LandmarkAt, "采集样板 · 地标"))
                            {
                                Pump(450);
                                Rectangle clientScreen = backdrop.RectangleToScreen(backdrop.ClientRectangle);
                                using (Bitmap screen = DesktopFrameCapture.CaptureRegion(clientScreen))
                                {
                                    // Retain failed candidates for diagnosis; only the report marks pixel validation success.
                                    Save(screen, output, "desktop-" + theme + ".png", hashes);
                                    double h = CheckComposite(hero, screen, HeroAt, background, "hero");
                                    double m = CheckComposite(monster, screen, MonsterAt, background, "monster");
                                    double b = CheckComposite(landmark, screen, LandmarkAt, background, "landmark");
                                    samples.Add(new { theme = theme, screenBounds = new[] { clientScreen.X, clientScreen.Y, clientScreen.Width, clientScreen.Height }, heroMatched = h, monsterMatched = m, landmarkMatched = b,
                                        heroWindow = heroWindow.Handle.ToInt64(), monsterWindow = monsterWindow.Handle.ToInt64(), uiWindow = panel.Handle.ToInt64() });
                                }
                                if (theme == "dark" && holdSeconds > 0) Pump(holdSeconds * 1000);
                            }
                        }
                    }
                    panel.Close(); backdrop.Close();
                }
            }
            report["samples"] = samples; report["filesSha256"] = hashes;
            File.WriteAllText(marker, new JavaScriptSerializer().Serialize(report), new System.Text.UTF8Encoding(false));
            Console.WriteLine("Capture pixel checks passed; human image review remains required. Output: " + output);
        }

        private static KnightLayeredWindow Layer(Form owner, Bitmap frame, Point at, string title)
        {
            KnightLayeredWindow window = new KnightLayeredWindow();
            try
            {
                window.Text = title; window.PassThrough = true; window.Location = owner.PointToScreen(at);
                window.Present((Bitmap)frame.Clone()); window.Show(owner); return window;
            }
            catch { window.Dispose(); throw; }
        }

        private static double CheckComposite(Bitmap frame, Bitmap screen, Point at, Color background, string label)
        {
            int samples = 0, matches = 0, transparent = 0, backgroundMatches = 0;
            for (int y = 0; y < frame.Height; y += 2)
                for (int x = 0; x < frame.Width; x += 2)
                {
                    Color p = frame.GetPixel(x, y);
                    if (p.A == 0)
                    {
                        Color backgroundPixel = screen.GetPixel(at.X + x, at.Y + y); transparent++;
                        if (Math.Abs(background.R - backgroundPixel.R) + Math.Abs(background.G - backgroundPixel.G) + Math.Abs(background.B - backgroundPixel.B) <= 24) backgroundMatches++;
                        continue;
                    }
                    if (p.A < 245) continue;
                    Color q = screen.GetPixel(at.X + x, at.Y + y); samples++;
                    if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) <= 36) matches++;
                }
            double ratio = samples > 0 ? matches / (double)samples : 0;
            if (samples < 100 || ratio < .93) throw new InvalidOperationException("桌面透明窗口缺失、遮挡或坐标错误：" + label + ", matched=" + ratio.ToString("F3"));
            if (transparent >= 100 && backgroundMatches / (double)transparent < .95)
                throw new InvalidOperationException("透明边未保留桌面底色，或有其他窗口遮挡：" + label + ", matched=" + (backgroundMatches / (double)transparent).ToString("F3"));
            return ratio;
        }

        private static void DrawHeader(Graphics g, Color background)
        {
            Color ink = background.R < 100 ? Color.FromArgb(230, 234, 242) : Color.FromArgb(39, 45, 59);
            using (Font title = new Font("Microsoft YaHei UI", 16, FontStyle.Bold))
            using (Font note = new Font("Microsoft YaHei UI", 10))
            using (Brush brush = new SolidBrush(ink))
            {
                g.DrawString("桌面修仙传 · 旧素材画面基线", title, brush, 22, 17);
                g.DrawString("原生透明窗口：主角、妖物、地标", note, brush, 22, 82);
                g.DrawString("右侧：真实修行面板，全部为内存演示数据", note, brush, 22, 108);
                g.DrawString("用于验证采集链路，尚未制作新美术", note, brush, 22, 135);
            }
        }

        private static object Field(object value, string name)
        { return value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value); }
        private static void StopTimer(object value, string name) { ((System.Windows.Forms.Timer)Field(value, name)).Stop(); }
        private static void Pump(int milliseconds)
        { System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); } }
        private static void Save(Bitmap bitmap, string directory, string name, IDictionary<string, string> hashes)
        {
            string path = Path.Combine(directory, name); if (File.Exists(path)) throw new IOException("Capture already exists: " + path);
            bitmap.Save(path, ImageFormat.Png); hashes[name] = Hash(path);
        }
        private static string Hash(string path)
        { using (SHA256 sha = SHA256.Create()) using (FileStream stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        private static Dictionary<string, string> HashDirectory(string path)
        { Dictionary<string, string> result = new Dictionary<string, string>(); foreach (string file in Directory.GetFiles(path)) result[Path.GetFileName(file)] = Hash(file); return result; }
    }
}
