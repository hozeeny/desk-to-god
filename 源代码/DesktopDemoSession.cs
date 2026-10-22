using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace NetBunny
{
    // Lifecycle only: all combat, cultivation and persistence remain in the
    // production AdventureController. No desktop-sized host or scene window.
    internal sealed class DesktopDemoSession : ApplicationContext
    {
        readonly Form owner;
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer statusTimer;
        readonly Mutex profileGate;
        readonly DesktopPanelLayerCoordinator panelLayers;
        DesktopPracticePanel panel;
        DesktopEcologyPanel ecology;
        bool exiting, disposedSession;
        readonly bool acceptanceMode;
        int exitCode;
        internal AdventureController Game { get; private set; }
        internal string ProfileRoot { get; private set; }

        internal static string ProfilePath(string name)
        {
            if (!Regex.IsMatch(name ?? "", "^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$"))
                throw new ArgumentException("隔离档名称仅可使用字母、数字、下划线和短横线，最长64字。");
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles", name);
        }
        internal static bool ReadDesktopObserve(string[] args)
        {
            return args != null && args.Length > 0 && args[0] == "--desktop-demo"
                && !args.Contains("--seed") && !args.Contains("--normal-speed");
        }
        internal static int Run(string[] args)
        {
            string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            string art = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArtDemoAssets");
            if (!Directory.Exists(art)) throw new DirectoryNotFoundException("缺少独立新美术包：" + art);
            Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, art);
            bool acceptance = args[0] == "--desktop-demo-accept";
            bool smoke = args[0] == "--desktop-demo-smoke";
            string name = "player", seed = null, output = null;
            bool normalSpeed = false;
            if (acceptance)
            {
                if (args.Length < 2) throw new ArgumentException("请指定全新的验收产物目录。");
                output = Path.GetFullPath(args[1]); name = "accept-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            }
            for (int i = acceptance ? 2 : 1; i < args.Length; i++)
            {
                if (args[i] == "--profile" && i + 1 < args.Length) name = args[++i];
                else if (args[i] == "--seed" && i + 1 < args.Length) seed = args[++i];
                else if (args[i] == "--normal-speed" && acceptance) normalSpeed = true;
                else throw new ArgumentException("未知的透明桌面 Demo 参数：" + args[i]);
            }
            if (acceptance && !name.StartsWith("accept-", StringComparison.Ordinal) && !name.StartsWith("verify-", StringComparison.Ordinal))
                throw new ArgumentException("自动验收只能使用 accept- 或 verify- 开头的隔离测试档。");
            if (smoke)
            {
                string result = DesktopDemoProfiles.Smoke(assets, ProfilePath("smoke-" + Guid.NewGuid().ToString("N")));
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "desktop-demo-smoke.log"), result);
                return 0;
            }
            string profile = ProfilePath(name);
            using (DesktopDemoSession session = new DesktopDemoSession(assets, profile, acceptance, seed, ReadDesktopObserve(args)))
            {
                if (acceptance)
                {
                    EventHandler firstIdle = null;
                    firstIdle = delegate { Application.Idle -= firstIdle; new DesktopDemoAcceptance(session, output, normalSpeed).Start(); };
                    Application.Idle += firstIdle;
                }
                Application.Run(session);
                return session.exitCode;
            }
        }
        internal DesktopDemoSession(string assets, string profile, bool acceptanceMode = false, string seed = null, bool observeSystem = false)
        {
            this.acceptanceMode = acceptanceMode;
            ProfileRoot = Path.GetFullPath(profile);
            // The gate covers only this private profile; running production games
            // or tests are not enumerated, terminated, attached to or reused.
            string gateId;
            using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
                gateId = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(ProfileRoot.ToUpperInvariant()))).Replace("-", "");
            bool first;
            profileGate = new Mutex(true, "Local\\DeskToGod.ArtDesktop." + gateId, out first);
            if (!first) { profileGate.Dispose(); throw new InvalidOperationException("此隔离档已在运行，请通过托盘找到它，或使用另一个 --profile 名称。"); }
            owner = new Form { Text = "桌面修仙传 · 隔离生命周期", ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Location = new Point(-25000, -25000), Size = new Size(1, 1), AutoScaleMode = AutoScaleMode.None };
            IntPtr hiddenOwner = owner.Handle;
            try
            {
                panelLayers = new DesktopPanelLayerCoordinator();
                if (seed != null) DesktopDemoProfiles.CreateSeed(ProfileRoot, seed);
                Game = new AdventureController(owner, assets, false, null, false, ProfileRoot, observeSystem && !acceptanceMode && seed == null);
                Game.DesktopPanelRequested = delegate { OpenPanel("practice"); };
                Game.DesktopProcessConfirmation = delegate(ProcessIdentity target)
                {
                    string text = "请求「" + target.Name + "」正常退出？\nPID " + target.Id + "\n" + target.ExecutablePath
                        + "\n\n程序可能显示保存提示。取消或超时不会强制结束；本次确认只针对这个进程。";
                    // Layered actors intentionally never activate. A native modal
                    // without a visible owner can disappear before presentation.
                    // This explicit, manual action opens its own detail panel.
                    OpenPanel("ecology");
                    bool previousTopMost = ecology.TopMost;
                    try
                    {
                        // Keep animated topmost actors below this one explicit
                        // system-action confirmation; restore the panel afterward.
                        ecology.TopMost = true; ecology.BringToFront(); ecology.Activate();
                        return MessageBox.Show(ecology,text,"确认本次程序退出",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2) == DialogResult.Yes;
                    }
                    finally { if (!ecology.IsDisposed) ecology.TopMost = previousTopMost; }
                };
                tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "桌面修仙传 · 透明桌面隔离版", Visible = true };
                ContextMenuStrip menu = new ContextMenuStrip();
                menu.Items.Add("修行与操作", null, delegate { OpenPanel("practice"); });
                menu.Items.Add("功法与修行", null, delegate { OpenPanel("training"); });
                menu.Items.Add("行囊与兵器", null, delegate { OpenPanel("inventory"); });
                menu.Items.Add("电脑生态与维护", null, delegate { OpenPanel("ecology"); });
                menu.Items.Add("百业道碑", null, delegate { OpenPanel("baiye"); });
                menu.Items.Add("破境", null, delegate { OpenPanel("breakthrough"); });
                menu.Items.Add("灵域历练", null, delegate { OpenPanel("lingyu"); });
                menu.Items.Add("洞天", null, delegate { OpenPanel("dongtian"); });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("隐藏／恢复桌面角色", null, delegate { Game.DesktopSetActorsVisible(!Game.DesktopActorsVisible); });
                menu.Items.Add("保存并退出", null, delegate { ExitAfterAcceptance(0); });
                tray.ContextMenuStrip = menu;
                tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) OpenPanel("practice"); };
                statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
                statusTimer.Tick += delegate
                {
                    if (exiting) return;
                    Game.RefreshDesktopDisplayLayout();
                    string text = "桌面修仙传 · " + (Game.Cultivation.Mortal ? "凡人 " + Game.Cultivation.MortalKills + "/10" : Game.Cultivation.RealmName + " " + Game.Cultivation.Layer + "层");
                    tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
                };
                Game.ConfigureDesktopDemo();
                statusTimer.Start();
                Application.ApplicationExit += ApplicationExit;
                if (Game.Cultivation.Mortal && Game.Cultivation.MortalKills == 0)
                {
                    panel = new DesktopPracticePanel(this);
                    panel.ShowWelcome();
                    PlaceFeaturePanel(panel, true);
                }
            }
            catch
            {
                if (statusTimer != null) statusTimer.Dispose();
                if (panelLayers != null) panelLayers.Dispose();
                if (tray != null) { tray.Visible = false; if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose(); tray.Dispose(); }
                if (Game != null) Game.Dispose(); owner.Dispose(); profileGate.ReleaseMutex(); profileGate.Dispose(); throw;
            }
        }
        internal void OpenPanel(string id)
        {
            if (exiting) return;
            if (id == "ecology")
            {
                if (ecology == null || ecology.IsDisposed) ecology = new DesktopEcologyPanel(Game);
                ecology.UpdateState();if(ecology.WindowState==FormWindowState.Minimized)ecology.WindowState=FormWindowState.Normal;
                ecology.Show();PlaceFeaturePanel(ecology);ecology.BringToFront();ecology.Activate();return;
            }
            if (id == "baiye") { Game.OpenBaiyeStele(); PlaceFeaturePanel(Application.OpenForms.OfType<BaiyeSteleForm>().FirstOrDefault(f => f.Visible)); return; }
            if (id == "dongtian") { Game.OpenDongtian(); PlaceFeaturePanel(Application.OpenForms.OfType<DongtianForm>().FirstOrDefault(f => f.Visible)); return; }
            if (id == "lingyu") { Game.OpenLingyu(); PlaceFeaturePanel(Application.OpenForms.OfType<LingyuForm>().FirstOrDefault(f => f.Visible)); return; }
            if (id == "breakthrough") { Game.OpenBreakthrough(); PlaceFeaturePanel(Application.OpenForms.OfType<BreakthroughForm>().FirstOrDefault(f => f.Visible)); return; }
            if (panel == null || panel.IsDisposed) panel = new DesktopPracticePanel(this);
            panel.OpenSection(id);
            panel.UpdateState();
            if (!panel.Visible) panel.Show();
            if (panel.WindowState == FormWindowState.Minimized) panel.WindowState = FormWindowState.Normal;
            PlaceFeaturePanel(panel);
            panel.BringToFront(); panel.Activate();
        }
        void PlaceFeaturePanel(Form form, bool initialWelcome = false)
        {
            if (form == null) return;
            Rectangle work = Screen.FromPoint(Game.DesktopHeroWindow.Bounds.Location).WorkingArea;
            Rectangle[] actors = Game.DesktopEnemyWindows.Values.Where(w => w.Visible).Select(w => w.Bounds).Concat(new[] { Game.DesktopHeroWindow.Bounds }).ToArray();
            Point[] corners = { new Point(work.Left + 12, work.Top + 12), new Point(work.Right - form.Width - 12, work.Top + 12), new Point(work.Left + 12, work.Bottom - form.Height - 12), new Point(work.Right - form.Width - 12, work.Bottom - form.Height - 12) };
            Func<Point, long> overlap = delegate(Point point)
            {
                Rectangle bounds = new Rectangle(point, form.Size); long total = 0;
                foreach (Rectangle actor in actors) { Rectangle intersection = Rectangle.Intersect(bounds, actor); total += Math.Max(0, intersection.Width) * (long)Math.Max(0, intersection.Height) * (actor == Game.DesktopHeroWindow.Bounds ? 10 : 1); }
                return total;
            };
            Point chosen = corners.OrderBy(overlap).First();
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(Math.Max(work.Left, chosen.X), Math.Max(work.Top, chosen.Y));
            panelLayers.Register(form, initialWelcome);
        }
        internal void ClosePanels()
        {
            foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
                if (form != owner && !(form is KnightLayeredWindow)) form.Hide();
        }
        internal void ExitAfterAcceptance(int code)
        {
            if (exiting) return;
            if (!Game.DesktopSaveNow())
            {
                exitCode = 1;
                TryWriteDiagnostic(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "desktop-demo-save-error.log"), DateTime.UtcNow.ToString("o") + " 保存未完成；" + Game.DesktopNotice);
                // Report the failure explicitly, never claim a successful save.
                if (code == 0 && !acceptanceMode)
                {
                    MessageBox.Show("隔离档暂未能全部保存，修士暂不退出，以保留本次进度。\n请检查磁盘权限与空间后重试。", "桌面修仙传 · 保存未完成");
                    return;
                }
            }
            else exitCode = code;
            exiting = true;
            ExitThread();
        }
        void ApplicationExit(object sender, EventArgs e) { if (!exiting) { Game.DesktopSaveNow(); exiting = true; } }
        internal static bool TryWriteDiagnostic(string path, string message)
        {
            try { File.WriteAllText(path, message); return true; }
            catch { return false; } // A full/unwritable disk must not defeat save-failure handling.
        }
        protected override void Dispose(bool disposing)
        {
            // WinForms may dispose its ApplicationContext when the message loop
            // exits, followed by the Run() using block. Release native handles once.
            if (disposedSession) return;
            disposedSession = true;
            if (disposing)
            {
                Application.ApplicationExit -= ApplicationExit;
                if (statusTimer != null) { statusTimer.Stop(); statusTimer.Dispose(); }
                if (panelLayers != null) panelLayers.Dispose();
                if (tray != null) { tray.Visible = false; if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose(); tray.Dispose(); }
                if (panel != null) panel.Dispose();
                if (ecology != null) ecology.Dispose();
                if (Game != null) { Game.Dispose(); Game = null; }
                owner.Dispose();
                try { profileGate.ReleaseMutex(); } catch (ApplicationException) { }
                profileGate.Dispose();
            }
            base.Dispose(disposing);
        }
    }

}
