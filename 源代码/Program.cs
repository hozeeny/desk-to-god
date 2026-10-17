using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName=".NET Framework 4.8")]

namespace NetBunny
{
    internal static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread] static void Main(string[] args)
        {
            // This process only: legacy csc builds otherwise negotiate obsolete TLS.
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            if (args.Length > 0 && args[0] == "--growth-sim")
            {
                try { Console.WriteLine(CultivationPacing.Report()); Environment.ExitCode = 0; }
                catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
                return;
            }
            if (args.Length > 0 && args[0] == "--perf-probe")
            {
                try
                {
                    double minutes = 3;
                    if (args.Length > 1) Double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out minutes);
                    Console.WriteLine(AdventureController.RunPerfProbe(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets"), minutes));
                    Environment.ExitCode = 0;
                }
                catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
                return;
            }
            if (args.Length > 0 && args[0] == "--selftest")
            {
                try { Console.WriteLine(NetworkEngine.SelfTest()); Console.WriteLine(FirewallManager.SelfTest()); Console.WriteLine(KnightPetController.SelfTest()); Console.WriteLine(TrafficLedger.SelfTest()); Console.WriteLine(SpeedTestView.SelfTest()); Console.WriteLine(NetworkToolsView.SelfTest()); Console.WriteLine(CleanupService.SelfTest()); Console.WriteLine(RegionThemeService.SelfTest()); Console.WriteLine(DiskSpaceService.SelfTest()); Console.WriteLine(LiveTempPopulation.SelfTest()); Console.WriteLine(EnemyBalance.SelfTest()); Console.WriteLine(MemoryReclaimer.SelfTest()); Console.WriteLine(CombatTactics.SelfTest()); Console.WriteLine(EnemyFieldBudget.SelfTest()); Console.WriteLine(CultivationProgress.SelfTest()); Console.WriteLine(CultivationCombatPower.SelfTest()); Console.WriteLine(CultivationEncounterScheduler.SelfTest()); Console.WriteLine(CultivationPacing.SelfTest()); Console.WriteLine(BaiyeProgress.SelfTest()); Console.WriteLine(BaiyeTracker.SelfTest()); Console.WriteLine(LingyuSelfTest.Run()); Console.WriteLine(DongtianProgress.SelfTest()); Console.WriteLine(MaintenanceConsentStore.SelfTest()); Console.WriteLine(HeroPoseRig.SelfTest()); Console.WriteLine(AdventureController.SelfTest()); Console.WriteLine(AdventureRenderer.SelfTest()); Console.WriteLine(AdventureRenderer.AssetsSelfTest(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets"))); Environment.ExitCode = 0; }
                catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
                return;
            }
            if(args.Length>0 && args[0]=="--adventure-preview")
            {
                try { SetProcessDPIAware(); Application.EnableVisualStyles(); AdventureController.ExportPreview(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets"),args.Length>1?args[1]:"adventure-preview.png"); }
                catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; }
                return;
            }
            if(args.Length>0 && args[0]=="--equipment-preview")
            {
                try { SetProcessDPIAware(); Application.EnableVisualStyles(); AdventureController.ExportEquipmentPreview(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets"),args.Length>1?args[1]:"equipment-preview.png"); }
                catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; }
                return;
            }
            if(args.Length>0 && args[0]=="--baiye-preview")
            {
                try { SetProcessDPIAware(); Application.EnableVisualStyles(); AdventureController.ExportBaiyeStelePreview(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets"),args.Length>1?args[1]:"baiye-stele-preview.png"); }
                catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; }
                return;
            }
            if(args.Length>0 && args[0]=="--lingyu-preview")
            {
                try { SetProcessDPIAware(); Application.EnableVisualStyles(); AdventureController.ExportLingyuStelePreview(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets"),args.Length>1?args[1]:"lingyu-preview.png"); }
                catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; }
                return;
            }
            if(args.Length>0 && args[0]=="--breakthrough-preview")
            {
                try { SetProcessDPIAware(); Application.EnableVisualStyles(); AdventureController.ExportBreakthroughPreview(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets"),args.Length>1?args[1]:"breakthrough-preview.png"); }
                catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; }
                return;
            }
            if(args.Length>0 && args[0]=="--adventure-smoke")
            {
                try { SetProcessDPIAware(); Application.EnableVisualStyles(); Console.WriteLine(AdventureController.NativeSmoke(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets"))); }
                catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; }
                return;
            }
            if(args.Length>0 && args[0]=="--skins-preview")
            {
                try { SetProcessDPIAware(); Application.EnableVisualStyles(); AdventureRenderer.ExportPreview(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets"),args.Length>1?args[1]:"skins-preview.png"); }
                catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; }
                return;
            }
            if (args.Length > 0 && args[0] == "--pet-preview")
            {
                try
                {
                    SetProcessDPIAware(); Application.EnableVisualStyles();
                    KnightPetController.ExportPreview(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets","knight-atlas.png"),args.Length>1?args[1]:"knight-preview.png");
                    Environment.ExitCode=0;
                }
                catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; }
                return;
            }
            bool preview = args.Length > 0 && args[0] == "--preview";
            if(args.Length>0 && args[0]=="--center-preview")
            {
                try
                {
                    SetProcessDPIAware(); Application.EnableVisualStyles();
                    using(TrafficLedger ledger=new TrafficLedger(true))
                    using(NetworkCenterForm center=new NetworkCenterForm(true,ledger,delegate{return new List<AppInfo>();},delegate{return "演示数据 · 未修改任何网络规则";},delegate(bool active){}))
                    {
                        int page=0; if(args.Length>1)int.TryParse(args[1],out page);
                        center.ShowPage(page); Application.DoEvents();
                        Stopwatch settle=Stopwatch.StartNew();while(settle.ElapsedMilliseconds<400){Application.DoEvents();Thread.Sleep(15);}center.Refresh();
                        using(Bitmap bitmap=new Bitmap(center.Width,center.Height)){center.DrawToBitmap(bitmap,new Rectangle(Point.Empty,center.Size));bitmap.Save(args.Length>2?args[2]:"network-center-preview.png");}
                        center.Shutdown();
                    }
                    Environment.ExitCode=0;
                }
                catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
                return;
            }
            // V6: pure cultivation mode must not depend on administrator rights.
            // The app starts as the current user; desktop cultivation, adventure and
            // temp-file features all work. Only network capture / firewall / rate
            // limits need elevation, and the main window offers an explicit
            // "restart as administrator" entry for that instead of forcing UAC.
            bool elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            if (args.Length > 0 && args[0] == "--elevated-restart")
            {
                // The previous non-elevated instance releases its mutex while exiting.
                for (int i = 0; i < 40; i++)
                {
                    bool free;
                    using (Mutex probe = new Mutex(true, "Local\\NetBunny.Desktop.v1", out free)) { }
                    if (free) break;
                    Thread.Sleep(500);
                }
            }
            bool first; using (Mutex gate = new Mutex(true, "Local\\NetBunny.Desktop.v1" + (preview ? ".Preview" : ""), out first))
            {
                if (!first) { MessageBox.Show("桌面修仙传已经在运行。\n请先退出正在运行的旧版，再打开。\n可在任务栏或右下角托盘找到已运行的窗口。", "桌面修仙传 · 持续修仙桌宠"); return; }
                SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { MessageBox.Show(e.Exception.Message, "操作没有完成", MessageBoxButtons.OK, MessageBoxIcon.Information); };
                using (MainForm form = new MainForm(preview, elevated))
                {
                    if (preview)
                    {
                        form.Show(); Application.DoEvents();
                        using (Bitmap bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(args.Length > 1 ? args[1] : "preview.png"); }
                        form.Close();
                    }
                    else Application.Run(form);
                }
            }
        }
    }

    internal static class Art
    {
        internal static readonly Color Ink = Color.FromArgb(47, 46, 66), Muted = Color.FromArgb(130, 129, 148), Purple = Color.FromArgb(119, 91, 231), Bg = Color.FromArgb(247, 246, 252), Line = Color.FromArgb(235, 232, 244), Mint = Color.FromArgb(49, 165, 136), Coral = Color.FromArgb(228, 113, 125);
        internal static Font Font(float size, FontStyle style = FontStyle.Regular) { return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point); }
        internal static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height)); GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right-d, r.Y, d, d, 270, 90); p.AddArc(r.Right-d, r.Bottom-d, d, d, 0, 90); p.AddArc(r.X, r.Bottom-d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        internal static void Fill(Graphics g, RectangleF r, float radius, Color c) { using (GraphicsPath p = Rounded(r, radius)) using (Brush b = new SolidBrush(c)) g.FillPath(b,p); }
        internal static void Bunny(Graphics g, Rectangle r)
        {
            GraphicsState state = g.Save(); g.TranslateTransform(r.X, r.Y); g.ScaleTransform(r.Width/120f, r.Height/120f); g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Brush white = new SolidBrush(Color.White)) using (Brush pink = new SolidBrush(Color.FromArgb(252,190,207))) using (Pen ink = new Pen(Ink,3.1f))
            {
                g.FillEllipse(white,28,7,22,57); g.DrawEllipse(ink,28,7,22,57); g.FillEllipse(white,66,1,22,62); g.DrawEllipse(ink,66,1,22,62);
                g.FillEllipse(pink,34,16,10,34); g.FillEllipse(pink,72,11,10,38); g.FillEllipse(white,15,43,90,66); g.DrawEllipse(ink,15,43,90,66);
                using (Brush dark = new SolidBrush(Ink)) { g.FillEllipse(dark,39,69,5,7); g.FillEllipse(dark,75,69,5,7); }
                g.FillEllipse(pink,25,78,15,8); g.FillEllipse(pink,80,78,15,8); g.DrawArc(ink,49,76,11,12,0,150); g.DrawArc(ink,60,76,11,12,30,150);
                g.FillEllipse(white,26,99,23,14); g.FillEllipse(white,72,99,23,14);
            }
            g.Restore(state);
        }
    }

    internal sealed class RoundPanel : Panel
    {
        internal Color FillColor = Color.White;
        public RoundPanel() { DoubleBuffered = true; BackColor = Art.Bg; }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias; Art.Fill(e.Graphics,new Rectangle(0,0,Width-1,Height-1),20,FillColor); }
    }

    internal sealed class SoftButton : Button
    {
        internal Color FillColor = Art.Purple;
        private bool hovered;
        public SoftButton() { FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0; Font=Art.Font(10,FontStyle.Bold); ForeColor=Color.White; Cursor=Cursors.Hand; SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); }
        protected override void OnMouseEnter(EventArgs e) { hovered=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            RoundPanel card=Parent as RoundPanel;e.Graphics.Clear(card!=null?card.FillColor:(Parent is Hero?Color.FromArgb(237,232,255):(Parent == null ? Art.Bg : Parent.BackColor))); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Color c=Enabled ? (hovered ? ControlPaint.Light(FillColor,.12f) : FillColor) : Color.FromArgb(231,229,237);
            Art.Fill(e.Graphics,new Rectangle(0,0,Width-1,Height-1),12,c);
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled?ForeColor:Art.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            if (Focused) using (Pen p=new Pen(Color.FromArgb(100,ForeColor))) { p.DashStyle=DashStyle.Dot; e.Graphics.DrawRectangle(p,5,5,Width-11,Height-11); }
        }
    }

    internal sealed class Hero : Control
    {
        public Hero() { DoubleBuffered=true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
            Art.Fill(g,new Rectangle(0,0,Width-1,Height-1),24,Color.FromArgb(237,232,255));
            Art.Fill(g,new Rectangle(Width-232,-45,180,180),80,Color.FromArgb(228,219,253));
            KnightBrand.DrawHelmet(g,new Rectangle(Width-183,1,111,111));
            using(Font title=Art.Font(23,FontStyle.Bold)) using(Brush b=new SolidBrush(Art.Ink)) g.DrawString("桌面修仙传",title,b,26,17);
            using(Font sub=Art.Font(10)) using(Brush b=new SolidBrush(Color.FromArgb(107,92,156))) g.DrawString("一机一洞天，万机共仙界。修士持续修炼；真实清理与网络管理按需单独授权。",sub,b,29,64);
            using(Font small=Art.Font(9,FontStyle.Bold)) using(Brush b=new SolidBrush(Art.Purple)) g.DrawString("DESK TO GOD  /  06.0",small,b,Width-355,45);
        }
    }

    internal sealed class Metric : Control
    {
        internal string Caption,Value="—",Sub=""; internal Color Accent;
        public Metric(string caption,Color accent) { Caption=caption; Accent=accent; DoubleBuffered=true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias; Art.Fill(g,new Rectangle(0,0,Width-1,Height-1),18,Color.White);
            Art.Fill(g,new Rectangle(17,19,5,57),3,Accent);
            using(Font f=Art.Font(9)) using(Brush b=new SolidBrush(Art.Muted)) { g.DrawString(Caption,f,b,34,15); g.DrawString(Sub,f,b,Width-115,56); }
            using(Font f=Art.Font(22,FontStyle.Bold)) using(Brush b=new SolidBrush(Art.Ink)) g.DrawString(Value,f,b,31,35);
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly bool preview;
        private readonly bool elevated;
        private NetworkEngine engine;
        private FirewallManager firewall;
        private AdventureController pet;
        private TrafficLedger ledger;
        private NetworkCenterForm center;
        private SpeedMiniWindow mini;
        private bool speedTesting;
        private NotifyIcon tray;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem trayPet,trayRoaming;
        private bool closing, refreshing, actionBusy, rendering,resourcesDisposed,trayHintShown;
        private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
        private readonly ToolTip tips=new ToolTip();
        private readonly Hero hero=new Hero();
        private readonly Metric downMetric=new Metric("↓  实时下载",Art.Mint),upMetric=new Metric("↑  实时上传",Art.Purple),appsMetric=new Metric("正在传输的程序",Art.Coral);
        private readonly DiskSpaceBar diskSpace=new DiskSpaceBar();
        private readonly RoundPanel listPanel=new RoundPanel(),detailPanel=new RoundPanel();
        private readonly DataGridView grid=new DataGridView();
        private readonly TextBox search=new TextBox();
        private readonly SoftButton allTab=new SoftButton(),activeTab=new SoftButton(),rulesTab=new SoftButton(),restore=new SoftButton();
        private readonly SoftButton locate=new SoftButton(),block=new SoftButton(),apply=new SoftButton(),clearLimit=new SoftButton();
        private readonly SoftButton petToggle=new SoftButton(),petRoam=new SoftButton(),petDemo=new SoftButton(),hideToTray=new SoftButton();
        private readonly Label petStatus=new Label();
        private readonly SoftButton petEquipment=new SoftButton();
        private readonly SoftButton autoPlan=new SoftButton(),clearField=new SoftButton();
        private readonly SoftButton speedCenter=new SoftButton(),trafficCenter=new SoftButton(),healthCenter=new SoftButton(),infoCenter=new SoftButton(),miniToggle=new SoftButton();
        private readonly SoftButton elevateAdmin=new SoftButton();
        private readonly Label selectedName=new Label(),selectedPath=new Label(),selectedPids=new Label(),selectedState=new Label(),footer=new Label(),feedback=new Label();
        private readonly NumericUpDown down=new NumericUpDown(),up=new NumericUpDown();
        private readonly SoftButton unitKb=new SoftButton(),unitMb=new SoftButton();
        private bool useMegabytes;
        private List<AppInfo> apps=new List<AppInfo>();
        private HashSet<string> blockedPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> managedPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private AppInfo selected;
        private int tab;
        private string selectedKey="";

        internal MainForm(bool isPreview, bool isElevated)
        {
            preview=isPreview; elevated=isElevated; Text="桌面修仙传 · 持续修仙与网络管理"; Font=Art.Font(10); BackColor=Art.Bg; ForeColor=Art.Ink;
            ledger=new TrafficLedger(preview);
            AutoScaleMode=AutoScaleMode.None; ClientSize=new Size(1224,914); MinimumSize=new Size(1000,700); StartPosition=FormStartPosition.CenterScreen;
            Rectangle screen=Screen.PrimaryScreen.WorkingArea;if(Height>screen.Height-20)Height=Math.Max(700,screen.Height-20);if(Width>screen.Width-20)Width=Math.Max(1000,screen.Width-20);
            using(Bitmap image=new Bitmap(64,64)) { using(Graphics g=Graphics.FromImage(image)) KnightBrand.DrawHelmet(g,new Rectangle(0,0,64,64)); IntPtr handle=image.GetHicon(); Icon=(Icon)Icon.FromHandle(handle).Clone(); NativeIcons.DestroyIcon(handle); }
            Controls.AddRange(new Control[]{hero,downMetric,upMetric,appsMetric,diskSpace,listPanel,detailPanel,restore,footer});
            BuildList(); BuildDetails(); BuildPetControls(); BuildNetworkControls(); restore.Text="全部恢复"; restore.FillColor=Color.FromArgb(235,230,251); restore.ForeColor=Art.Purple; restore.Click+=async delegate { await RunAction(delegate { engine.ClearLimits(); firewall.UnblockAll(); },"所有限速和本工具的断网规则已解除。"); };
            footer.ForeColor=Art.Muted; footer.Font=Art.Font(8.7f);footer.AutoEllipsis=true; footer.Text="正在准备网络监控…";
            Resize+=delegate { LayoutPage(); if(!preview&&!closing&&WindowState==FormWindowState.Minimized)MinimizeToTray(); }; LayoutPage();
            timer.Interval=1200; timer.Tick+=async delegate { if(pet!=null&&!closing){diskSpace.SetSnapshot(pet.DiskSnapshot);tips.SetToolTip(diskSpace,diskSpace.Summary);}await RefreshApps(); };
            Shown+=async delegate
            {
                if(preview) { LoadDemo(); return; }
                InitializePet();
                if(!elevated)
                {
                    footer.Text="普通权限运行：修仙、桌面冒险与临时文件清理正常；网络监控、限速与断网需要管理员权限。";
                    tips.SetToolTip(footer,footer.Text);
                    if(!closing)timer.Start();
                    return;
                }
                firewall=new FirewallManager(); engine=new NetworkEngine();
                NetworkEngine startingEngine=engine;
                try { await Task.Run(delegate { startingEngine.Start(); }); } catch(Exception e) { if(!closing)ShowFeedback("监控启动失败："+e.Message,true); }
                if(!closing) {if(!engine.IsCapturing)ShowFeedback(engine.Status,true);await RefreshApps(); if(!closing)timer.Start(); }
            };
            FormClosing+=delegate(object sender,FormClosingEventArgs e) { if(actionBusy){e.Cancel=true;RestoreFromTray();ShowFeedback("正在应用网络设置，请完成后再关闭。",false);return;}closing=true; DisposeResources(); };
        }

        private void BuildPetControls()
        {
            petToggle.Text="桌面冒险 · 开";petToggle.Font=Art.Font(9,FontStyle.Bold);petToggle.SetBounds(28,110,120,36);
            petRoam.Text="全屏漫游";petRoam.Font=Art.Font(9,FontStyle.Bold);petRoam.SetBounds(158,110,92,36);
            petDemo.Text="战斗演示";petDemo.Font=Art.Font(9,FontStyle.Bold);petDemo.SetBounds(260,110,88,36);
            hideToTray.Text="收进托盘";hideToTray.Font=Art.Font(9,FontStyle.Bold);hideToTray.SetBounds(358,110,96,36);
            foreach(SoftButton b in new[]{petRoam,petDemo,hideToTray}) {b.FillColor=Color.FromArgb(224,215,250);b.ForeColor=Art.Purple;}
            petStatus.BackColor=Color.FromArgb(237,232,255);petStatus.Font=Art.Font(8.3f);petStatus.ForeColor=Color.FromArgb(107,92,156);petStatus.AutoEllipsis=true;
            petStatus.Text="点击怪物排队清理，右键角色打开装备。\n临时文件清理释放磁盘空间。";
            petEquipment.Text="装备 / 地区";petEquipment.Font=Art.Font(9,FontStyle.Bold);petEquipment.SetBounds(464,110,106,36);petEquipment.FillColor=Color.FromArgb(224,215,250);petEquipment.ForeColor=Art.Purple;
            petEquipment.Click+=delegate {if(pet!=null)pet.ShowSettings();};
            tips.SetToolTip(petEquipment,"查看等级、经验、武器、内存压力与清理空间；切换地区皮肤。也可右键角色或墓碑打开。");
            autoPlan.Text="自动规划 · 关";autoPlan.SetBounds(580,110,108,36);autoPlan.Font=Art.Font(9,FontStyle.Bold);autoPlan.FillColor=Color.FromArgb(224,215,250);autoPlan.ForeColor=Art.Purple;
            clearField.Text="一键清场";clearField.SetBounds(698,110,108,36);clearField.Font=Art.Font(9,FontStyle.Bold);clearField.FillColor=Art.Coral;
            autoPlan.Click+=delegate {if(pet!=null){pet.SetAutoPlan(!pet.AutoPlanning);UpdatePetControls();}};
            clearField.Click+=delegate {if(pet!=null){pet.ClearField();UpdatePetControls();}};
            tips.SetToolTip(autoPlan,"开启后自动规划目标并清理对应旧临时文件；手动点击优先。关闭会取消尚未开始的自动目标。");
            tips.SetToolTip(clearField,"清场并清理当前这一批怪物对应的临时文件。无需解锁武器；不包括之后新出现的怪物，可在装备栏取消待办。");
            hero.Controls.AddRange(new Control[]{petToggle,petRoam,petDemo,hideToTray,petEquipment,autoPlan,clearField,petStatus});
            petToggle.Click+=delegate {if(pet!=null){pet.Enabled=!pet.Enabled;UpdatePetControls();}};
            petRoam.Click+=delegate {if(pet!=null){pet.Roaming=!pet.Roaming;UpdatePetControls();}};
            petDemo.Click+=delegate {if(pet!=null){pet.Enabled=true;pet.PlayDemo();UpdatePetControls();}};
            hideToTray.Click+=delegate {if(!preview)MinimizeToTray();};
            tips.SetToolTip(petToggle,"显示或隐藏桌面骑士。隐藏后不影响流量监控与现有网络规则。");
            tips.SetToolTip(petRoam,"只切换骑士自由漫游；敌人仍会主动追击和攻击。角色战死或断网时敌人改为巡逻。右键角色打开装备。");
            tips.SetToolTip(petDemo,"演示战斗、武器与动作；演示使用虚拟怪物，不删除真实临时文件，不改变网络连接。");
            tips.SetToolTip(hideToTray,"主界面收进右下角托盘，骑士与网络管理继续运行。关闭窗口则退出整个程序。");
            tips.SetToolTip(petStatus,"动作取决于监测到的实际上传＋下载流量；没下载东西不等于断网。联网状态独立检测，网络状态不明时不会误判死亡。");
            if(preview)return;
            trayMenu=new ContextMenuStrip();trayMenu.Items.Add("打开桌面修仙传",null,delegate{RestoreFromTray();});
            trayPet=new ToolStripMenuItem("显示桌面修士");trayPet.Click+=delegate{if(pet!=null){pet.Enabled=!pet.Enabled;UpdatePetControls();}};trayMenu.Items.Add(trayPet);
            trayRoaming=new ToolStripMenuItem("全屏漫游");trayRoaming.Click+=delegate{if(pet!=null){pet.Roaming=!pet.Roaming;UpdatePetControls();}};trayMenu.Items.Add(trayRoaming);
            trayMenu.Items.Add("战斗与动作演示",null,delegate{if(pet!=null){pet.Enabled=true;pet.PlayDemo();UpdatePetControls();}});
            trayMenu.Items.Add("修行面板与装备",null,delegate{if(pet!=null)pet.ShowSettings();});
            trayMenu.Items.Add("切换自动规划杀怪",null,delegate{if(pet!=null){pet.SetAutoPlan(!pet.AutoPlanning);UpdatePetControls();}});
            trayMenu.Items.Add("一键清场并清理当前批次",null,delegate{if(pet!=null){pet.ClearField();UpdatePetControls();}});
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("测速中心",null,delegate{OpenCenter(0);});
            trayMenu.Items.Add("今日流量与曲线",null,delegate{OpenCenter(1);});
            trayMenu.Items.Add("网络体检",null,delegate{OpenCenter(2);});
            trayMenu.Items.Add("显示 / 隐藏网速小窗",null,delegate{ToggleMini();});
            trayMenu.Items.Add(new ToolStripSeparator());trayMenu.Items.Add("退出桌面修仙传",null,delegate{Close();});
            trayMenu.Opening+=delegate{UpdatePetControls();};
            tray=new NotifyIcon {Icon=Icon,Text="桌面修仙传 · 桌面修士",ContextMenuStrip=trayMenu,Visible=true};
            tray.DoubleClick+=delegate{RestoreFromTray();};
        }

        private void InitializePet()
        {
            try
            {
                string assetDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets");
                pet=new AdventureController(this,assetDirectory);pet.Roaming=true;pet.Enabled=true;UpdatePetControls();
            }
            catch(Exception e)
            {
                if(pet!=null){pet.Dispose();pet=null;}
                petToggle.Text="冒险暂不可用";petToggle.Enabled=petRoam.Enabled=petDemo.Enabled=petEquipment.Enabled=autoPlan.Enabled=clearField.Enabled=false;
                petStatus.Text="骑士未能启动，网络管理可继续使用。\n"+e.Message;tips.SetToolTip(petStatus,petStatus.Text);
            }
        }

        private void BuildNetworkControls()
        {
            speedCenter.Text="测速中心";trafficCenter.Text="今日流量与曲线";healthCenter.Text="网络体检";infoCenter.Text="网络信息";miniToggle.Text="网速悬浮小窗";
            foreach(SoftButton b in new[]{speedCenter,trafficCenter,healthCenter,infoCenter,miniToggle})
            {b.FillColor=Color.FromArgb(235,230,251);b.ForeColor=Art.Purple;b.Font=Art.Font(9,FontStyle.Bold);Controls.Add(b);}
            speedCenter.Click+=delegate{OpenCenter(0);};trafficCenter.Click+=delegate{OpenCenter(1);};healthCenter.Click+=delegate{OpenCenter(2);};infoCenter.Click+=delegate{OpenCenter(3);};miniToggle.Click+=delegate{ToggleMini();};
            tips.SetToolTip(speedCenter,"分别测试下载、上传，以及空闲和负载时的 HTTP 延迟；手动开始才产生测速流量。");
            tips.SetToolTip(trafficCenter,"仅累计本工具运行并成功监控期间的流量，按本机日期保存。");
            elevateAdmin.Text="以管理员身份重启 · 启用网络管理";elevateAdmin.Font=Art.Font(9,FontStyle.Bold);
            elevateAdmin.FillColor=Color.FromArgb(252,226,229);elevateAdmin.ForeColor=Color.FromArgb(181,77,93);
            elevateAdmin.Visible=!elevated&&!preview;
            elevateAdmin.Click+=delegate{RestartElevated();};
            tips.SetToolTip(elevateAdmin,"网络监控、限速和断网需要管理员权限。点击后程序将以管理员身份重启；修仙进度与存档不受影响。");
            Controls.Add(elevateAdmin);
        }
        private void RestartElevated()
        {
            if(closing||preview||elevated)return;
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath){UseShellExecute=true,Verb="runas",WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory,Arguments="--elevated-restart"});
                Close();
            }
            catch(Exception e){ShowFeedback("未能请求管理员权限："+e.Message,true);}
        }

        private string RuleSummary()
        {
            int limits=apps.Count(a=>a.DownloadLimit>0||a.UploadLimit>0);
            string result="当前限速规则："+limits+" 个程序；本工具保留的断网规则："+managedPaths.Count+" 个。";
            if(engine==null||!engine.IsCapturing)result+="\n流量监控不可用，无法确认后台传输，请自行检查。";
            return result;
        }

        private void OpenCenter(int page)
        {
            if(closing||preview)return;
            if(center==null||center.IsDisposed)
                center=new NetworkCenterForm(false,ledger,delegate{return apps.ToList();},RuleSummary,SetTestActivity);
            center.ShowPage(page);
        }

        private void SetTestActivity(bool active)
        {
            if(closing||IsDisposed)return;
            if(InvokeRequired){try{BeginInvoke(new Action<bool>(SetTestActivity),active);}catch(InvalidOperationException){}return;}
            // Capture the previous interval before changing its category.
            if(engine!=null&&ledger!=null)ledger.Observe(engine.TrafficSnapshot(),engine.IsCapturing);
            speedTesting=active;if(ledger!=null)ledger.SetSpeedTestActive(active);
            UpdateNetworkFeatures(false);
        }

        private void ToggleMini()
        {
            if(closing||preview)return;
            if(mini==null||mini.IsDisposed)mini=new SpeedMiniWindow(RestoreFromTray);
            if(mini.Visible)mini.Hide();else {UpdateNetworkFeatures(false);mini.Show();}
        }

        private void UpdateNetworkFeatures(bool record=true)
        {
            if(closing||preview||engine==null)return;
            List<AppInfo> traffic=engine.TrafficSnapshot();
            double download=traffic.Sum(a=>Math.Max(0,a.DownloadBps)),upload=traffic.Sum(a=>Math.Max(0,a.UploadBps));
            if(record&&ledger!=null)ledger.Observe(traffic,engine.IsCapturing);
            if(ledger!=null&&!string.IsNullOrEmpty(ledger.PersistenceWarning)){footer.Text=ledger.PersistenceWarning;tips.SetToolTip(footer,ledger.PersistenceWarning);}
            if(center!=null&&!center.IsDisposed)center.RefreshData();
            if(mini!=null&&!mini.IsDisposed)mini.UpdateRates(download,upload,engine.IsCapturing,speedTesting);
            if(tray!=null){string text="桌面修仙传 ↓"+Speed(download)+" ↑"+Speed(upload)+(speedTesting?" · 测速中":"");tray.Text=text.Length>63?text.Substring(0,63):text;}
        }

        private void UpdatePetControls()
        {
            if(preview||pet==null||closing)return;
            diskSpace.SetSnapshot(pet.DiskSnapshot);tips.SetToolTip(diskSpace,diskSpace.Summary);
            petToggle.Text=pet.Enabled?"桌面冒险 · 开":"桌面冒险 · 隐藏";
            petToggle.FillColor=pet.Enabled?Art.Purple:Color.FromArgb(224,215,250);petToggle.ForeColor=pet.Enabled?Color.White:Art.Purple;
            petRoam.Text=pet.Roaming?"全屏漫游":"原地活动";
            petStatus.Text=pet.CultivationLine+"\n"+pet.StateText;tips.SetToolTip(petStatus,pet.CultivationLine+"\n"+pet.StateText);
            autoPlan.Text=pet.AutoPlanning?"自动规划 · 开":"自动规划 · 关";autoPlan.FillColor=pet.AutoPlanning?Art.Mint:Color.FromArgb(224,215,250);autoPlan.ForeColor=pet.AutoPlanning?Color.White:Art.Purple;autoPlan.Enabled=!pet.IsDemo;clearField.Enabled=pet.CanClearField;
            petToggle.Invalidate();petRoam.Invalidate();
            if(trayPet!=null){trayPet.Checked=pet.Enabled;trayRoaming.Checked=pet.Roaming;}
        }

        private void MinimizeToTray()
        {
            if(preview||closing||tray==null)return;
            Hide();
            if(!trayHintShown){trayHintShown=true;tray.ShowBalloonTip(3000,"桌面修仙传仍在运行","双击托盘图标打开主界面；修士继续挂机修炼，右键图标可隐藏或退出。",ToolTipIcon.Info);}
        }

        private void RestoreFromTray()
        {
            if(closing)return;Show();WindowState=FormWindowState.Normal;Activate();
        }

        private void DisposeResources()
        {
            if(resourcesDisposed)return;resourcesDisposed=true;timer.Stop();
            if(center!=null){center.Shutdown();center.Dispose();center=null;}
            if(mini!=null){mini.Dispose();mini=null;}
            if(tray!=null){tray.Visible=false;tray.Dispose();tray=null;}
            if(trayMenu!=null){trayMenu.Dispose();trayMenu=null;}
            if(pet!=null){pet.Dispose();pet=null;}
            if(engine!=null)engine.Dispose();
            if(ledger!=null)
            {
                if(engine!=null)try{ledger.Observe(engine.TrafficSnapshot(),false);}catch{}
                ledger.Dispose();string saveWarning=ledger.PersistenceWarning;ledger=null;
                if(!preview&&!string.IsNullOrEmpty(saveWarning))MessageBox.Show("最后一次流量记录没有成功保存。最近未保存的一段用量可能丢失，之前保存的记录仍保留。\n\n"+saveWarning,"骑士助手 · 记录保存失败",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
            engine=null;
            timer.Dispose();tips.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if(disposing){closing=true;DisposeResources();}
            base.Dispose(disposing);
        }

        private void BuildList()
        {
            Label heading=MakeLabel("程序们的网络小队",14,FontStyle.Bold); heading.Location=new Point(22,18); heading.Size=new Size(255,29); listPanel.Controls.Add(heading);
            search.BorderStyle=BorderStyle.FixedSingle; search.Font=Art.Font(10); search.AccessibleName="搜索程序名称或路径"; search.TextChanged+=delegate { RenderRows(); };search.HandleCreated+=delegate{NativeIcons.SendMessage(search.Handle,0x1501,new IntPtr(1),"搜索程序 / 路径");}; listPanel.Controls.Add(search);
            allTab.Text="全部程序";activeTab.Text="正在传输";rulesTab.Text="已设规则";
            SoftButton[] tabs={allTab,activeTab,rulesTab}; for(int i=0;i<tabs.Length;i++){ int index=i; tabs[i].Bounds=new Rectangle(22+i*110,59,102,34); tabs[i].Click+=delegate {tab=index;RenderRows();};listPanel.Controls.Add(tabs[i]); }
            grid.BackgroundColor=Color.White; grid.BorderStyle=BorderStyle.None;grid.ReadOnly=true;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.AllowUserToResizeRows=false;grid.AllowUserToResizeColumns=false;grid.RowHeadersVisible=false;
            grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.MultiSelect=false;grid.AutoGenerateColumns=false;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
            grid.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;grid.GridColor=Color.FromArgb(244,242,249);grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;grid.ColumnHeadersHeight=40;grid.RowTemplate.Height=62;
            grid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle { BackColor=Color.FromArgb(249,248,253),ForeColor=Art.Muted,Font=Art.Font(9),SelectionBackColor=Color.FromArgb(249,248,253) };
            grid.DefaultCellStyle=new DataGridViewCellStyle { BackColor=Color.White,ForeColor=Art.Ink,SelectionBackColor=Color.FromArgb(242,238,255),SelectionForeColor=Art.Ink,Font=Art.Font(10),Padding=new Padding(8,0,5,0) };
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name="App",HeaderText="程序 / 进程",FillWeight=43,SortMode=DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn {Name="Down",HeaderText="↓ 下载",FillWeight=19,SortMode=DataGridViewColumnSortMode.NotSortable});
            grid.Columns.Add(new DataGridViewTextBoxColumn {Name="Up",HeaderText="↑ 上传",FillWeight=19,SortMode=DataGridViewColumnSortMode.NotSortable});
            grid.Columns.Add(new DataGridViewTextBoxColumn {Name="State",HeaderText="状态",FillWeight=19,SortMode=DataGridViewColumnSortMode.NotSortable});
            grid.CellPainting+=PaintCell; grid.SelectionChanged+=delegate { if(!rendering&&grid.SelectedRows.Count>0) SelectApp(grid.SelectedRows[0].Tag as AppInfo,false); };
            grid.CellDoubleClick+=delegate { OpenLocation(); }; listPanel.Controls.Add(grid);
            Label hint=MakeLabel("每 1.2 秒刷新  ·  点击程序管理，双击打开位置",8.7f);hint.ForeColor=Art.Muted;hint.Tag="hint";listPanel.Controls.Add(hint);
        }

        private void BuildDetails()
        {
            detailPanel.AutoScroll=true;detailPanel.AutoScrollMinSize=new Size(0,598);
            Label eyebrow=MakeLabel("程序控制台",10,FontStyle.Bold);eyebrow.ForeColor=Art.Purple;eyebrow.SetBounds(22,20,280,25);detailPanel.Controls.Add(eyebrow);
            selectedName.Font=Art.Font(17,FontStyle.Bold);selectedName.SetBounds(22,55,276,33);selectedName.AutoEllipsis=true;selectedName.Text="选一位小伙伴";
            selectedPids.Font=Art.Font(9);selectedPids.ForeColor=Art.Muted;selectedPids.SetBounds(22,91,276,23);
            selectedPath.Font=Art.Font(8.6f);selectedPath.ForeColor=Art.Muted;selectedPath.SetBounds(22,119,276,50);selectedPath.AutoEllipsis=true;selectedPath.Text="在左侧点击程序，查看路径并控制网络。";
            locate.Text="打开程序位置  ↗";locate.FillColor=Color.FromArgb(242,240,250);locate.ForeColor=Art.Ink;locate.SetBounds(22,180,276,38);locate.Click+=delegate { OpenLocation(); };
            block.Text="一键断网";block.FillColor=Color.FromArgb(252,226,229);block.ForeColor=Color.FromArgb(181,77,93);block.SetBounds(22,228,276,42);block.Click+=async delegate
            {
                AppInfo app=selected;if(app==null)return;bool wasBlocked=managedPaths.Contains(app.Path);
                await RunAction(delegate { if(wasBlocked)firewall.Unblock(app.Path);else firewall.Block(app.Path); },wasBlocked?"已恢复这个程序的网络。":"已添加双向断网规则。点击「恢复联网」可解除。");
            };
            selectedState.Font=Art.Font(8.7f);selectedState.ForeColor=Art.Muted;selectedState.SetBounds(22,279,276,23);
            Label limits=MakeLabel("给网速设个上限",12,FontStyle.Bold);limits.SetBounds(22,322,250,25);detailPanel.Controls.Add(limits);
            Label downLabel=MakeLabel("↓ 下载上限",9);downLabel.SetBounds(22,362,130,23);detailPanel.Controls.Add(downLabel);
            Label upLabel=MakeLabel("↑ 上传上限",9);upLabel.SetBounds(168,362,130,23);detailPanel.Controls.Add(upLabel);
            foreach(NumericUpDown n in new[]{down,up}) {n.Minimum=0;n.Maximum=1048576;n.DecimalPlaces=2;n.ThousandsSeparator=true;n.Font=Art.Font(12);n.BorderStyle=BorderStyle.FixedSingle;}
            down.SetBounds(22,390,130,34);up.SetBounds(168,390,130,34);
            unitKb.Text="KB/s";unitMb.Text="MB/s";unitKb.Font=unitMb.Font=Art.Font(8,FontStyle.Bold);unitKb.SetBounds(194,438,50,30);unitMb.SetBounds(248,438,50,30);unitKb.Click+=delegate{SetUnit(false);};unitMb.Click+=delegate{SetUnit(true);};SetUnit(false);
            Label zero=MakeLabel("填 0 表示不限速",8.7f);zero.ForeColor=Art.Muted;zero.SetBounds(22,442,175,23);detailPanel.Controls.Add(zero);
            apply.Text="应用限速";apply.SetBounds(22,480,170,42);apply.Click+=async delegate
            {
                AppInfo app=selected;if(app==null)return;long multiplier=useMegabytes?1048576L:1024L;long d=(long)(down.Value*multiplier),u=(long)(up.Value*multiplier);
                await RunAction(delegate {engine.SetLimit(app.Path,d,u);},d==0&&u==0?"已取消这个程序的限速。":"限速已应用，同一路径下的多个进程共享额度。");
            };
            clearLimit.Text="取消限速";clearLimit.FillColor=Color.FromArgb(242,240,250);clearLimit.ForeColor=Art.Purple;clearLimit.SetBounds(202,480,96,42);clearLimit.Click+=async delegate {AppInfo app=selected;if(app!=null)await RunAction(delegate {engine.SetLimit(app.Path,0,0);},"已取消这个程序的限速。");};
            feedback.Font=Art.Font(8.7f);feedback.ForeColor=Art.Muted;feedback.SetBounds(22,536,276,54);feedback.Text="限速在退出后解除。断网规则保留，\n可随时点击「全部恢复」。";
            foreach(Label label in new[]{selectedName,selectedPath,selectedPids,selectedState,feedback})label.BackColor=Color.Transparent;
            detailPanel.Controls.AddRange(new Control[]{selectedName,selectedPids,selectedPath,locate,block,selectedState,down,up,unitKb,unitMb,apply,clearLimit,feedback});
            UpdateActions();
        }

        private static Label MakeLabel(string text,float size,FontStyle style=FontStyle.Regular) {return new Label {Text=text,Font=Art.Font(size,style),ForeColor=Art.Ink,BackColor=Color.Transparent};}
        private void LayoutPage()
        {
            int w=ClientSize.Width;hero.SetBounds(24,20,w-48,164);int card=(w-84)/4;
            petStatus.SetBounds(820,108,Math.Max(90,hero.Width-840),42);
            SoftButton[] navigation={speedCenter,trafficCenter,healthCenter,infoCenter,miniToggle};
            int navWidth=(w-80)/5;for(int i=0;i<navigation.Length;i++)navigation[i].SetBounds(24+i*(navWidth+8),198,navWidth,34);
            downMetric.SetBounds(24,246,card,95);upMetric.SetBounds(36+card,246,card,95);appsMetric.SetBounds(48+card*2,246,card,95);diskSpace.SetBounds(60+card*3,246,w-(60+card*3)-24,95);
            int bodyHeight=Math.Max(220,ClientSize.Height-397);int leftWidth=w-394;
            listPanel.SetBounds(24,358,leftWidth,bodyHeight);detailPanel.SetBounds(w-346,358,322,bodyHeight);
            search.SetBounds(Math.Max(290,leftWidth-218),20,194,28);
            grid.SetBounds(14,107,leftWidth-28,bodyHeight-146);
            foreach(Control c in listPanel.Controls)if(c.Tag as string=="hint")c.SetBounds(22,bodyHeight-29,leftWidth-40,23);
            restore.SetBounds(w-152,ClientSize.Height-38,128,29);if(elevateAdmin.Visible)elevateAdmin.SetBounds(w-352,ClientSize.Height-38,192,29);
            footer.SetBounds(25,ClientSize.Height-33,elevateAdmin.Visible?w-380:w-195,25);
        }

        private async Task RefreshApps(bool force=false)
        {
            if(preview||refreshing||closing||engine==null||(actionBusy&&!force))return;refreshing=true;
            try
            {
                List<AppInfo> next=null;List<string> blocked=null;List<string> verified=new List<string>();
                await Task.Run(delegate {next=engine.Snapshot();blocked=firewall.BlockedPaths();foreach(string p in blocked)if(firewall.IsBlocked(p))verified.Add(p);});
                if(closing)return;apps=next;managedPaths=new HashSet<string>(blocked,StringComparer.OrdinalIgnoreCase);blockedPaths=new HashSet<string>(verified,StringComparer.OrdinalIgnoreCase);
                foreach(string p in blocked)if(!apps.Any(a=>string.Equals(a.Path,p,StringComparison.OrdinalIgnoreCase)))apps.Add(new AppInfo {Name=Path.GetFileName(p),Path=p,Pids=new List<int>()});
                RenderRows();footer.Text=engine.Status+"  ·  "+firewall.Status;tips.SetToolTip(footer,footer.Text);
                if(pet!=null){pet.UpdateTelemetry(apps.Sum(a=>Math.Max(0,a.DownloadBps)+Math.Max(0,a.UploadBps)),engine.IsCapturing);UpdatePetControls();}
            }
            catch(Exception e){if(!closing){footer.Text="刷新遇到问题："+e.Message;if(pet!=null){pet.UpdateTelemetry(0,false);UpdatePetControls();}if(mini!=null)mini.UpdateRates(0,0,false,speedTesting);}}
            finally
            {
                if(!closing&&engine!=null)try{UpdateNetworkFeatures();}catch(Exception e){footer.Text="流量统计暂不可用："+e.Message;if(mini!=null)mini.UpdateRates(0,0,false,speedTesting);}
                refreshing=false;
            }
        }

        private static string Key(AppInfo a){return string.IsNullOrEmpty(a.Path)?a.Name+":"+string.Join(",",a.Pids):a.Path;}
        private void RenderRows()
        {
            if(closing)return; string query=search.Text.Trim();
            IEnumerable<AppInfo> filtered=apps.Where(a=>query.Length==0||(a.Name??"").IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0||(a.Path??"").IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0);
            if(tab==1)filtered=filtered.Where(a=>a.DownloadBps+a.UploadBps>1);
            if(tab==2)filtered=filtered.Where(a=>managedPaths.Contains(a.Path??"")||a.DownloadLimit>0||a.UploadLimit>0);
            List<AppInfo> view=filtered.OrderByDescending(a=>a.DownloadBps+a.UploadBps).ThenBy(a=>a.Name).ToList();
            string keep=selectedKey;rendering=true;grid.SuspendLayout();grid.Rows.Clear();
            foreach(AppInfo a in view)
            {
                int i=grid.Rows.Add(a.Name,Speed(a.DownloadBps),Speed(a.UploadBps),State(a));grid.Rows[i].Tag=a;
                grid.Rows[i].Cells[1].Style.ForeColor=Art.Mint;grid.Rows[i].Cells[2].Style.ForeColor=Art.Purple;
                grid.Rows[i].Cells[3].Style.ForeColor=blockedPaths.Contains(a.Path??"")?Art.Coral:(a.DownloadLimit>0||a.UploadLimit>0?Art.Purple:Art.Muted);
            }
            grid.ClearSelection();DataGridViewRow found=null;
            foreach(DataGridViewRow row in grid.Rows)if(Key((AppInfo)row.Tag)==keep){found=row;break;}
            if(found==null&&keep.Length==0&&grid.Rows.Count>0)found=grid.Rows[0];
            if(found!=null){found.Selected=true;SelectApp((AppInfo)found.Tag,false);}else{SelectApp(null,false);selectedKey=keep;}
            grid.ResumeLayout();rendering=false;
            allTab.FillColor=tab==0?Art.Purple:Color.FromArgb(246,244,251);activeTab.FillColor=tab==1?Art.Purple:Color.FromArgb(246,244,251);rulesTab.FillColor=tab==2?Art.Purple:Color.FromArgb(246,244,251);
            foreach(SoftButton b in new[]{allTab,activeTab,rulesTab}){b.ForeColor=b.FillColor==Art.Purple?Color.White:Art.Muted;b.Invalidate();}
            downMetric.Value=Speed(apps.Sum(a=>a.DownloadBps));upMetric.Value=Speed(apps.Sum(a=>a.UploadBps));appsMetric.Value=apps.Count(a=>a.DownloadBps+a.UploadBps>1).ToString();appsMetric.Sub="共 "+apps.Count+" 个程序";
            downMetric.Invalidate();upMetric.Invalidate();appsMetric.Invalidate();
        }

        private string State(AppInfo a)
        {
            if(blockedPaths.Contains(a.Path??""))return "已断网";
            if(managedPaths.Contains(a.Path??""))return "规则异常";
            if(a.DownloadLimit>0||a.UploadLimit>0)return "限速中";
            return a.DownloadBps+a.UploadBps>1?"传输中":"待机";
        }
        private void SelectApp(AppInfo app,bool force)
        {
            bool changed=force||selected==null||(app==null?selected!=null:Key(app)!=selectedKey);selected=app;selectedKey=app==null?"":Key(app);
            if(app==null){selectedName.Text="没有匹配的程序";selectedPids.Text="";selectedPath.Text="试试其他搜索词或筛选条件。";selectedState.Text="";UpdateActions();return;}
            selectedName.Text=app.Name;selectedPids.Text="PID "+(app.Pids.Count==0?"—":string.Join(", ",app.Pids.Take(5)))+(app.Pids.Count>5?" …":"");
            selectedPath.Text=string.IsNullOrEmpty(app.Path)?"系统未允许读取这个程序的路径。":app.Path;
            selectedState.Text=State(app)+"  ·  TCP "+app.TcpConnections+" / UDP "+app.UdpEndpoints;
            if(changed){SetUnit(false);down.Value=Math.Min(down.Maximum,app.DownloadLimit/1024m);up.Value=Math.Min(up.Maximum,app.UploadLimit/1024m);}
            block.Text=managedPaths.Contains(app.Path??"")?"恢复联网":"一键断网";
            UpdateActions();
        }
        private void UpdateActions()
        {
            bool valid=selected!=null&&!string.IsNullOrWhiteSpace(selected.Path)&&!actionBusy&&!preview;
            locate.Enabled=selected!=null&&!string.IsNullOrWhiteSpace(selected.Path)&&!preview;
            block.Enabled=valid&&elevated;apply.Enabled=valid&&elevated&&engine!=null&&engine.IsCapturing;clearLimit.Enabled=valid&&elevated;
            down.Enabled=valid&&elevated;up.Enabled=valid&&elevated;unitKb.Enabled=unitMb.Enabled=valid&&elevated;restore.Enabled=!actionBusy&&!preview&&engine!=null&&firewall!=null;
            if(preview){locate.Enabled=block.Enabled=apply.Enabled=clearLimit.Enabled=down.Enabled=up.Enabled=unitKb.Enabled=unitMb.Enabled=true;}
        }
        private void SetUnit(bool megabytes){useMegabytes=megabytes;unitKb.FillColor=megabytes?Color.FromArgb(242,240,250):Art.Purple;unitMb.FillColor=megabytes?Art.Purple:Color.FromArgb(242,240,250);unitKb.ForeColor=megabytes?Art.Purple:Color.White;unitMb.ForeColor=megabytes?Color.White:Art.Purple;unitKb.Invalidate();unitMb.Invalidate();}
        private async Task RunAction(Action action,string success)
        {
            if(actionBusy||preview||closing)return;actionBusy=true;UpdateActions();ShowFeedback("正在应用，请稍等…",false);
            try{await Task.Run(action);if(!closing){while(refreshing&&!closing)await Task.Delay(60);if(closing)return;ShowFeedback(success,false);await RefreshApps(true);SelectApp(selected,true);}}
            catch(Exception e){if(!closing)ShowFeedback(e.Message,true);}
            finally{actionBusy=false;if(!closing)UpdateActions();}
        }
        private void ShowFeedback(string message,bool error){feedback.Text=message;feedback.ForeColor=error?Color.FromArgb(179,68,83):Art.Mint;tips.SetToolTip(feedback,message);}
        private void OpenLocation()
        {
            if(preview||selected==null||string.IsNullOrWhiteSpace(selected.Path))return;
            try{if(File.Exists(selected.Path))Process.Start("explorer.exe","/select,\""+selected.Path+"\"");else ShowFeedback("文件已移动或删除，路径："+selected.Path,true);}
            catch(Exception e){ShowFeedback(e.Message,true);}
        }
        private void PaintCell(object sender,DataGridViewCellPaintingEventArgs e)
        {
            if(e.RowIndex<0||e.ColumnIndex!=0)return;AppInfo app=grid.Rows[e.RowIndex].Tag as AppInfo;if(app==null)return;
            e.PaintBackground(e.ClipBounds,true);Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;Rectangle r=e.CellBounds;
            int hash=(app.Name??"?").GetHashCode()&0x7fffffff;Color[] colors={Color.FromArgb(230,243,239),Color.FromArgb(238,232,252),Color.FromArgb(252,235,221),Color.FromArgb(228,239,252)};
            Art.Fill(g,new Rectangle(r.X+10,r.Y+13,36,36),11,colors[hash%4]);
            string letter=string.IsNullOrEmpty(app.Name)?"?":app.Name.Substring(0,1).ToUpperInvariant();using(Font f=Art.Font(13,FontStyle.Bold))TextRenderer.DrawText(g,letter,f,new Rectangle(r.X+10,r.Y+13,36,36),Art.Purple,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            using(Font f=Art.Font(10,FontStyle.Bold))TextRenderer.DrawText(g,app.Name,f,new Rectangle(r.X+55,r.Y+10,r.Width-60,24),Art.Ink,TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            string note=app.Pids.Count==0?"规则已保留":app.Pids.Count+" 个进程  ·  "+(app.TcpConnections+app.UdpEndpoints)+" 个端点";
            using(Font f=Art.Font(8))TextRenderer.DrawText(g,note,f,new Rectangle(r.X+56,r.Y+34,r.Width-61,20),Art.Muted,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
            using(Pen p=new Pen(grid.GridColor))g.DrawLine(p,r.Left,r.Bottom-1,r.Right,r.Bottom-1);e.Handled=true;
        }
        internal static string Speed(double bps){if(double.IsNaN(bps)||bps<0)return "—";if(bps>=1048576)return(bps/1048576).ToString("0.00")+" MB/s";if(bps>=1024)return(bps/1024).ToString("0.0")+" KB/s";return bps.ToString("0")+" B/s";}
        private void LoadDemo()
        {
            apps=new List<AppInfo>{
                new AppInfo{Name="Chrome",Path=@"C:\Program Files\Google\Chrome\Application\chrome.exe",Pids=new List<int>{4216,9520,10824},TcpConnections=18,UdpEndpoints=4,DownloadBps=2560000,UploadBps=48128},
                new AppInfo{Name="BaiduNetdisk",Path=@"D:\Apps\BaiduNetdisk\BaiduNetdisk.exe",Pids=new List<int>{6124,8180},TcpConnections=12,UdpEndpoints=2,DownloadBps=502784,UploadBps=18432,DownloadLimit=524288,UploadLimit=131072},
                new AppInfo{Name="WeChat",Path=@"D:\Apps\WeChat\WeChat.exe",Pids=new List<int>{9232},TcpConnections=6,UdpEndpoints=3,DownloadBps=253952,UploadBps=132096},
                new AppInfo{Name="CloudMusic",Path=@"D:\Apps\CloudMusic\cloudmusic.exe",Pids=new List<int>{5196,6236},TcpConnections=4,UdpEndpoints=0,DownloadBps=123904,UploadBps=2048},
                new AppInfo{Name="Autodesk Access",Path=@"C:\Program Files\Autodesk\AdODIS\V1\Setup\AdskAccessServiceHost.exe",Pids=new List<int>(),TcpConnections=0,UdpEndpoints=0},
                new AppInfo{Name="Steam",Path=@"D:\Steam\steam.exe",Pids=new List<int>{3892,4012},TcpConnections=3,UdpEndpoints=1}};
            blockedPaths.Add(apps[4].Path);managedPaths.Add(apps[4].Path);selectedKey=apps[1].Path;RenderRows();footer.Text="演示预览 · 此图为示例数据，实际运行显示本机程序";
            restore.Enabled=true;feedback.Text="同一路径的进程共享上限。\n限速随退出解除，断网规则保留。";
            petStatus.Text="骑士冲刺中 · 3.45 MB/s（示例）\n按实际上传＋下载流量变速，不代表宽带测速结果";
            diskSpace.SetSnapshot(DiskSpaceSnapshot.FromValues(512L*1073741824,78L*1073741824));tips.SetToolTip(diskSpace,"演示数据 · "+diskSpace.Summary);
        }
        private static class NativeIcons{[DllImport("user32.dll")]internal static extern bool DestroyIcon(IntPtr icon);[DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,string l);}
    }
}

