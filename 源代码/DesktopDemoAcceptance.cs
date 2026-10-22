using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetBunny
{
    // Native desktop acceptance is opt-in. Normal play never captures the
    // screen, moves the mouse, creates fixtures or accelerates the clock.
    internal sealed class DesktopDemoAcceptance : IDisposable
    {
        private readonly DesktopDemoSession session;
        private readonly string output;
        private readonly bool normalSpeed;
        private bool observedFirstSteps;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch wall = new Stopwatch();
        private readonly List<object> assertions = new List<object>(), captures = new List<object>(), performance = new List<object>(), movieFrames = new List<object>();
        private readonly List<object> displays = new List<object>();
        private readonly Dictionary<string, string> realArchives = new Dictionary<string, string>();
        private readonly Dictionary<string, double> blockedInputSince = new Dictionary<string, double>();
        private readonly Dictionary<IntPtr, double> dialogWaitSince = new Dictionary<IntPtr, double>();
        private readonly Dictionary<IntPtr, string> dialogDiagnostics = new Dictionary<IntPtr, string>();
        private readonly StringBuilder log = new StringBuilder();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private bool busy, finished, sawRest, recovered, waitingRest, restart;
        private int phase, clicks, lastKills, firstClickHealth, movieIndex, exploreSteps;
        private double nextAction, autoStarted, normalStarted = -1, startEnemyWaitSince = -1, nextPerformance, previousCpu, previousSample, movieUntil, nextMovie;
        private long scoreBeforeAuto, qiBeforeAuto;
        private string targetId, movieName, switchOtherId;
        private AdventureController Game { get { return session.Game; } }
        internal DesktopDemoAcceptance(DesktopDemoSession session, string output, bool normalSpeed = false)
        { this.session = session; this.output = output; this.normalSpeed = normalSpeed; }
        internal void Start()
        {
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("验收输出目录必须为空，既有证据不会覆盖。");
            try
            {
                Directory.CreateDirectory(output);
                if (File.Exists(Path.Combine(output, "desktop-acceptance.json"))) throw new IOException("验收输出已存在，不覆盖历史证据。");
                SnapshotRealArchives(); wall.Start(); ReadDisplayConfiguration();
                previousCpu = Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds;
                Assert(Game.IsolatedDesktop && String.Equals(Game.IsolatedProfileRoot, session.ProfileRoot, StringComparison.OrdinalIgnoreCase), "业务控制器使用独立持久化存档");
                Assert(Game.DesktopAcceptanceMaintenanceOff(), "清理、内存、无人值守维护授权及服务全部关闭");
                Assert(!Application.OpenForms.OfType<ArtDemoForm>().Any(), "正式入口不存在传统场景演示窗口");
                string marker = Path.Combine(session.ProfileRoot, "acceptance-expected-restart.json");
                if (File.Exists(marker))
                {
                    Dictionary<string, object> expected = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(marker, Encoding.UTF8));
                    Assert(Convert.ToInt32(expected["processId"]) != Process.GetCurrentProcess().Id, "存档恢复由真正的新进程执行");
                    Game.DesktopAcceptanceVerifyRestart(expected);
                    Assert(true, "新进程恢复同一身份、境界、自动作战、历练及灵域进度");
                    restart = true;
                }
                Game.DesktopSetAcceptanceSpeed(normalSpeed ? 1 : 6);
                Log(normalSpeed ? "NORMAL 全流程×1正常速率，主战斗循环出招尝试间隔至少0.75秒；初始输入/切敌探针另计，无数值或时间快进。" : "ACCEL 验收快进 ×6：相同真实节拍，未修改伤害、冷却、十杀、HP或奖励；所有战斗操作使用本机鼠标输入。");
                timer.Interval = 100; timer.Tick += Tick; timer.Start(); nextAction = Game.Cultivation.Mortal ? 1.4 : .25;
                Application.ApplicationExit += OnApplicationExit;
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }
        private void Tick(object sender, EventArgs args)
        {
            if (busy || finished) return;
            busy = true;
            try
            {
                double now = wall.Elapsed.TotalSeconds;
                if (now > 1500) throw new TimeoutException("真实桌面流程超过 25 分钟，保留证据并拒绝通过。");
                if (!String.IsNullOrEmpty(Game.ArtDemoFailure)) throw new InvalidOperationException(Game.ArtDemoFailure);
                if (!Game.DesktopHeroWindow.Visible || Game.DesktopHeroWindow.IsDisposed) throw new InvalidOperationException("透明角色窗口被关闭或隐藏，验收不能继续。");
                SamplePerformance(now); RecordMovie(now);
                if (now < nextAction) return;
                if (phase == 0)
                {
                    if (normalSpeed && !restart && Game.Cultivation.Mortal && !observedFirstSteps)
                    {
                        DesktopPracticePanel welcome = Application.OpenForms.OfType<DesktopPracticePanel>().FirstOrDefault(f => f.Visible);
                        Assert(welcome != null, "首次零击杀进入即显示可收起的修行手札，操作入口可发现");
                        for (int localY = 40; localY < welcome.ClientSize.Height - 20; localY += 90)
                            Assert(RootAt(welcome.PointToScreen(new Point(6, localY))) == welcome.Handle, "首次手札在真实桌面可见，未被其他应用遮住（" + localY + "）");
                        using (Bitmap firstPanel = DesktopFrameCapture.CaptureRegion(welcome.Bounds, true))
                            firstPanel.Save(Path.Combine(output, "00-first-visible-welcome.png"), ImageFormat.Png);
                        observedFirstSteps = true; nextAction = now + 20;
                        Log("READING 留出20秒阅读首次操作，无任何战斗输入。"); return;
                    }
                    if (normalSpeed && !restart && Game.Cultivation.Mortal)
                    {
                        Assert(!Game.Battle.Progress.Dead && Game.Battle.Progress.Health == Game.Battle.Progress.MaxHealth, "首次阅读20秒未选敌，普通妖物不会主动围攻伤人");
                        session.ClosePanels();
                    }
                    if (!Game.DesktopAcceptanceHasLiveEnemy)
                    {
                        if (startEnemyWaitSince < 0) { startEnemyWaitSince = now; Log("WAIT initial capture needs naturally living visible enemy: " + Game.DesktopAcceptanceEnemySummary()); }
                        if (now - startEnemyWaitSince > 30) throw new TimeoutException("启动后30秒内没有自然存活且窗口可见的妖物，未注入敌人：" + Game.DesktopAcceptanceEnemySummary());
                        nextAction = now + .2; return;
                    }
                    if (startEnemyWaitSince >= 0) Log("WAIT initial natural visible enemy ready after=" + (now - startEnemyWaitSince).ToString("F2") + "s; " + Game.DesktopAcceptanceEnemySummary());
                    Assert(Game.DesktopAcceptanceActorBoundsSafe(), "每个角色/妖物均为小尺寸独立透明窗，无整屏覆盖层");
                    Capture("01-transparent-desktop-start", true);
                    VerifyNativeInput();
                    BeginMovie("desktop-idle-walk", 4);
                    if (restart) { phase = 80; nextAction = now + 8; return; }
                    if (Game.Cultivation.Mortal)
                    {
                        Assert(Game.Cultivation.MortalKills == 0 && !Game.AutoPlanning, "全新凡人 0 杀，自动战斗尚未解锁");
                        AdventureEnemy target = Game.DesktopAcceptanceTarget(null);
                        if (target == null) throw new InvalidOperationException("桌面没有可交互虚拟妖物。");
                        targetId = target.Id; firstClickHealth = target.Health;
                        if (normalSpeed)
                        {
                            AdventureEnemy other = Game.Battle.Enemies.FirstOrDefault(e => e != target && e.IsVirtual && !e.Defeated && !e.VisualDefeated);
                            if (other == null) throw new InvalidOperationException("缺少第二个真实目标，不能验证手动切换。");
                            switchOtherId = other.Id;
                            phase = 11; nextAction = now; return;
                        }
                        phase = 10; nextAction = now;
                    }
                    else if (normalSpeed && (Game.Cultivation.RealmIndex >= 1 || Game.Cultivation.RealmComplete))
                    { session.OpenPanel("training"); phase = 90; nextAction = now + .5; }
                    else if (Game.Cultivation.RealmIndex == 0 && Game.Cultivation.RealmComplete)
                    { session.OpenPanel("breakthrough"); phase = 20; nextAction = now + .7; }
                    else if (Game.Cultivation.RealmIndex >= 1)
                    { OpenCave(); phase = 30; nextAction = now + .8; }
                    else throw new InvalidOperationException("验收初始档须为全新凡人、炼气圆满、筑基或带验收重启标记的档案。");
                }
                else if (phase == 10)
                {
                    AdventureEnemy target = Game.DesktopAcceptanceTarget(targetId);
                    if (target != null && ClickEnemy(target)) { phase = 1; nextAction = now + .3; }
                }
                else if (phase == 11)
                {
                    AdventureEnemy target = Game.DesktopAcceptanceTarget(targetId);
                    if (target != null && ClickEnemy(target)) { phase = 12; nextAction = now + .3; }
                }
                else if (phase == 12)
                {
                    Assert(Game.Battle.Target != null && Game.Battle.Target.Id == targetId, "真实点击首个妖物完成选中");
                    AdventureEnemy other = Game.DesktopAcceptanceTarget(switchOtherId);
                    if (other != null && ClickEnemy(other)) { phase = 13; nextAction = now + .3; }
                }
                else if (phase == 13)
                {
                    Assert(Game.Battle.Target != null && Game.Battle.Target.Id == switchOtherId, "真实点击第二只妖物立即切换当前目标，不再仅排队");
                    AdventureEnemy target = Game.DesktopAcceptanceTarget(targetId);
                    if (target != null && ClickEnemy(target)) { phase = 14; nextAction = now + .3; }
                }
                else if (phase == 14)
                {
                    Assert(Game.Battle.Target != null && Game.Battle.Target.Id == targetId, "真实点击可切回第一只妖物");
                    phase = 1; nextAction = now;
                }
                else if (phase == 1)
                {
                    AdventureEnemy target = Game.DesktopAcceptanceTarget(targetId);
                    if (target == null) throw new InvalidOperationException("单击验收目标提前消失。");
                    firstClickHealth = target.Health; phase = 2; nextAction = now + .9;
                }
                else if (phase == 2)
                {
                    AdventureEnemy target = Game.DesktopAcceptanceTarget(targetId);
                    Assert(target != null && target.Health == firstClickHealth && !Game.AutoPlanning, "只点击一次后不会自行连续攻击；自动接近不等于自动战斗");
                    phase = 3; nextAction = now; BeginMovie("mortal-manual-combat", 7);
                }
                else if (phase == 3) MortalCombat(now);
                else if (phase == 4)
                {
                    if (Game.DesktopAcceptanceDead)
                    {
                        sawRest = true; Capture("03-mortal-resting", true); phase = 5;
                        Log("REST 自然战斗力竭，等待正式调息恢复；没有注入死亡或改血。");
                    }
                    else if (now - autoStarted > 80) throw new TimeoutException("等待自然力竭超过 80 秒。");
                }
                else if (phase == 5)
                {
                    if (Game.DesktopAcceptanceDead) return;
                    recovered = true;
                    Assert(!Game.AutoPlanning && Game.DesktopAcceptanceDeathTime < 1, "凡人自动调息恢复，死亡计时归零且仍需手动战斗");
                    Capture("04-mortal-recovered", false); phase = 3; nextAction = now + .2;
                }
                else if (phase == 6)
                {
                    Button awaken = FindButton("觉醒");
                    if (awaken == null || !awaken.Enabled) throw new InvalidOperationException("十杀后按需修行面板没有可用的觉醒按钮。");
                    if (!File.Exists(Path.Combine(output, "05-ten-kills-awaken-panel.png"))) Capture("05-ten-kills-awaken-panel", false);
                    if (ClickControl(awaken)) { phase = 7; nextAction = now + .6; }
                }
                else if (phase == 7)
                {
                    Assert(!Game.Cultivation.Mortal && Game.ArtDemoHeroStage == "qishi" && Game.Cultivation.AutoBattleUnlocked && Game.Cultivation.AutoBattle && Game.AutoPlanning, "主动觉醒后同人外观切为炼气，自动战斗默认开启");
                    session.ClosePanels(); scoreBeforeAuto = Game.Battle.Progress.Score; qiBeforeAuto = Game.Cultivation.LifetimeQi; autoStarted = now;
                    Capture("06-qishi-desktop-automatic", false); BeginMovie("qishi-automatic-combat", 8); phase = 8;
                }
                else if (phase == 8)
                {
                    if (Game.Battle.Progress.Score > scoreBeforeAuto && Game.Cultivation.LifetimeQi > qiBeforeAuto && now - autoStarted > 8)
                    {
                        Assert(true, "功能面板收起后零战斗点击仍自动击杀并持续修炼");
                        Assert(sawRest && recovered, "本轮真实桌面战斗经过力竭与调息恢复");
                        Game.DesktopSetAcceptanceSpeed(1); normalStarted = now; phase = 9;
                        Log(normalSpeed ? "PERF 继续保持正常×1；功能面板关闭、零输入观察120秒。" : "PERF 结束验收快进，切为正常 ×1；保持功能面板关闭、零输入观察 120 秒，记录常驻性能。");
                    }
                    else if (now - autoStarted > 180) throw new TimeoutException("炼气关闭面板后未在 180 秒内观察到自动击杀及修为增长。");
                }
                else if (phase == 9)
                {
                    if (now - normalStarted < 120) return;
                    if (!Game.DesktopAcceptanceHasLiveEnemy)
                    { if (now - normalStarted > 180) throw new TimeoutException("正常速率观察后桌面没有可验证的存活妖物。"); return; }
                    Assert(true, "正常×1速率持续120秒，面板关闭且没有额外鼠标输入");
                    Capture("07-qishi-panels-closed-still-cultivating", true); Finish(true, null);
                }
                else if (phase == 20)
                {
                    Capture("20-qishi-full-breakthrough-panel", false);
                    Button attempt = FindButton("发起破境");
                    if (attempt == null || !attempt.Enabled) throw new InvalidOperationException("圆满测试档没有可用的正式破境入口。");
                    if (ClickControl(attempt)) { phase = 21; nextAction = now + .4; }
                }
                else if (phase == 21)
                {
                    IntPtr dialog = OwnDialog("破境仪式");
                    if (dialog == IntPtr.Zero) return;
                    if (ClickDialogOk(dialog)) { phase = 22; nextAction = now + .4; }
                }
                else if (phase == 22)
                {
                    IntPtr success = OwnDialog("破境成功"), failed = OwnDialog("破境未成");
                    if (failed != IntPtr.Zero) { ClickDialogOk(failed); throw new InvalidOperationException("正式保底破境意外失败。"); }
                    if (success == IntPtr.Zero) return;
                    if (ClickDialogOk(success)) { phase = 23; nextAction = now + .7; }
                }
                else if (phase == 23)
                {
                    Assert(Game.Cultivation.RealmIndex == 1 && Game.ArtDemoHeroStage == "zhuji", "炼气圆满经正式确认破境为筑基，外观由修仙状态真实切换");
                    Capture("21-zhuji-after-real-breakthrough", false); OpenCave(); phase = 30; nextAction = now + .8;
                }
                else if (phase == 30)
                {
                    Assert(Game.DongtianUnlocked && VisiblePanel<DongtianForm>() != null, "筑基洞天按需面板可打开");
                    Capture("30-dongtian-panel-on-real-desktop", false);
                    Button plant = FindButton("种"); if (plant != null && plant.Enabled && !ClickControl(plant)) return;
                    phase = 31; nextAction = now + .6;
                }
                else if (phase == 31)
                {
                    session.ClosePanels(); qiBeforeAuto = Game.Cultivation.LifetimeQi; autoStarted = now;
                    phase = 32; nextAction = now + 4;
                }
                else if (phase == 32)
                {
                    Assert(Game.Cultivation.LifetimeQi > qiBeforeAuto && Game.DesktopHeroWindow.Visible, "洞天收起后修士仍在桌面持续修行");
                    session.OpenPanel("lingyu"); phase = 40; nextAction = now + .6;
                }
                else if (phase == 40) Explore(now);
                else if (phase == 41)
                {
                    if (Game.Lingyu.CombatPending)
                    {
                        if (now - autoStarted > 150) throw new TimeoutException("灵域镜妖的正式自动战斗超过 150 秒。");
                        return;
                    }
                    Assert(!Game.DesktopAcceptanceDead && Game.Battle.Progress.Score > scoreBeforeAuto, "灵域镜妖经正式战斗胜利结算，探索继续");
                    Capture("42-lingyu-combat-settled", false); Finish(true, null);
                }
                else if (phase == 80)
                {
                    Assert(Game.DesktopAcceptanceMaintenanceOff(), "重启后维护授权仍关闭");
                    Capture("80-restored-profile-on-real-desktop", false); Finish(true, null);
                }
                else if (phase == 90)
                {
                    DesktopPracticePanel handbook = VisiblePanel<DesktopPracticePanel>();
                    Assert(handbook != null, "修行手札可打开功法与成长目标页");
                    CapturePanel(handbook, "10-training-handbook");
                    Button baiye = FindButton("百业道碑");
                    if (baiye == null) throw new InvalidOperationException("修行页缺少百业入口。");
                    if (ClickControl(baiye)) { phase = 91; nextAction = now + .6; }
                }
                else if (phase == 91)
                {
                    BaiyeSteleForm stele = VisiblePanel<BaiyeSteleForm>();
                    Assert(stele != null && Game.Baiye.Mode == 0 && !Game.BaiyeExplained, "手札真实按钮打开百业道碑，未开启识别或代替玩家确认隐私");
                    CapturePanel(stele, "11-baiye-from-handbook");
                    session.ClosePanels();
                    if (Game.Cultivation.RealmIndex == 0) { session.OpenPanel("breakthrough"); phase = 20; }
                    else { OpenCave(); phase = 30; }
                    nextAction = now + .8;
                }
            }
            catch (Exception error) { Finish(false, error.ToString()); }
            finally { busy = false; }
        }
        private void MortalCombat(double now)
        {
            if (Game.Cultivation.MortalKills != lastKills)
            { lastKills = Game.Cultivation.MortalKills; Log("KILL 有效击杀=" + lastKills + "; native mouse clicks=" + clicks); }
            if (Game.DesktopAcceptanceDead)
            {
                if (!sawRest) { sawRest = true; Capture("03-mortal-resting", true); }
                phase = 5; return;
            }
            if (Game.Cultivation.CanAwaken)
            {
                Assert(Game.Cultivation.MortalKills == CultivationBalance.MortalAwakenKills && !Game.AutoPlanning, "十次真实有效击杀达成，仍由玩家主动觉醒");
                session.OpenPanel("practice"); phase = 6; nextAction = now + .6; return;
            }
            AdventureEnemy target = Game.DesktopAcceptanceTarget(targetId);
            if (target == null) { target = Game.DesktopAcceptanceTarget(null); if (target == null) return; targetId = target.Id; }
            if (!waitingRest && !sawRest && Game.Cultivation.MortalKills >= 4)
            {
                if (!ClickEnemy(target)) return;
                waitingRest = true; autoStarted = now; phase = 4;
                Log("REST 停止出招，让已选敌人自然还击以验证力竭，不注入HP。"); return;
            }
            if (Game.DesktopAcceptanceCanBlock)
            { ClickActor(Game.DesktopHeroWindow); nextAction = now + .1; return; }
            ClickEnemy(target); nextAction = now + (normalSpeed ? .75 : .14);
        }
        private void OpenCave() { session.ClosePanels(); session.OpenPanel("dongtian"); }
        private void Explore(double now)
        {
            LingyuForm panel = VisiblePanel<LingyuForm>();
            if (panel == null) throw new InvalidOperationException("灵域面板未显示。");
            if (Game.Lingyu.CombatPending)
            {
                Assert(Game.DesktopAcceptanceMirrorPresent, "灵域探索真实生成桌面独立可交互镜妖");
                session.ClosePanels(); Capture("41-lingyu-mirror-on-desktop", true, false, true); BeginMovie("lingyu-mirror-automatic", 8);
                scoreBeforeAuto = Game.Battle.Progress.Score; autoStarted = now; phase = 41; return;
            }
            if (!Game.Lingyu.ExpeditionActive)
            {
                Button enter = FindButton("进入蜃楼") ?? FindButton("继续探索");
                if (enter == null || !enter.Enabled) throw new InvalidOperationException("筑基测试档没有可用的千面蜃楼入口。");
                if (ClickControl(enter)) nextAction = now + .5; return;
            }
            string[] choices = Game.Mirage.CurrentChoices();
            int choiceIndex = 0;
            for (int i = 0; i < choices.Length; i++) if (choices[i].Contains("击碎") || choices[i].Contains("直面")) { choiceIndex = i; break; }
            if (choices.Length == 0) return;
            Button choice = FindButtonExact(choices[choiceIndex]);
            if (choice == null || !choice.Enabled) return;
            if (!ClickControl(choice)) return;
            Log("LINGYU 正式选择：" + choices[choiceIndex]); nextAction = now + .5;
            if (++exploreSteps > 45) throw new InvalidOperationException("灵域探索 45 次正式选择仍未遇到镜妖。");
        }
        private void VerifyNativeInput()
        {
            bool running = Game.PauseArtDemoCapture(); Point originalCursor = Cursor.Position;
            try
            {
                KnightLayeredWindow hero = Game.DesktopHeroWindow;
                using (Bitmap frame = SnapshotWindowFrame(hero))
                using (InputProbe underlay = new InputProbe())
                {
                    underlay.Bounds = hero.Bounds; IntPtr foreground = GetForegroundWindow(); underlay.Show(); underlay.Refresh();
                    if (!ArtDemoWindowOrdering.RaiseWithoutActivation(hero.Handle))
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    Pump(90);
                    int heroOrder = WindowZOrder(hero.Handle), underlayOrder = WindowZOrder(underlay.Handle);
                    Log("INPUT probe setup visible=" + underlay.Visible + "; nativeVisible=" + IsWindowVisible(underlay.Handle) + "; style=0x" + GetWindowLongPtr(underlay.Handle, -20).ToInt64().ToString("X")
                        + "; underlay=" + underlay.Handle + "; underlayBounds=" + underlay.Bounds + "; underlayZ=" + underlayOrder + "; hero=" + hero.Handle + "; heroBounds=" + hero.Bounds + "; heroZ=" + heroOrder
                        + "; cursor=" + Cursor.Position + "; capture=" + GetCapture());
                    foreach (Point local in new[] { new Point(5, 5), new Point(frame.Width - 6, 5), new Point(5, frame.Height - 6), new Point(frame.Width - 6, frame.Height - 6), new Point(frame.Width / 2, frame.Height / 2) })
                    {
                        Point screen = new Point(hero.Left + local.X, hero.Top + local.Y);
                        Log("INPUT probe sample local=" + local + "; alpha=" + frame.GetPixel(local.X, local.Y).A + "; screen=" + screen + "; root=" + RootAt(screen));
                    }
                    Assert(heroOrder >= 0 && underlayOrder >= 0 && heroOrder < underlayOrder && underlay.Bounds == hero.Bounds, "局部测试底窗置于角色下方，原生Z序及220×240边界一致");
                    Assert(GetForegroundWindow() == foreground, "透明角色与测试底窗显示时不抢占前台焦点");
                    Point transparent = FindPixel(hero, frame, false, underlay.Handle);
                    Assert(RootAt(transparent) == underlay.Handle, "零alpha透明区域的真实Windows命中穿透角色");
                    int underneathBefore = underlay.Clicks;
                    if (!NativeClick(transparent, underlay.Handle, "transparent-probe")) throw new InvalidOperationException("静止透明穿透探针未命中局部底窗。");
                    Pump(90);
                    Log("INPUT transparent-probe requested=" + transparent + "; actual=" + Cursor.Position + "; actualRoot=" + RootAt(Cursor.Position) + "; expectedUnderlay=" + underlay.Handle + "; underlayBounds=" + underlay.Bounds + "; heroBounds=" + hero.Bounds + "; underlayClicks=" + underneathBefore + "→" + underlay.Clicks);
                    Assert(underlay.Clicks == underneathBefore + 1, "透明区域的真实鼠标点击到达下方窗口");
                    Point opaque = FindPixel(hero, frame, true); Assert(RootAt(opaque) == hero.Handle, "角色可见区域正确接受真实鼠标命中");
                    int clicksOnActor = 0; MouseEventHandler counted = delegate { clicksOnActor++; }; hero.MouseClick += counted;
                    Point before = hero.Location; double block = Game.DesktopAcceptanceBlockCooldown;
                    try
                    {
                        NativeMove(opaque); Pump(20);
                        if (RootAt(Cursor.Position) != hero.Handle) throw new InvalidOperationException("拖动按下前角色被遮挡，拒绝点击其他应用。");
                        MouseButton(true); Pump(45);
                        Size threshold = SystemInformation.DragSize;
                        NativeMove(new Point(opaque.X + Math.Max(32, threshold.Width * 4), opaque.Y - Math.Max(25, threshold.Height * 4))); Pump(80);
                        MouseButton(false); Pump(80);
                        Assert(AdventureBattle.Distance(before.X, before.Y, hero.Left, hero.Top) > 12, "真实鼠标拖动角色可以改变桌面位置");
                        Assert(clicksOnActor == 0 && Math.Abs(Game.DesktopAcceptanceBlockCooldown - block) < .001, "拖动结束不会误触格挡或点击动作");
                    }
                    finally { hero.MouseClick -= counted; MouseButton(false); }
                    underlay.Bounds = hero.Bounds;
                    using (Bitmap moved = SnapshotWindowFrame(hero))
                    {
                        if (!NativeClick(FindPixel(hero, moved, true), hero.Handle, "hero-block")) throw new InvalidOperationException("静止可见角色探针未命中角色。");
                        Pump(90);
                        Assert(Game.DesktopAcceptanceBlockCooldown > block, "可见角色真实点击触发格挡及冷却");
                    }
                    Assert(GetForegroundWindow() == foreground, "选择和拖动透明角色不抢占其他应用焦点");
                    using (Bitmap clicked = SnapshotWindowFrame(hero))
                    {
                        Point alpha = FindPixel(hero, clicked, false);
                        long packed = ((long)(ushort)alpha.Y << 16) | (ushort)alpha.X;
                        Assert(SendMessage(hero.Handle, 0x84, IntPtr.Zero, new IntPtr(packed)).ToInt64() == -1, "透明坐标WM_NCHITTEST返回HTTRANSPARENT（含负屏幕坐标编码）");
                        Assert(SendMessage(hero.Handle, 0x21, IntPtr.Zero, IntPtr.Zero).ToInt64() == 3, "角色WM_MOUSEACTIVATE明确返回MA_NOACTIVATE");
                    }
                }
                VerifyMonitorDrag(hero);
                ArtDemoPixelVerifier.TransparentNegativeCheck(); Assert(true, "纯透明画面负例会被截图像素校验拒绝");
            }
            finally { MouseButton(false); Cursor.Position = originalCursor; Game.ResumeArtDemoCapture(running); }
        }
        private void ReadDisplayConfiguration()
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                Dictionary<string, object> info = new Dictionary<string, object> { { "device", screen.DeviceName }, { "primary", screen.Primary }, { "bounds", screen.Bounds.ToString() }, { "workingArea", screen.WorkingArea.ToString() } };
                try
                {
                    IntPtr monitor = MonitorFromPoint(new Point(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top + screen.Bounds.Height / 2), 2);
                    uint dx, dy; int dpiResult = GetDpiForMonitor(monitor, 0, out dx, out dy), scale;
                    int scaleResult = GetScaleFactorForMonitor(monitor, out scale);
                    info["effectiveDpiX"] = dpiResult == 0 ? (object)dx : null; info["effectiveDpiY"] = dpiResult == 0 ? (object)dy : null;
                    info["scalePercent"] = scaleResult == 0 ? (object)scale : null;
                }
                catch (Exception error) { info["dpiQueryUnavailable"] = error.GetType().Name; }
                displays.Add(info); Log("DISPLAY " + json.Serialize(info));
            }
            try { Log("DISPLAY heroWindowDpi=" + GetDpiForWindow(Game.DesktopHeroWindow.Handle) + "; queryOnly=true; system settings unchanged"); }
            catch (EntryPointNotFoundException) { Log("DISPLAY GetDpiForWindow unavailable; system settings unchanged"); }
        }
        private void VerifyMonitorDrag(KnightLayeredWindow hero)
        {
            Screen[] screens = Screen.AllScreens;
            if (screens.Length < 2) { Log("SKIP 跨屏真实拖动：本次Windows只检测到一块显示器，未冒充多屏实测。"); return; }
            Rectangle original = hero.Bounds;
            Screen origin = Screen.FromPoint(new Point(original.Left + original.Width / 2, original.Top + original.Height / 2));
            Screen destination = screens.FirstOrDefault(s => s.DeviceName != origin.DeviceName && s.WorkingArea.Width >= hero.Width + 24 && s.WorkingArea.Height >= hero.Height + 24);
            if (destination == null) throw new InvalidOperationException("另一显示器有效工作区不足以容纳角色，无法安全验证跨屏拖动。");
            Point target = new Point(destination.WorkingArea.Left + (destination.WorkingArea.Width - hero.Width) / 2,
                destination.WorkingArea.Top + (destination.WorkingArea.Height - hero.Height) / 2);
            IntPtr foreground = GetForegroundWindow(); double block = Game.DesktopAcceptanceBlockCooldown;
            int actorClicks = 0; MouseEventHandler counted = delegate { actorClicks++; }; hero.MouseClick += counted;
            try
            {
                DragHeroTo(hero, target);
                Log("MULTISCREEN native drag from=" + origin.DeviceName + " " + original + "; target=" + destination.DeviceName + " " + destination.WorkingArea + "; actual=" + hero.Bounds + "; windowDpi=" + GetDpiForWindow(hero.Handle));
                Assert(destination.WorkingArea.Contains(hero.Bounds) && Screen.FromRectangle(hero.Bounds).DeviceName == destination.DeviceName, "真实鼠标跨屏拖动后角色完整位于目标显示器工作区");
                Assert(actorClicks == 0 && Math.Abs(Game.DesktopAcceptanceBlockCooldown - block) < .001, "跨屏拖动不会触发格挡或点击动作");
                Capture("00-multi-monitor-secondary", false, true);
                DragHeroTo(hero, original.Location);
                Log("MULTISCREEN native drag back to=" + origin.DeviceName + "; requested=" + original + "; actual=" + hero.Bounds + "; windowDpi=" + GetDpiForWindow(hero.Handle));
                Assert(origin.WorkingArea.Contains(hero.Bounds) && Screen.FromRectangle(hero.Bounds).DeviceName == origin.DeviceName, "真实鼠标可拖回原显示器且角色完整落在有效工作区");
                Assert(actorClicks == 0 && Math.Abs(Game.DesktopAcceptanceBlockCooldown - block) < .001 && GetForegroundWindow() == foreground, "跨屏往返不抢前台焦点、不误触格挡");
            }
            finally { MouseButton(false); hero.MouseClick -= counted; }
        }
        private void DragHeroTo(KnightLayeredWindow hero, Point topLeft)
        {
            Point start;
            using (Bitmap frame = SnapshotWindowFrame(hero)) start = FindPixel(hero, frame, true);
            Point original = hero.Location, end = new Point(start.X + topLeft.X - original.X, start.Y + topLeft.Y - original.Y);
            NativeMove(start);
            if (RootAt(Cursor.Position) != hero.Handle) throw new InvalidOperationException("跨屏拖动起点没有真实命中本角色，拒绝操作其他应用。");
            MouseButton(true); Pump(35);
            try
            {
                Assert(hero.Capture && GetCapture() == hero.Handle, "跨屏拖动由角色自身真实鼠标捕获承载");
                for (int step = 1; step <= 12; step++)
                {
                    NativeMove(new Point(start.X + (end.X - start.X) * step / 12, start.Y + (end.Y - start.Y) * step / 12)); Pump(12);
                }
            }
            finally { MouseButton(false); Pump(60); }
        }
        private Point FindPixel(KnightLayeredWindow window, Bitmap frame, bool opaque, IntPtr expectedUnderlay = default(IntPtr))
        {
            List<Point> candidates = new List<Point>();
            const int safetyRadius = 3;
            for (int y = opaque ? frame.Height / 4 : safetyRadius; y < (opaque ? frame.Height * 4 / 5 : frame.Height - safetyRadius); y += 2)
                for (int x = safetyRadius; x < frame.Width - safetyRadius; x += 2)
                {
                    bool safe = true;
                    for (int yy = y - safetyRadius; safe && yy <= y + safetyRadius; yy++)
                        for (int xx = x - safetyRadius; xx <= x + safetyRadius; xx++)
                        { byte a = frame.GetPixel(xx, yy).A; if (opaque ? a <= 245 : a != 0) { safe = false; break; } }
                    if (safe) candidates.Add(new Point(window.Left + x, window.Top + y));
                }
            foreach (Point point in candidates.OrderBy(p => AdventureBattle.Distance(p.X, p.Y, window.Left + frame.Width / 2, window.Top + frame.Height / 2)))
            {
                IntPtr target = opaque ? window.Handle : expectedUnderlay;
                bool matches = true;
                if (target != IntPtr.Zero)
                    for (int dy = -safetyRadius; matches && dy <= safetyRadius; dy += safetyRadius)
                        for (int dx = -safetyRadius; dx <= safetyRadius; dx += safetyRadius)
                            if (RootAt(new Point(point.X + dx, point.Y + dy)) != target) { matches = false; break; }
                if (matches) return point;
            }
            throw new InvalidOperationException("找不到未被遮挡的" + (opaque ? "可见角色" : "零alpha透明") + "鼠标采样点。");
        }
        private bool ClickEnemy(AdventureEnemy enemy)
        {
            KnightLayeredWindow window;
            if (!Game.DesktopEnemyWindows.TryGetValue(enemy.Id, out window)) return false;
            return ClickActor(window);
        }
        private bool ClickActor(KnightLayeredWindow window)
        {
            using (Bitmap frame = SnapshotWindowFrame(window))
            {
                if (!NativeClick(FindPixel(window, frame, true), window.Handle, "actor-" + window.Text)) return false;
            }
            clicks++; return true;
        }
        private static Bitmap SnapshotWindowFrame(KnightLayeredWindow window)
        {
            Bitmap frame = typeof(KnightLayeredWindow).GetField("current", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window) as Bitmap;
            if (frame == null) throw new InvalidOperationException("真实透明窗尚未呈现任何位图。");
            return (Bitmap)frame.Clone();
        }
        private Button FindButton(string text) { return Buttons().FirstOrDefault(b => b.Text.Contains(text) && b.Visible); }
        private Button FindButtonExact(string text) { return Buttons().FirstOrDefault(b => b.Text == text && b.Visible); }
        private IEnumerable<Button> Buttons()
        { foreach (Form panel in Application.OpenForms.Cast<Form>().Where(f => f.Visible && !(f is KnightLayeredWindow))) foreach (Button b in DescendantButtons(panel)) yield return b; }
        private static IEnumerable<Button> DescendantButtons(Control root)
        {
            Button button = root as Button; if (button != null) yield return button;
            foreach (Control child in root.Controls) foreach (Button nested in DescendantButtons(child)) yield return nested;
        }
        private static T VisiblePanel<T>() where T : Form { return Application.OpenForms.OfType<T>().FirstOrDefault(f => f.Visible && !f.IsDisposed); }
        private bool ClickControl(Control control)
        {
            for (Control parent = control.Parent; parent != null; parent = parent.Parent)
            { ScrollableControl scroll = parent as ScrollableControl; if (scroll != null) scroll.ScrollControlIntoView(control); }
            Point point = control.PointToScreen(new Point(control.Width / 2, control.Height / 2));
            if (!NativeClick(point, control.FindForm().Handle, "button-" + control.Text)) return false;
            clicks++; return true;
        }
        private void Capture(string name, bool requireEnemy, bool heroOnly = false, bool requireMirror = false)
        {
            bool running = Game.PauseArtDemoCapture(); List<ArtDemoActorFrame> actors = null;
            Bitmap actual = null, background = null, expected = null;
            try
            {
                actors = Game.SnapshotArtDemoActors();
                ArtDemoActorFrame hero = actors.FirstOrDefault(a => a.Role == "hero");
                if (hero == null) throw new InvalidOperationException("没有真实显示的主角窗口。");
                Rectangle bounds;
                if (heroOnly) { bounds = hero.Bounds; bounds.Inflate(28, 28); bounds = Rectangle.Intersect(bounds, SystemInformation.VirtualScreen); }
                else bounds = CaptureBounds(actors, true);
                actual = DesktopFrameCapture.CaptureRegion(bounds);
                actual.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
                try
                { foreach (ArtDemoActorFrame actor in actors) actor.Window.Hide(); background = DesktopFrameCapture.CaptureRegion(bounds, false); }
                finally
                { foreach (ArtDemoActorFrame actor in actors.OrderByDescending(a => a.ZOrder)) if (!actor.Window.IsDisposed) actor.Window.Show(); }
                expected = ArtDemoPixelVerifier.ExpectedComposite(background, actors, bounds.Location);
                List<object> checks = new List<object>(); checks.Add(ArtDemoPixelVerifier.VerifyActor(hero, actors, actual, background, expected, bounds.Location));
                if (requireEnemy)
                {
                    object verified = null; List<string> failures = new List<string>();
                    IntPtr mirrorHandle = Game.DesktopAcceptanceMirrorHandle;
                    foreach (ArtDemoActorFrame enemy in actors.Where(a => a.Role == "enemy" && (!requireMirror || a.Window.Handle == mirrorHandle)))
                        try { verified = ArtDemoPixelVerifier.VerifyActor(enemy, actors, actual, background, expected, bounds.Location); break; }
                        catch (InvalidOperationException error) { failures.Add("handle=" + enemy.Window.Handle + "; bounds=" + enemy.Bounds + "; " + error.Message); }
                    foreach (string failure in failures) Log("CAPTURE candidate rejected " + name + ": " + failure);
                    if (verified == null)
                        throw new InvalidOperationException("真实桌面截图没有可验证的" + (requireMirror ? "指定灵域镜妖" : "存活妖物") + "像素。snapshots=" + actors.Count(a => a.Role == "enemy") + "; model=" + Game.DesktopAcceptanceEnemySummary() + "; pixelFailures=" + String.Join(" | ", failures.ToArray()));
                    checks.Add(verified);
                }
                captures.Add(new Dictionary<string, object> { { "image", name + ".png" }, { "bounds", bounds.ToString() }, { "actors", checks.ToArray() }, { "requiredEnemy", requireMirror ? "specific-lingyu-mirror" : requireEnemy ? "living-enemy" : "none-for-this-capture-purpose" }, { "capture", "native BitBlt SRCCOPY|CAPTUREBLT with presented-frame alpha verification; no scene background" } });
                Log("CAPTURE verified real desktop hero" + (requireEnemy ? "+enemy" : "") + " " + name);
            }
            finally
            {
                if (actual != null) actual.Dispose(); if (background != null) background.Dispose(); if (expected != null) expected.Dispose();
                if (actors != null) foreach (ArtDemoActorFrame actor in actors) actor.Dispose(); Game.ResumeArtDemoCapture(running);
            }
        }
        private void CapturePanel(Form panel, string name)
        {
            using (Bitmap bitmap = DesktopFrameCapture.CaptureRegion(panel.Bounds, true))
                bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
            captures.Add(new Dictionary<string, object> { { "image", name + ".png" }, { "bounds", panel.Bounds.ToString() }, { "purpose", "on-demand panel reached through native input; no actor-visibility claim for this panel capture" }, { "capture", "native Windows desktop pixels" } });
            Log("CAPTURE real on-demand panel " + name);
        }
        private Rectangle CaptureBounds(IList<ArtDemoActorFrame> actors, bool includePanels)
        {
            Rectangle bounds = actors.Select(a => a.Bounds).Aggregate(Rectangle.Union);
            if (includePanels)
                foreach (Form panel in Application.OpenForms.Cast<Form>().Where(f => f.Visible && !(f is KnightLayeredWindow) && f.Width > 200 && f.Height > 180)) bounds = Rectangle.Union(bounds, panel.Bounds);
            bounds.Inflate(28, 28); return Rectangle.Intersect(bounds, SystemInformation.VirtualScreen);
        }
        private void BeginMovie(string name, double duration)
        {
            movieName = name; movieUntil = wall.Elapsed.TotalSeconds + duration; nextMovie = wall.Elapsed.TotalSeconds;
            Directory.CreateDirectory(Path.Combine(output, "native-frames"));
        }
        private void RecordMovie(double now)
        {
            if (String.IsNullOrEmpty(movieName) || now > movieUntil || now < nextMovie || movieIndex >= 96) return;
            nextMovie = now + .33;
            // Follow the *real native windows* each frame. A crop fixed at the
            // beginning of a clip lets a moving desktop actor walk out of shot.
            // There is no invented scene, bitmap actor compositing or relocation.
            Rectangle hero = Game.DesktopHeroWindow.Bounds, movieBounds = hero;
            Dictionary<string, KnightLayeredWindow> windows = Game.DesktopEnemyWindows;
            KnightLayeredWindow nearest = Game.Battle.Enemies.Where(e => !e.Defeated && !e.VisualDefeated && windows.ContainsKey(e.Id) && windows[e.Id].Visible)
                .Select(e => windows[e.Id]).OrderBy(w => AdventureBattle.Distance(hero.Left + hero.Width / 2, hero.Top + hero.Height / 2, w.Left + w.Width / 2, w.Top + w.Height / 2)).FirstOrDefault();
            bool enemyIncluded = false;
            if (nearest != null)
            {
                Rectangle union = Rectangle.Union(hero, nearest.Bounds);
                if (union.Width <= 650 && union.Height <= 500) { movieBounds = union; enemyIncluded = true; }
            }
            movieBounds.Inflate(28, 28); movieBounds = Rectangle.Intersect(movieBounds, SystemInformation.VirtualScreen);
            if (!movieBounds.Contains(hero)) throw new InvalidOperationException("原生录像裁切无法完整容纳角色窗口，拒绝记录空主体片段。");
            string file = "native-frames/" + movieName + "-" + (++movieIndex).ToString("D3") + ".png";
            using (Bitmap frame = DesktopFrameCapture.CaptureRegion(movieBounds)) frame.Save(Path.Combine(output, file), ImageFormat.Png);
            movieFrames.Add(new Dictionary<string, object> { { "image", file }, { "elapsedSeconds", now }, { "bounds", movieBounds.ToString() },
                { "captureLeft", movieBounds.Left }, { "captureTop", movieBounds.Top }, { "captureWidth", movieBounds.Width }, { "captureHeight", movieBounds.Height },
                { "heroBounds", hero.ToString() }, { "enemyIncluded", enemyIncluded }, { "enemyBounds", enemyIncluded ? nearest.Bounds.ToString() : null },
                { "cameraFollowsHero", true }, { "source", "native desktop pixels; no DrawToBitmap, no actor compositing" } });
        }
        private void SamplePerformance(double now)
        {
            if (now < nextPerformance) return; nextPerformance = now + 2;
            using (Process p = Process.GetCurrentProcess())
            {
                p.Refresh(); double cpu = p.TotalProcessorTime.TotalSeconds;
                double percent = now > previousSample ? (cpu - previousCpu) * 100 / (now - previousSample) : 0;
                performance.Add(new Dictionary<string, object> { { "elapsedSeconds", now }, { "cpuOneCorePercent", percent }, { "cpuMachinePercent", percent / Environment.ProcessorCount }, { "privateMiB", p.PrivateMemorySize64 / 1048576.0 }, { "workingSetMiB", p.WorkingSet64 / 1048576.0 }, { "handles", p.HandleCount }, { "gdi", GetGuiResources(p.Handle, 0) }, { "user", GetGuiResources(p.Handle, 1) }, { "frames", Game.PresentedFrames } });
                previousCpu = cpu; previousSample = now;
                if (GetGuiResources(p.Handle, 0) > 5000) throw new InvalidOperationException("本进程GDI资源超过验收保护阈值5000。");
            }
        }
        private void SnapshotRealArchives()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant");
            foreach (string name in new[] { "adventure-v1.txt", "cultivation-v2.txt", "baiye-v1.txt", "lingyu-v1.txt", "dongtian-v1.txt", "maintenance-consent.txt", "maintenance-consent.txt.revoked" }) realArchives[name] = Hash(Path.Combine(root, name));
        }
        private static string Hash(string path)
        { if (!File.Exists(path)) return "missing"; using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
        private void Assert(bool ok, string message)
        {
            assertions.Add(new Dictionary<string, object> { { "passed", ok }, { "detail", message }, { "elapsedSeconds", wall.Elapsed.TotalSeconds } });
            Log((ok ? "PASS " : "FAIL ") + message); if (!ok) throw new InvalidOperationException(message);
        }
        private void Log(string message)
        { log.AppendLine(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz") + " " + message); File.WriteAllText(Path.Combine(output, "desktop-playthrough.log"), log.ToString(), new UTF8Encoding(true)); }
        private void Finish(bool passed, string error)
        {
            if (finished) return; finished = true; timer.Stop();
            try
            {
                Game.DesktopSetAcceptanceSpeed(1);
                if (passed)
                {
                    Assert(Game.DesktopSaveNow(), "五份隔离业务存档安全落盘");
                    Dictionary<string, object> marker = Game.DesktopAcceptanceRestartMarker();
                    Game.DesktopAcceptanceVerifyFiles(marker); Assert(true, "独立存档重新Load后身份、境界、灵域及洞天一致");
                    File.WriteAllText(Path.Combine(session.ProfileRoot, "acceptance-expected-restart.json"), json.Serialize(marker), new UTF8Encoding(true));
                }
                Dictionary<string, string> current = new Dictionary<string, string>(); string live = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant");
                foreach (string name in realArchives.Keys) current[name] = Hash(Path.Combine(live, name));
                bool archivesUnchanged = realArchives.All(p => current[p.Key] == p.Value);
                if (passed) Assert(archivesUnchanged, "真实用户五档及维护授权文件哈希保持不变");
                if (error != null) Log("ABORT " + error);
                File.WriteAllText(Path.Combine(output, "desktop-acceptance.json"), json.Serialize(new Dictionary<string, object> {
                    { "passed", passed }, { "error", error }, { "processId", Process.GetCurrentProcess().Id }, { "restartVerification", restart },
                    { "profile", session.ProfileRoot }, { "elapsedSeconds", wall.Elapsed.TotalSeconds }, { "nativeMouseClicks", clicks },
                    { "clockAcceleration", normalSpeed ? "x1 throughout; main combat-loop strike attempts at least 0.75 seconds apart; setup/input/switch probes may use quicker clicks; 20-second no-input onboarding observation" : "x6 explicit acceptance only; same combat/state/save rules, no HP or kill injection; mortal flow returns to normal x1 for final 120 seconds" },
                    { "normalSpeedObservationStartedAt", normalStarted }, { "normalSpeedObservationSeconds", normalStarted < 0 ? 0 : wall.Elapsed.TotalSeconds - normalStarted },
                    { "displays", displays.ToArray() }, { "displayConfigurationChanged", false },
                    { "assertions", assertions.ToArray() }, { "captures", captures.ToArray() }, { "nativeMovieFrames", movieFrames.ToArray() },
                    { "performance", performance.ToArray() }, { "performanceScope", "short live desktop sample incl. capture overhead, not a multi-hour soak" },
                    { "realArchivesBefore", realArchives }, { "realArchivesAfter", current }, { "realArchivesUnchanged", archivesUnchanged }
                }), new UTF8Encoding(true));
                WritePerformanceCsv();
            }
            catch (Exception finishError)
            {
                passed = false; Log("FAIL FINALIZE " + finishError);
                File.WriteAllText(Path.Combine(output, "desktop-acceptance.json"), json.Serialize(new Dictionary<string, object> { { "passed", false }, { "error", (error ?? "") + "\n" + finishError }, { "assertions", assertions.ToArray() }, { "captures", captures.ToArray() }, { "performance", performance.ToArray() } }), new UTF8Encoding(true));
                WritePerformanceCsv();
            }
            finally { MouseButton(false); Application.ApplicationExit -= OnApplicationExit; session.ExitAfterAcceptance(passed ? 0 : 1); }
        }
        private void WritePerformanceCsv()
        {
            StringBuilder csv = new StringBuilder("elapsedSeconds,cpuOneCorePercent,cpuMachinePercent,privateMiB,workingSetMiB,handles,gdi,user,frames\r\n");
            string[] keys = { "elapsedSeconds", "cpuOneCorePercent", "cpuMachinePercent", "privateMiB", "workingSetMiB", "handles", "gdi", "user", "frames" };
            foreach (Dictionary<string, object> sample in performance) csv.AppendLine(String.Join(",", keys.Select(k => Convert.ToString(sample[k], System.Globalization.CultureInfo.InvariantCulture)).ToArray()));
            File.WriteAllText(Path.Combine(output, "desktop-performance.csv"), csv.ToString(), new UTF8Encoding(true));
        }
        private void OnApplicationExit(object sender, EventArgs args)
        { if (!finished) Finish(false, "程序在完整验收完成前关闭，不把退出码或空白截图当成功。"); }
        public void Dispose() { timer.Stop(); timer.Dispose(); Application.ApplicationExit -= OnApplicationExit; }

        private sealed class InputProbe : Form
        {
            internal int Clicks;
            internal InputProbe() { FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; AutoScaleMode = AutoScaleMode.None; StartPosition = FormStartPosition.Manual; BackColor = Color.FromArgb(234, 232, 221); Text = "透明区域点击穿透测试底窗"; MouseDown += delegate { Clicks++; }; }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams { get { CreateParams p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; } }
            protected override void WndProc(ref Message m) { if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; } base.WndProc(ref m); }
        }
        private static void Pump(int milliseconds)
        { Stopwatch watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); System.Threading.Thread.Sleep(5); } }
        private static IntPtr RootAt(Point point) { return GetAncestor(WindowFromPoint(point), 2); }
        private static int WindowZOrder(IntPtr target)
        {
            IntPtr window = GetTopWindow(IntPtr.Zero);
            for (int index = 0; window != IntPtr.Zero && index < 20000; index++, window = GetWindow(window, 2))
                if (window == target) return index;
            return -1;
        }
        private bool NativeClick(Point point, IntPtr expectedRoot, string context)
        {
            // SetCursorPos is synchronous. Do not pump a game timer between
            // observing a moving actor and delivering the real button input.
            NativeMove(point);
            Point actual = Cursor.Position; IntPtr hit = RootAt(actual);
            if (context == "transparent-probe" || hit != expectedRoot || actual != point)
                Log("INPUT " + context + " requested=" + point + "; actual=" + actual + "; actualRoot=" + hit + "; expectedRoot=" + expectedRoot);
            string key = context + ":" + expectedRoot;
            if (actual != point || hit != expectedRoot)
            {
                double since;
                if (!blockedInputSince.TryGetValue(key, out since)) blockedInputSince[key] = since = wall.Elapsed.TotalSeconds;
                if (wall.Elapsed.TotalSeconds - since > 30) throw new TimeoutException("目标持续30秒无法安全命中（可能被遮挡），未向其他应用点击：" + context);
                return false;
            }
            blockedInputSince.Remove(key); MouseButton(true); MouseButton(false); return true;
        }
        private static void NativeMove(Point point)
        {
            if (!SystemInformation.VirtualScreen.Contains(point)) throw new ArgumentOutOfRangeException("point", "真实鼠标目标必须位于当前可见桌面。");
            // WinForms uses the same DPI-aware screen coordinates as actor
            // windows. SetCursorPos is synchronous and avoids 16-bit absolute
            // SendInput rounding at narrow transparent silhouette boundaries.
            // Button presses still enter Windows' real mouse input queue.
            Cursor.Position = point;
        }
        private static void MouseButton(bool down) { NativeInput input = new NativeInput(); input.Type = 0; input.Mouse.Flags = down ? 2u : 4u; Send(input); }
        private static void Send(NativeInput input)
        { if (SendInput(1, new[] { input }, Marshal.SizeOf(typeof(NativeInput))) != 1) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        private static IntPtr OwnDialog(string title)
        { IntPtr dialog = FindWindow("#32770", title); uint pid; GetWindowThreadProcessId(dialog, out pid); return pid == Process.GetCurrentProcess().Id ? dialog : IntPtr.Zero; }
        private bool ClickDialogOk(IntPtr dialog)
        {
            double since;
            if (!dialogWaitSince.TryGetValue(dialog, out since)) dialogWaitSince[dialog] = since = wall.Elapsed.TotalSeconds;
            IntPtr idOk = GetDlgItem(dialog, 1), button = IntPtr.Zero; NativeRect rectangle = new NativeRect();
            List<object> children = new List<object>(); List<IntPtr> confirmations = new List<IntPtr>();
            EnumChildWindows(dialog, delegate(IntPtr child, IntPtr ignored)
            {
                NativeRect bounds; bool foundBounds = GetWindowRect(child, out bounds);
                string className = NativeClass(child), text = NativeText(child);
                children.Add(new Dictionary<string, object> { { "handle", child.ToInt64() }, { "class", NativeClass(child) }, { "text", NativeText(child) },
                    { "controlId", GetDlgCtrlID(child) }, { "visible", IsWindowVisible(child) }, { "enabled", IsWindowEnabled(child) },
                    { "rect", foundBounds ? new Rectangle(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top).ToString() : null } });
                // The observed Windows success box labels its sole native
                // confirmation ID 2. Select its verified meaning, never guess
                // another ID or use a global Return key.
                string caption = new String(text.Where(c => c != '&' && !Char.IsWhiteSpace(c)).ToArray());
                if (className.Equals("Button", StringComparison.OrdinalIgnoreCase) && caption == "确定" && IsWindowVisible(child) && IsWindowEnabled(child)
                    && foundBounds && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top) confirmations.Add(child);
                return true;
            }, IntPtr.Zero);
            if (confirmations.Count == 1) button = confirmations.Contains(idOk) ? idOk : confirmations[0];
            bool ready = IsWindowVisible(dialog) && button != IntPtr.Zero && GetWindowRect(button, out rectangle);
            string diagnostic = json.Serialize(new Dictionary<string, object> { { "dialog", dialog.ToInt64() }, { "class", NativeClass(dialog) }, { "text", NativeText(dialog) },
                { "visible", IsWindowVisible(dialog) }, { "idOk", idOk.ToInt64() }, { "idOkReady", ready && button == idOk },
                { "uniqueConfirmationCount", confirmations.Count }, { "selectedConfirmation", button.ToInt64() }, { "selectedControlId", button == IntPtr.Zero ? (object)null : GetDlgCtrlID(button) },
                { "confirmationReady", ready }, { "children", children.ToArray() } });
            string previous;
            if (!dialogDiagnostics.TryGetValue(dialog, out previous) || previous != diagnostic)
            { Log("DIALOG readiness " + diagnostic); dialogDiagnostics[dialog] = diagnostic; }
            if (confirmations.Count > 1) throw new InvalidOperationException("本进程对话框出现多个可见启用且文字为确定的按钮，拒绝猜测或确认。");
            if (!ready)
            {
                if (wall.Elapsed.TotalSeconds - since > 30) throw new TimeoutException("本进程确认对话框30秒内未显示且未准备好唯一的确定按钮，未发送键盘或伪造确认。");
                return false;
            }
            Point point = new Point((rectangle.Left + rectangle.Right) / 2, (rectangle.Top + rectangle.Bottom) / 2);
            bool clicked = NativeClick(point, dialog, "dialog-confirmation");
            if (clicked) dialogWaitSince.Remove(dialog);
            return clicked;
        }
        private static string NativeText(IntPtr window) { StringBuilder text = new StringBuilder(2048); GetWindowText(window, text, text.Capacity); return text.ToString(); }
        private static string NativeClass(IntPtr window) { StringBuilder text = new StringBuilder(256); GetClassName(window, text, text.Capacity); return text.ToString(); }
        [return: MarshalAs(UnmanagedType.Bool)] private delegate bool EnumChildCallback(IntPtr window, IntPtr parameter);
        [StructLayout(LayoutKind.Sequential)] private struct NativeInput { internal uint Type; internal MouseInput Mouse; }
        [StructLayout(LayoutKind.Sequential)] private struct MouseInput { internal int Dx, Dy; internal uint Data, Flags, Time; internal UIntPtr ExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] input, int size);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr parent);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern IntPtr GetCapture();
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flag);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr dialog, int item);
        [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr control);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumChildCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int capacity);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        [DllImport("shcore.dll")] private static extern int GetScaleFactorForMonitor(IntPtr monitor, out int scale);
    }

    // Read-only observations and save-format checks live next to the acceptance
    // driver. They never authorize attacks, adjust HP, award kills or seed a
    // profile. Inputs above must pass through the existing real UI handlers.
    internal sealed partial class AdventureController
    {
        internal bool DesktopAcceptanceDead { get { return battle.Progress.Dead; } }
        internal double DesktopAcceptanceDeathTime { get { return battle.DeathTime; } }
        internal double DesktopAcceptanceBlockCooldown { get { return blockCooldownUntil; } }
        internal bool DesktopAcceptanceCanBlock { get { return !battle.Progress.Dead && clock.Elapsed.TotalSeconds > blockCooldownUntil + .1; } }
        internal bool DesktopAcceptanceMirrorPresent { get { return battle.Enemies.Any(e => e.Id == pendingLingyuEnemyId && !e.Defeated && !e.VisualDefeated) && enemyWindows.ContainsKey(pendingLingyuEnemyId); } }
        internal bool DesktopAcceptanceHasLiveEnemy { get { return battle.Enemies.Any(e => !e.Defeated && !e.VisualDefeated && enemyWindows.ContainsKey(e.Id) && enemyWindows[e.Id].Visible && !enemyWindows[e.Id].IsDisposed); } }
        internal IntPtr DesktopAcceptanceMirrorHandle { get { return pendingLingyuEnemyId != null && enemyWindows.ContainsKey(pendingLingyuEnemyId) ? enemyWindows[pendingLingyuEnemyId].Handle : IntPtr.Zero; } }
        internal string DesktopAcceptanceEnemySummary()
        {
            return "[" + String.Join("; ", battle.Enemies.Select(e => e.Id + ":hp=" + e.Health + "/" + e.MaxHealth + ",defeated=" + e.Defeated + ",visualDefeated=" + e.VisualDefeated
                + ",window=" + (enemyWindows.ContainsKey(e.Id) ? enemyWindows[e.Id].Handle + "/visible=" + enemyWindows[e.Id].Visible : "missing")).ToArray()) + "]";
        }
        internal AdventureEnemy DesktopAcceptanceTarget(string id)
        {
            Func<AdventureEnemy, bool> alive = e => e.IsVirtual && !e.IsBoss && !e.Defeated && !e.VisualDefeated && !e.Cleaning && !e.Failed;
            if (id != null) return battle.Enemies.FirstOrDefault(e => e.Id == id && alive(e));
            return battle.Enemies.Where(alive).OrderBy(e => EnemyHeroDistance(e)).FirstOrDefault();
        }
        internal bool DesktopAcceptanceMaintenanceOff()
        {
            return IsolatedDesktop && !demonstrationOnly && !consent.CleanupEnabled && !consent.MemoryEnabled && !consent.AutoMaintenanceEnabled
                && connectivity == null && tempWatcher == null && !scanBusy && !memoryBusy && !diskBusy && cleanupTask == null && memoryReclaimTask == null
                && new[] { progressPath, CultivationWritePath, BaiyeWritePath, LingyuWritePath, DongtianWritePath, ConsentWritePath }.All(p =>
                    String.Equals(Path.GetDirectoryName(Path.GetFullPath(p)), IsolatedProfileRoot, StringComparison.OrdinalIgnoreCase));
        }
        internal bool DesktopAcceptanceActorBoundsSafe()
        {
            Rectangle screen = SystemInformation.VirtualScreen;
            foreach (KnightLayeredWindow window in Application.OpenForms.OfType<KnightLayeredWindow>().Where(w => w.Visible))
            {
                long style = GetWindowLongForAcceptance(window.Handle, -20).ToInt64();
                if (window.Width > 440 || window.Height > 480 || !screen.Contains(window.Bounds)
                    || (style & 0x80000) == 0 || (style & 0x8000000) == 0 || (style & 0x80) == 0) return false;
            }
            return heroWindow.Visible;
        }
        internal Dictionary<string, object> DesktopAcceptanceRestartMarker()
        {
            return new Dictionary<string, object> {
                { "processId", Process.GetCurrentProcess().Id }, { "profile", IsolatedProfileRoot }, { "mortal", cultivation.Mortal },
                { "realm", cultivation.RealmIndex }, { "layer", cultivation.Layer }, { "root", cultivation.SpiritRoot }, { "title", cultivation.DaoTitle },
                { "mortalKills", cultivation.MortalKills }, { "auto", cultivation.AutoBattle }, { "lifetimeQi", cultivation.LifetimeQi },
                { "score", battle.Progress.Score }, { "lingyuActive", lingyu.ExpeditionActive }, { "lingyuNode", lingyu.ExpeditionNode },
                { "lingyuDepth", lingyu.ExpeditionDepth }, { "lingyuPending", lingyu.CombatPending }, { "lingyuOutcome", lingyu.CombatOutcome },
                { "caveFields", dongtian.Fields.ToArray() }, { "caveFurnace", dongtian.Furnace }, { "caveForge", dongtian.Forge },
                { "stage", ArtDemoHeroStage }, { "createdUtc", DateTime.UtcNow.ToString("o") }
            };
        }
        internal void DesktopAcceptanceVerifyRestart(Dictionary<string, object> expected)
        {
            if (!String.Equals(Convert.ToString(expected["profile"]), IsolatedProfileRoot, StringComparison.OrdinalIgnoreCase)
                || cultivation.Mortal != Convert.ToBoolean(expected["mortal"]) || cultivation.RealmIndex != Convert.ToInt32(expected["realm"])
                || cultivation.Layer < Convert.ToInt32(expected["layer"]) || cultivation.SpiritRoot != Convert.ToString(expected["root"])
                || cultivation.DaoTitle != Convert.ToString(expected["title"]) || cultivation.MortalKills != Convert.ToInt32(expected["mortalKills"])
                || cultivation.AutoBattle != Convert.ToBoolean(expected["auto"]) || cultivation.LifetimeQi < Convert.ToInt64(expected["lifetimeQi"])
                || battle.Progress.Score < Convert.ToInt64(expected["score"]) || lingyu.ExpeditionActive != Convert.ToBoolean(expected["lingyuActive"])
                || lingyu.ExpeditionNode != Convert.ToString(expected["lingyuNode"]) || lingyu.ExpeditionDepth != Convert.ToInt32(expected["lingyuDepth"])
                || lingyu.CombatPending != Convert.ToBoolean(expected["lingyuPending"]) || lingyu.CombatOutcome != Convert.ToString(expected["lingyuOutcome"])
                || dongtian.Furnace != Convert.ToString(expected["caveFurnace"]) && !(dongtian.Furnace == null && expected["caveFurnace"] == null)
                || dongtian.Forge != Convert.ToString(expected["caveForge"]) && !(dongtian.Forge == null && expected["caveForge"] == null)
                || ArtDemoHeroStage != Convert.ToString(expected["stage"]))
                throw new InvalidOperationException("真正的新进程Load后与上轮持久化身份/状态不一致。");
            System.Collections.IEnumerable expectedFields = expected["caveFields"] as System.Collections.IEnumerable;
            if (expectedFields == null || !dongtian.Fields.SequenceEqual(expectedFields.Cast<object>().Select(v => v == null ? null : Convert.ToString(v))))
                throw new InvalidOperationException("真正的新进程没有恢复洞天灵田。");
        }
        internal void DesktopAcceptanceVerifyFiles(Dictionary<string, object> expected)
        {
            foreach (string path in new[] { progressPath, CultivationWritePath, BaiyeWritePath, LingyuWritePath, DongtianWritePath })
                if (!File.Exists(path)) throw new InvalidOperationException("独立档未落盘：" + Path.GetFileName(path));
            CultivationProgress p = CultivationProgress.Load(CultivationWritePath);
            AdventureProgress a = AdventureProgress.Load(progressPath);
            LingyuProgress l = LingyuProgress.Load(LingyuWritePath);
            DongtianProgress d = DongtianProgress.Load(DongtianWritePath);
            if (p.Mortal != cultivation.Mortal || p.RealmIndex != cultivation.RealmIndex || p.Layer != cultivation.Layer
                || p.SpiritRoot != cultivation.SpiritRoot || p.DaoTitle != cultivation.DaoTitle || p.AutoBattle != cultivation.AutoBattle || p.MortalKills != cultivation.MortalKills
                || p.LifetimeQi != cultivation.LifetimeQi || a.Score != battle.Progress.Score || l.CombatPending != lingyu.CombatPending
                || l.ExpeditionNode != lingyu.ExpeditionNode || l.ExpeditionDepth != lingyu.ExpeditionDepth || !d.Fields.SequenceEqual(dongtian.Fields)
                || d.Furnace != dongtian.Furnace || d.Forge != dongtian.Forge)
                throw new InvalidOperationException("隔离文件通过原模型Load后不符合保存时状态。");
        }
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongForAcceptance(IntPtr window, int index);
    }
}
