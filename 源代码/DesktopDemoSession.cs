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
        DesktopPracticePanel panel;
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
        internal static int Run(string[] args)
        {
            string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            string art = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArtDemoAssets");
            if (!Directory.Exists(art)) throw new DirectoryNotFoundException("缺少独立新美术包：" + art);
            Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, art);
            bool acceptance = args[0] == "--desktop-demo-accept";
            bool smoke = args[0] == "--desktop-demo-smoke";
            string name = "player", seed = null, output = null;
            if (acceptance)
            {
                if (args.Length < 2) throw new ArgumentException("请指定全新的验收产物目录。");
                output = Path.GetFullPath(args[1]); name = "accept-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            }
            for (int i = acceptance ? 2 : 1; i < args.Length; i++)
            {
                if (args[i] == "--profile" && i + 1 < args.Length) name = args[++i];
                else if (args[i] == "--seed" && i + 1 < args.Length) seed = args[++i];
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
            using (DesktopDemoSession session = new DesktopDemoSession(assets, profile, acceptance, seed))
            {
                if (acceptance)
                {
                    EventHandler firstIdle = null;
                    firstIdle = delegate { Application.Idle -= firstIdle; new DesktopDemoAcceptance(session, output).Start(); };
                    Application.Idle += firstIdle;
                }
                Application.Run(session);
                return session.exitCode;
            }
        }
        internal DesktopDemoSession(string assets, string profile, bool acceptanceMode = false, string seed = null)
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
                if (seed != null) DesktopDemoProfiles.CreateSeed(ProfileRoot, seed);
                Game = new AdventureController(owner, assets, false, null, false, ProfileRoot);
                Game.DesktopPanelRequested = delegate { OpenPanel("practice"); };
                tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "桌面修仙传 · 透明桌面隔离版", Visible = true };
                ContextMenuStrip menu = new ContextMenuStrip();
                menu.Items.Add("修行与操作", null, delegate { OpenPanel("practice"); });
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
                    string text = "桌面修仙传 · " + (Game.Cultivation.Mortal ? "凡人 " + Game.Cultivation.MortalKills + "/10" : Game.Cultivation.RealmName + " " + Game.Cultivation.Layer + "层");
                    tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
                };
                Game.ConfigureDesktopDemo();
                statusTimer.Start();
                Application.ApplicationExit += ApplicationExit;
            }
            catch
            {
                if (statusTimer != null) statusTimer.Dispose();
                if (tray != null) { tray.Visible = false; if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose(); tray.Dispose(); }
                if (Game != null) Game.Dispose(); owner.Dispose(); profileGate.ReleaseMutex(); profileGate.Dispose(); throw;
            }
        }
        internal void OpenPanel(string id)
        {
            if (exiting) return;
            if (id == "dongtian") { Game.OpenDongtian(); PlaceFeaturePanel(Application.OpenForms.OfType<DongtianForm>().FirstOrDefault(f => f.Visible)); return; }
            if (id == "lingyu") { Game.OpenLingyu(); PlaceFeaturePanel(Application.OpenForms.OfType<LingyuForm>().FirstOrDefault(f => f.Visible)); return; }
            if (id == "breakthrough") { Game.OpenBreakthrough(); PlaceFeaturePanel(Application.OpenForms.OfType<BreakthroughForm>().FirstOrDefault(f => f.Visible)); return; }
            if (panel == null || panel.IsDisposed) panel = new DesktopPracticePanel(this);
            panel.UpdateState();
            if (!panel.Visible) panel.Show();
            if (panel.WindowState == FormWindowState.Minimized) panel.WindowState = FormWindowState.Normal;
            PlaceFeaturePanel(panel);
            panel.BringToFront(); panel.Activate();
        }
        void PlaceFeaturePanel(Form form)
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
                if (tray != null) { tray.Visible = false; if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose(); tray.Dispose(); }
                if (panel != null) panel.Dispose();
                if (Game != null) { Game.Dispose(); Game = null; }
                owner.Dispose();
                try { profileGate.ReleaseMutex(); } catch (ApplicationException) { }
                profileGate.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class DesktopPracticePanel : Form
    {
        readonly DesktopDemoSession session;
        readonly Label stats = new Label(), notice = new Label();
        readonly Button awaken = new Button(), automatic = new Button(), practice = new Button();
        readonly ComboBox weapons = new ComboBox();
        readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
        string inventoryKey = "";
        internal DesktopPracticePanel(DesktopDemoSession session)
        {
            this.session = session;
            Text = "桌面修仙传 · 修行与操作（隔离档）";
            Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.FromArgb(239, 243, 237); ForeColor = Color.FromArgb(40, 67, 61);
            StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(440, work.Width - 32), Math.Min(640, work.Height - 72));
            MinimumSize = new Size(Math.Min(380, work.Width - 24), Math.Min(400, work.Height - 24));
            var content = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16) };
            Controls.Add(content);
            stats.AutoSize = true; stats.MaximumSize = new Size(385, 0); stats.Margin = new Padding(0, 0, 0, 12); stats.AccessibleName = "当前修仙状态"; content.Controls.Add(stats);
            var hint = new Label { Text = "桌面点妖物：选敌、接近后再点出一招。\n点修士：格挡；拖动：移动；右键：打开本面板。\n关闭或最小化面板后，桌面修仙持续运行。", AutoSize = true, MaximumSize = new Size(385, 0), Margin = new Padding(0, 0, 0, 10) }; content.Controls.Add(hint);
            Add(content, "对选中妖物出招", delegate { session.Game.DesktopStrikeSelected(); });
            Add(content, "格挡", delegate { session.Game.TryBlock(); });
            Configure(awaken, "灵根觉醒 · 十次有效击杀后", delegate { session.Game.AwakenSpiritRoot(); }); content.Controls.Add(awaken);
            Configure(automatic, "自动作战", delegate { session.Game.SetAutoPlan(!session.Game.AutoPlanning); }); content.Controls.Add(automatic);
            Configure(practice, "修炼", delegate { session.Game.SetCultivationPaused(!session.Game.CultivationPaused); }); content.Controls.Add(practice);
            weapons.Width = 380; weapons.DropDownStyle = ComboBoxStyle.DropDownList; weapons.AccessibleName = "已拥有兵器"; content.Controls.Add(weapons);
            Add(content, "装备所选兵器", delegate { WeaponChoice choice = weapons.SelectedItem as WeaponChoice; if (choice != null) session.Game.ChooseWeapon(choice.Id); });
            Add(content, "破境", delegate { session.OpenPanel("breakthrough"); });
            Add(content, "灵域历练", delegate { session.OpenPanel("lingyu"); });
            Add(content, "洞天", delegate { session.OpenPanel("dongtian"); });
            var screenRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 385 };
            foreach (Screen screen in Screen.AllScreens)
            {
                Screen selected = screen; Button button = new Button { Text = "移至屏幕 " + (Array.IndexOf(Screen.AllScreens, screen) + 1), AutoSize = true };
                button.Click += delegate { session.Game.ChangeScreen(selected); }; screenRow.Controls.Add(button);
            }
            content.Controls.Add(screenRow);
            notice.AutoSize = true; notice.MaximumSize = new Size(385, 0); notice.Margin = new Padding(0, 8, 0, 8); notice.AccessibleName = "战斗与保存消息"; content.Controls.Add(notice);
            Add(content, "收起面板 · 继续桌面修仙", delegate { Hide(); });
            Add(content, "保存并退出隔离版", delegate { session.ExitAfterAcceptance(0); });
            refresh.Interval = 500; refresh.Tick += delegate { if (Visible) UpdateState(); }; refresh.Start();
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        }
        internal void UpdateState()
        {
            AdventureController game = session.Game; if (game == null) return;
            var cultivation = game.Cultivation; var progress = game.Battle.Progress;
            stats.Text = (cultivation.Mortal ? "凡人 · 红尘历练 " + cultivation.MortalKills + "/10" : cultivation.RealmName + " · 第" + cultivation.Layer + "层")
                + "\n气血 " + progress.Health + "/" + progress.MaxHealth + "　修为 " + cultivation.Qi
                + "\n" + (progress.Dead ? "力竭调息中 · 自动恢复" : "桌面活动中") + "　" + (game.AutoPlanning ? "自动作战" : "手动作战");
            awaken.Enabled = cultivation.CanAwaken;
            automatic.Enabled = cultivation.AutoBattleUnlocked && !progress.Dead;
            automatic.Text = cultivation.AutoBattleUnlocked ? "自动作战：" + (game.AutoPlanning ? "开启" : "关闭") : "自动作战：觉醒后默认开启";
            practice.Text = "修炼：" + (game.CultivationPaused ? "暂停" : "持续进行");
            string key = string.Join("|", progress.Inventory.ToArray());
            if (key != inventoryKey)
            {
                inventoryKey = key; weapons.Items.Clear();
                foreach (string id in progress.Inventory) weapons.Items.Add(new WeaponChoice { Id = id, Text = WeaponName(id) });
                if (weapons.Items.Count > 0) weapons.SelectedIndex = 0;
            }
            notice.Text = game.DesktopNotice + "\n隔离档：" + Path.GetFileName(session.ProfileRoot) + " · 正常退出保存";
        }
        static string WeaponName(string id) { return id == "spear" ? "灵铁短剑" : id == "greatsword" || id == "wave" || id == "boomerang" ? "青钢剑" : "凝玉剑"; }
        sealed class WeaponChoice { internal string Id, Text; public override string ToString() { return Text; } }
        void Add(FlowLayoutPanel content, string text, Action action) { Button button = new Button(); Configure(button, text, action); content.Controls.Add(button); }
        void Configure(Button button, string text, Action action)
        {
            button.Text = text; button.Width = 380; button.Height = 34; button.FlatStyle = FlatStyle.Flat; button.BackColor = Color.FromArgb(251, 250, 242);
            button.Margin = new Padding(0, 0, 0, 6); button.Click += delegate { action(); UpdateState(); };
        }
        protected override void Dispose(bool disposing) { if (disposing) { refresh.Stop(); refresh.Dispose(); } base.Dispose(disposing); }
    }
}
