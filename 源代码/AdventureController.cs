using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NetBunny
{
    internal sealed partial class AdventureController : IDisposable
    {
        private readonly Form owner;
        private readonly AdventureRenderer renderer;
        private readonly KnightLayeredWindow heroWindow, landmarkWindow;
        private readonly List<LootView> lootViews = new List<LootView>();
        private sealed class LootView { internal KnightLayeredWindow Window; internal string WeaponId, Label; internal double Life = 3.2, X, Y; }
        private readonly Dictionary<string, KnightLayeredWindow> enemyWindows = new Dictionary<string, KnightLayeredWindow>();
        private readonly CleanupService cleanup = new CleanupService();
        private readonly LiveTempPopulation liveTemp = new LiveTempPopulation();
        private FileSystemWatcher tempWatcher;
        private int tempChangeVersion, scannedChangeVersion;
        private Task<MemoryReclaimResult> memoryReclaimTask;
        private CancellationTokenSource memoryReclaimCancellation;
        private Func<CancellationToken, Task<MemoryReclaimResult>> simulatedMemoryReclaimer;
        private double lastRefill = -100;
        private readonly DiskSpaceService diskSpace = new DiskSpaceService();
        private readonly LowDiskBossPolicy diskBossPolicy = new LowDiskBossPolicy();
        private DiskSpaceSnapshot diskSnapshot = DiskSpaceSnapshot.Unavailable("等待 C 盘容量采样");
        private bool diskBusy;
        private double lastDisk = -100;
        private readonly AdventureEffects effects = new AdventureEffects();
        private readonly CombatTactics combatTactics = new CombatTactics();
        private string tacticalStatus = "";
        // V6 persistent cultivation: independent model, archive and consent file.
        // Cultivation never requires administrator rights and never decides cleanup.
        private readonly CultivationProgress cultivation;
        private readonly CultivationEncounterScheduler encounters;
        private readonly CultivationDeviceGrade deviceGrade;
        private readonly string cultivationPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant", "cultivation-v2.txt");
        private string cultivationPathOverride;
        private readonly string consentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant", "maintenance-consent.txt");
        // 百业证道：独立存档与识别器；与修仙主进度、战斗、真实维护互不耦合。
        private readonly string baiyePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant", "baiye-v1.txt");
        private string baiyePathOverride;
        private readonly BaiyeProgress baiye;
        private readonly BaiyeTracker baiyeTracker;
        private BaiyeSteleForm stele;
        private double lastBaiyeAt = -1;
        private string baiyeNotice;
        // 灵域历练：第四个独立存档（lingyu-v1.txt）与蜃楼探索引擎；
        // 与修仙主进度、百业、真实维护互不耦合，战斗只生成虚拟妖物。
        private readonly string lingyuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant", "lingyu-v1.txt");
        private string lingyuPathOverride;
        private readonly LingyuProgress lingyu;
        private readonly MirageEngine mirage;
        private LingyuForm lingyuForm;
        private BreakthroughForm breakthroughForm;
        private string pendingLingyuEnemyId;
        // 启动时排队一次的蜃楼战斗恢复标记：避免在构造期生成妖物。
        private bool lingyuCombatResumeQueued;
        // Two independent real-maintenance scopes; neither is implied by combat
        // or by the (separately persisted) unattended auto-battle preference.
        private readonly MaintenanceConsentStore consent = new MaintenanceConsentStore();
        private bool cultivationPaused, encounterDue;
        private KnightConnectivity lastConnectivity = KnightConnectivity.Unknown;
        private double lastCultivationAt = -1;
        private readonly RegionThemeService region;
        private readonly KnightConnectivityReader connectivity;
        private readonly bool demonstrationOnly;
        private string demoTheme = "OTHER";
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Random random = new Random();
        private readonly string progressPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant", "adventure-v1.txt");
                private readonly AdventureBattle battle;
        private readonly Queue<AdventureEnemy> pendingCleanup = new Queue<AdventureEnemy>();
        private Task<CleanupResult> cleanupTask;
        private int cleanupEpoch;
        private int enemyPaintCursor;
        private double lastLandmarkPaint = -100;
        // Set only by NativeSmoke in demonstration-only mode; never reaches filesystem cleanup.
        private Func<CleanupBatch, CancellationToken, Task<CleanupResult>> simulatedCleaner;
        private AdventureEnemy activeCleanupEnemy;
        private CancellationTokenSource workCancellation;
        private CancellationTokenSource scanCancellation;
        private bool demoPending, memoryAvailable, resumeAutomaticAfterDemo;
        private KnightState networkState = new KnightState();
        private AdventureEquipmentForm equipment;
        private Rectangle area;
        private bool enabled, roaming = true, disposed, dragging, scanBusy, memoryBusy, monitoringAvailable, heroFacing = true;
        private double x, y, targetX, targetY, bytesPerSecond, lastTick, lastTelemetry = -100, lastScan = -100, lastMemory = -100, lastRegion = -100, lastSave, lastEnemyPaint, demoStarted = -1, pauseMotionUntil;
        private double memoryPercent, attackAnimation, renderTime, runCycle, actualMoveSpeed, blackholeUntil, blackholeX, blackholeY, blockUntil = -1, blockCooldownUntil = -1;
        private float displayScale = 1f;
        private string scanStatus = "等待扫描临时文件", connectivityDetail = "正在确认网络", failure;
        private long candidateBytes;
        private long recheckBytes;
        private int recheckFiles;
        private readonly Dictionary<string, CleanupCandidate> recheckCandidates = new Dictionary<string, CleanupCandidate>(StringComparer.OrdinalIgnoreCase);
        private string cleanupNotice = "等待击杀 · 每只怪物获得 2～3 经验";
        private bool lastInventoryComplete;
        private double lastInventoryObservation = -1;
        internal const double IdleInventoryIntervalSeconds = 2, ActiveInventoryIntervalSeconds = 2;
        public AdventureController(Form owner, string assetDirectory, bool demonstrationOnly = false, Func<double> waveClock = null, bool artDemoRuntime = false, string isolatedProfileRoot = null)
            : this(owner, assetDirectory, demonstrationOnly, waveClock, artDemoRuntime, isolatedProfileRoot, false) { }
        public AdventureController(Form owner, string assetDirectory, bool demonstrationOnly, Func<double> waveClock, bool artDemoRuntime, string isolatedProfileRoot, bool desktopObserve)
        {
            if (desktopObserve && isolatedProfileRoot == null) throw new ArgumentException("电脑生态入口必须使用独立存档。");
            desktopEnvironment = new DesktopEnvironmentPolicy(!desktopObserve);
            if (owner == null) throw new ArgumentNullException("owner"); this.owner = owner; this.demonstrationOnly = demonstrationOnly;
            if (artDemoRuntime && !demonstrationOnly) throw new ArgumentException("美术 Demo 必须使用隔离内存模式。");
            this.artDemoRuntime = artDemoRuntime;
            if (isolatedProfileRoot != null)
            {
                if (demonstrationOnly || artDemoRuntime) throw new ArgumentException("桌面隔离档使用正式游戏状态，不能与内存演示模式混用。");
                this.isolatedProfileRoot = DesktopDemoProfiles.ValidateRoot(isolatedProfileRoot);
                progressPath = Path.Combine(this.isolatedProfileRoot, "adventure-v1.txt");
                cultivationPath = Path.Combine(this.isolatedProfileRoot, "cultivation-v2.txt");
                baiyePath = Path.Combine(this.isolatedProfileRoot, "baiye-v1.txt");
                lingyuPath = Path.Combine(this.isolatedProfileRoot, "lingyu-v1.txt");
                dongtianPath = Path.Combine(this.isolatedProfileRoot, "dongtian-v1.txt");
                // Defense in depth: this mode never loads or enables consent,
                // and even a future caller cannot resolve its path to live data.
                consentPath = Path.Combine(this.isolatedProfileRoot, "maintenance-consent.txt");
            }

            renderer = new AdventureRenderer(assetDirectory);
            region = new RegionThemeService();
            connectivity = demonstrationOnly || IsolatedDesktop ? null : new KnightConnectivityReader();
            // Freeze the historical v5.4 archive once before anything can rewrite
            // it; adventure-v1.txt itself stays the live v1-format runtime archive.
            if (!demonstrationOnly) AdventureProgress.EnsureLegacyBackup(progressPath);
            battle = new AdventureBattle(demonstrationOnly ? new AdventureProgress() : AdventureProgress.Load(progressPath));
            // Demonstration and smoke runs never touch the real cultivation archive.
            cultivation = demonstrationOnly ? new CultivationProgress() : CultivationProgress.Load(CultivationWritePath);
            cultivation.EnsureIdentity(random);
            // Demonstration and smoke runs keep the real baiye archive untouched too.
            baiye = demonstrationOnly ? new BaiyeProgress() : BaiyeProgress.Load(BaiyeWritePath);
            baiyeTracker = new BaiyeTracker(baiye);
            baiyeTracker.Reset();
            // 灵域存档与蜃楼引擎；示范运行同样只走内存，不写真实档案。
            lingyu = demonstrationOnly ? new LingyuProgress() : LingyuProgress.Load(LingyuWritePath);
            mirage = new MirageEngine(lingyu, random);
            mirage.CombatRequested += LingyuCombatRequested;
            // P4 洞天第五个独立存档；示范运行同样只走内存。
            dongtian = demonstrationOnly ? new DongtianProgress() : DongtianProgress.Load(DongtianWritePath);
            if (!demonstrationOnly)
            {
                // 启动对账：补齐上次异常退出丢失的获取记录；机缘奖励按两份存档标记对账，不重发不丢失。
                baiye.ReconcileLog(DateTime.UtcNow);
                SettleBaiyeRewards();
                // 灵域修为奖励同一协议：修仙侧有标记则销账，无则补发。
                SettleLingyuRewards();
                // 破境材料跨存档事务兜底：修仙档已记账而灵域档未扣的材料在此补平（幂等）。
                if (ReconcileBreakthroughMaterials(cultivation, lingyu))
                {
                    try { lingyu.Save(LingyuWritePath); } catch (Exception e) { desktopLastSaveSucceeded = false; scanStatus = "灵域档案暂未保存：" + e.Message; }
                }
                // 洞天入库同一双向协议：任一方向的半截入库都被补平，材料不复制不消失。
                if (DongtianProgress.ReconcileTransfers(dongtian, lingyu))
                {
                    try { lingyu.Save(LingyuWritePath); } catch (Exception e) { desktopLastSaveSucceeded = false; scanStatus = "灵域档案暂未保存：" + e.Message; }
                    SaveDongtian();
                }
                // 蜃楼重启恢复排队：上次退出时战斗未决（ExpeditionActive + CombatPending
                // 已持久化）的，首个主节拍补发镜妖——界面与渲染此时尚未就绪，不能在此生成。
                lingyuCombatResumeQueued = lingyu.ExpeditionActive && lingyu.CombatPending;
            }
            encounters = new CultivationEncounterScheduler(random);
            deviceGrade = CultivationHardware.Classify(Environment.ProcessorCount, ReadTotalRamBytes());
            if (IsolatedDesktop && !DesktopObservesSystem) { consent.CleanupEnabled = consent.MemoryEnabled = consent.AutoMaintenanceEnabled = false; RestoreAutoBattlePreference(); }
            else if (demonstrationOnly) { consent.CleanupEnabled = consent.MemoryEnabled = !artDemoRuntime; }
            else
            {
                MaintenanceConsentStore persisted = MaintenanceConsentStore.Load(consentPath);
                consent.CleanupEnabled = persisted.CleanupEnabled; consent.MemoryEnabled = persisted.MemoryEnabled;
                // 无人值守自动维护同样是持久授权：授权文件是事实源，显式授予
                // 并落盘成功的 automaint 在重启后按文件恢复；撤销若写盘失败会
                // 持续重试（见 TryFlushConsentRevocation），不给残留文件复活机会。
                consent.AutoMaintenanceEnabled = persisted.AutoMaintenanceEnabled;
                // Unattended self-battle is the default and independent from
                // real maintenance consent; a dead hero first finishes resting.
                // 凡人未觉醒：运行时绝不自动索敌（AutoBattleUnlocked）。
                if (cultivation.AutoBattle && cultivation.AutoBattleUnlocked && !battle.Progress.Dead) battle.SetAutomatic(true);
            }
            if (!demonstrationOnly && !IsolatedDesktop) region.SelectMode(battle.Progress.LastThemeCode);
            LoadDesktopMaintenanceHistory();
            region.SelectMode(demonstrationOnly || IsolatedDesktop ? "OFF" : battle.Progress.ThemeMode);
            region.Changed += RegionChanged;
            area = Screen.FromControl(owner).WorkingArea;
            heroWindow = NewWindow("骑士助手 · 角色"); landmarkWindow = NewWindow("骑士助手 · 地区地标");
            heroWindow.Size = HeroSize; landmarkWindow.Size = LandmarkSize;
            x = area.Right - HeroSize.Width - 40; y = area.Bottom - HeroSize.Height - 30; ClampHero(); PickTarget();
            heroWindow.ContextRequested = OpenEquipment;
            Point heroPressedAt = Point.Empty; bool heroDragged = false;
            heroWindow.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { heroPressedAt = Cursor.Position; heroDragged = false; } };
            heroWindow.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left && !heroDragged && !IsDemo) { if (IsolatedDesktop && battle.Progress.Dead) OpenEquipment(); else TryBlock(); RenderWorld(true); } };
            heroWindow.DragChanged = delegate(bool active) { dragging = active; if (!active) { if (!artDemoRuntime) area = Screen.FromPoint(heroWindow.Location).WorkingArea; pauseMotionUntil = clock.Elapsed.TotalSeconds + 2; ClampHero(); PickTarget(); PositionLandmark(); } };
            heroWindow.DragMoved = delegate(Point p) { if (AdventureBattle.Distance(heroPressedAt.X, heroPressedAt.Y, Cursor.Position.X, Cursor.Position.Y) > 6) heroDragged = true; if (IsolatedDesktop) area = Screen.FromPoint(new Point(p.X + HeroSize.Width / 2, p.Y + HeroSize.Height - 1)).WorkingArea; x = p.X; y = p.Y; if (IsolatedDesktop) ClampHero(); else heroWindow.Location = p; };
            landmarkWindow.ContextRequested = OpenEquipment;
            landmarkWindow.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) OpenEquipment(); };
            // 15 ms avoids this WinForms timer rounding a 16 ms request to a
            // second Windows tick; movement still uses measured elapsed time.
            PositionLandmark(); timer.Interval = 15; timer.Tick += Tick; lastTick = clock.Elapsed.TotalSeconds; timer.Start();
        }
        private Size HeroSize { get { return new Size((int)(220 * displayScale), (int)(240 * displayScale)); } }
        private Size LandmarkSize { get { return new Size((int)(200 * displayScale), (int)(280 * displayScale)); } }
        private KnightLayeredWindow NewWindow(string name) { KnightLayeredWindow w = new KnightLayeredWindow(); w.Text = name; return w; }
        public bool Enabled
        {
            get { return enabled; }
            set
            {
                if (disposed) return; if (!value) CancelPending(); if (enabled == value) return; enabled = value;
                if (enabled) { failure = null; lastTick = clock.Elapsed.TotalSeconds; RestoreAutoBattlePreference(); RenderWorld(true); if (!scanBusy && demoStarted < 0 && !demonstrationOnly) RefreshWorld(false); }
                else { effects.Clear(); ClearLoot(); heroWindow.Hide(); landmarkWindow.Hide(); foreach (KnightLayeredWindow w in enemyWindows.Values) w.Hide(); }
            }
        }
        // Hiding, demos and combat death all switch the runtime flag off; the
        // persisted unattended-battle preference is re-applied whenever normal
        // play resumes, so a single hide never silently disables auto battle.
        private void RestoreAutoBattlePreference()
        {
            if (demoStarted >= 0 || demoPending) return;
            // 凡人未觉醒：偏好保持存档原样，但运行时绝不自动索敌。
            battle.SetAutomatic(cultivation.AutoBattle && cultivation.AutoBattleUnlocked && !battle.Progress.Dead);
        }
        public bool Roaming { get { return roaming; } set { roaming = value; if (value) PickTarget(); } }
        public bool AutoPlanning { get { return battle.Automatic; } }
        public DiskSpaceSnapshot DiskSnapshot { get { return diskSnapshot; } }
        internal CultivationProgress Cultivation { get { return cultivation; } }
        internal string CaveText { get { return CultivationHardware.GradeName(deviceGrade); } }
        internal bool CultivationPaused { get { return cultivationPaused; } }
        // Pure cultivation mode (default) settles every kill virtually. Real file
        // cleanup and process working-set trims each need their own explicit,
        // separately persisted consent; boss kills never imply either.
        internal bool MaintenanceConsentCleanup { get { return consent.CleanupEnabled; } }
        internal bool MaintenanceConsentMemory { get { return consent.MemoryEnabled; } }
        internal double NextEncounterSeconds { get { return encounters.SecondsUntilNext; } }
        internal string CultivationLine
        {
            get
            {
                string state = cultivationPaused ? "已暂停" : "吐纳中";
                return cultivation.Summary + " · " + state + " · " + CaveText;
            }
        }
        private static ulong ReadTotalRamBytes()
        {
            try { MemorySnapshot snapshot = CleanupService.ReadMemory(); return snapshot.Available ? snapshot.TotalBytes : 0UL; }
            catch { return 0UL; }
        }
        // Fail-closed enable: the store applies the flag only after a durable
        // write; on failure consent stays off and the checkbox reverts.
        // Disable ALWAYS applies in memory and cancels in-flight work first —
        // a persistence failure must never skip the cancellation — and the
        // stale grant file is retried until removed so a restart cannot
        // resurrect the authorization.
        private bool consentRetryCleanup, consentRetryMemory, consentRetryAutoMaint;
        private double consentRetryAt = -1;
        // Process-local UI feedback; never persisted or used to decide consent.
        private string desktopCleanupConsentFailure, desktopMemoryConsentFailure, desktopAutoConsentFailure;
        private string DesktopConsentFailureNotice
        {
            get
            {
                string notice = String.Join(Environment.NewLine, new[] { desktopCleanupConsentFailure, desktopMemoryConsentFailure, desktopAutoConsentFailure }.Where(text => !String.IsNullOrEmpty(text)));
                return notice.Length == 0 ? null : notice;
            }
        }
        private void RecordDesktopConsentResult(int scope, bool value, bool persisted, string error, string result = null)
        {
            if (!IsolatedDesktop) return;
            string name = scope == 0 ? "文件清理授权" : scope == 1 ? "内存维护授权" : "无人值守自动维护";
            bool pending = scope == 0 ? consentRetryCleanup : scope == 1 ? consentRetryMemory : consentRetryAutoMaint;
            string message = result ?? battle.LastReward;
            if (!persisted)
                message = value ? message + (String.IsNullOrEmpty(error) ? "" : Environment.NewLine + error)
                    : name + "已在本机立即关闭；授权文件未能更新，将自动重试保存关闭状态，请保持程序运行";
            else if (pending)
                message = name + "关闭状态仍待重试保存；此前授权文件未能更新，请保持程序运行";
            string failure = !persisted || pending ? message : null;
            if (scope == 0) desktopCleanupConsentFailure = failure;
            else if (scope == 1) desktopMemoryConsentFailure = failure;
            else desktopAutoConsentFailure = failure;
            RecordDesktopEvent(message);
        }
        internal bool SetCleanupConsent(bool value)
        {
            if (disposed) return false;
            if (IsolatedDesktop && !DesktopObservesSystem) return !value;
            string error;
            bool persisted = consent.SetCleanup(ConsentWritePath, value, out error);
            if (!persisted) scanStatus = "授权状态暂未保存：" + error;
            if (!value)
            {
                // Not-yet-started file cleanups return to a pure virtual state;
                // the in-flight worker gets a cancellation request and reports
                // its actual result as soon as it stops.
                CancelCleanupWork("文件清理授权已关闭 · 清理未执行");
                if (persisted) { consentRetryCleanup = false; battle.LastReward = "文件清理授权已关闭：未开始的清理已撤销，进行中的任务已请求取消并尽快汇报实际结果；此后击败文件怪只结算虚拟奖励"; }
                else
                {
                    consentRetryCleanup = true; consentRetryAt = clock.Elapsed.TotalSeconds;
                    battle.LastReward = "文件清理授权已在本机立即关闭并取消任务，但授权文件未能更新；将自动重试清除，若重启后仍显示已授权请手动删除 " + consentPath;
                }
                RecordDesktopConsentResult(0, value, persisted, error);
                return persisted;
            }
            if (!persisted) { battle.LastReward = "授权文件写入失败，为安全起见文件清理授权保持关闭"; RecordDesktopConsentResult(0, value, false, error); return false; }
            battle.LastReward = "文件清理授权已开启：击败文件怪将安全清理其绑定的、超过 24 小时的本用户临时文件";
            RecordDesktopConsentResult(0, value, true, error);
            return true;
        }
        internal bool SetMemoryConsent(bool value)
        {
            if (disposed) return false;
            if (IsolatedDesktop && !DesktopObservesSystem) return !value;
            string error;
            bool persisted = consent.SetMemory(ConsentWritePath, value, out error);
            if (!persisted) scanStatus = "授权状态暂未保存：" + error;
            if (!value)
            {
                // Memory reclaim only starts at a boss kill, so future reclaim is
                // gated by the flag itself; the in-flight task is cancelled here.
                if (memoryReclaimCancellation != null) memoryReclaimCancellation.Cancel();
                if (persisted) { consentRetryMemory = false; battle.LastReward = "内存维护授权已关闭：未开始的工作集回收不会执行，进行中的回收已请求取消；此后击败内存 Boss 只结算虚拟奖励"; }
                else
                {
                    consentRetryMemory = true; consentRetryAt = clock.Elapsed.TotalSeconds;
                    battle.LastReward = "内存维护授权已在本机立即关闭并取消任务，但授权文件未能更新；将自动重试清除，若重启后仍显示已授权请手动删除 " + consentPath;
                }
                RecordDesktopConsentResult(1, value, persisted, error);
                return persisted;
            }
            if (!persisted) { battle.LastReward = "授权文件写入失败，为安全起见内存维护授权保持关闭"; RecordDesktopConsentResult(1, value, false, error); return false; }
            battle.LastReward = "内存维护授权已开启：击败内存 Boss 将尝试回收同会话后台进程的工作集";
            RecordDesktopConsentResult(1, value, true, error);
            return true;
        }
        // Third, separately confirmed permission: unattended real maintenance.
        // Auto battle without it fights virtually even where a scope consent is on.
        internal bool MaintenanceConsentAutoMaintenance { get { return consent.AutoMaintenanceEnabled; } }
        internal bool SetAutoMaintenanceConsent(bool value)
        {
            if (disposed) return false;
            if (IsolatedDesktop && !DesktopObservesSystem) return !value;
            string error;
            bool persisted = consent.SetAutoMaintenance(ConsentWritePath, value, out error);
            if (!persisted)
            {
                scanStatus = "授权状态暂未保存：" + error;
                if (!value)
                {
                    // 与另两项授权同一承诺：本机立即生效、任务先取消，
                    // 残留授权文件持续重试清除，重启不得复活授权。
                    consentRetryAutoMaint = true; consentRetryAt = clock.Elapsed.TotalSeconds;
                    battle.LastReward = "无人值守自动维护已在本机立即关闭并撤销待办，但授权文件未能更新；将自动重试清除，若重启后仍显示已授权请手动删除 " + consentPath;
                    RetireAutomaticRealEffectJobs();
                }
                else battle.LastReward = "授权文件写入失败，为安全起见无人值守自动维护保持关闭";
                RecordDesktopConsentResult(2, value, false, error);
                return false;
            }
            if (!value)
            {
                consentRetryAutoMaint = false;
                // Queued-but-unstarted automatic targets with real effects retire;
                // auto battle itself continues against virtual fiends.
                RetireAutomaticRealEffectJobs();
                battle.LastReward = "无人值守自动维护已关闭：自动作战继续，但只结算虚拟奖励；真实清理/回收需要手动击杀或重新开启";
            }
            else battle.LastReward = "无人值守自动维护已开启：自动作战击败文件怪/内存 Boss 时将执行对应的真实清理或回收（仍须对应授权保持开启）；重启后按授权文件恢复，可随时关闭";
            RecordDesktopConsentResult(2, value, true, error);
            return true;
        }
        private void RetireAutomaticRealEffectJobs()
        {
            // Cycle automatic mode: unstarted automatic targets (including defeated
            // ones awaiting the cleanup worker) are retired; manual orders survive.
            bool resume = battle.Automatic || (cultivation.AutoBattle && cultivation.AutoBattleUnlocked);
            battle.SetAutomatic(false); RemoveCancelledAutomaticJobs();
            if (resume) battle.SetAutomatic(cultivation.AutoBattleUnlocked && !battle.Progress.Dead);
        }
        // Automatic planning must never start unattended real maintenance on its
        // own: real-effect targets require the separate auto-maintenance consent.
        private bool AutoMaintenanceEligibility(AdventureEnemy enemy)
        {
            if (enemy.ProcessTarget != null) return false;
            // Desktop combat remains available independently of real maintenance.
            // BeginCleanup/PumpCleanup enforce the separate scope/automatic grants.
            if (IsolatedDesktop) return true;
            if (enemy.IsVirtual) return true; // pure combat, never any real effect
            bool memoryBoss = enemy.IsBoss && !enemy.IsDiskBoss;
            if (memoryBoss) return !consent.MemoryEnabled || consent.AutoMaintenanceEnabled;
            return !consent.CleanupEnabled || consent.AutoMaintenanceEnabled;
        }
        private string ConsentWritePath { get { return consentPathOverride ?? (demonstrationOnly ? null : consentPath); } }
        // Test hook: force consent persistence failures in smoke; null in production.
        private string consentPathOverride;
        private void TryFlushConsentRevocation()
        {
            if (!consentRetryCleanup && !consentRetryMemory && !consentRetryAutoMaint) return;
            string ignored;
            if (consentRetryCleanup && consent.SetCleanup(ConsentWritePath, false, out ignored))
            { consentRetryCleanup = false; RecordDesktopConsentResult(0, false, true, null, "文件清理授权关闭状态已重试保存成功"); }
            if (consentRetryMemory && consent.SetMemory(ConsentWritePath, false, out ignored))
            { consentRetryMemory = false; RecordDesktopConsentResult(1, false, true, null, "内存维护授权关闭状态已重试保存成功"); }
            if (consentRetryAutoMaint && consent.SetAutoMaintenance(ConsentWritePath, false, out ignored))
            { consentRetryAutoMaint = false; RecordDesktopConsentResult(2, false, true, null, "无人值守自动维护关闭状态已重试保存成功"); }
            if (!consentRetryCleanup && !consentRetryMemory && !consentRetryAutoMaint) scanStatus = "授权关闭状态已重试保存成功";
        }
        private void CancelCleanupWork(string notice)
        {
            if (workCancellation != null) workCancellation.Cancel();
            while (pendingCleanup.Count > 0)
            {
                AdventureEnemy enemy = pendingCleanup.Dequeue();
                if (enemy.VisualDefeated && enemy.Batch != null) RecordRecheck(enemy.Batch.Candidates);
                enemy.CancelBeforeCleanup(notice);
            }
        }
        internal void SetCultivationPaused(bool paused)
        {
            if (disposed || cultivationPaused == paused) return;
            cultivationPaused = paused; lastCultivationAt = clock.Elapsed.TotalSeconds;
            cultivation.LastNotice = paused ? "修士入定暂歇，修为暂停积累" : "修士结束暂歇，继续吐纳修行";
            Save();
        }
        internal void ChooseTechnique(string id)
        {
            if (disposed || IsDemo || !CultivationTechnique.IsValid(id) || cultivation.TechniqueId == id) return;
            cultivation.TechniqueId = id;
            cultivation.LastNotice = "改修「" + cultivation.Technique.Name + "」，修行路线仍可随时调整";
            Save();
        }
        // 破境材料按当前境界的配置结算（如炼气→筑基的「水月灵髓」，蜃楼主镜机缘）。
        // 勾选使用且库存足够即消耗（成败皆消耗——灵髓入炉化入药力），加成与保底由修仙核心配置决定。
        internal int BreakthroughMaterialCount
        {
            get
            {
                if (lingyu == null || cultivation == null) return 0;
                string materialId = cultivation.RealmDef.MaterialId;
                if (materialId.Length == 0) return 0;
                int count;
                return lingyu.Materials.TryGetValue(materialId, out count) ? count : 0;
            }
        }
        // 灵根觉醒：凡人十次有效击杀后引气入体。旧档修士永不走此路径（Mortal=false）。
        // demonstrationOnly 允许：示范运行只在内存中推进（Save 自带演示保护），烟测走同一入口。
        internal bool AwakenSpiritRoot()
        {
            if (disposed || IsDemo || !cultivation.CanAwaken) return false;
            bool awakened = cultivation.Awaken(random);
            // 觉醒即解锁并默认开启自动作战（核心已置 AutoBattle=true），力竭中待恢复后自行接续。
            if (awakened && cultivation.AutoBattle && !battle.Progress.Dead) battle.SetAutomatic(true);
            if (awakened) RecordDesktopEvent("灵根觉醒 · " + cultivation.RootName + " · " + cultivation.DaoTitle + " · 自动战斗与吐纳已开启");
            Save();
            if (awakened && enabled) RenderWorld(true);
            return awakened;
        }
        // B1 破境材料安全结算：扣除任何材料之前，先统一校验角色状态、境界、
        // 圆满条件与破境契机（核心 CanAttemptBreakthrough），材料种类与库存以
        // 当前境界配置为准。事务采用灵域档预留先行 + 启动双向对账：
        //   1) 内存扣料并记 btmat-<境界>-<尝试序号> 双台账后，灵域档立即落盘
        //      （材料预留权威先行）；
        //   2) 修仙档随后落盘（突破结果权威）。
        // 崩溃在任一落盘点只产生一种可识别形态，由启动对账
        // ReconcileBreakthroughMaterials 双向补平：修仙已记账灵域未扣→补扣；
        // 灵域已扣修仙未记账→回滚返还。材料与突破结果永不永久不一致。
        internal bool TryBreakthrough(bool useMaterial)
        {
            if (disposed || IsDemo) return false;
            // 无资格（凡人/巅峰/未圆满/缺契机）：核心写出具体原因，绝不触碰材料。
            if (!cultivation.CanAttemptBreakthrough)
            {
                cultivation.TryBreakthrough(random, false);
                Save();
                return false;
            }
            CultivationRealmDef def = cultivation.RealmDef;
            bool materialUsed = false;
            if (useMaterial && def.MaterialId.Length > 0 && lingyu != null)
            {
                int need = Math.Max(1, def.MaterialCount);
                int stock;
                lingyu.Materials.TryGetValue(def.MaterialId, out stock);
                if (stock >= need)
                {
                    string grantId = "btmat-" + cultivation.RealmIndex + "-" + (cultivation.BreakthroughAttempts + 1);
                    if (!lingyu.Grants.ContainsKey(grantId))
                    {
                        lingyu.Materials[def.MaterialId] = stock - need;
                        if (lingyu.Materials[def.MaterialId] <= 0) lingyu.Materials.Remove(def.MaterialId);
                        lingyu.Grants[grantId] = DateTime.UtcNow.Ticks;
                        // 材料预留先行落盘：此点之后崩溃，启动对账按双向规则补平。
                        // 演示/烟测运行永不写真实档案（LingyuWritePath 指向真实路径）。
                        if (!demonstrationOnly)
                            try { lingyu.Save(LingyuWritePath); } catch (Exception e) { desktopLastSaveSucceeded = false; scanStatus = "灵域档案暂未保存：" + e.Message; }
                    }
                    cultivation.ExternalGrants.Add(grantId);
                    materialUsed = true;
                    // 消耗即刻对齐持久化增量基线，下一滴灵髓仍是新入手时刻。
                    cultivation.OppBaselineLingsui = BreakthroughMaterialCount;
                }
            }
            bool success = cultivation.TryBreakthrough(random, materialUsed);
            RecordDesktopEvent(cultivation.LastNotice);
            Save();
            if (success && enabled) RenderWorld(true);
            return success;
        }
        // B1 启动对账（幂等，双向）：任一方向的崩溃半截写入都被补平，
        // 材料与突破结果永不永久不一致。
        // 方向一（补扣）：修仙档有 btmat-* 记账而灵域台账缺失——按对应境界的
        //   材料定义补扣并补台账；库存已被其他路径消耗时只补台账防重复，
        //   绝不扣成负数。
        // 方向二（回滚）：灵域台账有 btmat-* 而修仙档无对应记账——说明灵域档
        //   写入成功而修仙档（突破结果的权威）未能落盘，该次尝试视为未发生：
        //   返还材料并移除孤儿台账；同序号重试仍幂等（attempts 未变，grantId 相同）。
        // 纯逻辑静态方法，便于用临时档案直接测试崩溃/重启场景。
        internal static bool ReconcileBreakthroughMaterials(CultivationProgress cultivation, LingyuProgress lingyu)
        {
            if (cultivation == null || lingyu == null) return false;
            bool changed = false;
            foreach (string grant in cultivation.ExternalGrants)
            {
                if (!grant.StartsWith("btmat-", StringComparison.Ordinal) || lingyu.Grants.ContainsKey(grant)) continue;
                ApplyMaterialDelta(lingyu, grant, -1);
                lingyu.Grants[grant] = DateTime.UtcNow.Ticks;
                changed = true;
            }
            List<string> orphans = null;
            foreach (KeyValuePair<string, long> pair in lingyu.Grants)
                if (pair.Key.StartsWith("btmat-", StringComparison.Ordinal) && !cultivation.ExternalGrants.Contains(pair.Key))
                    (orphans ?? (orphans = new List<string>())).Add(pair.Key);
            if (orphans != null)
                foreach (string orphan in orphans)
                {
                    ApplyMaterialDelta(lingyu, orphan, +1); // 回滚：材料返还库存
                    lingyu.Grants.Remove(orphan);
                    changed = true;
                }
            return changed;
        }
        // sign=-1 补扣 / sign=+1 回滚返还；库存不足补扣时不动库存（只补台账）。
        private static void ApplyMaterialDelta(LingyuProgress lingyu, string grantId, int sign)
        {
            string[] parts = grantId.Split('-');
            int realmIdx;
            if (parts.Length != 3 || !Int32.TryParse(parts[1], out realmIdx)) return;
            CultivationRealmDef def = CultivationRealms.For(realmIdx);
            if (def.MaterialId.Length == 0) return;
            int need = Math.Max(1, def.MaterialCount);
            int stock;
            lingyu.Materials.TryGetValue(def.MaterialId, out stock);
            if (sign < 0)
            {
                if (stock < need) return;
                stock -= need;
                if (stock <= 0) lingyu.Materials.Remove(def.MaterialId); else lingyu.Materials[def.MaterialId] = stock;
            }
            else lingyu.Materials[def.MaterialId] = stock + need;
        }
        // —— P4 洞天经营（与 CultivationProgress / 战斗 / 真实维护解耦）——
        private readonly DongtianProgress dongtian;
        private readonly string dongtianPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant", "dongtian-v1.txt");
        private string dongtianPathOverride;
        internal string DongtianWritePath { get { return dongtianPathOverride ?? dongtianPath; } }
        internal DongtianProgress Dongtian { get { return dongtian; } }
        internal int LingyuMaterialCount(string material) { if (lingyu == null) return 0; int n; return lingyu.Materials.TryGetValue(material, out n) ? n : 0; }
        // 筑基后解锁洞天经营；炼气期只见入口提示（埋期待感）。
        internal bool DongtianUnlocked { get { return cultivation != null && !cultivation.Mortal && cultivation.RealmIndex >= 1; } }
        private void SaveDongtian()
        {
            SaveDongtianCore();
        }
        private bool dongtianPersistPending; private double dongtianPersistRetryAt = -1;
        // 持久化状态三段式：待确认（内存生效、档案未落盘）→ 提交成功（双档落盘）。
        // 任何真实写入失败都不静默报成功：状态栏与洞天面板明示，Tick 每 15 秒重试。
        internal bool DongtianPersistPending { get { return dongtianPersistPending; } }
        private bool SaveDongtianCore()
        {
            if (dongtian == null || (demonstrationOnly && dongtianPathOverride == null)) return true;
            try
            {
                dongtian.Save(DongtianWritePath);
                return true;
            }
            catch (Exception e) { dongtianPersistPending = true; scanStatus = "洞天档案暂未保存（操作待确认，将持续自动重试）：" + e.Message; return false; }
        }
        // 灵域档同款保存：入库协议要求灵域档先行落盘，失败同样进入待确认。
        private bool SaveLingyuCore()
        {
            if (lingyu == null || (demonstrationOnly && lingyuPathOverride == null)) return true;
            try { lingyu.Save(LingyuWritePath); return true; }
            catch (Exception e) { dongtianPersistPending = true; scanStatus = "灵域档案暂未保存（操作待确认，将持续自动重试）：" + e.Message; return false; }
        }
        // 待确认重试：两份档案都落盘才算提交成功；崩溃窗口由启动对账（ReconcileTransfers）补平。
        internal void TryFlushDongtianPersistence()
        {
            if (disposed || !dongtianPersistPending) return;
            bool lingyuOk = lingyu == null || (demonstrationOnly && lingyuPathOverride == null);
            if (!lingyuOk) { try { lingyu.Save(LingyuWritePath); lingyuOk = true; } catch { } }
            bool dongtianOk = dongtian == null || (demonstrationOnly && dongtianPathOverride == null);
            if (!dongtianOk) { try { dongtian.Save(DongtianWritePath); dongtianOk = true; } catch { } }
            if (lingyuOk && dongtianOk) { dongtianPersistPending = false; if (dongtian != null) dongtian.LastNotice = "洞天与灵域档案均已落盘（提交成功）"; }
        }
        private double DongtianQiFactor() { return dongtian == null ? 1.0 : dongtian.QiRateFactor(DateTime.UtcNow); }
        private double DongtianDamageFactor() { return dongtian == null ? 1.0 : dongtian.DamageFactor(DateTime.UtcNow); }
        private double DongtianReviveFactor() { return dongtian == null ? 1.0 : dongtian.ReviveTimeFactor(DateTime.UtcNow); }
        // 灵域材料带回洞天入库：dtin-<mat>-<seq> 双向台账，灵域档先行落盘，
        // 启动对账双向补平（协议同 B1 破境材料事务）。
        internal bool DongtianImport(string material)
        {
            if (disposed || IsDemo || dongtian == null || lingyu == null || !DongtianUnlocked) return false;
            int stock;
            lingyu.Materials.TryGetValue(material, out stock);
            if (stock < 1) { dongtian.LastNotice = "灵域没有可入库的「" + DongtianRecipes.MaterialName(material) + "」"; return false; }
            string grantId = "dtin-" + material + "-" + (dongtian.ImportSeq + 1);
            stock -= 1;
            if (stock <= 0) lingyu.Materials.Remove(material); else lingyu.Materials[material] = stock;
            lingyu.Grants[grantId] = DateTime.UtcNow.Ticks;
            bool committed = SaveLingyuCore();
            dongtian.ImportSeq++;
            dongtian.Bag[material] = dongtian.CountOf(material) + 1;
            dongtian.Grants.Add(grantId);
            committed = SaveDongtianCore() && committed;
            dongtian.LastNotice = "「" + DongtianRecipes.MaterialName(material) + "」已带回洞天入库" + (committed ? "" : "（档案待落盘，自动重试中）");
            return true;
        }
        // 六个操作入口共用同一持久化语义：内存生效即返回成功，但写盘失败必明示待确认并自动重试。
        private void SaveDongtianWithNotice()
        {
            if (!SaveDongtianCore() && dongtian != null) dongtian.LastNotice += "（档案待落盘，自动重试中）";
        }
        internal bool DongtianPlant(int slot, string seedId) { if (disposed || IsDemo || !DongtianUnlocked) return false; bool ok = dongtian.Plant(slot, seedId, DateTime.UtcNow); if (ok) SaveDongtianWithNotice(); return ok; }
        internal bool DongtianExpand(int expectedLevel)
        {
            if (disposed || IsDemo || !DongtianUnlocked) return false;
            if (DongtianPersistPending) { dongtian.LastNotice = "档案待落盘，请等待自动重试后再扩建"; return false; }
            bool ok = dongtian.Expand(expectedLevel); if (ok) SaveDongtianWithNotice(); return ok;
        }
        internal bool DongtianHarvest(int slot) { if (disposed || IsDemo || !DongtianUnlocked) return false; bool ok = dongtian.Harvest(slot, DateTime.UtcNow); if (ok) SaveDongtianWithNotice(); return ok; }
        internal bool DongtianStartPill(string pillId) { if (disposed || IsDemo || !DongtianUnlocked) return false; bool ok = dongtian.StartPill(pillId, DateTime.UtcNow); if (ok) SaveDongtianWithNotice(); return ok; }
        internal bool DongtianCollectPill() { if (disposed || IsDemo || !DongtianUnlocked) return false; bool ok = dongtian.CollectPill(DateTime.UtcNow); if (ok) SaveDongtianWithNotice(); return ok; }
        internal bool DongtianStartGear(string gearId) { if (disposed || IsDemo || !DongtianUnlocked) return false; bool ok = dongtian.StartGear(gearId, DateTime.UtcNow); if (ok) SaveDongtianWithNotice(); return ok; }
        internal bool DongtianCollectGear() { if (disposed || IsDemo || !DongtianUnlocked) return false; bool ok = dongtian.CollectGear(DateTime.UtcNow); if (ok) SaveDongtianWithNotice(); return ok; }
        private DongtianForm dongtianForm;
        internal void OpenDongtian()
        {
            if (disposed) return;
            if (dongtianForm == null || dongtianForm.IsDisposed) dongtianForm = new DongtianForm(this);
            dongtianForm.RefreshData(); dongtianForm.Show(); dongtianForm.BringToFront(); dongtianForm.Activate();
        }
        // 破境契机评估（B2 时刻授予，幂等，四条路径并行不互斥）：
        // 秘境寻宝 = 蜃楼通关时刻；天材地宝 = 水月灵髓入手时刻；百业悟道见下；
        // 长期苦修由核心按感悟自动领悟。契机绑定获得时的境界与来源事件编号。
        // 评估基线随修仙档持久化（oppsrc.*）：重启只认增量事件，历史蜃楼成绩
        // 与旧灵髓永不会被重新认定为当前境界的新机缘。
        // 基线为 -1 的旧档在首次评估时一次性钉住当前计数并迁移补发（仅此一次）。
        private void EvaluateBreakthroughOpportunities()
        {
            if (cultivation == null || lingyu == null || cultivation.Mortal) return;
            int visits = lingyu.Visits("qianmian");
            int lingsui = BreakthroughMaterialCount;
            bool migrating = cultivation.OppBaselineQianmian < 0 || cultivation.OppBaselineLingsui < 0;
            bool changed = false;
            if (migrating)
            {
                // 旧档迁移：历史成绩钉住当前境界补发一次（不向后放大），基线落定后不再触发。
                if (visits >= 1 && cultivation.GrantOpportunity("realm", "migrate-qv" + visits))
                { battle.LastReward = "破境契机 · 秘境寻宝：蜃楼一行，窥见" + cultivation.RealmName + "门径"; changed = true; }
                if (lingsui > 0 && cultivation.GrantOpportunity("treasure", "migrate-ls" + lingsui))
                { battle.LastReward = "破境契机 · 天材地宝：水月灵髓蕴藏一缕先天灵机"; changed = true; }
            }
            else
            {
                if (visits > cultivation.OppBaselineQianmian && cultivation.GrantOpportunity("realm", "qv" + visits))
                { battle.LastReward = "破境契机 · 秘境寻宝：蜃楼一行，窥见" + cultivation.RealmName + "门径"; changed = true; }
                if (lingsui > cultivation.OppBaselineLingsui && cultivation.GrantOpportunity("treasure", "ls" + lingsui))
                { battle.LastReward = "破境契机 · 天材地宝：水月灵髓蕴藏一缕先天灵机"; changed = true; }
            }
            if (migrating || visits != cultivation.OppBaselineQianmian || lingsui != cultivation.OppBaselineLingsui)
            {
                cultivation.OppBaselineQianmian = visits;
                cultivation.OppBaselineLingsui = lingsui;
                changed = true;
            }
            if (changed) Save();
        }
        private bool BaiyeWudaoEligible()
        {
            if (baiye == null) return false;
            for (int i = 0; i < BaiyeCatalog.Paths.Length; i++)
                if (baiye.LevelOf(BaiyeCatalog.Paths[i].Id) >= 3) return true;
            return baiye.Heritages.Contains("tianji-chart");
        }
        // Real elapsed seconds feed cultivation exactly once per tick, independent of
        // the render-frame dt clamp, the pet's visibility and the network state.
        private void AdvanceCultivation(double now)
        {
            if ((demonstrationOnly && !artDemoRuntime) || cultivation == null) return;
            if (lastCultivationAt < 0 || cultivationPaused || IsDemo) { lastCultivationAt = now; return; }
            double elapsed = (now - lastCultivationAt) * (artDemoRuntime ? artDemoSpeed : IsolatedDesktop ? desktopAcceptanceSpeed : 1); lastCultivationAt = now;
            if (elapsed <= 0) return;
            if (cultivation.AdvanceActiveTime(elapsed, deviceGrade, DongtianQiFactor()) > 0) Save();
            if (encounters.Observe(elapsed)) encounterDue = true;
        }
        // 百业证道：同一节拍用真实流逝秒数结算道韵。与修仙吐纳互不依赖——
        // 暂停修炼不影响百业，关闭百业也不影响修炼；演示与示范运行不结算。
        private void AdvanceBaiye(double now)
        {
            // 示范运行不驱动百业：烟测用 TickBaiyeForTest 走同一结算路径，结果可复现。
            if (demonstrationOnly || baiye == null || baiyeTracker == null) return;
            if (lastBaiyeAt < 0 || IsDemo) { lastBaiyeAt = now; return; }
            double elapsed = now - lastBaiyeAt; lastBaiyeAt = now;
            if (elapsed <= 0) return;
            AdvanceBaiyeCore(elapsed);
        }
        // 结算 + 机缘评估 + 按需即时存档。机缘修为经幂等结算接口发放，
        // 百业模块本身不反向依赖 CultivationProgress。
        private void AdvanceBaiyeCore(double elapsed)
        {
            bool leveledUp;
            string notice = baiyeTracker.Tick(elapsed, out leveledUp);
            if (notice != null) baiyeNotice = notice;
            List<BaiyeEncounterDef> unlocked = BaiyeEncounters.Evaluate(baiye, baiyeTracker.UtcNow());
            if (unlocked.Count > 0)
            {
                BaiyeEncounterDef def = unlocked[0];
                baiyeNotice = "机缘 · " + def.Name + "：" + def.RewardText;
                battle.LastReward = baiyeNotice;
                Save(); // 先持久化解锁记录，再进入奖励结算
                SettleBaiyeRewards();
            }
            if (leveledUp) Save();
            // 破境契机 · 百业悟道（B2 时刻授予）：道途升级或新机缘解锁的瞬间，
            // 已登堂（Lv.3）的积累或天机阵图传承凝作当前境界的破境灵机；
            // 破境后须等下一次真实悟道时刻，旧等级不会自动化作新境界的资格。
            if ((leveledUp || unlocked.Count > 0) && cultivation != null && !cultivation.Mortal
                && !cultivation.HasOpportunity("baiye") && BaiyeWudaoEligible()
                && cultivation.GrantOpportunity("baiye", unlocked.Count > 0 ? "enc-" + unlocked[0].Id : "bw" + DateTime.UtcNow.Ticks))
            {
                cultivation.OppBaselineBaiye = DateTime.UtcNow.Ticks;
                baiyeNotice = battle.LastReward = "破境契机 · 百业悟道：红尘百业凝作一缕悟道灵机";
                Save();
            }
            if (baiyeRewardRetry) SettleBaiyeRewards(); // 上次写盘失败的机缘奖励持续补试
            if (lingyuRewardRetry) SettleLingyuRewards(); // 灵域修为奖励同款补试
        }
        // 机缘奖励幂等结算：协议见 BaiyeSettlement 注释。
        // 进程内入账集合防止单会话重复发放；跨会话由两份存档的标记对账保证。
        private readonly HashSet<string> baiyeRewardApplied = new HashSet<string>(StringComparer.Ordinal);
        private bool baiyeRewardRetry;
        private void SettleBaiyeRewards()
        {
            if (demonstrationOnly || baiye == null || cultivation == null) return;
            DateTime utc = DateTime.UtcNow;
            bool changed = BaiyeSettlement.SyncGranted(baiye, cultivation.ExternalGrants, utc);
            foreach (BaiyeEncounterDef def in BaiyeSettlement.PendingRewards(baiye, cultivation.ExternalGrants))
            {
                if (baiyeRewardApplied.Contains(def.Id)) continue; // 本进程已入账，等待存档对齐
                cultivation.AddQi(def.QiBonus);
                cultivation.ExternalGrants.Add(def.Id);
                baiyeRewardApplied.Add(def.Id);
                try
                {
                    cultivation.Save(CultivationWritePath); // 第二步：修仙存档带标记落地
                    baiye.RewardGrantedUtc[def.Id] = utc.Ticks; // 第三步：百业侧完成标记（随 Save 落地）
                    baiyeNotice = battle.LastReward = "机缘 · " + def.Name + "：" + def.RewardText + "（修为 +" + def.QiBonus + "）";
                    baiyeRewardRetry = false; changed = true;
                }
                catch { baiyeRewardRetry = true; } // 修仙存档写失败：不标完成，下轮对账重试
            }
            if (changed) Save();
        }
        // Smoke hook: feed synthetic elapsed time through the real settlement path.
        internal void TickBaiyeForTest(double elapsedSeconds) { AdvanceBaiyeCore(elapsedSeconds); }
        internal string BaiyeWritePath { get { return baiyePathOverride ?? baiyePath; } }
        internal BaiyeProgress Baiye { get { return baiye; } }
        internal BaiyeTracker BaiyeTracking { get { return baiyeTracker; } }
        internal bool BaiyeExplained { get { return baiye != null && baiye.FirstRunExplained; } }
        internal string BaiyeActivityText { get { return baiyeTracker == null ? "百业识别未开启" : baiyeTracker.StatusText; } }
        internal string BaiyeCurrentPathId { get { return baiyeTracker == null ? null : baiyeTracker.CurrentPathId; } }
        internal string BaiyeNotice { get { return baiyeNotice; } }
        internal string BaiyeResonanceHint { get { return ""; } }
        internal void AcknowledgeBaiye()
        {
            if (baiye == null || baiye.FirstRunExplained) return;
            baiye.FirstRunExplained = true;
            Save();
        }
        // 关闭识别立即生效：停止采样、结算并丢弃当前片段；存档失败不阻塞关闭，
        // 由下一轮周期存档继续尝试（模式已写入内存，本进程内不会再采样）。
        internal bool SetBaiyeMode(int mode)
        {
            if (disposed || baiye == null || mode < 0 || mode > 2) return false;
            if (mode > 0 && !baiye.FirstRunExplained) return false;
            if (baiye.Mode == mode) return true;
            baiye.Mode = mode;
            baiyeTracker.Reset();
            Save();
            return true;
        }
        internal void SetBaiyeManualMark(string markId)
        {
            if (disposed || baiye == null) return;
            string next = markId != null && BaiyeCatalog.FindManualMark(markId) != null ? markId : "";
            if (baiye.ManualMarkId == next) return;
            baiye.ManualMarkId = next;
            baiyeTracker.Reset();
            Save();
        }
        public void OpenBaiyeStele()
        {
            if (disposed) return;
            if (stele == null || stele.IsDisposed) stele = new BaiyeSteleForm(this);
            stele.RefreshData(); stele.Show(); stele.BringToFront(); stele.Activate();
        }
        public void OpenLingyu()
        {
            if (disposed) return;
            if (lingyuForm == null || lingyuForm.IsDisposed) lingyuForm = new LingyuForm(this);
            lingyuForm.RefreshData(); lingyuForm.Show(); lingyuForm.BringToFront(); lingyuForm.Activate();
        }
        public void OpenBreakthrough()
        {
            if (disposed) return;
            if (breakthroughForm == null || breakthroughForm.IsDisposed) breakthroughForm = new BreakthroughForm(this);
            breakthroughForm.RefreshData(); breakthroughForm.Show(); breakthroughForm.BringToFront(); breakthroughForm.Activate();
        }
        // ---- 灵域历练（P3）：进入门控 / 节点选择 / 暂离；秘境战斗只生成虚拟妖物 ----
        internal string LingyuWritePath { get { return lingyuPathOverride ?? lingyuPath; } }
        internal string CultivationWritePath { get { return cultivationPathOverride ?? cultivationPath; } }
        internal LingyuProgress Lingyu { get { return lingyu; } }
        internal MirageEngine Mirage { get { return mirage; } }
        internal bool LingyuCanEnter(LingyuRealmDef realm) { return !disposed && lingyu != null && LingyuCatalog.CanEnter(realm, baiye, cultivation); }
        private JiyuanContext BuildJiyuanContext()
        {
            return new JiyuanContext
            {
                SpiritRoot = cultivation == null ? "" : cultivation.SpiritRoot,
                RealmIndex = cultivation == null ? 0 : cultivation.RealmIndex,
                Layer = cultivation == null ? 1 : cultivation.Layer,
                MirageVisits = lingyu == null ? 0 : lingyu.Visits("qianmian"),
                Baiye = baiye, Lingyu = lingyu
            };
        }
        internal bool LingyuEnter(string realmId)
        {
            if (!LingyuCanExplore || lingyu == null || lingyu.ExpeditionActive) return false;
            LingyuRealmDef realm = LingyuCatalog.FindRealm(realmId);
            if (realm == null || !realm.Playable || !LingyuCanEnter(realm)) return false;
            mirage.Enter(BuildJiyuanContext());
            EvaluateBreakthroughOpportunities(); // 进入灵域也是契机评估点：旧档历史成就在此一次性迁移补发
            Save();
            return true;
        }
        internal void LingyuChoose(int index)
        {
            if (!LingyuCanExplore || lingyu == null || !lingyu.ExpeditionActive) return;
            mirage.Choose(index, BuildJiyuanContext());
            SettleLingyuRewards();
            Save();
        }
        internal void LingyuSuspend()
        {
            if (!LingyuCanSuspend) return;
            AdventureEnemy pending = PendingLingyuEnemy;
            if (pending != null && !battle.WithdrawExpeditionEnemy(pending)) return;
            pendingLingyuEnemyId = null;
            mirage.Suspend();
            Save();
            if (IsolatedDesktop) RecordDesktopEvent(desktopLastSaveSucceeded ? "已暂离蜃楼 · 进度已保存，桌面修行继续" : "已暂离蜃楼 · 档案暂未保存，请保持程序运行等待重试");
            RenderWorld(true);
        }
        private AdventureEnemy PendingLingyuEnemy
        { get { return pendingLingyuEnemyId == null ? null : battle.Enemies.FirstOrDefault(e => e.Id == pendingLingyuEnemyId); } }
        internal bool LingyuCanExplore { get { return !disposed && !IsDemo && !battle.Progress.Dead; } }
        internal bool LingyuPersistencePending { get { return !desktopLastSaveSucceeded; } }
        internal int LingyuRestSeconds
        { get { return !battle.Progress.Dead ? 0 : Math.Max(0, (int)Math.Ceiling(CultivationBalance.ReviveRestSeconds * CultivationCombatPower.ReviveFactor(cultivation) * DongtianReviveFactor() - battle.DeathTime)); } }
        internal bool LingyuCanSuspend
        {
            get
            {
                if (disposed || IsDemo || lingyu == null || !lingyu.ExpeditionActive) return false;
                AdventureEnemy pending = PendingLingyuEnemy;
                return pending == null || (!battle.Progress.Dead && pending.IsVirtual && pending.NoFieldClear && pending.ProcessTarget == null
                    && !pending.CleanupStarted && !pending.Cleaning && !pending.Defeated && !pending.VisualDefeated && !pending.FellInCombat && !pending.Failed);
            }
        }
        // 蜃楼战斗请求：生成一只带秘境标记的虚拟妖物（主镜战更耐打）。
        // 镜妖免疫一键清场——秘境战斗必须真实分出胜负，不走清场捷径。
        // 沿用既有虚拟战斗：自动作战会迎击，玩家也可以手动点击出击；
        // 绝不删除真实文件、关闭进程或回收内存。
        private void LingyuCombatRequested(string label, bool boss)
        {
            if (disposed || IsDemo) return;
            AdventureEnemy former = battle.Enemies.FirstOrDefault(e => e.Id == pendingLingyuEnemyId);
            if (former != null) battle.Enemies.Remove(former);
            AdventureMonster kind = AdventureMonster.All[random.Next(AdventureMonster.All.Length)];
            AdventureEnemy fiend = new AdventureEnemy
            {
                Id = "lingyu-" + Guid.NewGuid().ToString("N"), IsVirtual = true, MonsterKind = kind.Id, NoFieldClear = true,
                SpawnLevel = battle.Progress.Level
            };
            fiend.MaxHealth = EnemyBalance.OrdinaryHealth(battle.Progress.Level, kind.HealthFactor, 0) * (boss ? 4 : 1);
            fiend.Health = fiend.MaxHealth;
            pendingLingyuEnemyId = fiend.Id;
            // 满员时镜妖必须仍能现身（否则蜃楼战斗软锁）：退休一只未交战、
            // 未授权的普通妖物腾名额；绝不挤掉 Boss、在编清理或秘境目标。
            if (!EnemyFieldBudget.CanAddOrdinary(battle.Enemies))
            {
                AdventureEnemy idle = battle.Enemies.FirstOrDefault(e =>
                    !e.IsBoss && !e.NoFieldClear && !e.Authorized && !e.Cleaning && !e.Defeated && !e.Failed);
                if (idle != null) battle.Enemies.Remove(idle);
            }
            Spawn(fiend); battle.Enemies.Add(fiend);
            battle.LastReward = label + "现身桌面 · 击败它继续蜃楼之行";
            RenderWorld(true);
        }
        // 蜃楼战斗恢复入口（重启后首拍自动排队一次；灵域面板刷新时也会触发）：
        // 战斗未决且桌面没有存活镜妖时补发一只，玩家从真实界面即可恢复战斗。
        // 已有存活镜妖时不重复生成；满员由生成路径内部退休闲置普通妖物腾位。
        internal bool TryResumeLingyuCombat()
        {
            if (disposed || IsDemo || lingyu == null) return false;
            if (!lingyu.ExpeditionActive || !lingyu.CombatPending) return false;
            if (pendingLingyuEnemyId != null && battle.Enemies.Any(e => e.Id == pendingLingyuEnemyId)) return false;
            return mirage.ResumePendingCombat();
        }
        // 蜃楼战斗失败出口：修士力竭时镜妖遁入镜中，解除挂起，秘境不软锁。
        internal void LingyuDeathCheck()
        {
            if (!battle.Progress.Dead || pendingLingyuEnemyId == null) return;
            AdventureEnemy lost = battle.Enemies.FirstOrDefault(e => e.Id == pendingLingyuEnemyId);
            if (lost != null) battle.Enemies.Remove(lost);
            pendingLingyuEnemyId = null;
            mirage.OnCombatFailed(BuildJiyuanContext());
            Save();
        }
        // 灵域修为奖励幂等结算：与百业同一跨存档协议（灵域 PendingQi ↔ 修仙 ExternalGrants）。
        private readonly HashSet<string> lingyuRewardApplied = new HashSet<string>(StringComparer.Ordinal);
        private bool lingyuRewardRetry;
        private void SettleLingyuRewards()
        {
            if (demonstrationOnly || lingyu == null || cultivation == null) return;
            bool changed = LingyuSettlement.SyncPaid(lingyu, cultivation.ExternalGrants);
            foreach (KeyValuePair<string, long> pair in LingyuSettlement.PendingQiRewards(lingyu, cultivation.ExternalGrants))
            {
                if (lingyuRewardApplied.Contains(pair.Key)) continue; // 本进程已入账，等待存档对齐
                cultivation.AddQi(pair.Value);
                cultivation.ExternalGrants.Add(pair.Key);
                try
                {
                    cultivation.Save(CultivationWritePath); // 第二步：修仙侧落账 + 标记
                    lingyu.PendingQi.Remove(pair.Key); // 第三步：灵域侧销账（随 Save 落地）
                    lingyuRewardApplied.Add(pair.Key);
                    lingyuRewardRetry = false; changed = true;
                }
                catch { lingyuRewardRetry = true; } // 修仙存档写失败：不销账，下轮对账重试
            }
            if (changed) { try { lingyu.Save(LingyuWritePath); } catch (Exception e) { desktopLastSaveSucceeded = false; scanStatus = "灵域档案暂未保存：" + e.Message; } }
        }
        private void SpawnVirtualFiend()
        {
            AdventureMonster kind = AdventureMonster.All[random.Next(AdventureMonster.All.Length)];
            AdventureEnemy fiend = new AdventureEnemy
            {
                Id = "fiend-" + Guid.NewGuid().ToString("N"), IsVirtual = true, MonsterKind = kind.Id,
                SpawnLevel = battle.Progress.Level
            };
            fiend.MaxHealth = EnemyBalance.OrdinaryHealth(battle.Progress.Level, kind.HealthFactor, 0);
            // 境界-战斗强度联动：炼气中期起偶有精英妖物，更耐打、击杀修为翻倍。
            if (cultivation != null && !cultivation.Mortal && cultivation.RealmIndex == 0
                && cultivation.Layer >= CultivationBalance.EliteMinLayer && random.NextDouble() < CultivationBalance.EliteChance)
            {
                fiend.Elite = true;
                fiend.MaxHealth = (int)(fiend.MaxHealth * CultivationBalance.EliteHealthFactor);
            }
            fiend.Health = fiend.MaxHealth;
            Spawn(fiend); battle.Enemies.Add(fiend);
            battle.LastReward = fiend.Elite
                ? "一头精英" + kind.Name + "被洞天灵气吸引而来 · 击败可获得双倍修为"
                : "一只" + kind.Name + "被洞天灵气吸引而来 · 击败可获得修为";
        }
        private void SettleVirtual(AdventureEnemy enemy)
        {
            int qi = GrantCultivationKill(enemy);
            battle.CompleteVirtual(enemy, qi);
            if (cultivation.Mortal && enemy.FellInCombat) battle.LastReward = cultivation.CanAwaken
                ? "十次红尘历练已满 · 打开修炼面板，觉醒灵根"
                : "红尘历练 " + cultivation.MortalKills + "/" + CultivationBalance.MortalAwakenKills + " · 凡躯渐生灵明";
            if (pendingLingyuEnemyId != null && enemy.Id == pendingLingyuEnemyId)
            {
                pendingLingyuEnemyId = null;
                // 蜃楼战斗胜利：秘境节点/机缘按持久化语义结算，修为随后跨存档对账。
                mirage.OnCombatWon(BuildJiyuanContext());
                SettleLingyuRewards();
                EvaluateBreakthroughOpportunities();
            }
            Save();
        }
        private void SettleHeld(AdventureEnemy enemy)
        {
            int qi = GrantCultivationKill(enemy);
            battle.CompleteHeld(enemy, qi);
            Save();
        }
        public bool CanClearField { get { return !disposed && !demonstrationOnly && enabled && !IsDemo && !battle.Progress.Dead; } }
        public string StateText
        {
            get
            {
                if (failure != null) return "桌面显示异常：" + failure;
                if (!enabled) return "骑士与怪物已隐藏";
                if (demoPending) return "正在停止清理，准备安全演示";
                if (demoStarted >= 0) return "冒险演示 · 不清理文件，不改变经验和装备";
                if (battle.Progress.Dead) return "修士力竭 · 入定调息约 " + Math.Max(1, (int)(CultivationBalance.ReviveRestSeconds * CultivationCombatPower.ReviveFactor(cultivation) - battle.DeathTime)) + " 秒后自行恢复";
                if (lastConnectivity == KnightConnectivity.Offline) return "断网 · 修士入定吐纳，本地修炼与战斗继续";
                if (networkState.Phase == KnightPhase.Revive) return "网络恢复 · 从坟地爬出";
                if (battle.QueueCount > 0) return (battle.Target == null ? "后台清理 · " : battle.Automatic ? "自动规划 · " + tacticalStatus + " · " : "清理队列 · ") + battle.QueueCount + " 个目标 · " + battle.Weapon.Name + " · 战斗Lv." + battle.Progress.Level;
                return "战斗Lv." + battle.Progress.Level + " · " + (actualMoveSpeed > 1 ? "随网速奔跑" : networkState.Phase == KnightPhase.Pant ? "入定吐纳" : "原地待命") + " · " + CleanupAmountText;
            }
        }
        public void UpdateTelemetry(double bytes, bool available)
        {
            if (disposed) return;
            if (owner.InvokeRequired) { Post(delegate { UpdateTelemetry(bytes, available); }); return; }
            bytesPerSecond = Double.IsNaN(bytes) || Double.IsInfinity(bytes) ? 0 : Math.Max(0, bytes); monitoringAvailable = available;
            lastTelemetry = clock.Elapsed.TotalSeconds; if (connectivity != null) connectivity.TrafficEvidence(bytesPerSecond, available);
        }
        public void PlayDemo()
        {
            if (disposed || artDemoRuntime || IsolatedDesktop) return;
            // The demo pauses auto battle; afterwards the persisted unattended
            // preference is restored (auto battle alone never deletes anything).
            resumeAutomaticAfterDemo = false; battle.SetAutomatic(false); RemoveCancelledAutomaticJobs();
            if (workCancellation != null) workCancellation.Cancel(); demoPending = true;
            if (memoryReclaimCancellation != null) memoryReclaimCancellation.Cancel();
            if (cleanupTask == null) StartDemo();
        }
        private void StartDemo()
        {
            demoPending = false; effects.Clear(); ClearLoot(); demoStarted = clock.Elapsed.TotalSeconds; Enabled = true; RenderWorld(true);
        }        internal void StopDemo() { if (demonstrationOnly) return; effects.Clear(); demoStarted = -1; demoPending = false; resumeAutomaticAfterDemo = false; RestoreAutoBattlePreference(); lastTick = clock.Elapsed.TotalSeconds; RenderWorld(true); }
        public void ShowSettings() { OpenEquipment(); }
        public void OpenEquipment()
        {
            if (disposed) return;
            if (IsolatedDesktop) { if (DesktopPanelRequested != null) DesktopPanelRequested(); return; }
            if (artDemoRuntime) { owner.Activate(); return; }
            if (equipment == null || equipment.IsDisposed) equipment = new AdventureEquipmentForm(this);
            equipment.RefreshData(); equipment.Show(); equipment.BringToFront(); equipment.Activate();
        }
        public void RefreshWorld() { RefreshWorld(true); }
        private void RefreshWorld(bool manualRequest)
        {
            if (disposed || demonstrationOnly || !MayObserveLocalResources || scanBusy || IsDemo) return;
            EnsureTempWatcher(); scannedChangeVersion = Interlocked.CompareExchange(ref tempChangeVersion,0,0);
            scanBusy = true; int scanEpoch = cleanupEpoch; lastScan = clock.Elapsed.TotalSeconds; scanStatus = "正在实时复核临时文件与可处理项目…";
            scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            CancellationToken scanToken = scanCancellation.Token;
            Task.Run(delegate
            {
                CleanupSnapshot snapshot = null; Exception error = null;
                try { snapshot = cleanup.Scan(scanToken); } catch (Exception e) { error = e; }
                Post(delegate
                {
                    scanBusy = false; if (scanCancellation != null) { scanCancellation.Dispose(); scanCancellation = null; } if (disposed) return;
                    if (error != null) { scanStatus = "扫描失败：" + error.Message; return; }
                    if (scanEpoch != cleanupEpoch) { scanStatus = "清理期间文件已变化，正在准备重新扫描"; lastScan = -100; return; }
                    ApplyInventory(snapshot, manualRequest);
                    if (enabled) RenderWorld(true);
                });
            });
        }
        private void ApplyInventory(CleanupSnapshot snapshot, bool manualRequest)
        {
            if (snapshot == null || IsDemo) return;
            liveTemp.Observe(snapshot, manualRequest);
            lastInventoryComplete = snapshot.InventoryComplete;
            lastInventoryObservation = clock.Elapsed.TotalSeconds;
            ReconcileRecheck(snapshot, new List<CleanupBatch>());
            foreach (AdventureEnemy enemy in battle.Enemies.ToArray())
            {
                if (enemy.Batch == null || enemy.Authorized || enemy.Cleaning || enemy.Defeated) continue;
                CleanupBatch updated = liveTemp.Refresh(enemy.Batch);
                if (updated == null && snapshot.InventoryComplete)
                {
                    if (enemy.IsDiskBoss) { enemy.Batch = null; enemy.VisualDirty = true; }
                    else battle.Enemies.Remove(enemy);
                }
                else if (updated != null) { enemy.Batch = updated; enemy.VisualDirty = true; }
            }
            candidateBytes = liveTemp.EligibleBytes;
            scanStatus = snapshot.InventoryComplete ? "实时普查已更新：可处理 " + liveTemp.EligibleFiles + " 个，监测 " + liveTemp.TotalFiles + " 个临时文件。" : "本次普查不完整；合并已确认数据，未核实项目保留。";
            lastLandmarkPaint = -100;
            lastRefill = -100;
            RefillEnemies();
        }
        private void ReconcileRecheck(CleanupSnapshot snapshot, IEnumerable<CleanupBatch> fieldBatches)
        {
            HashSet<string> reboundPaths = new HashSet<string>(fieldBatches.SelectMany(batch => batch.Candidates).Select(file => file.Path), StringComparer.OrdinalIgnoreCase);
            foreach (string path in recheckCandidates.Keys.ToArray())
                if (reboundPaths.Contains(path) || (snapshot.InventoryComplete && snapshot.ObservedPathsAvailable && !snapshot.ObservedPaths.Contains(path))) recheckCandidates.Remove(path);
            recheckBytes = recheckCandidates.Values.Sum(file => file.Length); recheckFiles = recheckCandidates.Count;
        }
        private void RefillEnemies()
        {
            if (IsDemo) return;
            int slots = EnemyFieldBudget.AvailableOrdinarySlots(battle.Enemies);
            if (DesktopObservesSystem) slots = Math.Min(slots, Math.Max(0, 3 - EnemyFieldBudget.CountOrdinary(battle.Enemies)));
            if (slots <= 0 || clock.Elapsed.TotalSeconds - lastRefill < .25) return;
            lastRefill = clock.Elapsed.TotalSeconds;
            foreach (CleanupBatch batch in liveTemp.TakePending(battle.Enemies, slots))
            {
                AdventureEnemy enemy = AdventureMonster.Create(batch, random, battle.Progress.Level);
                Spawn(enemy); battle.Enemies.Add(enemy);
            }
            BindDiskBossCandidates();
        }
        private void EnsureTempWatcher()
        {
            if (tempWatcher != null || demonstrationOnly || !MayObserveLocalResources || disposed) return;
            try
            {
                InstallTempWatcher(cleanup.RootPath);
            }
            catch { if (tempWatcher != null) tempWatcher.Dispose(); tempWatcher = null; }
        }
        private void InstallTempWatcher(string root)
        {
                tempWatcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite, InternalBufferSize = 32768 };
                FileSystemEventHandler changed = delegate { Interlocked.Increment(ref tempChangeVersion); };
                tempWatcher.Created += changed; tempWatcher.Changed += changed; tempWatcher.Deleted += changed;
                tempWatcher.Renamed += delegate { Interlocked.Increment(ref tempChangeVersion); };
                tempWatcher.Error += delegate { Interlocked.Increment(ref tempChangeVersion); };
                tempWatcher.EnableRaisingEvents = true;
        }
        private void ReadDisk(double now)
        {
            if (diskBusy || demonstrationOnly || !MayObserveLocalResources || disposed) return;
            diskBusy = true; lastDisk = now;
            Task.Run(delegate
            {
                DiskSpaceSnapshot value = diskSpace.Read();
                Post(delegate { diskBusy = false; ObserveDisk(value, clock.Elapsed.TotalSeconds); });
            });
        }
        private void ObserveDisk(DiskSpaceSnapshot value, double now)
        {
            if (disposed) return; diskSnapshot = value ?? DiskSpaceSnapshot.Unavailable("磁盘采样失败");
            if (IsDemo) return;
            AdventureEnemy existing = battle.Enemies.FirstOrDefault(e => e.IsDiskBoss && !e.Defeated);
            if (diskSnapshot.Available && diskSnapshot.AvailableBytes >= LowDiskBossPolicy.RecoveryThresholdBytes && existing != null && !existing.Authorized && !existing.Cleaning)
            {
                // When disk pressure recovers, safe file work remains ordinary work.
                // Do not invalidate a target the user already queued or a running cleaner.
                CleanupBatch formerBatch = existing.Batch; battle.Enemies.Remove(existing);
                if (formerBatch != null)
                    foreach (CleanupBatch batch in EnemyFieldBudget.CoalesceBatches(new[] { formerBatch }, EnemyFieldBudget.AvailableOrdinarySlots(battle.Enemies)))
                    {
                        AdventureEnemy ordinary = AdventureMonster.Create(batch, random, battle.Progress.Level);
                        ordinary.Failed = existing.Failed; Spawn(ordinary); battle.Enemies.Add(ordinary);
                    }
                existing = null;
            }
            if (diskBossPolicy.Observe(diskSnapshot, now, existing != null || !EnemyFieldBudget.CanAddBoss(battle.Enemies, true)))
            {
                AdventureEnemy boss = new AdventureEnemy { Id = "disk-boss-" + Guid.NewGuid().ToString("N"), IsBoss = true, IsDiskBoss = true, MonsterKind = "boss", MaxHealth = 800 + battle.Progress.Level * 40 };
                boss.Health = boss.MaxHealth; Spawn(boss); battle.Enemies.Add(boss);
                BindDiskBossCandidates();
                if (boss.IsDiskWarning) { battle.LastReward = AdventureBattle.DiskWarningMessage; lastScan = -100; }
                else battle.LastReward = "C 盘剩余空间低于 1 GiB：磁盘危机 Boss 出现！击败后清理它对应的临时文件。";
            }
            else BindDiskBossCandidates();
        }
        private void BindDiskBossCandidates()
        {
            // Without cleanup consent the disk boss stays a pure warning omen:
            // it never takes ownership of real cleanup candidates.
            if (!consent.CleanupEnabled) return;
            AdventureEnemy boss = battle.Enemies.FirstOrDefault(e => e.IsDiskBoss && e.IsDiskWarning && !e.Defeated);
            if (boss == null) return;
            AdventureEnemy[] donors = battle.Enemies.Where(e => !e.IsBoss && e.Batch != null && !e.Authorized && !e.Cleaning && !e.Defeated && !e.Failed).ToArray();
            if (donors.Length == 0) return;
            // Transfer ownership, never duplicate it. Both normal mobs and the disk
            // boss enter the same exclusive, identity-checked cleanup worker.
            List<CleanupCandidate> files = donors.SelectMany(e => e.Batch.Candidates).Where(f => f.EligibleForCleanup && String.Equals(Path.GetPathRoot(f.Path), @"C:\", StringComparison.OrdinalIgnoreCase)).GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            if (files.Count == 0) return;
            boss.Batch = new CleanupBatch(files, 1); boss.VisualDirty = true;
            HashSet<string> transferred = new HashSet<string>(files.Select(f=>f.Path),StringComparer.OrdinalIgnoreCase);
            foreach (AdventureEnemy donor in donors)
            {
                List<CleanupCandidate> rest=donor.Batch.Candidates.Where(f=>!transferred.Contains(f.Path)).ToList();
                if(rest.Count==0)battle.Enemies.Remove(donor);
                else { donor.Batch=new CleanupBatch(rest,1); donor.VisualDirty=true; }
            }
            battle.LastReward = "磁盘危机 Boss 已绑定 " + files.Count + " 个临时文件 · 可清理 " + AdventureBattle.FormatBytes(boss.Bytes);
        }
        // --perf-probe 分模块测量（默认关闭，正常运行的开销仅为每节一次布尔判断）。
        private static readonly int SecTotal = PerfProbe.Register("tick-total");
        private static readonly int SecCultivation = PerfProbe.Register("cultivation");
        private static readonly int SecBaiye = PerfProbe.Register("baiye");
        private static readonly int SecCleanupDrain = PerfProbe.Register("cleanup-drain");
        private static readonly int SecDisk = PerfProbe.Register("disk-read");
        private static readonly int SecConnectivity = PerfProbe.Register("connectivity");
        private static readonly int SecBattleCore = PerfProbe.Register("battle-core");
        private static readonly int SecCombatMove = PerfProbe.Register("combat-move");
        private static readonly int SecEnemyLoop = PerfProbe.Register("enemy-loop");
        private static readonly int SecWorldMaint = PerfProbe.Register("world-maint");
        private static readonly int SecSave = PerfProbe.Register("save");
        private static readonly int SecRender = PerfProbe.Register("render");
        private void Tick(object sender, EventArgs args)
        {
            if (disposed) return;
            bool probe = PerfProbe.Enabled;
            if (probe) { PerfProbe.BeginTotal(); PerfProbe.Begin(); }
            double now = clock.Elapsed.TotalSeconds, dt = Math.Max(0, Math.Min(.1, now - lastTick)) * (artDemoRuntime ? artDemoSpeed : IsolatedDesktop ? desktopAcceptanceSpeed : 1); lastTick = now;
            // P1 自适应节拍：战斗动画/拖拽/演示/战利品期间保持 15ms；
            // 静态桌面降到 33ms（30fps 对桌宠足够流畅），整拍 CPU 近半。
            bool busyFrame = demoStarted >= 0 || demoPending || dragging || attackAnimation > 0 || actualMoveSpeed > 1 || lootViews.Count > 0 || now < blackholeUntil;
            if (!busyFrame)
                foreach (AdventureEnemy e in battle.Enemies)
                    if (!e.IsVisuallyHidden && (e.AttackFlash > .05 || e.HitFlash > 0 || e.VisualDefeated || e.Dragging)) { busyFrame = true; break; }
            int wantedInterval = busyFrame ? 15 : 33;
            if (timer.Interval != wantedInterval) timer.Interval = wantedInterval;
            // Cultivation uses the raw elapsed time, never the render-clamped dt.
            AdvanceCultivation(now);
            if (probe) { PerfProbe.End(SecCultivation); PerfProbe.Begin(); }
            // 百业道韵同样按真实流逝结算，且藏在后台/隐藏桌宠时照常识别前台工作。
            AdvanceBaiye(now);
            if (probe) { PerfProbe.End(SecBaiye); PerfProbe.Begin(); }
            // A consent revocation whose file update failed is retried until the
            // stale grant is gone — even while the pet is hidden.
            if ((consentRetryCleanup || consentRetryMemory || consentRetryAutoMaint) && now >= consentRetryAt + 5) { consentRetryAt = now; TryFlushConsentRevocation(); }
            // 洞天/灵域档案的待确认操作同款：每 15 秒重试直至双档落盘提交成功。
            if (dongtianPersistPending && now >= dongtianPersistRetryAt) { dongtianPersistRetryAt = now + 15; TryFlushDongtianPersistence(); }
            DrainCleanup(); DrainMemoryReclaim(); DrainDesktopProcessAction(); if (demoPending && cleanupTask == null) StartDemo();
            if (probe) { PerfProbe.End(SecCleanupDrain); PerfProbe.Begin(); }
            if (!demonstrationOnly && MayObserveLocalResources && now - lastDisk >= 5 && !diskBusy) ReadDisk(now);
            if (probe) { PerfProbe.End(SecDisk); PerfProbe.Begin(); }
            if (!enabled) { if (probe) { PerfProbe.End(SecRender); PerfProbe.EndTotal(SecTotal); } return; }
            effects.Tick(dt);
            if (demoPending) { RenderWorld(false); if (probe) { PerfProbe.End(SecRender); PerfProbe.EndTotal(SecTotal); } return; }
            if (demoStarted >= 0)
            {
                if (demonstrationOnly || now - demoStarted < 24) { renderTime += dt; RenderWorld(now - lastEnemyPaint >= .065); if (probe) { PerfProbe.End(SecRender); PerfProbe.EndTotal(SecTotal); } return; }
                StopDemo();
            }
            if (probe) { PerfProbe.End(SecRender); PerfProbe.Begin(); }
            KnightConnectivity online = connectivity == null ? KnightConnectivity.Online : connectivity.Read(out connectivityDetail);
            lastConnectivity = online;
            // Cultivation is local-first: losing the network never kills the
            // cultivator or stops local battle; the pet meditates (Pant) instead.
            networkState.Tick(dt, bytesPerSecond, monitoringAvailable && now - lastTelemetry < 6,
                online == KnightConnectivity.Offline ? KnightConnectivity.Unknown : online);
            if (probe) { PerfProbe.End(SecConnectivity); PerfProbe.Begin(); }
            // 境界-战斗强度联动：每拍同步修仙战力系数（凡人偏弱、觉醒渐强、筑基攻防质变）。
            battle.CultivationDamageFactor = CultivationCombatPower.DamageFactor(cultivation) * DongtianDamageFactor();
            battle.CultivationDefenseFactor = CultivationCombatPower.DefenseFactor(cultivation);
            battle.Tick(dt);
            // 蜃楼重启恢复：构造期仅排队（渲染未就绪），首拍补发未决战斗的镜妖。
            if (lingyuCombatResumeQueued) { lingyuCombatResumeQueued = false; TryResumeLingyuCombat(); }
            // 蜃楼战斗中修士力竭：镜妖遁入镜中，机缘战斗按已选落空，秘境不软锁。
            LingyuDeathCheck();
            // Unattended recovery: a defeated cultivator always meditates back to
            // full health; no manual revival and no fixed online schedule needed.
            // 筑基修士恢复加倍（CultivationCombatPower.ReviveFactor）。
            if (battle.Progress.Dead && !IsDemo && battle.DeathTime >= CultivationBalance.ReviveRestSeconds * CultivationCombatPower.ReviveFactor(cultivation) * DongtianReviveFactor())
            {
                battle.Revive();
                // 凡人力竭后照常自行调息恢复，但恢复后保持手动待机，等待玩家主动出手。
                if (cultivation.AutoBattle && cultivation.AutoBattleUnlocked) battle.SetAutomatic(true);
                battle.LastReward = cultivation.AutoBattleUnlocked ? "修士调息完毕，伤势尽复，继续自主修行" : "凡躯调息完毕，伤势尽复；点击妖物继续历练";
                RecordDesktopEvent(battle.LastReward);
                Save();
            }
            ScaleOrdinaryEnemies(); foreach (LootView loot in lootViews.ToArray()) { loot.Life -= dt; if (loot.Life <= 0) { loot.Window.Dispose(); lootViews.Remove(loot); } } renderTime += dt; attackAnimation = Math.Max(0, attackAnimation - dt * 3);
            if (probe) { PerfProbe.End(SecBattleCore); PerfProbe.Begin(); }
            bool canFight = !battle.Progress.Dead && networkState.Phase != KnightPhase.Dying && networkState.Phase != KnightPhase.Grave && networkState.Phase != KnightPhase.Revive;
            // Ambient virtual fiends keep the cultivation world alive even when the
            // machine has no temp files at all; the field budget caps render cost.
            if (encounterDue)
            {
                encounterDue = false;
                if (canFight && EnemyFieldBudget.AvailableOrdinarySlots(battle.Enemies) > 0) SpawnVirtualFiend();
                else encounters.Postpone();
            }
            if (canFight && battle.Automatic) battle.PlanNext(x, y, AutoMaintenanceEligibility);
            AdventureEnemy target = canFight ? battle.Target : null;
            if (target == null || !battle.Automatic) { combatTactics.Reset(); tacticalStatus = ""; }
            double moveStartX = x, moveStartY = y;
            if (!dragging && now > pauseMotionUntil)
            {
                if (target != null)
                {
                    if (battle.Automatic) AdvanceAutomaticCombat(target, now, dt);
                    else if (IsolatedDesktop) AdvanceDesktopManualApproach(target, now, dt);
                    else
                    {
                    double tx = target.X + (target.IsBoss ? 30 : -35), ty = target.Y - (HeroSize.Height - EnemySize(target).Height);
                    double distance = AdventureBattle.Distance(x, y, tx, ty);
                    if (distance > battle.Weapon.Range * displayScale * .8) MoveToward(tx, ty, Math.Max(200, networkState.PixelsPerSecond), dt);
                    else
                    {
                        heroFacing = target.X >= x;
                        // 凡人手动战斗：接近可以自动，出招必须玩家点击（TryMortalStrike），
                        // 绝不能点一下就自动看完整场战斗；觉醒后手动模式保持原样。
                        if (!cultivation.Mortal && battle.Cooldown <= 0) StrikeTarget(target, now);
                    }
                    }
                }
                else if (canFight && roaming && (!IsolatedDesktop || !cultivation.Mortal) && networkState.Phase == KnightPhase.Run)
                { if (AdventureBattle.Distance(x, y, targetX, targetY) < 18) PickTarget(); MoveToward(targetX, targetY, networkState.PixelsPerSecond, dt); }
            }
            actualMoveSpeed = dragging || dt <= 0 ? 0 : AdventureBattle.Distance(moveStartX, moveStartY, x, y) / dt;
            runCycle = (runCycle + actualMoveSpeed * dt / (displayScale * ((IsolatedDesktop || artDemoRuntime) ? ArtDemoHeroRig.TravelPerCycle : HeroPoseRig.TravelPerCycle))) % 1;
            if (probe) { PerfProbe.End(SecCombatMove); PerfProbe.Begin(); }
            foreach (AdventureEnemy enemy in battle.Enemies.ToArray())
            {
                enemy.MoveSpeed = 0;
                if (now < blackholeUntil && enemy.Authorized && !enemy.IsBoss && !enemy.Failed && !enemy.IsVisuallyHidden) MoveEnemy(enemy, blackholeX, blackholeY, 280, dt);
                if (enemy.Defeated) { if (enemy.IsVisuallyHidden) battle.Enemies.Remove(enemy); continue; }
                if (enemy.Cleaning || enemy.VisualDefeated) continue;
                AdvanceEnemyPatrol(enemy, canFight, dt);
                AttackHero(enemy, canFight, dt);
                if (battle.Progress.Dead && canFight) { battle.StopFightingAfterDeath(); canFight = false; Save(); }
            }
            if (probe) { PerfProbe.End(SecEnemyLoop); PerfProbe.Begin(); }
            PumpCleanup();
            RefillEnemies();
            if (now - lastScan >= InventoryIntervalSeconds || (now - lastScan >= .35 && scannedChangeVersion != Interlocked.CompareExchange(ref tempChangeVersion,0,0))) RefreshWorld(false);
            if (!demonstrationOnly && MayObserveLocalResources && now - lastMemory >= 1 && !memoryBusy) ReadMemory(now);
            if (!demonstrationOnly && !IsolatedDesktop && now - lastRegion >= 60) { lastRegion = now; region.RefreshAsync(); }
            if (IsolatedDesktop) MaintainDesktopField();
            if (probe) { PerfProbe.End(SecWorldMaint); PerfProbe.Begin(); }
            if (now - lastSave >= 20) Save();
            if (probe) { PerfProbe.End(SecSave); PerfProbe.Begin(); }
            RenderWorld(false);
            if (probe) { PerfProbe.End(SecRender); PerfProbe.EndTotal(SecTotal); }
        }
        private void AdvanceAutomaticCombat(AdventureEnemy target, double now, double dt)
        {
            PointF heroFeet = new PointF((float)(x + HeroSize.Width / 2.0), (float)(y + HeroSize.Height));
            Size targetSize = EnemySize(target);
            PointF targetFeet = new PointF((float)(target.X + targetSize.Width / 2.0), (float)(target.Y + targetSize.Height));
            PointF[] threats = battle.Enemies.Where(e => !e.Defeated && !e.VisualDefeated && !e.Cleaning && !e.Failed && !e.IsDiskWarning)
                .Select(e => new PointF((float)(e.X + EnemySize(e).Width / 2.0), (float)(e.Y + EnemySize(e).Height))).ToArray();
            CombatTacticsDecision choice = combatTactics.Plan(new CombatTacticsInput {
                Automatic = true, TargetId = target.Id, Hero = heroFeet, Target = targetFeet,
                MovementBounds = new RectangleF(area.Left + HeroSize.Width / 2f, area.Top + HeroSize.Height, Math.Max(0, area.Width - HeroSize.Width), Math.Max(0, area.Height - HeroSize.Height)),
                Scale = displayScale, WeaponRange = battle.Weapon.Range * displayScale * .8,
                Cooldown = battle.Cooldown, HealthRatio = (double)battle.Progress.Health / Math.Max(1, battle.Progress.MaxHealth), Time = now, Threats = threats
            });
            tacticalStatus = choice.Mode == "retreat" ? "拉开距离" : choice.Mode == "flank" ? "侧移避让" : choice.Mode == "counter" ? "反击" : choice.Mode == "approach" ? "接近目标" : "观察敌人";
            bool shouldMove = choice.Move; PointF destination = choice.Destination;
            if (IsolatedDesktop && (choice.Mode == "approach" || DesktopActorsOverlap(target)))
            { destination = DesktopCombatStandPoint(target); shouldMove = true; }
            if (shouldMove) MoveToward(destination.X - HeroSize.Width / 2.0, destination.Y - HeroSize.Height, Math.Max(200, networkState.PixelsPerSecond), dt);
            if (choice.Attack && battle.Cooldown <= 0 && EnemyHeroDistance(target) <= battle.Weapon.Range * displayScale * .8 + .01)
            {
                heroFacing = targetFeet.X >= heroFeet.X;
                StrikeTarget(target, now);
            }
        }
        private void ScaleOrdinaryEnemies()
        {
            foreach (AdventureEnemy enemy in battle.Enemies)
            {
                if (enemy.IsBoss || enemy.Cleaning || enemy.Defeated || enemy.VisualDefeated || enemy.SpawnLevel >= battle.Progress.Level) continue;
                double remaining = enemy.Health / (double)Math.Max(1,enemy.MaxHealth);
                enemy.MaxHealth = EnemyBalance.OrdinaryHealth(battle.Progress.Level,AdventureMonster.Find(enemy.MonsterKind).HealthFactor,enemy.Bytes);
                enemy.Health = Math.Max(1,(int)Math.Ceiling(enemy.MaxHealth*remaining)); enemy.SpawnLevel=battle.Progress.Level; enemy.VisualDirty=true;
            }
        }
        // 格挡：点击角色触发，1 秒内来袭伤害大幅减免，10 秒冷却；凡人期关键生存操作，修士期同样可用。
        internal bool TryBlock()
        {
            if (disposed || battle.Progress.Dead) return false;
            double now = clock.Elapsed.TotalSeconds;
            if (now < blockCooldownUntil) { battle.LastReward = "格挡调息中（约 " + Math.Max(1, (int)Math.Ceiling(blockCooldownUntil - now)) + " 秒后再用）"; return false; }
            blockUntil = now + 1.0; blockCooldownUntil = now + 10;
            battle.LastReward = "格挡！凝住气血，接下一瞬的来势将被卸去大半";
            RecordDesktopEvent("格挡 · 保护持续1秒，10秒后可再次使用");
            return true;
        }

        // 凡人主动攻击：点击已选目标且进入射程、冷却就绪时出一招；每招都要玩家出手，禁止站桩自动打完整场战斗。
        private void TryMortalStrike(AdventureEnemy target)
        {
            if (disposed || IsDemo || battle.Automatic || battle.Progress.Dead) return;
            if (battle.Target != target || battle.Cooldown > 0) return;
            if (EnemyHeroDistance(target) > battle.Weapon.Range * displayScale * .8 + .01) { battle.LastReward = "距离太远：角色会先自动接近，再点击出招"; return; }
            heroFacing = target.X >= x;
            StrikeTarget(target, clock.Elapsed.TotalSeconds);
        }

        private void StrikeTarget(AdventureEnemy target, double now)
        {
            attackAnimation = 1;
            if (battle.Weapon.Id == "blackhole") { blackholeUntil = now + 1.1; blackholeX = target.X; blackholeY = target.Y; }
            if ((!artDemoRuntime || !artDemoHostHidden) && (!IsolatedDesktop || (desktopActorsVisible && !desktopTestAdvancing))) effects.Fire(new PointF((float)x + 110 * displayScale, (float)y + 125 * displayScale), new PointF((float)target.X + EnemySize(target).Width / 2f, (float)target.Y + EnemySize(target).Height * .47f), battle.Weapon.Id, displayScale);
            int inventoryBefore = battle.Progress.Inventory.Count;
            int targetHealthBefore = target.Health;
            List<AdventureEnemy> killed = battle.Strike(target);
            if (IsolatedDesktop && cultivation.Mortal && target.Health < targetHealthBefore)
                RecordDesktopEvent("出招命中" + DesktopEnemyName(target) + " · 伤害 " + (targetHealthBefore - target.Health) + " · 剩余气血 " + target.Health + "/" + target.MaxHealth);
            foreach (AdventureEnemy defeated in killed) BeginCleanup(defeated);
            if (battle.Progress.Inventory.Count > inventoryBefore) ShowLoot(battle.Progress.Inventory.Last(), target.X, target.Y);
            if (killed.Count > 0) Save();
        }
        private void ReadMemory(double now)
        {
            if (!MayObserveLocalResources) return;
            double elapsed = lastMemory < 0 ? 5 : now - lastMemory; lastMemory = now; memoryBusy = true;
            Task.Run(delegate
            {
                MemorySnapshot value = null;
                try { value = CleanupService.ReadMemory(); } catch { }
                Post(delegate
                {
                    memoryBusy = false; memoryAvailable = value != null && value.Available; if (!memoryAvailable || disposed) return; memoryPercent = value.UsedPercent;
                    
                    // RAM updates the visible progress every five seconds. Inventory
                    // keeps its own cadence, even while growth waits for safe files.
                    if (!IsDemo) lastLandmarkPaint = -100;
                    if (!IsDemo && memoryReclaimTask == null && EnemyFieldBudget.CanAddBoss(battle.Enemies, false) && battle.ObserveMemory(value.UsedPercent, elapsed, now))
                    {
                        AdventureEnemy boss = EnemyBalance.MemoryBoss(battle.Progress, battle.Weapon, random);
                        boss.Health = boss.MaxHealth; Spawn(boss); battle.Enemies.Add(boss);
                        battle.LastReward = "内存超过 70%：邪恶分身 Boss 出现！击败后尝试回收后台工作集";
                    }
                });
            });
        }
        private void BeginCleanup(AdventureEnemy enemy)
        {
            if (disposed || IsDemo || !enemy.Authorized || enemy.Defeated || enemy.CleanupStarted) return;
            GrantCultivationKill(enemy);
            if (enemy.ProcessTarget != null) { BeginDesktopProcessAction(enemy); return; }
            // Pure cultivation mode (default): virtual fiends and, without explicit
            // maintenance consent, file monsters and bosses all settle virtually.
            // No kill ever deletes real files or trims another process on its own.
            if (enemy.IsVirtual) { SettleVirtual(enemy); return; }
            if (!DesktopCanMaintain(enemy)) { SettleHeld(enemy); return; }
            // Each real-maintenance scope is gated by its own persisted consent:
            // memory bosses need memory consent; file monsters and disk bosses
            // need cleanup consent. Without it the kill settles purely virtually.
            bool memoryBoss = enemy.IsBoss && !enemy.IsDiskBoss;
            if (memoryBoss ? !consent.MemoryEnabled : !consent.CleanupEnabled) { SettleHeld(enemy); return; }
            if (memoryBoss)
            {
                if (demonstrationOnly && simulatedMemoryReclaimer == null) return;
                battle.Complete(enemy, 0, 0, null, 1); StartMemoryReclaim(); Save(); return;
            }
            if (demonstrationOnly && simulatedCleaner == null) return;
            if (enemy.Batch == null) { battle.Complete(enemy, 0, 0, "没有真实文件批次", 1); return; }
            if (!pendingCleanup.Contains(enemy)) pendingCleanup.Enqueue(enemy);
            // The read-only census yields its filesystem gate to a newly killed target.
            cleanupNotice = "怪物已击败 · 正在清理对应临时文件";
            PumpCleanup();
        }
        private void StartMemoryReclaim()
        {
            if (memoryReclaimTask != null || disposed || IsDemo || (demonstrationOnly && simulatedMemoryReclaimer == null)) return;
            memoryReclaimCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            CancellationToken token = memoryReclaimCancellation.Token;
            memoryReclaimTask = simulatedMemoryReclaimer != null ? simulatedMemoryReclaimer(token) : Task.Run(delegate {
                try { return MemoryReclaimer.Reclaim(token); }
                catch (Exception e) { return new MemoryReclaimResult { Error = e.Message }; }
            });
            battle.LastReward = "邪恶分身已击败 · 正在回收后台进程工作集…";
        }
        private void DrainMemoryReclaim()
        {
            if (memoryReclaimTask == null || !memoryReclaimTask.IsCompleted) return;
            MemoryReclaimResult result = memoryReclaimTask.IsFaulted ? new MemoryReclaimResult {Error="工作集回收失败"} : memoryReclaimTask.IsCanceled ? new MemoryReclaimResult {Error="回收已取消"} : memoryReclaimTask.Result;
            memoryReclaimTask = null; memoryReclaimCancellation.Dispose(); memoryReclaimCancellation = null;
            cleanupNotice = result.Summary; battle.LastReward = result.Summary;
            RecordDesktopMaintenance(new DesktopMaintenanceEntry { Kind = "Memory", Summary = result.Summary, WorkingSetReduction = result.WorkingSetReduction, TrimmedProcesses = result.Trimmed });
            if (result.After != null && result.After.Available) { memoryAvailable = true; memoryPercent = result.After.UsedPercent; }
            lastMemory = -100; lastLandmarkPaint = -100;
        }
        private void PumpCleanup()
        {
            if (disposed || (demonstrationOnly && simulatedCleaner == null) || !enabled || IsDemo || cleanupTask != null) return;
            while (pendingCleanup.Count > 0)
            {
                AdventureEnemy enemy = pendingCleanup.Dequeue();
                if (!enemy.Cleaning || !enemy.Authorized || enemy.Defeated) continue;
                if (!DesktopCanMaintain(enemy)) { SettleHeld(enemy); continue; }
                enemy.CleanupStarted = true; activeCleanupEnemy = enemy; cleanupEpoch++; workCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                CancellationToken token = workCancellation.Token;
                cleanupTask = simulatedCleaner != null ? simulatedCleaner(enemy.Batch, token) : Task.Run(delegate
                {
                    try { token.ThrowIfCancellationRequested(); return cleanup.Clean(enemy.Batch, token); }
                    catch (OperationCanceledException) { return new CleanupResult { Cancelled = true, Message = "清理已取消" }; }
                    catch (Exception e) { return new CleanupResult { Message = e.Message }; }
                });
                return;
            }
        }
        private void DrainCleanup()
        {
            if (cleanupTask == null || !cleanupTask.IsCompleted) return;
            AdventureEnemy enemy = activeCleanupEnemy;
            CleanupResult result = cleanupTask.IsFaulted ? new CleanupResult { Message = "清理任务失败：" + cleanupTask.Exception.GetBaseException().Message }
                : cleanupTask.IsCanceled ? new CleanupResult { Cancelled = true, Message = "清理任务已取消" } : cleanupTask.Result;
            cleanupTask = null; activeCleanupEnemy = null; cleanupEpoch++; workCancellation.Dispose(); workCancellation = null;
            int priorWeapons = battle.Progress.Inventory.Count;
            battle.Complete(enemy, result.FreedBytes, result.DeletedFiles, result.Message, random.NextDouble(), result.Cancelled);
            if (battle.Progress.Inventory.Count > priorWeapons && enabled && !IsDemo) ShowLoot(battle.Progress.Inventory.Last(), enemy.X, enemy.Y);
            // File failure is an accounting result, never a reason to resurrect a dead mob.
            if (result.RemainingCandidates.Count > 0) RecordRecheck(result.RemainingCandidates);
            else if (result.DeletedFiles == 0 && enemy.Batch != null) RecordRecheck(enemy.Batch.Candidates);
            candidateBytes = Math.Max(0, candidateBytes - result.FreedBytes);
            cleanupNotice = "已清理 " + result.DeletedFiles + " 个文件 / " + AdventureBattle.FormatBytes(result.FreedBytes) + (recheckFiles > 0 ? "；待重扫 " + recheckFiles + " 个 / " + AdventureBattle.FormatBytes(recheckBytes) : "");
            RecordDesktopMaintenance(new DesktopMaintenanceEntry { Kind = "Files", Summary = cleanupNotice + "；" + result.Message + (result.Cancelled ? "（已取消，以上为实际完成量）" : ""), FreedBytes = result.FreedBytes, DeletedFiles = result.DeletedFiles });
            liveTemp.Deleted(enemy.Batch, result); candidateBytes = liveTemp.EligibleBytes;
            RefillEnemies();
            lastLandmarkPaint = -100; lastDisk = -100;
            lastScan = -100;
            Save();
            PumpCleanup();
            // Completion reduces the current field's displayed bytes immediately.
            // Do not force an instant rescan that refills it from the same old files.
        }        private void Post(Action action)
        {
            if (disposed || owner.IsDisposed || !owner.IsHandleCreated) return;
            try { owner.BeginInvoke(new Action(delegate { if (!disposed) action(); })); } catch (InvalidOperationException) { }
        }
        private string CleanupAmountText { get { return liveTemp.Known ? "可处理 " + liveTemp.EligibleFiles + " 项 · " + AdventureBattle.FormatBytes(candidateBytes) : "可处理：等待扫描"; } }
        private double InventoryIntervalSeconds { get { return 2; } }
        private string CastleAmountText { get { return CleanupAmountText; } }
        private string GrowthStatusText { get { return liveTemp.Known ? "临时文件 " + liveTemp.TotalFiles + " 项 · 敌人 " + battle.Enemies.Count(e=>!e.IsVisuallyHidden) + "/11" : "等待临时文件采样"; } }
        internal double ResourceGrowthRatio { get { return battle.Enemies.Count(e=>!e.IsVisuallyHidden) / 11.0; } }
        internal string ResourceGrowthText
        {
            get
            {
                string freshness = lastInventoryObservation < 0 ? "等待扫描" : "最近复核 " + (int)Math.Max(0,clock.Elapsed.TotalSeconds-lastInventoryObservation) + " 秒前";
                return "临时文件实时监测 · " + GrowthStatusText + "\r\n" + CleanupAmountText + "；监测总量 " + AdventureBattle.FormatBytes(liveTemp.TotalBytes) + "\r\n" +
                    "待重扫 " + recheckFiles + " 项；近期/占用文件须通过安全检查后才能删除。\r\n" + freshness + (lastInventoryComplete ? "" : " · 普查未完整，部分项目等待核实") + "；文件变化立即触发复核，满员后等待空位。";
            }
        }
        private void RecordRecheck(IEnumerable<CleanupCandidate> candidates)
        {
            foreach (CleanupCandidate candidate in candidates)
                recheckCandidates[candidate.Path] = candidate;
            recheckBytes = recheckCandidates.Values.Sum(file => Math.Max(0, file.Length)); recheckFiles = recheckCandidates.Count;
        }
        private void RegionChanged(object sender, EventArgs args) { Post(delegate { if (!demonstrationOnly) battle.Progress.LastThemeCode = region.CurrentCode; if (enabled) RenderWorld(true); }); }
        private void Spawn(AdventureEnemy enemy)
        {
            enemy.X = area.Left + 35 + random.NextDouble() * Math.Max(1, area.Width - 230);
            enemy.Y = area.Top + 30 + random.NextDouble() * Math.Max(1, area.Height - 240); BoundEnemy(enemy);
            if (IsolatedDesktop) PlaceDesktopSpawn(enemy);
            PickEnemyTarget(enemy);
        }
        private void PickTarget() { targetX = area.Left + 10 + random.NextDouble() * Math.Max(1, area.Width - HeroSize.Width - 20); targetY = area.Top + 10 + random.NextDouble() * Math.Max(1, area.Height - HeroSize.Height - 20); }
        private void PickEnemyTarget(AdventureEnemy enemy)
        {
            Size size = EnemySize(enemy);
            AdventureEnemy.ChoosePatrolTarget(enemy, random, area.Left + 5, area.Top + 5, Math.Max(0, area.Width - size.Width - 10), Math.Max(0, area.Height - size.Height - 10));
        }
        private void AdvanceEnemyPatrol(AdventureEnemy enemy, bool canFight, double dt)
        {
            if (enemy.Dragging || enemy.Cleaning || enemy.VisualDefeated || enemy.Defeated || enemy.Failed || enemy.IsDiskWarning) return;
            if (DesktopMortalUnengaged(enemy)) canFight = false;
            // The original field pull has already moved this actor this tick.
            // Do not counteract it with desktop ring/patrol steering.
            if (IsolatedDesktop && clock.Elapsed.TotalSeconds < blackholeUntil && enemy.Authorized && !enemy.IsBoss) return;
            double heroDistance = EnemyHeroDistance(enemy);
            if (canFight)
            {
                Size size = EnemySize(enemy);
                double chaseX = x + HeroSize.Width / 2.0 - size.Width / 2.0;
                double chaseY = y + HeroSize.Height - size.Height;
                double reach = (!String.IsNullOrEmpty(enemy.EvilTheme) ? Math.Min(210,AdventureWeapon.Find(enemy.BossWeapon).Range*.5) : enemy.IsBoss ? 88 : 70) * displayScale;
                if (IsolatedDesktop && !enemy.IsBoss && String.IsNullOrEmpty(enemy.EvilTheme))
                {
                    PointF feet = DesktopEnemyStandPoint(enemy);
                    MoveEnemy(enemy, feet.X - size.Width / 2.0, feet.Y - size.Height,
                        AdventureMonster.Find(enemy.MonsterKind).WalkSpeed * displayScale, dt);
                    if (enemy.MoveSpeed <= .01) FaceEnemy(enemy, chaseX - enemy.X);
                    return;
                }
                if (heroDistance > reach) MoveEnemy(enemy, chaseX, chaseY, (enemy.IsBoss ? 56 : AdventureMonster.Find(enemy.MonsterKind).WalkSpeed) * displayScale, dt);
                else FaceEnemy(enemy, chaseX - enemy.X);
                return;
            }
            if (AdventureBattle.Distance(enemy.X, enemy.Y, enemy.TargetX, enemy.TargetY) < 10) PickEnemyTarget(enemy);
            double speed = enemy.IsBoss ? 55 : AdventureMonster.Find(enemy.MonsterKind).WalkSpeed;
            // During death/disconnection, idle patrol remains independent of hero roaming.
            if (enemy.Authorized) speed *= heroDistance < 180 * displayScale ? .18 : .42;
            PointF idle = IsolatedDesktop ? DesktopIdleWaypoint(enemy) : new PointF((float)enemy.TargetX, (float)enemy.TargetY);
            MoveEnemy(enemy, idle.X, idle.Y, speed * displayScale, dt);
        }
        private double EnemyHeroDistance(AdventureEnemy enemy)
        {
            Size size = EnemySize(enemy);
            return AdventureBattle.Distance(enemy.X + size.Width / 2.0, enemy.Y + size.Height, x + HeroSize.Width / 2.0, y + HeroSize.Height);
        }
        private void AttackHero(AdventureEnemy enemy, bool canFight, double dt)
        {
            if (!canFight || DesktopMortalUnengaged(enemy) || enemy.Dragging || enemy.Cleaning || enemy.VisualDefeated || enemy.Defeated || enemy.Failed || enemy.IsDiskWarning) { enemy.AttackTime = 0; return; }
            double cooldown = EnemyAttackCooldown(enemy);
            if (EnemyHeroDistance(enemy) > EnemyAttackReach(enemy) * displayScale) { enemy.AttackTime = 0; return; }
            enemy.AttackTime += dt;
            if (enemy.AttackTime < cooldown) return;
            enemy.AttackTime = 0; enemy.VisualDirty = true; enemy.AttackFlash = 1;
            int incoming = !String.IsNullOrEmpty(enemy.EvilTheme) ? 2 + enemy.SpawnLevel / 12 : enemy.IsBoss ? 12 : AdventureMonster.Find(enemy.MonsterKind).Damage;
            int healthBefore = battle.Progress.Health, unblockedIncoming = incoming;
            bool blocked = clock.Elapsed.TotalSeconds < blockUntil;
            if (blocked) { incoming = Math.Max(1, incoming / 5); if (!IsolatedDesktop) battle.LastReward = "格挡成功：来势被卸去大半（伤害 " + incoming + "）"; }
            battle.Damage(incoming);
            RecordDesktopIncomingHit(enemy, healthBefore, unblockedIncoming, blocked);
            if (battle.Progress.Dead) RecordDesktopEvent("气血耗尽 · 入定调息后自动恢复，历练保留");
            if (!String.IsNullOrEmpty(enemy.EvilTheme) && enabled && (!artDemoRuntime || !artDemoHostHidden) && (!IsolatedDesktop || (desktopActorsVisible && !desktopTestAdvancing))) effects.Fire(new PointF((float)enemy.X+EnemySize(enemy).Width/2f,(float)enemy.Y+EnemySize(enemy).Height/2f),new PointF((float)x+HeroSize.Width/2f,(float)y+HeroSize.Height/2f),enemy.BossWeapon,displayScale);
        }
        private static void FaceEnemy(AdventureEnemy enemy, double dx)
        {
            if (Math.Abs(dx) <= .01) return;
            bool right = dx > 0;
            if (enemy.FacingRight != right) { enemy.FacingRight = right; enemy.VisualDirty = true; }
        }
        private void MoveToward(double xx, double yy, double speed, double dt)
        {
            double distance = AdventureBattle.Distance(x, y, xx, yy); if (distance < .01) return; double step = Math.Min(distance, speed * dt); heroFacing = xx > x;
            x += (xx - x) / distance * step; y += (yy - y) / distance * step; ClampHero();
        }
        private void MoveEnemy(AdventureEnemy e, double xx, double yy, double speed, double dt)
        {
            double d = AdventureBattle.Distance(e.X, e.Y, xx, yy); if (d < .01 || dt <= 0 || e.Dragging) return;
            double oldX = e.X, oldY = e.Y, step = Math.Min(d, Math.Max(0, speed) * dt);
            e.X += (xx - e.X) / d * step; e.Y += (yy - e.Y) / d * step; BoundEnemy(e);
            FaceEnemy(e, e.X - oldX);
            e.MoveSpeed = AdventureBattle.Distance(oldX, oldY, e.X, e.Y) / dt;
        }
        private void ClampHero() { PointF p = KnightPetController.BoundPosition(x, y, HeroSize, area); x = p.X; y = p.Y; heroWindow.Location = new Point((int)x, (int)y); }
        private void BoundEnemy(AdventureEnemy e) { PointF p = KnightPetController.BoundPosition(e.X, e.Y, EnemySize(e), area); e.X = p.X; e.Y = p.Y; }
        private Size EnemySize(AdventureEnemy e) { return e.IsBoss ? new Size((int)(190 * displayScale), (int)(220 * displayScale)) : new Size((int)(130 * displayScale), (int)(170 * displayScale)); }
        private void PositionLandmark() { landmarkWindow.Location = artDemoRuntime ? new Point(area.Left + 16, area.Top + 70) : new Point(area.Left + 16, area.Bottom - LandmarkSize.Height - 16); }
        private void PaintWindow(KnightLayeredWindow window, AdventureRenderState state)
        {
            PresentedFrames++;
            window.Present(renderer.Render(state)); if (!window.Visible) window.Show();
        }
        // 实际栅格化计数（性能探针用）：每次 PaintWindow +1，跳绘不计。
        internal long PresentedFrames;
        private string lastHeroSignature;
        // P0 动画帧量化：战斗特效期间 24fps，跑动 24fps/12 步循环，静态 12fps；
        // 签名（帧、相位、朝向、血量档位、标签等）任一不变则跳过栅格化，仅更新窗口位置。
        private string HeroSignature(AdventureRenderState s)
        {
            double now = clock.Elapsed.TotalSeconds;
            int q = attackAnimation > 0 || now < blackholeUntil || s.MoveSpeed > 1 ? 24 : 12;
            if (IsolatedDesktop)
                return s.Phase + "|" + s.WeaponId + "|" + s.HeroStage + "|" + region.CurrentCode + "|" + s.Size.Width + "x" + s.Size.Height
                    + "|" + (long)(now * q) + "|" + (s.FacingRight ? 1 : 0) + "|" + s.Level + "|" + (int)(s.HealthRatio * 40) + "|" + s.Label + "|" + s.ActionHint;
            double cycleFrac = s.Cycle - Math.Floor(s.Cycle);
            return s.Phase + "|" + s.WeaponId + "|" + s.HeroStage + "|" + (demonstrationOnly ? demoTheme : region.CurrentCode) + "|" + s.Size.Width
                + "|" + (int)(s.Time * q) + "|" + (int)(cycleFrac * (s.MoveSpeed > 1 ? 12 : 24)) + "|" + (int)(s.MoveSpeed / 25)
                + "|" + (s.FacingRight ? 1 : 0) + "|" + s.Level + "|" + (int)(s.HealthRatio * 40)
                + "|" + (int)(s.GlowProgress * 24) + "|" + (int)(s.AttackProgress * q) + "|" + (int)(s.PhaseTime * q) + "|" + s.Label;
        }
        private string EnemySignature(AdventureEnemy enemy, string label, double deathProgress)
        {
            int q = enemy.AttackFlash > .05 || enemy.HitFlash > 0 ? 24 : 10;
            if (IsolatedDesktop)
                return (enemy.IsDiskBoss ? "D" : !String.IsNullOrEmpty(enemy.EvilTheme) ? "E" : enemy.IsBoss ? "B" : "S")
                    + "|" + enemy.MonsterKind + "|" + (enemy.BossWeapon ?? "") + "|" + (enemy.EvilTheme ?? region.CurrentCode)
                    + "|" + EnemySize(enemy).Width + "|" + (enemy.VisualDefeated ? "shatter" : enemy.AttackFlash > .05 ? "attack" : enemy.MoveSpeed > 1 ? "run" : "idle")
                    + "|" + (long)(clock.Elapsed.TotalSeconds * q) + "|" + (enemy.FacingRight ? 1 : 0)
                    + "|" + enemy.Health + "/" + enemy.MaxHealth + "|" + enemy.QueueNumber + "|" + label;
            return (enemy.IsDiskBoss ? "D" : !String.IsNullOrEmpty(enemy.EvilTheme) ? "E" : enemy.IsBoss ? "B" : "S")
                + "|" + enemy.MonsterKind + "|" + (enemy.BossWeapon ?? "") + "|" + (enemy.EvilTheme ?? (demonstrationOnly ? demoTheme : region.CurrentCode))
                + "|" + EnemySize(enemy).Width + "|" + (enemy.VisualDefeated ? "shatter" : enemy.AttackFlash > .05 ? "attack" : enemy.MoveSpeed > 1 ? "run" : "idle")
                + "|" + (int)(enemy.AnimationTime * q) + "|" + (int)(enemy.DeathElapsed * q) + "|" + (int)(deathProgress * 12)
                + "|" + (enemy.FacingRight ? 1 : 0) + "|" + (int)(enemy.MoveSpeed / 10)
                + "|" + enemy.Health + "/" + enemy.MaxHealth + "|" + enemy.QueueNumber + "|" + label;
        }
        private AdventureRenderState HeroVisual(double now)
        {
            string phase = networkState.Phase.ToString().ToLowerInvariant();
            if (battle.Progress.Dead) phase = battle.DeathTime < 3.4 ? "dying" : "grave";
            else if (IsolatedDesktop && clock.Elapsed.TotalSeconds < blockUntil) phase = "guard";
            else if (attackAnimation > 0) phase = "attack";
            else if (actualMoveSpeed > 1 && phase != "grave" && phase != "dying" && phase != "revive") phase = "run";
            else if (phase == "run") phase = "idle";
            return new AdventureRenderState { Kind = "Hero", HeroStage = ArtDemoHeroStage, Size = HeroSize, ThemeCode = demonstrationOnly ? demoTheme : region.CurrentCode, Phase = phase, WeaponId = battle.Weapon.Id, Time = now, Cycle = runCycle, MoveSpeed = actualMoveSpeed / displayScale, FacingRight = heroFacing, Level = battle.Progress.Level, HealthRatio = (double)battle.Progress.Health / battle.Progress.MaxHealth, ActionHint = IsolatedDesktop ? DesktopHudHint : null, Label = battle.Progress.Dead ? "入定调息 · 稍后自行恢复" : (artDemoRuntime || IsolatedDesktop) && cultivation.Mortal ? "凡人 · 红尘历练" : cultivation.RealmName + " · 第" + cultivation.Layer + "层", GlowProgress = battle.LevelGlow > 0 ? 1 - battle.LevelGlow / 4.5 : -1, AttackProgress = attackAnimation, Opacity = 1, PhaseTime = battle.Progress.Dead ? battle.DeathTime : networkState.PhaseTime };
        }
        private static readonly int SecRenderHero = PerfProbe.Register("render.hero");
        private static readonly int SecRenderEnemies = PerfProbe.Register("render.enemies");
        private static readonly int SecRenderWindows = PerfProbe.Register("render.windows");
        private void RenderWorld(bool enemies)
        {
            if (disposed || !enabled || (artDemoRuntime && artDemoHostHidden) || (IsolatedDesktop && (!desktopActorsVisible || desktopTestAdvancing))) return;
            bool probe = PerfProbe.Enabled;
            try
            {
                if (demoStarted >= 0) { RenderDemo(enemies || clock.Elapsed.TotalSeconds - lastEnemyPaint >= .066); return; }
                if (probe) PerfProbe.Begin();
                heroWindow.Location = new Point((int)x, (int)y);
                AdventureRenderState heroState = HeroVisual(renderTime);
                string heroSig = HeroSignature(heroState);
                if (heroSig != lastHeroSignature || !heroWindow.Visible) { lastHeroSignature = heroSig; PaintWindow(heroWindow, heroState); }
                double paintNow = clock.Elapsed.TotalSeconds;
                if (!IsolatedDesktop && (enemies || !landmarkWindow.Visible || paintNow - lastLandmarkPaint >= .25))
                {
                    lastLandmarkPaint = paintNow;
                    PaintWindow(landmarkWindow, new AdventureRenderState { Kind = "Landmark", HeroStage = ArtDemoHeroStage, Size = LandmarkSize, ThemeCode = demonstrationOnly ? demoTheme : region.CurrentCode, Time = renderTime, Label = CastleAmountText, GrowthLabel = GrowthStatusText, GrowthRatio = ResourceGrowthRatio, DiskLabel = diskSnapshot.Available ? "C盘剩余 " + AdventureBattle.FormatBytes(diskSnapshot.AvailableBytes) : "C盘剩余：等待采样", DiskFreeRatio = diskSnapshot.Available ? diskSnapshot.FreeRatio : -1, DiskCritical = diskSnapshot.Available && diskSnapshot.AvailableBytes < LowDiskBossPolicy.LowThresholdBytes, Opacity = 1 });
                }
                if (probe) { PerfProbe.End(SecRenderHero); PerfProbe.Begin(); }
                HashSet<string> visible = new HashSet<string>();
                // Dispose retired windows BEFORE showing replacements, including demo transitions.
                RemoveUnusedWindows(new HashSet<string>(battle.Enemies.Where(e => !e.IsVisuallyHidden).Take(EnemyFieldBudget.MaxEnemies).Select(e => e.Id)));
                if (probe) { PerfProbe.End(SecRenderWindows); PerfProbe.Begin(); }
                // Keep positions current on every tick, but spread pose rasterization across
                // four enemies at a time so a full field cannot stall the hero's animation.
                int enemyCount = battle.Enemies.Count, paintStart = enemyCount == 0 ? 0 : enemyPaintCursor % enemyCount;
                for (int enemyIndex = 0; enemyIndex < enemyCount; enemyIndex++)
                {
                    AdventureEnemy enemy = battle.Enemies[enemyIndex];
                    // Presentation disappears on the short combat animation clock,
                    // independently of the queued/active filesystem operation.
                    if (enemy.IsVisuallyHidden) continue;
                    if (visible.Count >= EnemyFieldBudget.MaxEnemies) break;
                    visible.Add(enemy.Id); KnightLayeredWindow window;
                    bool newWindow = !enemyWindows.TryGetValue(enemy.Id, out window);
                    if (newWindow)
                    {
                        AdventureEnemy captured = enemy; window = NewWindow(enemy.IsDiskBoss ? "C 盘空间危机 Boss" : enemy.IsBoss ? "邪恶分身 Boss" : AdventureMonster.Find(enemy.MonsterKind).Name + " · 临时文件怪物"); enemyWindows.Add(enemy.Id, window);
                        Point pressedAt = Point.Empty; bool dragged = false;
                        if (artDemoRuntime || IsolatedDesktop) window.Text = enemy.ProcessTarget != null ? DesktopEnemyName(enemy) : enemy.NoFieldClear ? enemy.IsBoss ? "蜃楼主镜" : "蜃楼镜妖" : (enemy.Elite ? "精英 · " : "") + ArtDemoMonsterLabel(enemy.MonsterKind);
                        window.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressedAt = Cursor.Position; dragged = false; } };
                        window.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left && !dragged && !IsDemo && !captured.VisualDefeated) { if (battle.Progress.Dead) OpenEquipment(); else if (IsolatedDesktop) DesktopChooseEnemy(captured.Id, true); else { battle.Authorize(captured); if (cultivation.Mortal) TryMortalStrike(captured); } RenderWorld(true); } };
                        window.ContextRequested = OpenEquipment;
                        window.DragChanged = delegate(bool active) { captured.Dragging = active; };
                        window.DragMoved = delegate(Point p) { if (AdventureBattle.Distance(pressedAt.X, pressedAt.Y, Cursor.Position.X, Cursor.Position.Y) > 6) dragged = true; captured.X = p.X; captured.Y = p.Y; BoundEnemy(captured); };
                    }
                    window.Location = new Point((int)enemy.X, (int)enemy.Y);
                    if (!enemies && !newWindow && !enemy.VisualDirty && !enemy.VisualDefeated && (enemyIndex - paintStart + enemyCount) % enemyCount >= 4) continue;
                    string label = enemy.Failed ? "暂不能清理 · 重新扫描" : enemy.IsDiskBoss ? (enemy.IsDiskWarning ? "C 盘不足 · 无可清理文件" : "磁盘 Boss · " + AdventureBattle.FormatBytes(enemy.Bytes)) : enemy.IsBoss ? "邪恶分身 · " + AdventureWeapon.Find(enemy.BossWeapon).Name : enemy.Cleaning ? (enemy == activeCleanupEnemy ? "正在清理…" : "等待清理…") : enemy.Authorized ? "#" + enemy.QueueNumber + " · " + AdventureBattle.FormatBytes(enemy.Bytes) : AdventureMonster.Find(enemy.MonsterKind).Name + (enemy.Batch != null && enemy.Batch.Candidates.Any(f=>!f.EligibleForCleanup) ? " · 近期文件" : "");
                    if (artDemoRuntime || IsolatedDesktop) label = enemy.VisualDefeated ? "" : enemy.ProcessTarget != null ? DesktopEnemyName(enemy) : enemy.NoFieldClear ? enemy.IsBoss ? "蜃楼主镜" : "蜃楼镜妖" : enemy.Elite ? "精英 · " + ArtDemoMonsterLabel(enemy.MonsterKind) : ArtDemoMonsterLabel(enemy.MonsterKind);
                    double deathProgress = enemy.VisualDefeated ? Math.Min(1, enemy.DeathElapsed / AdventureEnemy.ShatterSeconds) : 0;
                    // P0：完全静止且无特效的妖物整体跳绘（姿态冻结）；活动妖物按帧量化签名跳绘。
                    bool idleStatic = !enemy.VisualDefeated && enemy.AttackFlash <= .05 && enemy.HitFlash <= 0 && enemy.MoveSpeed <= 1;
                    string enemySig = null;
                    if (!newWindow && window.Visible && !enemy.VisualDirty)
                    {
                        if (idleStatic) continue;
                        enemySig = EnemySignature(enemy, label, deathProgress);
                        if (enemySig == enemy.PaintedSignature) continue;
                    }
                    if (!idleStatic && enemySig == null) enemySig = EnemySignature(enemy, label, deathProgress);
                    enemy.PaintedSignature = enemySig;
                    PaintWindow(window, new AdventureRenderState { Kind = !String.IsNullOrEmpty(enemy.EvilTheme) ? "EvilBoss" : enemy.IsBoss ? "Boss" : "Skeleton", HeroStage = ArtDemoHeroStage, WeaponId = enemy.BossWeapon ?? "spear", MonsterKind = (artDemoRuntime || IsolatedDesktop) && enemy.NoFieldClear && enemy.ProcessTarget == null ? enemy.IsBoss ? "mirror-boss" : ArtDemoHeroStage == "mortal" ? "mirror-shard" : "mirror-wraith" : (artDemoRuntime || IsolatedDesktop) && enemy.Elite ? enemy.MonsterKind == "mushroom" ? "elite-mushroom" : "elite" : enemy.MonsterKind, Size = EnemySize(enemy), ThemeCode = enemy.EvilTheme ?? (demonstrationOnly ? demoTheme : region.CurrentCode), Time = enemy.AnimationTime, PhaseTime = enemy.DeathElapsed, DeathProgress = deathProgress, Phase = enemy.VisualDefeated ? "shatter" : enemy.AttackFlash > .05 ? "attack" : enemy.MoveSpeed > 1 ? "run" : "idle", Label = enemy.VisualDefeated ? "" : label, QueueNumber = enemy.QueueNumber, FacingRight = enemy.FacingRight, MoveSpeed = enemy.MoveSpeed / displayScale, HealthRatio = (double)enemy.Health / Math.Max(1, enemy.MaxHealth), HealthCurrent = enemy.Health, HealthMaximum = enemy.MaxHealth, AttackProgress = !String.IsNullOrEmpty(enemy.EvilTheme) ? enemy.AttackFlash : enemy.HitFlash * 4, HitFlash = enemy.HitFlash * 4, Opacity = 1 });
                    enemy.VisualDirty = false;
                }
                enemyPaintCursor = enemyCount == 0 ? 0 : (paintStart + 4) % enemyCount;
                if (probe) { PerfProbe.End(SecRenderEnemies); PerfProbe.Begin(); }
                RemoveUnusedWindows(visible);
                foreach (LootView loot in lootViews) { loot.Window.Location = new Point((int)loot.X, (int)(loot.Y - (3.2 - loot.Life) * 18)); PaintWindow(loot.Window, new AdventureRenderState { Kind = "Loot", HeroStage = ArtDemoHeroStage, Size = new Size(160, 155), ThemeCode = region.CurrentCode, WeaponId = loot.WeaponId, Time = 3.2 - loot.Life, Label = loot.Label, Opacity = Math.Min(1, loot.Life) }); }
                if (probe) PerfProbe.End(SecRenderWindows);
            }
            catch (Exception e) { failure = e.Message; Enabled = false; }
        }
        private void ShowLoot(string weaponId, double px, double py)
        {
            if (IsolatedDesktop) RecordDesktopEvent("获得「" + DesktopWeaponCatalog.Find(weaponId).Name + "」 · 已入行囊，可在行囊与设置中装备");
            if (IsolatedDesktop && (!desktopActorsVisible || desktopTestAdvancing)) return;
            while (lootViews.Count >= 2) { lootViews[0].Window.Dispose(); lootViews.RemoveAt(0); }
            KnightLayeredWindow window = NewWindow("装备掉落 · 已放入背包"); window.PassThrough = true;
            lootViews.Add(new LootView { Window = window, WeaponId = weaponId, Label = "获得 " + (IsolatedDesktop ? DesktopWeaponCatalog.Find(weaponId).Name : AdventureWeapon.Find(weaponId).Name), X = Math.Min(area.Right - 165, Math.Max(area.Left + 5, px)), Y = Math.Min(area.Bottom - 160, Math.Max(area.Top + 70, py)) });
        }
        private void ClearLoot() { foreach (LootView loot in lootViews) loot.Window.Dispose(); lootViews.Clear(); }
        private void RemoveUnusedWindows(HashSet<string> visible)
        {
            foreach (string key in enemyWindows.Keys.ToArray()) if (!visible.Contains(key)) { enemyWindows[key].Dispose(); enemyWindows.Remove(key); }
        }
        private void RenderDemo(bool enemies)
        {
            double t = (clock.Elapsed.TotalSeconds - demoStarted) % 24;
            string phase = t < 5 ? "run" : t < 10 ? "attack" : t < 14 ? "run" : t < 17.5 ? "dying" : t < 19 ? "grave" : t < 21.5 ? "revive" : "run";
            double phaseTime = t < 14 ? t : t < 17.5 ? t - 14 : t < 19 ? t - 17.5 : t - 19;
            int px = area.Left + Math.Min(400, area.Width / 3) + (int)(Math.Sin(t * .6) * 110);
            heroWindow.Location = new Point(px, area.Bottom - HeroSize.Height - 45);
            PaintWindow(heroWindow, new AdventureRenderState { Kind = "Hero", HeroStage = ArtDemoHeroStage, Size = HeroSize, ThemeCode = demonstrationOnly ? demoTheme : region.CurrentCode, Phase = phase, WeaponId = t < 5 ? "spear" : t < 10 ? "laser" : "blackhole", Time = t, PhaseTime = phaseTime, MoveSpeed = 400, FacingRight = true, Level = 7, HealthRatio = phase == "grave" ? 0 : 1, Label = "纯演示 · 不清理文件", GlowProgress = t >= 10 && t < 14 ? (t - 10) / 4 : -1, AttackProgress = phase == "attack" ? (t * 2) % 1 : 0, Opacity = 1 });
            if (!enemies) return; lastEnemyPaint = clock.Elapsed.TotalSeconds;
            PaintWindow(landmarkWindow, new AdventureRenderState { Kind = "Landmark", HeroStage = ArtDemoHeroStage, Size = LandmarkSize, ThemeCode = demonstrationOnly ? demoTheme : region.CurrentCode, Time = t, Label = "演示城堡 · 虚拟敌人", Opacity = 1 });
            HashSet<string> keys = new HashSet<string>();
            RemoveUnusedWindows(new HashSet<string>(Enumerable.Range(0, AdventureMonster.All.Length).Select(i => "demo-enemy-" + i)));
            for (int i = 0; i < AdventureMonster.All.Length; i++)
            {
                string key = "demo-enemy-" + i; keys.Add(key); KnightLayeredWindow w;
                if (!enemyWindows.TryGetValue(key, out w)) { w = NewWindow("演示怪物 · 不含真实文件"); enemyWindows[key] = w; }
                w.Location = new Point(area.Right - 145 - (i % 3) * 145, area.Bottom - 190 - (i / 3) * 185);
                PaintWindow(w, new AdventureRenderState { Kind = "Skeleton", MonsterKind = AdventureMonster.All[i].Id, Size = new Size(130, 170), ThemeCode = demonstrationOnly ? demoTheme : region.CurrentCode, Time = t + i, Phase = t >= 7 && t < 10 ? "defeated" : "run", DeathProgress = t >= 7 && t < 10 ? Math.Min(1, (t - 7) / AdventureEnemy.ShatterSeconds) : 0, FacingRight = false, HealthRatio = t >= 7 && t < 10 ? 0 : 1, Label = "演示 · " + AdventureMonster.All[i].Name, QueueNumber = i + 1, Opacity = 1 });
            }
            RemoveUnusedWindows(keys);
        }
        private void Save()
        {
            if (disposed || demonstrationOnly || demoStarted >= 0) return;
            desktopLastSaveSucceeded = true;
            lastSave = clock.Elapsed.TotalSeconds;
            try { battle.Progress.Save(progressPath); } catch (Exception e) { desktopLastSaveSucceeded = false; scanStatus = "进度暂未保存：" + e.Message; }
            // adventure-v1.txt stays the live runtime combat archive in the
            // unchanged v1 format (v5.4 can still read it); the frozen
            // *.v54-backup copy made at first launch preserves history.
            // Cultivation data lives in its own separate versioned archive.
            try { cultivation.Save(CultivationWritePath); } catch (Exception e) { desktopLastSaveSucceeded = false; scanStatus = "修炼档案暂未保存：" + e.Message; }
            // 百业道韵第三个独立存档；保存失败只记状态，不影响其他存档。
            if (baiye != null) { try { baiye.Save(BaiyeWritePath); } catch (Exception e) { desktopLastSaveSucceeded = false; scanStatus = "百业档案暂未保存：" + e.Message; } }
            // 灵域第四个独立存档；保存失败只记状态，不影响其他存档。
            if (lingyu != null) { try { lingyu.Save(LingyuWritePath); } catch (Exception e) { desktopLastSaveSucceeded = false; scanStatus = "灵域档案暂未保存：" + e.Message; } }
            // 洞天第五个独立存档（P4）；保存失败只记状态。
            if (dongtian != null) { try { dongtian.Save(DongtianWritePath); } catch (Exception e) { desktopLastSaveSucceeded = false; scanStatus = "洞天档案暂未保存：" + e.Message; } }
        }
        public void Dispose()
        {
            if (disposed) return;
            timer.Stop(); CancelPending(); cancellation.Cancel();
            if (desktopProcessTask != null && desktopProcessTask.IsCompleted) DrainDesktopProcessAction();
            DisposeDesktopProcessAction();
            // Final best-effort flush of any consent revocation whose file update
            // failed during the session, so a restart cannot resurrect it.
            if (consentRetryCleanup || consentRetryMemory || consentRetryAutoMaint) TryFlushConsentRevocation();
            // 洞天待确认操作同样在退出前做最后一次落盘尝试。
            if (dongtianPersistPending) TryFlushDongtianPersistence();
            if (tempWatcher != null) { tempWatcher.Dispose(); tempWatcher = null; }
            if (memoryReclaimTask != null) { try { memoryReclaimTask.Wait(6000); DrainMemoryReclaim(); } catch { } }
            // Clean only works under the local temporary directory and checks the
            // token between files. Finish the active result before saving progress.
            // Bounded wait: a worker that never answers cancellation must not wedge
            // the GUI shutdown. If it overruns, its result stays unclaimed — freed
            // bytes are never invented for an unfinished task.
            if (cleanupTask != null)
            {
                try
                {
                    if (cleanupTask.Wait(8000)) DrainCleanup();
                    else scanStatus = "退出时仍有清理任务未响应取消：已保留其未完成状态，未计入任何清理成果";
                }
                catch { }
            }
            demoStarted = -1; demoPending = false;
            // 退出前封口未落记录的活动片段，保证重启后道韵都有对应获取记录。
            if (baiyeTracker != null) baiyeTracker.Seal();
            Save(); disposed = true;
            timer.Dispose(); if (connectivity != null) connectivity.Dispose(); region.Changed -= RegionChanged; region.Dispose();
            if (dongtianForm != null) dongtianForm.Dispose();
            if (equipment != null) equipment.Dispose(); if (stele != null) stele.Dispose(); if (lingyuForm != null) lingyuForm.Dispose(); if (breakthroughForm != null) breakthroughForm.Dispose(); heroWindow.Dispose(); landmarkWindow.Dispose(); foreach (KnightLayeredWindow w in enemyWindows.Values) w.Dispose(); enemyWindows.Clear(); ClearLoot(); effects.Dispose(); renderer.Dispose(); cancellation.Dispose();
        }        internal AdventureBattle Battle { get { return battle; } }
        internal int CurrentTickInterval { get { return timer.Interval; } }
        internal string DetailText { get { return cleanupNotice + "\r\n" + scanStatus + "\r\n" + (diskSnapshot.Available ? "C 盘剩余 " + AdventureBattle.FormatBytes(diskSnapshot.AvailableBytes) + " / " + AdventureBattle.FormatBytes(diskSnapshot.TotalBytes) : diskSnapshot.Error) + "\r\n" + connectivityDetail + "\r\n" + region.Status; } }
        internal string ThemeMode { get { return battle.Progress.ThemeMode; } }
        internal bool IsDemo { get { return demoStarted >= 0 || demoPending; } }
        internal Rectangle WorkingArea { get { return area; } }
        internal string MemoryText { get { return memoryAvailable ? memoryPercent.ToString("0.0") + "%" : "等待采样"; } }
        internal bool IsScanning { get { return scanBusy; } }
        internal void ChooseTheme(string code) { if (demonstrationOnly || IsolatedDesktop) { demoTheme = code; RenderWorld(true); return; } if (IsDemo) return; battle.Progress.ThemeMode = code; region.SelectMode(code); battle.Progress.LastThemeCode = region.CurrentCode; region.RefreshAsync(); Save(); RenderWorld(true); }
        internal void RefreshRegion() { if (!demonstrationOnly && !IsolatedDesktop && !IsDemo) region.RequestRefresh(); }
        internal void ChooseWeapon(string id)
        {
            if (IsDemo || !battle.Progress.Inventory.Contains(id) || battle.Progress.WeaponId == id) return;
            battle.Progress.WeaponId = id; Save();
            if (IsolatedDesktop) { RecordDesktopEvent("装备「" + DesktopWeaponCatalog.Find(id).Name + "」 · " + DesktopWeaponCatalog.Find(id).Role); RenderWorld(true); }
        }
        internal void SetAutomatic(bool value) { SetAutoPlan(value); }
        public void SetAutoPlan(bool value)
        {
            if (disposed || IsDemo || (demonstrationOnly && !artDemoRuntime)) return;
            // 凡人期禁止自动作战：十次主动历练、灵根觉醒后方可解锁。
            if (value && !cultivation.AutoBattleUnlocked)
            {
                battle.LastReward = "凡人需亲历红尘战斗：完成十次主动历练、灵根觉醒后方解锁自动作战";
                return;
            }
            if (value && !enabled) Enabled = true;
            battle.SetAutomatic(value); RemoveCancelledAutomaticJobs();
            // The unattended-battle preference persists independently from both
            // real maintenance consents; it is honored again after every rest.
            cultivation.AutoBattle = value; Save();
            battle.LastReward = battle.Automatic
                ? (consent.CleanupEnabled || consent.MemoryEnabled)
                    ? (consent.AutoMaintenanceEnabled ? "自动作战已开启：击败授权范围内的怪物将执行对应真实维护；手动点击始终优先" : "自动作战已开启：自动索敌战斗并获得修为经验；真实维护授权已开启，但无人值守自动维护未开——真实清理/回收只在你手动击杀时执行")
                    : "自动作战已开启：自动索敌战斗并获得修为经验；未授权真实维护，不会删除文件或回收内存"
                : "自动作战已关闭；未开始的自动目标已撤销，手动队列保留；力竭调息后将保持手动待机";
            if (enabled) RenderWorld(true);
        }
        private void RemoveCancelledAutomaticJobs()
        {
            foreach (AdventureEnemy enemy in pendingCleanup.Where(e => !e.Cleaning || !e.Authorized)) if (enemy.VisualDefeated && enemy.Batch != null) RecordRecheck(enemy.Batch.Candidates);
            AdventureEnemy[] keep = pendingCleanup.Where(e => e.Cleaning && e.Authorized).ToArray();
            pendingCleanup.Clear(); foreach (AdventureEnemy enemy in keep) pendingCleanup.Enqueue(enemy);
        }
        internal void CancelPending()
        {
            if (memoryReclaimCancellation != null) memoryReclaimCancellation.Cancel();
            demoPending = false; demoStarted = -1; resumeAutomaticAfterDemo = false;
            blackholeUntil = 0; effects.Clear();
            if (workCancellation != null) workCancellation.Cancel();
            while (pendingCleanup.Count > 0) { AdventureEnemy enemy = pendingCleanup.Dequeue(); if (enemy.VisualDefeated && enemy.Batch != null) RecordRecheck(enemy.Batch.Candidates); enemy.CancelBeforeCleanup("清理已取消 · 对应文件待重扫"); }
            battle.CancelQueue();
            RefillEnemies();
        }
        internal void Resurrect() { if (IsDemo) return; battle.Revive(); Save(); }
        internal void AbsorbAll() { ClearField(); }
        public void ClearField()
        {
            if (!CanClearField) return;
            int inventoryBefore = battle.Progress.Inventory.Count;
            List<AdventureEnemy> selected = battle.ClearFieldSnapshot();
            if (battle.Progress.Inventory.Count > inventoryBefore) ShowLoot(battle.Progress.Inventory.Last(), x, y);
            if (selected.Count == 0) { battle.LastReward = "当前没有可清场的临时文件怪；邪恶分身 Boss 需要正常战斗"; return; }
            blackholeUntil = clock.Elapsed.TotalSeconds + 1.7; blackholeX = x + HeroSize.Width / 2; blackholeY = y + HeroSize.Height / 2; attackAnimation = 1;
            PointF origin = new PointF((float)blackholeX, (float)blackholeY);
            effects.FireField(origin, selected.Select(enemy => new PointF((float)enemy.X + 65 * displayScale, (float)enemy.Y + 80 * displayScale)), displayScale);
            foreach (AdventureEnemy enemy in selected) BeginCleanup(enemy);
            battle.LastReward = "一键清场：击败 " + selected.Count + " 个目标 · 经验 +" + selected.Sum(enemy => enemy.KillExperience) + (consent.CleanupEnabled ? "；对应临时文件逐批安全清理，可随时取消待办清理" : "；未授权文件清理，只结算虚拟奖励，不删除文件");
            PumpCleanup(); Save(); RenderWorld(true);
        }
        internal void ChangeScale(float value) { displayScale = value; heroWindow.Size = HeroSize; landmarkWindow.Size = LandmarkSize; ClampHero(); PositionLandmark(); foreach (AdventureEnemy e in battle.Enemies) BoundEnemy(e); RenderWorld(true); }
        internal void ChangeScreen(Screen screen)
        {
            if (IsolatedDesktop) { ChangeDesktopScreen(screen); return; }
            area = screen.WorkingArea; ClampHero(); PositionLandmark(); foreach (AdventureEnemy e in battle.Enemies) { BoundEnemy(e); PickEnemyTarget(e); } RenderWorld(true);
        }
        public static string SelfTest() { return AdventureBattle.SelfTest(); }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int flags);
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLong")] private static extern int GetWindowLong(IntPtr handle, int index);
        public static string NativeSmoke(string assetDirectory)
        {
            int count = 0; Action<bool, string> check = delegate(bool ok, string label) { if (!ok) throw new InvalidOperationException("Adventure native smoke: " + label); count++; };
            using (Form owner = new Form())
            {
                IntPtr ownerHandle = owner.Handle;
                using (AdventureController c = new AdventureController(owner, assetDirectory, true))
                {
                    c.timer.Stop();
                    c.area = new Rectangle(-24000, -24000, 1600, 1000); c.x = -23600; c.y = -23400; c.PositionLandmark(); c.ClampHero();
                    c.PlayDemo(); Application.DoEvents();
                    check(c.heroWindow.Visible && c.landmarkWindow.Visible && c.enemyWindows.Count == 5, "hero, landmark and five independent monster overlays");
                    check(c.heroWindow.Owner == null && !c.heroWindow.ShowInTaskbar && c.heroWindow.DesktopTopMost, "desktop overlay independent of main window");
                    check((GetWindowLong(c.heroWindow.Handle, -20) & 0x80000) != 0, "per-pixel layered window");
                    check(c.connectivity == null && c.region.Mode == "OFF" && !c.scanBusy && c.lastScan < 0, "demo starts without network probe or temp scan");
                    TaskCompletionSource<CleanupResult> completion = new TaskCompletionSource<CleanupResult>();
                    AdventureEnemy active = new AdventureEnemy { Id = "smoke-active", Cleaning = true, Authorized = true }, waiting = new AdventureEnemy { Id = "smoke-waiting", Cleaning = true, Authorized = true };
                    int started = 0; c.simulatedCleaner = delegate(CleanupBatch ignored, CancellationToken token) { started++; return completion.Task; };
                    c.demoStarted = -1; c.pendingCleanup.Enqueue(active); c.pendingCleanup.Enqueue(waiting);
                    c.battle.Progress.Dead = true; c.PumpCleanup(); bool startsDuringDeath = started == 1; c.battle.Progress.Dead = false;
                    c.PumpCleanup(); c.PumpCleanup(); check(started == 1 && c.activeCleanupEnemy == active && c.pendingCleanup.Count == 1, "FIFO cleanup starts only one worker at a time");
                    int scanEpochBefore = c.cleanupEpoch;
                    c.PlayDemo(); check(c.demoPending && c.demoStarted < 0 && c.workCancellation.IsCancellationRequested, "demo waits for active cleanup to cancel before presentation");
                    c.CancelPending(); check(c.pendingCleanup.Count == 0 && !waiting.Cleaning && !waiting.Authorized, "cancel removes all not-started cleanup jobs");
                    completion.SetResult(new CleanupResult { Cancelled = true, Message = "synthetic cancellation" }); c.DrainCleanup();
                    check(startsDuringDeath, "combat death does not suspend already defeated authorized cleanup jobs");
                    check(c.cleanupTask == null && c.battle.Progress.FreedBytes == 0 && active.Failed, "cancelled result grants no fake freed space");
                    check(c.cleanupEpoch != scanEpochBefore, "completed cleanup invalidates in-flight scan snapshots"); c.simulatedCleaner = null; c.StartDemo();
                    int xp = c.battle.Progress.Experience, hp = c.battle.Progress.Health; long freed = c.battle.Progress.FreedBytes;
                    c.RefreshWorld(); c.SetAutomatic(true); c.ClearField(); c.AbsorbAll();
                    check(!c.scanBusy && !c.battle.Automatic && c.battle.QueueCount == 0, "demo cannot authorize deletion");
                    check(!c.CanClearField && !c.AutoPlanning, "demo disables clear-field and automatic action entrypoints");
                    foreach (string theme in new[] { "OTHER", "CN", "JP", "US", "TW", "HK" }) { c.ChooseTheme(theme); c.RenderWorld(true); check(c.demoTheme == theme && c.region.Mode == "OFF", "manual preview theme " + theme); }
                    using (AdventureEquipmentForm panel = new AdventureEquipmentForm(c))
                    using (Bitmap capture = new Bitmap(panel.Width, panel.Height))
                    {
                        panel.StartPosition = FormStartPosition.Manual; panel.Location = new Point(-24000, -24000); panel.Show(); Application.DoEvents(); panel.RefreshData();
                        panel.DrawToBitmap(capture, new Rectangle(Point.Empty, panel.Size));
                        check(panel.Controls.Count > 0 && panel.Controls[0].Visible && panel.Controls[0].Controls.Count > 12, "equipment view materializes controls in offscreen preview");
                    }
                    c.RenderWorld(true); GC.Collect(); GC.WaitForPendingFinalizers(); int before = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                    for (int i = 0; i < 140; i++) { c.demoStarted = c.clock.Elapsed.TotalSeconds - i / 6.0; c.RenderWorld(true); }
                    GC.Collect(); GC.WaitForPendingFinalizers(); int after = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                    check(after <= before + 8, "GDI resources remain bounded (" + before + " -> " + after + ")");
                    check(c.battle.Progress.Experience == xp && c.battle.Progress.Health == hp && c.battle.Progress.FreedBytes == freed, "demo preserves progress");
                    c.demoStarted = -1;
                    for (int i = 0; i < 8; i++) c.battle.Enemies.Add(new AdventureEnemy { Id = "paint-" + i, MonsterKind = i >= 6 ? "boss" : AdventureMonster.All[i % 5].Id, IsBoss = i >= 6, IsDiskBoss = i == 7, X = -23900 + i * 35, Y = -23700, TargetX = -23000, Health = 20, MaxHealth = 20 });
                    c.RenderWorld(true); check(c.enemyWindows.Count == 8 && EnemyFieldBudget.IsWithinBudget(c.battle.Enemies), "full field contains six ordinary monsters and the two distinct bosses");
                    System.Reflection.FieldInfo currentFrame = typeof(KnightLayeredWindow).GetField("current", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    HashSet<string> repainted = new HashSet<string>(); bool boundedPaint = true, positionsCurrent = true;
                    // 移动中的妖物（MoveSpeed>1）：帧量化签名每拍变化，轮换预算内每只到点必重绘。
                    foreach (AdventureEnemy enemy in c.battle.Enemies) enemy.MoveSpeed = 5;
                    for (int tick = 0; tick < 4; tick++)
                    {
                        Dictionary<string, object> frames = c.enemyWindows.ToDictionary(pair => pair.Key, pair => currentFrame.GetValue(pair.Value));
                        foreach (AdventureEnemy enemy in c.battle.Enemies) { enemy.X += 2; enemy.AnimationTime += .09; }
                        c.renderTime += .016; c.RenderWorld(false); int changed = 0;
                        foreach (AdventureEnemy enemy in c.battle.Enemies)
                        {
                            KnightLayeredWindow window = c.enemyWindows[enemy.Id];
                            if (!Object.ReferenceEquals(frames[enemy.Id], currentFrame.GetValue(window))) { changed++; repainted.Add(enemy.Id); }
                            positionsCurrent &= window.Left == (int)enemy.X && window.Top == (int)enemy.Y;
                        }
                        boundedPaint &= changed == 4;
                    }
                    check(boundedPaint, "normal rendering spreads eight moving monsters across four rasterizations per tick");
                    check(positionsCurrent, "all monster positions follow movement even when their pose is cached");
                    check(repainted.Count == 8, "round-robin rendering never starves a monster pose");
                    // P0 静止跳绘：完全静止、无特效、非脏的妖物一拍零栅格化，窗口位置仍然跟随。
                    foreach (AdventureEnemy enemy in c.battle.Enemies) { enemy.MoveSpeed = 0; enemy.AttackFlash = 0; enemy.HitFlash = 0; }
                    Dictionary<string, object> idleFrames = c.enemyWindows.ToDictionary(pair => pair.Key, pair => currentFrame.GetValue(pair.Value));
                    foreach (AdventureEnemy enemy in c.battle.Enemies) enemy.X += 3;
                    c.RenderWorld(false);
                    check(c.battle.Enemies.All(enemy => Object.ReferenceEquals(idleFrames[enemy.Id], currentFrame.GetValue(c.enemyWindows[enemy.Id])))
                        && c.battle.Enemies.All(enemy => c.enemyWindows[enemy.Id].Left == (int)enemy.X),
                        "idle monsters skip rasterization entirely while their windows still track position");
                    AdventureEnemy reaction = c.battle.Enemies.First(e => e.Id == "paint-5"); reaction.Health = reaction.MaxHealth = 100;
                    reaction.Batch = new CleanupBatch(new List<CleanupCandidate> { new CleanupCandidate { Path = @"C:\KnightAssistantSynthetic\reaction.tmp", Length = 4096 } }, 1);
                    c.battle.Authorize(reaction); c.battle.Cooldown = 0; c.enemyPaintCursor = 0;
                    object oldReactionFrame = currentFrame.GetValue(c.enemyWindows[reaction.Id]); c.battle.Strike(reaction); c.RenderWorld(false);
                    check(reaction.Health == 76 && !reaction.Cleaning && !Object.ReferenceEquals(oldReactionFrame, currentFrame.GetValue(c.enemyWindows[reaction.Id])), "nonlethal hit repaints immediately even outside the normal four-monster render budget");
                    int xpBeforeKill = c.battle.Progress.Experience;
                    reaction.Health = 1; c.battle.Cooldown = 0; c.battle.Strike(reaction);
                    TaskCompletionSource<CleanupResult> delayedCleanup = new TaskCompletionSource<CleanupResult>();
                    c.simulatedCleaner = delegate(CleanupBatch ignored, CancellationToken token) { return delayedCleanup.Task; };
                    c.pendingCleanup.Enqueue(reaction); c.PumpCleanup(); c.battle.Tick(.31); c.RenderWorld(false);
                    bool pendingHidden = !c.enemyWindows.ContainsKey(reaction.Id) && reaction.Cleaning && reaction.Authorized && !reaction.Defeated && c.cleanupTask != null && !c.cleanupTask.IsCompleted;
                    delayedCleanup.SetResult(new CleanupResult { Message = "synthetic file busy" }); c.DrainCleanup(); c.RenderWorld(false);
                    check(pendingHidden, "lethal sprite window disappears within 0.31 seconds while a delayed single worker remains authorized");
                    check(reaction.KillExperience >= 2 && reaction.KillExperience <= 3 && c.battle.Progress.Experience == xpBeforeKill + reaction.KillExperience, "kill grants exactly two or three XP independently of deletion success");
                    check(!c.enemyWindows.ContainsKey(reaction.Id) && reaction.Failed && reaction.Defeated && reaction.Health == 0 && reaction.IsVisuallyHidden && c.battle.Progress.FreedBytes == freed, "failed delayed cleanup never resurrects the monster or invents freed bytes");
                    check(c.recheckFiles == 1 && c.recheckBytes == 4096 && c.DetailText.Contains("待重扫"), "failed cleanup has explicit independent pending-rescan accounting");
                    c.battle.Enemies.Remove(reaction);
                    AdventureEnemy cancelledEnemy = new AdventureEnemy { Id = "cancelled-queued", Batch = new CleanupBatch(new List<CleanupCandidate> { new CleanupCandidate { Path = @"C:\KnightAssistantSynthetic\cancelled.tmp", Length = 8192 } }, 1), Health = 1, MaxHealth = 20 };
                    c.battle.Enemies.Add(cancelledEnemy); c.Spawn(cancelledEnemy); c.battle.Authorize(cancelledEnemy); c.battle.Cooldown = 0; c.battle.Strike(cancelledEnemy); c.pendingCleanup.Enqueue(cancelledEnemy); c.battle.Tick(.31); c.RenderWorld(false);
                    int xpAfterCancelKill = c.battle.Progress.Experience; c.CancelPending(); c.RenderWorld(false);
                    check(!c.enemyWindows.ContainsKey(cancelledEnemy.Id) && !cancelledEnemy.Cleaning && cancelledEnemy.IsVisuallyHidden && cancelledEnemy.Defeated && !cancelledEnemy.Authorized, "cancelled queued kill remains hidden and never starts file cleanup");
                    check(c.recheckFiles == 2 && c.recheckBytes == 12288 && c.battle.Progress.Experience == xpAfterCancelKill, "cancelled files are reported for rescan and kill XP is not paid twice");
                    // V6 consent gate: kills never reach the real cleanup worker or the
                    // memory reclaimer unless the matching scope consent is on.
                    c.consent.CleanupEnabled = false; c.consent.MemoryEnabled = false;
                    AdventureEnemy heldEnemy = new AdventureEnemy { Id = "held-consent", Batch = new CleanupBatch(new List<CleanupCandidate> { new CleanupCandidate { Path = @"C:\KnightAssistantSynthetic\held.tmp", Length = 2048 } }, 1), Health = 1, MaxHealth = 1 };
                    c.battle.Enemies.Add(heldEnemy); c.Spawn(heldEnemy); c.battle.Authorize(heldEnemy); c.battle.Cooldown = 0; c.battle.Strike(heldEnemy);
                    long heldQi = c.cultivation.LifetimeQi; c.BeginCleanup(heldEnemy); c.battle.Tick(.31);
                    check(heldEnemy.Defeated && !heldEnemy.Failed && c.cultivation.LifetimeQi > heldQi && c.cleanupTask == null && c.pendingCleanup.Count == 0, "no consent: a file monster settles virtually without starting the cleanup worker");
                    check(c.battle.LastReward.Contains("未授权"), "no-consent settlement explains itself");
                    AdventureEnemy virtualFiend = new AdventureEnemy { Id = "virtual-fiend", IsVirtual = true, MonsterKind = "imp", Health = 1, MaxHealth = 1 };
                    c.battle.Enemies.Add(virtualFiend); c.Spawn(virtualFiend); c.battle.Authorize(virtualFiend); c.battle.Cooldown = 0; c.battle.Strike(virtualFiend);
                    long fiendQi = c.cultivation.LifetimeQi; c.BeginCleanup(virtualFiend); c.battle.Tick(.31);
                    check(virtualFiend.Defeated && virtualFiend.IsVisuallyHidden && c.cultivation.LifetimeQi > fiendQi && c.cleanupTask == null && c.battle.Progress.FreedBytes == freed, "virtual fiend grants qi and never touches files");
                    AdventureEnemy heldBoss = new AdventureEnemy { Id = "held-boss", IsBoss = true, MonsterKind = "boss", EvilTheme = "CN", BossWeapon = "spear", Health = 1, MaxHealth = 1 };
                    c.battle.Enemies.Add(heldBoss); c.Spawn(heldBoss); c.battle.Authorize(heldBoss); c.battle.Cooldown = 0; c.battle.Strike(heldBoss);
                    long bossQi = c.cultivation.LifetimeQi; c.BeginCleanup(heldBoss); c.battle.Tick(.31);
                    check(heldBoss.Defeated && c.cultivation.LifetimeQi > bossQi && c.memoryReclaimTask == null, "no consent: a boss kill never triggers memory reclaim");
                    // The two scopes are independent permissions, each gated alone.
                    c.consent.CleanupEnabled = true; c.consent.MemoryEnabled = false;
                    AdventureEnemy cleanupOnlyBoss = new AdventureEnemy { Id = "cleanup-only-boss", IsBoss = true, MonsterKind = "boss", EvilTheme = "CN", BossWeapon = "spear", Health = 1, MaxHealth = 1 };
                    c.battle.Enemies.Add(cleanupOnlyBoss); c.Spawn(cleanupOnlyBoss); c.battle.Authorize(cleanupOnlyBoss); c.battle.Cooldown = 0; c.battle.Strike(cleanupOnlyBoss);
                    c.BeginCleanup(cleanupOnlyBoss); c.battle.Tick(.31);
                    check(cleanupOnlyBoss.Defeated && c.memoryReclaimTask == null && c.battle.LastReward.Contains("未授权"), "cleanup consent alone never unlocks memory reclaim");
                    c.consent.CleanupEnabled = false; c.consent.MemoryEnabled = true;
                    AdventureEnemy memoryOnlyFile = new AdventureEnemy { Id = "memory-only-file", Batch = new CleanupBatch(new List<CleanupCandidate> { new CleanupCandidate { Path = @"C:\KnightAssistantSynthetic\memory-only.tmp", Length = 1024 } }, 1), Health = 1, MaxHealth = 1 };
                    c.battle.Enemies.Add(memoryOnlyFile); c.Spawn(memoryOnlyFile); c.battle.Authorize(memoryOnlyFile); c.battle.Cooldown = 0; c.battle.Strike(memoryOnlyFile);
                    c.BeginCleanup(memoryOnlyFile); c.battle.Tick(.31);
                    check(memoryOnlyFile.Defeated && c.cleanupTask == null && c.pendingCleanup.Count == 0 && c.battle.LastReward.Contains("未授权"), "memory consent alone never unlocks file deletion");
                    int reclaimCalls = 0; c.simulatedMemoryReclaimer = delegate(CancellationToken token) { reclaimCalls++; return Task.FromResult(new MemoryReclaimResult { Before = new MemorySnapshot { Available = true, UsedPercent = 80 }, After = new MemorySnapshot { Available = true, UsedPercent = 79 }, Trimmed = 1, WorkingSetReduction = 4096 }); };
                    AdventureEnemy memoryBoss = new AdventureEnemy { Id = "memory-ok-boss", IsBoss = true, MonsterKind = "boss", EvilTheme = "CN", BossWeapon = "spear", Health = 1, MaxHealth = 1 };
                    c.battle.Enemies.Add(memoryBoss); c.Spawn(memoryBoss); c.battle.Authorize(memoryBoss); c.battle.Cooldown = 0; c.battle.Strike(memoryBoss);
                    c.BeginCleanup(memoryBoss); c.DrainMemoryReclaim();
                    check(reclaimCalls == 1 && memoryBoss.Defeated && c.battle.Progress.FreedBytes == freed, "memory consent alone runs the reclaim path without touching files");
                    // Revoking consent mid-flight cancels the active task promptly.
                    TaskCompletionSource<MemoryReclaimResult> hangingReclaim = new TaskCompletionSource<MemoryReclaimResult>();
                    c.simulatedMemoryReclaimer = delegate(CancellationToken token) { token.Register(delegate { hangingReclaim.TrySetCanceled(); }); return hangingReclaim.Task; };
                    AdventureEnemy cancelBoss = new AdventureEnemy { Id = "memory-cancel-boss", IsBoss = true, MonsterKind = "boss", EvilTheme = "CN", BossWeapon = "spear", Health = 1, MaxHealth = 1 };
                    c.battle.Enemies.Add(cancelBoss); c.Spawn(cancelBoss); c.battle.Authorize(cancelBoss); c.battle.Cooldown = 0; c.battle.Strike(cancelBoss);
                    c.BeginCleanup(cancelBoss);
                    check(c.memoryReclaimTask != null && c.SetMemoryConsent(false) && !c.consent.MemoryEnabled, "memory consent revocation accepted while a reclaim runs");
                    c.DrainMemoryReclaim();
                    check(c.memoryReclaimTask == null && !c.consent.MemoryEnabled && c.battle.LastReward.Contains("取消"), "in-flight memory reclaim responds to revocation and reports cancellation");
                    // The fail-closed store path is unit-tested; in demo the setter still applies in memory.
                    check(c.SetCleanupConsent(true) && c.consent.CleanupEnabled && c.SetMemoryConsent(true) && c.consent.MemoryEnabled, "consent setters apply both scopes");
                    c.simulatedMemoryReclaimer = null;
                    c.simulatedCleaner = null; c.roaming = false;
                    AdventureEnemy walking = c.battle.Enemies.First(e => e.Id == "paint-0"); walking.X = -23500; walking.Y = -23500; walking.TargetX = -23000; walking.TargetY = -23500;
                    c.AdvanceEnemyPatrol(walking, false, .2); check(walking.X > -23500 && walking.FacingRight, "monsters patrol while the hero roaming switch is off");
                    c.battle.Authorize(walking); double queuedX = walking.X; c.AdvanceEnemyPatrol(walking, false, .2); check(walking.X > queuedX && walking.MoveSpeed < 100, "authorized monsters retain slow catchable patrol movement");
                    c.RenderWorld(true); c.enemyPaintCursor = 3; object turnFrame = currentFrame.GetValue(c.enemyWindows[walking.Id]); walking.VisualDirty = false;
                    c.MoveEnemy(walking, walking.X - 200, walking.Y, 80, .2); bool dirtyTurn = !walking.FacingRight && walking.VisualDirty; c.RenderWorld(false);
                    check(dirtyTurn && !Object.ReferenceEquals(turnFrame, currentFrame.GetValue(c.enemyWindows[walking.Id])), "direction reversal invalidates the cached sprite and paints immediately outside its normal slice");
                    c.CancelPending(); walking.X = c.x + 400; walking.Y = c.y + c.HeroSize.Height - c.EnemySize(walking).Height; c.BoundEnemy(walking);
                    double chaseDistance = c.EnemyHeroDistance(walking); c.AdvanceEnemyPatrol(walking, true, .2);
                    check(c.EnemyHeroDistance(walking) < chaseDistance && !walking.Authorized && c.battle.QueueCount == 0, "unauthorized monsters actively approach the hero without authorizing any file");
                    walking.X = c.x + c.HeroSize.Width / 2.0 - c.EnemySize(walking).Width / 2.0 + 20; walking.Y = c.y + c.HeroSize.Height - c.EnemySize(walking).Height;
                    int healthBeforeAttack = c.battle.Progress.Health; c.AttackHero(walking, true, .1);
                    check(c.battle.Progress.Health == healthBeforeAttack && c.EnemyHeroDistance(walking) == 20, "nearby monster measures feet distance and waits for attack cooldown");
                    c.AttackHero(walking, true, 5);
                    check(c.battle.Progress.Health < healthBeforeAttack && !walking.Authorized && c.pendingCleanup.Count == 0, "unauthorized monster damages the hero without entering cleanup");
                    c.actualMoveSpeed = 0; c.attackAnimation = 0; check(c.HeroVisual(0).Phase != "run" && c.HeroVisual(0).MoveSpeed == 0, "waiting next to a selected target does not animate an in-place run");
                    c.actualMoveSpeed = 80 * c.displayScale; check(c.HeroVisual(0).Phase == "run" && c.HeroVisual(0).MoveSpeed == 80, "walking animation receives actual reference-space movement speed");
                    c.CancelPending(); c.actualMoveSpeed = 0;
                    // Auto battle is filtered away from real-effect targets unless the
                    // separate unattended-maintenance consent is explicitly on.
                    // 本小节聚焦修士期的自动战斗与维护授权交互：直接切入觉醒后状态（凡人本就不能自动作战）。
                    c.cultivation.Mortal = false;
                    c.consent.CleanupEnabled = true; c.consent.AutoMaintenanceEnabled = false;
                    c.battle.Enemies.Clear(); c.battle.CancelQueue(); c.battle.SetAutomatic(true);
                    AdventureEnemy gatedFile = new AdventureEnemy { Id = "gated-file", Batch = new CleanupBatch(new List<CleanupCandidate> { new CleanupCandidate { Path = @"C:\KnightAssistantSynthetic\gated.tmp", Length = 512 } }, 1), Health = 5, MaxHealth = 100, X = c.x + 100, Y = c.y };
                    AdventureEnemy freeFiend = new AdventureEnemy { Id = "free-fiend", IsVirtual = true, MonsterKind = "bat", Health = 90, MaxHealth = 100, X = c.x + 800, Y = c.y };
                    c.battle.Enemies.Add(gatedFile); c.battle.Enemies.Add(freeFiend);
                    AdventureEnemy autoPick = c.battle.PlanNext(c.x, c.y, c.AutoMaintenanceEligibility);
                    check(autoPick == freeFiend && !gatedFile.Authorized, "cleanup consent alone never unleashes unattended real deletion");
                    c.consent.AutoMaintenanceEnabled = true; c.battle.CancelQueue(); c.battle.SetAutomatic(true);
                    check(c.battle.PlanNext(c.x, c.y, c.AutoMaintenanceEligibility) == gatedFile, "explicit unattended-maintenance consent admits real-effect targets");
                    check(c.SetAutoMaintenanceConsent(false) && !c.consent.AutoMaintenanceEnabled && !gatedFile.Authorized && c.battle.Automatic, "revoking unattended maintenance retires its queued automatic targets but keeps auto battle");
                    c.battle.CancelQueue(); c.battle.Enemies.Remove(gatedFile); c.battle.Enemies.Remove(freeFiend);
                    // A revocation against a locked grant file still applies in
                    // memory at once and cancels the in-flight worker; instead of
                    // only scheduling a retry it pins a revocation marker so a
                    // force-quit cannot let the stale grant resurrect consent.
                    string blockedConsent = Path.Combine(Path.GetTempPath(), "consent-blocked-" + Guid.NewGuid().ToString("N") + ".txt");
                    File.WriteAllText(blockedConsent, "cleanup=1\nmemory=1\nautomaint=0\n", new UTF8Encoding(false));
                    FileStream consentLock = new FileStream(blockedConsent, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    try
                    {
                        c.consentPathOverride = blockedConsent;
                        TaskCompletionSource<CleanupResult> hangingCleanup = new TaskCompletionSource<CleanupResult>();
                        c.simulatedCleaner = delegate(CleanupBatch ignoredBatch, CancellationToken token) { token.Register(delegate { hangingCleanup.TrySetResult(new CleanupResult { Cancelled = true, Message = "清理已取消" }); }); return hangingCleanup.Task; };
                        c.consent.CleanupEnabled = true;
                        AdventureEnemy inFlight = new AdventureEnemy { Id = "in-flight-revoke", Batch = new CleanupBatch(new List<CleanupCandidate> { new CleanupCandidate { Path = @"C:\KnightAssistantSynthetic\inflight.tmp", Length = 777 } }, 1), Health = 1, MaxHealth = 1 };
                        c.battle.Enemies.Add(inFlight); c.Spawn(inFlight); c.battle.Authorize(inFlight); c.battle.Cooldown = 0; c.battle.Strike(inFlight); c.BeginCleanup(inFlight);
                        check(c.cleanupTask != null && c.workCancellation != null, "in-flight cleanup worker started for the revocation-failure test");
                        check(c.SetCleanupConsent(false) && !c.consent.CleanupEnabled && !c.consentRetryCleanup && File.Exists(blockedConsent + ".revoked"),
                            "revocation against a locked grant applies in memory at once and pins a revocation marker instead of failing");
                        check(c.workCancellation.IsCancellationRequested, "in-flight cleanup is cancelled even when the consent file update fails");
                        c.DrainCleanup();
                        check(inFlight.Failed && c.battle.Progress.FreedBytes == freed && c.battle.LastReward.Contains("取消"), "cancelled worker reports its actual result without inventing freed bytes");
                        // Simulate a restart right now: the stale grant file still
                        // says cleanup=1, and while the lock keeps it unreadable
                        // the only safe answer is fully closed — never resurrected.
                        check(!MaintenanceConsentStore.Load(blockedConsent).CleanupEnabled && !MaintenanceConsentStore.Load(blockedConsent).MemoryEnabled,
                            "a restart after the revoked disable fails fully closed while the stale grant is locked");
                    }
                    finally
                    {
                        c.consentPathOverride = null; c.simulatedCleaner = null;
                        consentLock.Dispose();
                        try { File.Delete(blockedConsent); } catch { }
                        try { File.Delete(blockedConsent + ".revoked"); } catch { }
                    }
                    c.TryFlushConsentRevocation();
                    check(!c.consentRetryCleanup, "stale consent removal is retried until the file can no longer resurrect it");
                    // 无人值守自动维护同款承诺：锁定的授权文件撤销时同样钉入撤销
                    // 标记——本机立即生效，重启不会复活；显式落盘的授权仍按文件恢复。
                    string blockedAuto = Path.Combine(Path.GetTempPath(), "consent-automaint-" + Guid.NewGuid().ToString("N") + ".txt");
                    File.WriteAllText(blockedAuto, "cleanup=0\nmemory=0\nautomaint=1\n", new UTF8Encoding(false));
                    FileStream autoLock = new FileStream(blockedAuto, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    try
                    {
                        c.consentPathOverride = blockedAuto;
                        c.consent.AutoMaintenanceEnabled = true;
                        check(c.SetAutoMaintenanceConsent(false) && !c.consent.AutoMaintenanceEnabled && !c.consentRetryAutoMaint && File.Exists(blockedAuto + ".revoked"),
                            "automaint revocation against a locked grant applies in memory at once and pins the revocation marker");
                    }
                    finally { autoLock.Dispose(); }
                    c.TryFlushConsentRevocation(); // 锁已释放：无需重试，Load 会修正并清理
                    check(!c.consentRetryAutoMaint && !MaintenanceConsentStore.Load(blockedAuto).AutoMaintenanceEnabled && !File.Exists(blockedAuto + ".revoked"),
                        "after the lock clears, loading persists the corrected grant and retires the marker");
                    c.consentPathOverride = null;
                    File.WriteAllText(blockedAuto, "cleanup=0\nmemory=0\nautomaint=1\n", new UTF8Encoding(false));
                    check(MaintenanceConsentStore.Load(blockedAuto).AutoMaintenanceEnabled,
                        "an explicitly persisted automaint grant is exactly what a restart restores");
                    try { File.Delete(blockedAuto); } catch { }
                    try { File.Delete(blockedAuto + ".revoked"); } catch { }
                    // Hide/show and demo transitions never detach runtime state from
                    // the persisted unattended-battle preference.
                    c.cultivation.AutoBattle = true; c.battle.SetAutomatic(true);
                    c.Enabled = false; check(!c.battle.Automatic, "hiding stops runtime auto battle");
                    c.Enabled = true; check(c.battle.Automatic, "showing restores the persisted auto-battle preference");
                    c.cultivation.AutoBattle = false; c.Enabled = false; c.Enabled = true;
                    check(!c.battle.Automatic, "a saved manual preference survives hide/show too");
                    c.cultivation.AutoBattle = true; c.RestoreAutoBattlePreference(); c.battle.CancelQueue();
                    c.consent.CleanupEnabled = false;
                    // Manual pause/resume of cultivation applies instantly.
                    c.SetCultivationPaused(true);
                    check(c.CultivationPaused && c.cultivation.LastNotice.Contains("暂歇"), "cultivation pause applies immediately");
                    c.SetCultivationPaused(false);
                    check(!c.CultivationPaused && c.cultivation.LastNotice.Contains("继续"), "cultivation resumes on request");
                    // 百业证道（示范运行 + 合成数据源）：首启说明门槛、模式门控、
                    // 结算、空闲停算、手动标记与第一条机缘都经控制器入口生效。
                    // 回到示范档案的初始凡人态：凡人道途悟道尚不能凝作破境契机，机缘提示保持原样。
                    c.cultivation.Mortal = true;
                    Action<double> feedBaiye = delegate(double seconds) { for (double s = 0; s < seconds; s += 3) c.TickBaiyeForTest(3); };
                    string baiyeSmokeArchive = Path.Combine(Path.GetTempPath(), "baiye-smoke-" + Guid.NewGuid().ToString("N") + ".txt");
                    c.baiyePathOverride = baiyeSmokeArchive;
                    check(!c.SetBaiyeMode(1), "baiye recognition stays locked before the first-run explanation");
                    c.AcknowledgeBaiye();
                    check(c.SetBaiyeMode(1) && c.Baiye.Mode == 1, "category recognition enabled after acknowledgement");
                    c.baiyeTracker.ForegroundProcessName = delegate { return "devenv"; };
                    c.baiyeTracker.IdleSeconds = delegate { return 0; };
                    feedBaiye(660);
                    check(c.Baiye.YunOf("zhen") >= 63 && c.Baiye.LevelOf("zhen") >= 1, "coding accrues formation dao-yun through the controller path");
                    double heldYun = c.Baiye.YunOf("zhen");
                    c.baiyeTracker.IdleSeconds = delegate { return 300; };
                    feedBaiye(120);
                    check(c.Baiye.YunOf("zhen") == heldYun, "idle periods never accrue dao-yun");
                    c.baiyeTracker.IdleSeconds = delegate { return 0; };
                    check(c.SetBaiyeMode(0) && c.Baiye.Mode == 0, "recognition can be switched off at any time");
                    feedBaiye(120);
                    check(c.Baiye.YunOf("zhen") == heldYun, "closed recognition stops sampling immediately");
                    c.SetBaiyeMode(2); c.SetBaiyeManualMark("manual-tianji");
                    feedBaiye(1260);
                    check(c.Baiye.YunOf("tianji") >= 122, "manual marks accrue the chosen dao in mark mode");
                    c.SetBaiyeManualMark(null);
                    feedBaiye(720);
                    check(c.Baiye.YunOf("zhen") >= 120, "auto recognition resumes after the manual mark clears");
                    check(c.Baiye.EncounterUnlockedUtc.ContainsKey("tianji-yansuan") && c.Baiye.Heritages.Contains("tianji-chart"),
                        "zhen + tianji trigger the first resonance through the controller");
                    check(c.battle.LastReward.Contains("天机演算") && c.BaiyeNotice != null && c.BaiyeNotice.Contains("机缘"),
                        "resonance surfaces a reward notice instead of a bare popup");
                    check(!File.Exists(baiyeSmokeArchive), "demonstration runs leave the isolated baiye archive absent");
                    // —— 灵域历练（P3）：进入门控 / 节点转移 / 虚拟战斗回调 / 机缘 / 战败出口；示范运行不写真实灵域档 ——
                    if (c.battle.Progress.Dead) { c.battle.Progress.Dead = false; c.battle.Progress.Health = c.battle.Progress.MaxHealth; }
                    string lingyuSmokeArchive = Path.Combine(Path.GetTempPath(), "lingyu-smoke-" + Guid.NewGuid().ToString("N") + ".txt");
                    c.lingyuPathOverride = lingyuSmokeArchive;
                    // 成长体系：示范档案开局是凡人——凡人永远进不了灵域；完成觉醒后才测修士门控。
                    check(c.cultivation.Mortal && !c.LingyuEnter("qianmian"), "a mortal is always locked out of the mirage");
                    c.cultivation.Mortal = false; c.cultivation.EnsureIdentity(new Random(4));
                    check(!c.LingyuEnter("qianmian"), "the mirage stays locked before any dao progress");
                    check(!c.LingyuEnter("tiangong"), "unplayable realms refuse entry");
                    c.baiye.AddYun("youli", 60, DateTime.UtcNow); // 游历道 L1 → 蜃楼解锁
                    c.cultivation.Layer = 12; // 炼气后期：蜃楼主镜之战可凝筑基灵髓
                    check(c.LingyuEnter("qianmian") && c.Lingyu.ExpeditionActive && c.Lingyu.ExpeditionNode == "ferry",
                        "the wandering dao opens the mirage at the ferry");
                    Func<AdventureEnemy> mirrorFiend = delegate
                    {
                        return c.battle.Enemies.FirstOrDefault(e => e.Id.StartsWith("lingyu-", StringComparison.Ordinal) && !e.Defeated);
                    };
                    Action killMirrorFiend = delegate
                    {
                        AdventureEnemy fiend = mirrorFiend();
                        if (fiend == null) return;
                        c.battle.Authorize(fiend); fiend.Health = 1; c.battle.Cooldown = 0;
                        c.battle.Strike(fiend); c.BeginCleanup(fiend); c.battle.Tick(.31);
                    };
                    int mirageGuard = 0;
                    while (c.Lingyu.Visits("qianmian") == 0 && mirageGuard < 400)
                    {
                        mirageGuard++;
                        if (c.Mirage.PendingJiyuan != null) { c.LingyuChoose(0); continue; }
                        if (c.Lingyu.CombatPending) { killMirrorFiend(); continue; }
                        string mirageNode = c.Lingyu.ExpeditionNode;
                        if (mirageNode == "ferry") c.LingyuChoose(c.Lingyu.ExpeditionFlags.Contains("corridor-cleared") && !c.Lingyu.ExpeditionFlags.Contains("mist-cleared") ? 1 : 0);
                        else if (mirageNode == "echo") c.LingyuChoose(c.Lingyu.ExpeditionFlags.Contains("corridor-cleared") && c.Lingyu.ExpeditionFlags.Contains("mist-cleared") ? 0 : 1);
                        else if (mirageNode == "corridor" || mirageNode == "mist")
                        {
                            bool ownCleared = c.Lingyu.ExpeditionFlags.Contains(mirageNode + "-cleared");
                            bool otherCleared = c.Lingyu.ExpeditionFlags.Contains(mirageNode == "corridor" ? "mist-cleared" : "corridor-cleared");
                            c.LingyuChoose(ownCleared && !otherCleared ? 1 : 0);
                        }
                        else c.LingyuChoose(0); // 中庭 → 直面蜃楼主镜
                    }
                    check(c.Lingyu.Visits("qianmian") == 1 && c.Lingyu.Heritages.Contains("shuiyue-mirror"),
                        "a full mirage run through the controller pays the clear heritage");
                    check(c.Lingyu.Grants.Keys.Any(key => key.StartsWith("mirage-clear-", StringComparison.Ordinal)), "the clear reward is ledgered");
                    check(c.Lingyu.PendingQi.Count >= 3, "demonstration queues lingyu qi for settlement without paying the cultivation side");
                    check(mirrorFiend() == null, "no lingyu fiend survives its own fight");
                    check(c.Lingyu.Materials.ContainsKey("shuiyue-lingsui") && c.Lingyu.Materials["shuiyue-lingsui"] == 1,
                        "the layer-12 clear condenses the breakthrough lingsui through the controller");
                    check(c.cultivation.HasOpportunity("realm") && c.cultivation.HasOpportunity("treasure"),
                        "mirage progress grants the realm and treasure breakthrough opportunities");
                    // 蜃楼战斗中修士力竭：镜妖散去、奖励不落、探索不软锁。
                    int fightGuard = 0;
                    while (!c.Lingyu.CombatPending && fightGuard < 200)
                    {
                        fightGuard++;
                        if (c.Mirage.PendingJiyuan != null) { c.LingyuChoose(0); continue; }
                        string node = c.Lingyu.ExpeditionNode;
                        if (node == "ferry" || node == "corridor") c.LingyuChoose(0);
                        else c.LingyuChoose(1); // 其它节点先回渡口再入镜廊
                    }
                    check(c.Lingyu.CombatPending && mirrorFiend() != null, "a mirror fiend holds the expedition fight");
                    int grantsBeforeFall = c.Lingyu.Grants.Count; // 战斗挂起后再取样：探路过程的宝箱/机缘不计入
                    c.battle.Damage(99999);
                    c.LingyuDeathCheck();
                    check(!c.Lingyu.CombatPending && mirrorFiend() == null && c.Lingyu.Grants.Count == grantsBeforeFall,
                        "a fallen cultivator releases the fight without paying or soft-locking");
                    check(c.Lingyu.ExpeditionActive && c.Mirage.CurrentChoices().Length > 0, "the expedition continues after a lost fight");
                    c.Resurrect();
                    check(!File.Exists(lingyuSmokeArchive), "demonstration runs leave the isolated lingyu archive absent");
                    // —— 蜃楼重启软锁回归：战斗中退出 → 真实读档 → 控制器/界面补发镜妖 → 预算与去重 ——
                    string mirageArchive = Path.Combine(Path.GetTempPath(), "lingyu-restart-" + Guid.NewGuid().ToString("N") + ".txt");
                    c.lingyuPathOverride = mirageArchive;
                    int resumeGuard = 0; // 真实界面入口 API（与「继续探索」按钮同一路径）进入一场未决战斗
                    while (!c.Lingyu.CombatPending && resumeGuard < 200)
                    {
                        resumeGuard++;
                        if (c.Mirage.PendingJiyuan != null) { c.LingyuChoose(0); continue; }
                        string resumeNode = c.Lingyu.ExpeditionNode;
                        if (resumeNode == "ferry" || resumeNode == "corridor") c.LingyuChoose(0);
                        else c.LingyuChoose(1);
                    }
                    check(c.Lingyu.CombatPending && mirrorFiend() != null, "restart fixture: a mirror fiend holds the fight before exit");
                    string outcomeBeforeRestart = c.Lingyu.CombatOutcome;
                    c.lingyu.Save(c.LingyuWritePath); // 与生产 Save() 相同的写盘点：模拟退出落盘
                    check(File.Exists(mirageArchive), "restart fixture: expedition archive persisted to disk");
                    // 真实重启读档：新进度对象走生产 LingyuProgress.Load 同一代码路径。
                    LingyuProgress reloaded = LingyuProgress.Load(mirageArchive);
                    check(reloaded.ExpeditionActive && reloaded.CombatPending && reloaded.CombatOutcome == outcomeBeforeRestart,
                        "a restart reloads the pending fight with its settlement semantics intact");
                    int resumeRequests = 0; bool resumeBoss = false;
                    MirageEngine resumeEngine = new MirageEngine(reloaded, new Random(53));
                    resumeEngine.CombatRequested += delegate(string label, bool boss) { resumeRequests++; resumeBoss = boss; };
                    check(resumeEngine.ResumePendingCombat() && resumeRequests == 1 && resumeBoss == (outcomeBeforeRestart == "final"),
                        "resume re-issues exactly one combat request and a boss fight stays a boss fight");
                    LingyuProgress calmProgress = new LingyuProgress { ExpeditionActive = true };
                    MirageEngine calmEngine = new MirageEngine(calmProgress, new Random(59));
                    bool calmFired = false; calmEngine.CombatRequested += delegate(string label, bool boss) { calmFired = true; };
                    check(!calmEngine.ResumePendingCombat() && !calmFired, "resume refuses when no fight is pending");
                    // 控制器层恢复：镜妖对象丢失（进程重启后的真实情形）→ 补发一只且仅一只。
                    c.battle.Enemies.RemoveAll(e => e.Id.StartsWith("lingyu-", StringComparison.Ordinal));
                    check(c.TryResumeLingyuCombat() && mirrorFiend() != null && mirrorFiend().NoFieldClear,
                        "controller resume respawns one field-clear-immune mirror fiend");
                    string respawnedId = mirrorFiend() == null ? null : mirrorFiend().Id;
                    check(!c.TryResumeLingyuCombat() && mirrorFiend() != null && mirrorFiend().Id == respawnedId
                        && c.battle.Enemies.Count(e => e.Id.StartsWith("lingyu-", StringComparison.Ordinal)) == 1,
                        "resume is idempotent while the fiend lives - no duplicates");
                    // 敌场预算：填满普通名额后补发仍不超编（退休一只未交战普通妖物腾位）。
                    while (EnemyFieldBudget.CanAddOrdinary(c.battle.Enemies))
                    {
                        AdventureEnemy filler = new AdventureEnemy { Id = "filler-" + Guid.NewGuid().ToString("N"), IsVirtual = true, MonsterKind = AdventureMonster.All[0].Id, SpawnLevel = 1 };
                        filler.MaxHealth = filler.Health = 10;
                        c.battle.Enemies.Add(filler);
                    }
                    check(!EnemyFieldBudget.CanAddOrdinary(c.battle.Enemies), "restart fixture: field filled to the ordinary budget");
                    c.battle.Enemies.RemoveAll(e => e.Id.StartsWith("lingyu-", StringComparison.Ordinal));
                    check(c.TryResumeLingyuCombat() && EnemyFieldBudget.IsWithinBudget(c.battle.Enemies)
                        && c.battle.Enemies.Count(e => e.Id.StartsWith("lingyu-", StringComparison.Ordinal)) == 1,
                        "a full field still admits the resumed fiend by retiring an idle ordinary, never exceeding budget");
                    // 真实界面入口恢复：先移除镜妖模拟缺失，打开灵域面板即由 500ms 刷新路径补发。
                    c.battle.Enemies.RemoveAll(e => e.Id.StartsWith("lingyu-", StringComparison.Ordinal));
                    using (LingyuForm panel = new LingyuForm(c))
                    {
                        panel.StartPosition = FormStartPosition.Manual; panel.Location = new Point(-24000, -24000);
                        panel.Show(); Application.DoEvents(); panel.RefreshData();
                        check(mirrorFiend() != null, "opening the lingyu panel restores the pending fight through the real UI");
                        panel.Hide();
                    }
                    int winGuard = 0; // 恢复后的战斗胜利仍按持久化语义结算
                    while (mirrorFiend() != null && winGuard++ < 8) killMirrorFiend();
                    check(!c.Lingyu.CombatPending && mirrorFiend() == null, "a resumed fight settles its win and releases the expedition");
                    c.battle.Enemies.RemoveAll(e => e.Id.StartsWith("filler-", StringComparison.Ordinal));
                    c.lingyuPathOverride = lingyuSmokeArchive;
                    try { File.Delete(mirageArchive); } catch { }
                    // Unattended recovery: a defeated cultivator always rests back.
                    c.battle.Enemies.Clear(); c.CancelPending(); c.battle.SetAutomatic(false);
                    c.battle.Progress.Dead = true; c.battle.Progress.Health = 0; c.battle.DeathTime = CultivationBalance.ReviveRestSeconds + 1;
                    c.enabled = true; c.demoStarted = -1; c.demoPending = false;
                    c.Tick(null, EventArgs.Empty);
                    check(!c.battle.Progress.Dead && c.battle.Progress.Health == c.battle.Progress.MaxHealth && c.battle.Automatic && c.battle.LastReward.Contains("修士调息完毕"), "an unattended cultivator recovers from defeat with an exact rest notice and resumes auto battle");
                    c.cultivation.AutoBattle = false; c.battle.SetAutomatic(false);
                    c.battle.Progress.Dead = true; c.battle.Progress.Health = 0; c.battle.DeathTime = CultivationBalance.ReviveRestSeconds + 1;
                    c.Tick(null, EventArgs.Empty);
                    check(!c.battle.Progress.Dead && !c.battle.Automatic, "unattended rest respects the manual-battle preference");
                    c.cultivation.AutoBattle = true;
                    KnightState offlineProbe = new KnightState();
                    for (int probe = 0; probe < 400; probe++) offlineProbe.Tick(.1, 0, false, KnightConnectivity.Unknown);
                    check(offlineProbe.Phase == KnightPhase.Pant && c.networkState.Phase != KnightPhase.Dying && c.networkState.Phase != KnightPhase.Grave, "offline maps to meditating: no false death while the network is down");
                    AdventureEnemy offlineFiend = new AdventureEnemy { Id = "offline-fiend", IsVirtual = true, MonsterKind = "slime", Health = 20, MaxHealth = 20 };
                    c.battle.Enemies.Add(offlineFiend); c.Spawn(offlineFiend); c.battle.Authorize(offlineFiend);
                    check(c.StateText.Contains("战斗Lv."), "growth display separates combat level from cultivation realm");
                    c.battle.Enemies.Remove(offlineFiend); c.battle.CancelQueue();
                    c.battle.Enemies.Clear(); c.StartDemo();
                    c.demoPending = true; c.resumeAutomaticAfterDemo = true; c.Enabled = false; c.Tick(null, EventArgs.Empty);
                    check(!c.heroWindow.Visible && c.enemyWindows.Values.All(w => !w.Visible), "hide affects all desktop actors");
                    check(!c.demoPending && !c.resumeAutomaticAfterDemo && !c.Enabled && !c.battle.Automatic, "hide cannot resurrect a pending demo or automatic cleanup");
                    c.demoPending = true; c.resumeAutomaticAfterDemo = true; c.Enabled = false; c.Tick(null, EventArgs.Empty); check(!c.demoPending && !c.resumeAutomaticAfterDemo && !c.Enabled, "repeated hide cancels a demo requested while already hidden");
                    AdventureEnemy partial = new AdventureEnemy { Id = "smoke-partial", Authorized = true, Cleaning = true };
                    c.activeCleanupEnemy = partial; c.workCancellation = new CancellationTokenSource(); c.cleanupTask = Task.FromResult(new CleanupResult { FreedBytes = 1048576, DeletedFiles = 1, Cancelled = true, Message = "synthetic partial completion" });
                    int xpBeforeExit = c.battle.Progress.Experience;
                    c.Dispose(); check(c.battle.Progress.FreedBytes == freed + 1048576 && c.battle.Progress.Experience == xpBeforeExit, "exit drains real partial success without paying combat XP a second time");
                }
                using (AdventureController hanging = new AdventureController(owner, assetDirectory, true))
                {
                    // Exit with an in-flight background task: Dispose cancels the
                    // worker and still returns promptly instead of blocking the
                    // whole shutdown behind cleanupTask.Wait().
                    hanging.simulatedCleaner = delegate(CleanupBatch ignoredBatch, CancellationToken token)
                    {
                        TaskCompletionSource<CleanupResult> tcs = new TaskCompletionSource<CleanupResult>();
                        token.Register(delegate { tcs.TrySetResult(new CleanupResult { Cancelled = true, Message = "清理已取消" }); });
                        return tcs.Task;
                    };
                    AdventureEnemy inFlight = new AdventureEnemy { Id = "dispose-in-flight", Batch = new CleanupBatch(new List<CleanupCandidate> { new CleanupCandidate { Path = @"C:\KnightAssistantSynthetic\dispose.tmp", Length = 999 } }, 1), Health = 1, MaxHealth = 1 };
                    hanging.enabled = true;
                    hanging.battle.Enemies.Add(inFlight); hanging.Spawn(inFlight); hanging.battle.Authorize(inFlight); hanging.battle.Cooldown = 0; hanging.battle.Strike(inFlight); hanging.BeginCleanup(inFlight);
                    CancellationTokenSource inFlightCancellation = hanging.workCancellation;
                    check(hanging.cleanupTask != null && inFlightCancellation != null, "in-flight cleanup started for the dispose test");
                    System.Diagnostics.Stopwatch exitWatch = System.Diagnostics.Stopwatch.StartNew();
                    hanging.Dispose();
                    exitWatch.Stop();
                    check(inFlightCancellation.IsCancellationRequested && exitWatch.Elapsed.TotalSeconds < 10,
                        "dispose with an in-flight task cancels the worker and returns promptly instead of blocking shutdown");
                }
                using (AdventureController stubborn = new AdventureController(owner, assetDirectory, true))
                {
                    // A worker that NEVER answers cancellation: Dispose must bound
                    // its wait, still return, and never credit freed bytes to an
                    // unfinished task.
                    stubborn.simulatedCleaner = delegate(CleanupBatch ignoredBatch, CancellationToken token) { return new TaskCompletionSource<CleanupResult>().Task; };
                    long freedBefore = stubborn.battle.Progress.FreedBytes;
                    AdventureEnemy stuck = new AdventureEnemy { Id = "dispose-stubborn", Batch = new CleanupBatch(new List<CleanupCandidate> { new CleanupCandidate { Path = @"C:\KnightAssistantSynthetic\stubborn.tmp", Length = 555 } }, 1), Health = 1, MaxHealth = 1 };
                    stubborn.enabled = true;
                    stubborn.battle.Enemies.Add(stuck); stubborn.Spawn(stuck); stubborn.battle.Authorize(stuck); stubborn.battle.Cooldown = 0; stubborn.battle.Strike(stuck); stubborn.BeginCleanup(stuck);
                    check(stubborn.cleanupTask != null, "stubborn cleanup started for the bounded-wait test");
                    System.Diagnostics.Stopwatch stubbornWatch = System.Diagnostics.Stopwatch.StartNew();
                    stubborn.Dispose();
                    stubbornWatch.Stop();
                    check(stubbornWatch.Elapsed.TotalSeconds < 15 && stubborn.battle.Progress.FreedBytes == freedBefore && stubborn.cleanupTask != null,
                        "dispose bounds the wait for a cancellation-ignoring worker and never invents freed bytes for it");
                }
            }
            return "Adventure native smoke: " + count + " checks passed; no real cleanup, network query, or progress save.\r\n" + ResourceSmoke(assetDirectory);
        }
        private static string ResourceSmoke(string assetDirectory)
        {
            return LiveSmoke(assetDirectory) + "\r\n" + TacticsSmoke(assetDirectory) + "\r\n" + GrowthSmoke(assetDirectory);
        }
        // 成长体系闭环（示范运行 + 合成战斗，不写任何真实档案）：
        // 凡人开局 → 十次有效击杀 → 灵根觉醒 → 圆满瓶颈 → 契机/材料 → 破境结算，另验战力联动接线。
        private static string GrowthSmoke(string assetDirectory)
        {
            int n = 0; Action<bool, string> check = delegate(bool ok, string label) { if (!ok) throw new InvalidOperationException("Growth smoke: " + label); n++; };
            using (Form owner = new Form())
            {
                IntPtr handle = owner.Handle;
                using (AdventureController g = new AdventureController(owner, assetDirectory, true))
                {
                    g.timer.Stop(); g.enabled = true; g.demoStarted = -1; g.demoPending = false;
                    g.area = new Rectangle(-24000, -24000, 1600, 1000); g.x = -23600; g.y = -23400;
                    check(g.cultivation.Mortal && g.cultivation.MortalKills == 0 && !g.cultivation.CanAwaken, "a fresh archive begins as a mortal");
                    check(!g.LingyuEnter("qianmian"), "mortals cannot enter the mirage");
                    check(!g.AwakenSpiritRoot(), "the ritual stays locked before ten settled kills");
                    // —— 凡人期主动战斗规则：自动作战未解锁、出招靠点击、格挡有冷却 ——
                    check(!g.cultivation.AutoBattleUnlocked && !g.battle.Automatic, "a fresh mortal has auto battle locked");
                    g.cultivation.AutoBattle = true; g.RestoreAutoBattlePreference();
                    check(!g.battle.Automatic, "a mortal auto-battle preference never engages automatic combat");
                    AdventureEnemy sparFiend = new AdventureEnemy { Id = "growth-spar", IsVirtual = true, MonsterKind = "slime", Health = 60, MaxHealth = 60 };
                    sparFiend.X = g.x + g.HeroSize.Width / 2.0 - g.EnemySize(sparFiend).Width / 2.0 + 10;
                    sparFiend.Y = g.y + g.HeroSize.Height - g.EnemySize(sparFiend).Height;
                    g.battle.Enemies.Add(sparFiend); g.Spawn(sparFiend); g.battle.Authorize(sparFiend); g.battle.Cooldown = 0;
                    g.Tick(null, EventArgs.Empty);
                    check(sparFiend.Health == 60, "a mortal never auto-swings even with a selected target in range");
                    // Tick 中敌人会游走：出招前重新把它摆回角色身边。
                    sparFiend.X = g.x + g.HeroSize.Width / 2.0 - g.EnemySize(sparFiend).Width / 2.0 + 10;
                    sparFiend.Y = g.y + g.HeroSize.Height - g.EnemySize(sparFiend).Height;
                    g.battle.Cooldown = 0;
                    g.TryMortalStrike(sparFiend);
                    int sparAfterFirst = sparFiend.Health;
                    check(sparAfterFirst < 60 && g.battle.Cooldown > 0, "a deliberate click lands the mortal's blow and starts the swing cooldown");
                    g.TryMortalStrike(sparFiend);
                    check(sparFiend.Health == sparAfterFirst, "each strike waits for the weapon cooldown - no click spam");
                    g.battle.Enemies.Remove(sparFiend); g.battle.CancelQueue();
                    // 格挡：1 秒窗口内来袭伤害大减，10 秒冷却。
                    AdventureEnemy bruiser = new AdventureEnemy { Id = "growth-bruise", IsVirtual = true, MonsterKind = "imp", Health = 999, MaxHealth = 999 };
                    bruiser.X = g.x + g.HeroSize.Width / 2.0 - g.EnemySize(bruiser).Width / 2.0 + 20;
                    bruiser.Y = g.y + g.HeroSize.Height - g.EnemySize(bruiser).Height;
                    int impDamage = AdventureMonster.Find("imp").Damage;
                    int heroBeforeBlock = g.battle.Progress.Health;
                    check(g.TryBlock() && !g.TryBlock(), "bracing works once and then sits on its cooldown");
                    g.AttackHero(bruiser, true, 5);
                    check(g.battle.Progress.Health == heroBeforeBlock - Math.Max(1, impDamage / 5) && g.battle.LastReward.Contains("格挡成功"),
                        "a timed brace blunts the incoming blow");
                    g.blockUntil = -1; g.blockCooldownUntil = -1;
                    g.AttackHero(bruiser, true, 5);
                    check(g.battle.Progress.Health == heroBeforeBlock - Math.Max(1, impDamage / 5) - impDamage, "without a brace the same blow lands in full");
                    g.battle.Progress.Health = g.battle.Progress.MaxHealth;
                    // 凡人力竭：自动调息恢复仍在，但恢复后保持手动，等待玩家主动操作。
                    g.battle.Enemies.Clear(); g.CancelPending(); g.battle.SetAutomatic(false);
                    g.battle.Progress.Dead = true; g.battle.Progress.Health = 0; g.battle.DeathTime = CultivationBalance.ReviveRestSeconds * 3;
                    g.Tick(null, EventArgs.Empty);
                    check(!g.battle.Progress.Dead && !g.battle.Automatic && g.battle.LastReward.Contains("凡躯调息完毕"),
                        "a fallen mortal rests back unattended with an exact rest notice and keeps combat manual");
                    Action killVirtual = delegate
                    {
                        AdventureEnemy fiend = new AdventureEnemy { Id = "growth-" + Guid.NewGuid().ToString("N"), IsVirtual = true, MonsterKind = "imp", Health = 1, MaxHealth = 1 };
                        g.battle.Enemies.Add(fiend); g.Spawn(fiend); g.battle.Authorize(fiend); g.battle.Cooldown = 0; g.battle.Strike(fiend); g.BeginCleanup(fiend); g.battle.Tick(.31);
                    };
                    for (int i = 0; i < 9; i++) killVirtual();
                    check(g.cultivation.Mortal && g.cultivation.MortalKills == 9 && g.cultivation.DeferredQi > 0 && g.cultivation.Qi == 0 && !g.cultivation.CanAwaken,
                        "nine kills temper the mortal body while gains stay deferred");
                    // —— B3 凡人历练防绕过：只有真实战斗（Strike 致死）才计历练 ——
                    AdventureEnemy sweepFiend = new AdventureEnemy { Id = "growth-sweep", IsVirtual = true, MonsterKind = "slime", Health = 5, MaxHealth = 5 };
                    g.battle.Enemies.Add(sweepFiend); g.Spawn(sweepFiend);
                    List<AdventureEnemy> swept = g.battle.ClearFieldSnapshot(); // 一键清场的内部两步，绕过 Strike
                    foreach (AdventureEnemy e2 in swept) g.BeginCleanup(e2);
                    check(swept.Contains(sweepFiend) && g.cultivation.MortalKills == 9,
                        "one-click clear settles rewards but never tempers the mortal body");
                    // 真实战斗第十杀 + 重复回调：结算只认一次。
                    AdventureEnemy lastFiend = new AdventureEnemy { Id = "growth-repeat", IsVirtual = true, MonsterKind = "imp", Health = 1, MaxHealth = 1 };
                    g.battle.Enemies.Add(lastFiend); g.Spawn(lastFiend); g.battle.Authorize(lastFiend); g.battle.Cooldown = 0; g.battle.Strike(lastFiend);
                    g.BeginCleanup(lastFiend); g.BeginCleanup(lastFiend); g.battle.Tick(.31);
                    check(g.cultivation.MortalKills == 10 && g.cultivation.CanAwaken,
                        "the tenth combat kill opens the ritual and repeated callbacks never double-count");
                    long deferredBefore = g.cultivation.DeferredQi;
                    check(g.AwakenSpiritRoot() && !g.cultivation.Mortal && CultivationIdentity.IsValidRoot(g.cultivation.SpiritRoot)
                        && CultivationTechnique.IsValid(g.cultivation.TechniqueId) && g.cultivation.DaoTitle.Length > 0,
                        "the ritual assigns root, technique and dao title");
                    check(g.cultivation.DeferredQi == 0 && (g.cultivation.Qi > 0 || g.cultivation.Layer > 1) && g.cultivation.LifetimeQi >= deferredBefore,
                        "deferred gains convert to real cultivation on awakening");
                    check(!g.AwakenSpiritRoot(), "the awakening ritual never fires twice");
                    // 觉醒即解锁：自动作战偏好默认开启并立即生效。
                    check(g.cultivation.AutoBattle && g.cultivation.AutoBattleUnlocked && g.battle.Automatic,
                        "awakening unlocks auto battle and engages it by default");
                    // 旧档兼容：已觉醒但偏好手动的存档恢复后仍保持手动。
                    g.cultivation.AutoBattle = false; g.battle.SetAutomatic(false); g.RestoreAutoBattlePreference();
                    check(!g.battle.Automatic, "an awakened archive keeps a saved manual preference");
                    g.cultivation.AutoBattle = true; g.battle.SetAutomatic(true);
                    // 破境闭环：未圆满不可破；圆满缺契机不可破；任一契机可开；保底必成；材料成败皆消耗。
                    check(!g.TryBreakthrough(false), "an incomplete realm cannot break through");
                    g.cultivation.Layer = 12; g.cultivation.Qi = 0;
                    check(g.cultivation.RealmComplete && !g.cultivation.CanAttemptBreakthrough && !g.TryBreakthrough(false),
                        "a complete realm without an opportunity refuses the ritual");
                    g.cultivation.GrantOpportunity("insight");
                    check(g.cultivation.CanAttemptBreakthrough, "any single opportunity opens the ritual");
                    check(g.BreakthroughMaterialCount == 0, "no lingsui without the mirage feat");
                    g.lingyu.Materials["shuiyue-lingsui"] = 1;
                    check(g.BreakthroughMaterialCount == 1 && g.cultivation.BreakthroughPercent(true, true) > g.cultivation.BreakthroughPercent(false, false),
                        "an owned lingsui raises the measured chance");
                    g.cultivation.BreakthroughInsight = g.cultivation.RealmDef.GuaranteedInsight;
                    check(g.cultivation.BreakthroughPercent(false, false) >= 100, "deep insight guarantees the breakthrough");
                    check(g.TryBreakthrough(true) && g.cultivation.RealmIndex == 1 && g.cultivation.Layer == 1
                        && g.cultivation.BreakthroughOpportunities.Count == 0 && g.cultivation.BreakthroughInsight == 0,
                        "the guaranteed ritual succeeds and clears opportunities for the new realm");
                    check(g.BreakthroughMaterialCount == 0, "the consumed lingsui leaves the collection");
                    // —— B1 破境材料安全结算：前置校验、幂等事务、启动对账 ——
                    // 无资格重复点击：材料与两本台账分毫不动（任一存档写入失败都无从谈起）。
                    g.cultivation.RealmIndex = 0; g.cultivation.Layer = 3; g.cultivation.Qi = 0;
                    g.cultivation.BreakthroughOpportunities.Clear(); g.cultivation.ExternalGrants.Clear();
                    g.lingyu.Materials["shuiyue-lingsui"] = 1; g.lingyu.Grants.Clear();
                    int attemptsBefore = g.cultivation.BreakthroughAttempts;
                    check(!g.TryBreakthrough(true) && !g.TryBreakthrough(true)
                        && g.lingyu.Materials["shuiyue-lingsui"] == 1 && g.lingyu.Grants.Count == 0
                        && g.cultivation.ExternalGrants.Count == 0 && g.cultivation.BreakthroughAttempts == attemptsBefore,
                        "unqualified repeated clicks never touch the material or either ledger");
                    // 资格齐备：恰扣一份，唯一事务号同时落两本台账；成败皆消耗。
                    g.cultivation.Layer = 12; g.cultivation.GrantOpportunity("realm"); g.cultivation.BreakthroughInsight = 0;
                    bool btOutcome = g.TryBreakthrough(true);
                    string txId = "btmat-0-" + (attemptsBefore + 1);
                    check(!g.lingyu.Materials.ContainsKey("shuiyue-lingsui")
                        && g.lingyu.Grants.ContainsKey(txId) && g.cultivation.ExternalGrants.Contains(txId)
                        && (!btOutcome || g.cultivation.RealmIndex == 1),
                        "one settled attempt debits exactly one material and ledgers both archives once");
                    // 崩溃窗口重放（内存态）：修仙已记账、灵域未扣 → 对账补扣且幂等。
                    CultivationProgress crashedCult = new CultivationProgress();
                    crashedCult.Mortal = false; crashedCult.EnsureIdentity(new Random(9));
                    crashedCult.ExternalGrants.Add("btmat-0-3");
                    LingyuProgress crashedLingyu = new LingyuProgress();
                    crashedLingyu.Materials["shuiyue-lingsui"] = 2;
                    check(ReconcileBreakthroughMaterials(crashedCult, crashedLingyu)
                        && crashedLingyu.Materials["shuiyue-lingsui"] == 1 && crashedLingyu.Grants.ContainsKey("btmat-0-3"),
                        "reconcile settles the crashed material debit");
                    check(!ReconcileBreakthroughMaterials(crashedCult, crashedLingyu)
                        && crashedLingyu.Materials["shuiyue-lingsui"] == 1, "reconcile is idempotent");
                    crashedCult.ExternalGrants.Add("btmat-0-4"); crashedLingyu.Materials.Remove("shuiyue-lingsui");
                    check(ReconcileBreakthroughMaterials(crashedCult, crashedLingyu)
                        && crashedLingyu.Grants.ContainsKey("btmat-0-4") && !crashedLingyu.Materials.ContainsKey("shuiyue-lingsui"),
                        "reconcile never drives the stock negative when the material is already gone");
                    // 方向二（回滚）：灵域档已扣已记账、修仙档（结果权威）未记账——
                    // 即"修仙档写入失败、灵域材料档写入成功"的崩溃窗口：
                    // 孤儿台账回滚返还材料，该次尝试视为未发生，且回滚幂等。
                    CultivationProgress orphanCult = new CultivationProgress();
                    orphanCult.Mortal = false; orphanCult.EnsureIdentity(new Random(13));
                    LingyuProgress orphanLingyu = new LingyuProgress();
                    orphanLingyu.Grants["btmat-0-7"] = DateTime.UtcNow.Ticks; // 灵域已扣（库存 0），修仙无记录
                    check(ReconcileBreakthroughMaterials(orphanCult, orphanLingyu)
                        && orphanLingyu.Materials["shuiyue-lingsui"] == 1 && !orphanLingyu.Grants.ContainsKey("btmat-0-7"),
                        "an orphan ledger rolls the material back instead of losing it");
                    check(!ReconcileBreakthroughMaterials(orphanCult, orphanLingyu)
                        && orphanLingyu.Materials["shuiyue-lingsui"] == 1, "the orphan rollback is idempotent");
                    // 真实临时档案：崩溃现场落盘 → 重载 → 对账补平 → 再落盘重载核对（重启对账链路）。
                    string cultTmp = Path.Combine(Path.GetTempPath(), "btmat-cult-" + Guid.NewGuid().ToString("N") + ".txt");
                    string lingTmp = Path.Combine(Path.GetTempPath(), "btmat-ling-" + Guid.NewGuid().ToString("N") + ".txt");
                    try
                    {
                        CultivationProgress fileCult = new CultivationProgress();
                        fileCult.Mortal = false; fileCult.EnsureIdentity(new Random(11));
                        fileCult.ExternalGrants.Add("btmat-0-1"); // 修仙档已保存，灵域档未来得及保存
                        fileCult.Save(cultTmp);
                        LingyuProgress fileLingyu = new LingyuProgress();
                        fileLingyu.Materials["shuiyue-lingsui"] = 1;
                        fileLingyu.Save(lingTmp);
                        CultivationProgress reloadCult = CultivationProgress.Load(cultTmp);
                        LingyuProgress reloadLingyu = LingyuProgress.Load(lingTmp);
                        check(ReconcileBreakthroughMaterials(reloadCult, reloadLingyu)
                            && !reloadLingyu.Materials.ContainsKey("shuiyue-lingsui") && reloadLingyu.Grants.ContainsKey("btmat-0-1"),
                            "restart reconcile settles the interrupted transaction from disk");
                        reloadLingyu.Save(lingTmp);
                        LingyuProgress verifyLingyu = LingyuProgress.Load(lingTmp);
                        check(verifyLingyu.Grants.ContainsKey("btmat-0-1") && !verifyLingyu.Materials.ContainsKey("shuiyue-lingsui"),
                            "the reconciled ledger survives another save/load cycle");
                        // 写盘失败注入：灵域档指向目录路径 → 保存必须抛错（控制器路径有 try/catch 兜底）。
                        string dirAsFile = Path.Combine(Path.GetTempPath(), "btmat-dir-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(dirAsFile);
                        bool saveFailed = false;
                        try { reloadLingyu.Save(dirAsFile); } catch (IOException) { saveFailed = true; } catch (UnauthorizedAccessException) { saveFailed = true; }
                        check(saveFailed, "saving onto a directory path is reported as a failure");
                        Directory.Delete(dirAsFile);
                    }
                    finally
                    {
                        try { File.Delete(cultTmp); } catch { }
                        try { File.Delete(lingTmp); } catch { }
                    }
                    // 失败分支：不保底时结果不定，但要么晋境、要么加感悟，根基与契机无损。
                    g.cultivation.RealmIndex = 0; g.cultivation.Layer = 12; g.cultivation.Qi = 0; g.cultivation.BreakthroughInsight = 0;
                    g.cultivation.GrantOpportunity("realm");
                    bool outcome = g.TryBreakthrough(false);
                    check(outcome ? g.cultivation.RealmIndex == 1
                            : g.cultivation.RealmIndex == 0 && g.cultivation.Layer == 12 && g.cultivation.BreakthroughInsight > 0 && g.cultivation.HasOpportunity("realm"),
                        "every attempt either advances the realm or deepens insight without loss");
                    // —— B2 契机境界分层 + 事件编号（端到端）：历史成就迁移补发一次；
                    // 基线随档案持久化，重启绝不把旧成绩重新认定为新机缘 ——
                    g.cultivation.Mortal = false; g.cultivation.RealmIndex = 1; g.cultivation.Layer = 9; g.cultivation.Qi = 0;
                    g.cultivation.BreakthroughOpportunities.Clear(); g.cultivation.OpportunityEvents.Clear(); g.cultivation.ExternalGrants.Clear();
                    g.cultivation.OppBaselineQianmian = -1; g.cultivation.OppBaselineLingsui = -1; // 模拟无基线的旧档
                    g.lingyu.Grants.Clear(); g.lingyu.RealmVisits["qianmian"] = 1;
                    g.EvaluateBreakthroughOpportunities(); // 首次评估 = 旧档迁移补发（钉住当前境界）
                    check(g.cultivation.HasOpportunity("realm") && g.cultivation.CanAttemptBreakthrough
                        && g.cultivation.OpportunityEvents["realm"].StartsWith("migrate-qv", StringComparison.Ordinal),
                        "legacy mirage clears migrate to the current realm exactly once, event-numbered");
                    check(g.cultivation.OppBaselineQianmian == 1 && g.cultivation.OppBaselineLingsui >= 0,
                        "the first evaluation pins the persisted baselines");
                    // 存档 → 重载（模拟重启）：境界、事件编号与基线完整恢复，历史不重估。
                    string oppTmp = Path.Combine(Path.GetTempPath(), "opp-v4-" + Guid.NewGuid().ToString("N") + ".txt");
                    try
                    {
                        g.cultivation.Save(oppTmp);
                        CultivationProgress reloadedOpp = CultivationProgress.Load(oppTmp);
                        check(reloadedOpp.BreakthroughOpportunities["realm"] == 1
                            && reloadedOpp.OpportunityEvents["realm"] == g.cultivation.OpportunityEvents["realm"]
                            && reloadedOpp.OppBaselineQianmian == 1 && reloadedOpp.OppBaselineLingsui >= 0,
                            "opportunity realm, event id and baselines survive a restart");
                    }
                    finally { try { File.Delete(oppTmp); } catch { } }
                    g.cultivation.BreakthroughOpportunities.Clear(); g.cultivation.OpportunityEvents.Clear();
                    g.EvaluateBreakthroughOpportunities();
                    check(!g.cultivation.HasOpportunity("realm") && !g.cultivation.CanAttemptBreakthrough,
                        "without a new mirage moment the old clear is never re-granted, even after restart");
                    g.lingyu.RealmVisits["qianmian"] = 2; // 新的通关时刻
                    g.EvaluateBreakthroughOpportunities();
                    check(g.cultivation.HasOpportunity("realm") && g.cultivation.CanAttemptBreakthrough
                        && g.cultivation.BreakthroughOpportunities["realm"] == 1 && g.cultivation.OpportunityEvents["realm"] == "qv2",
                        "a fresh clear re-earns the opportunity pinned to the new realm with its event number");
                    // 境界-战斗强度联动：Tick 每拍同步系数（凡人偏弱 / 炼气渐强 / 筑基质变）。
                    g.cultivation.RealmIndex = 0; g.cultivation.Layer = 1; g.cultivation.Mortal = true;
                    g.Tick(null, EventArgs.Empty);
                    check(g.battle.CultivationDamageFactor < 1.0, "mortal battles run at the mortal disadvantage");
                    g.cultivation.Mortal = false; g.cultivation.Layer = 12; g.Tick(null, EventArgs.Empty);
                    check(g.battle.CultivationDamageFactor > 1.5, "late qi-refining sharpens every strike");
                    g.cultivation.RealmIndex = 1; g.Tick(null, EventArgs.Empty);
                    check(g.battle.CultivationDefenseFactor < 1.0 && g.battle.CultivationDamageFactor > 2.0, "foundation establishment brings real power");
                    // —— P4 洞天经营闭环（演示控制器，内存态）：解锁门槛、入库、种植炼制、增益接线 ——
                    g.cultivation.RealmIndex = 0;
                    check(!g.DongtianUnlocked && !g.DongtianPlant(0, "linggu"), "qi-refining cannot open the cave yet");
                    g.cultivation.RealmIndex = 1;
                    check(g.DongtianUnlocked, "foundation establishment unlocks the cave");
                    g.lingyu.Materials["shuiyue-lingsui"] = 2;
                    check(g.DongtianImport("shuiyue-lingsui") && g.DongtianImport("shuiyue-lingsui") && !g.DongtianImport("shuiyue-lingsui")
                        && g.Dongtian.CountOf("shuiyue-lingsui") == 2 && g.LingyuMaterialCount("shuiyue-lingsui") == 0,
                        "imports move lingsui one by one through the dual ledger");
                    check(g.DongtianStartPill("ninglu") && g.Dongtian.CountOf("shuiyue-lingsui") == 1, "the mirage material brews into a pill");
                    g.Dongtian.Furnace = "ninglu,1"; // 直接写成已成熟槽位，走真实收丹路径
                    check(g.DongtianCollectPill() && g.Dongtian.BuffActive("buff-ninglu", DateTime.UtcNow), "collecting the pill activates its buff");
                    g.Tick(null, EventArgs.Empty);
                    check(g.battle.CultivationDamageFactor > CultivationCombatPower.DamageFactor(g.cultivation),
                        "the pill buff feeds the per-tick combat factor");
                    check(g.DongtianPlant(0, "linggu") && !g.DongtianHarvest(0), "planting starts real-time growth and refuses early harvest");
                    g.Dongtian.Fields[0] = "linggu,1";
                    check(g.DongtianHarvest(0) && g.Dongtian.CountOf("linggu") == 2, "a ripe field harvests into the bag");
                    // —— 洞天面板刷新：低频定时、完成态自动出现领取、控件实例稳定、长开无 GDI/USER 泄漏 ——
                    using (DongtianForm panel = new DongtianForm(g))
                    {
                        panel.StartPosition = FormStartPosition.Manual; panel.Location = new Point(-24000, -24000);
                        panel.Show(); Application.DoEvents(); panel.RefreshData();
                        Control[] firstSet = panel.ActionControlsSnapshot();
                        check(firstSet.Length > 0 && firstSet.Any(b => b.Text.StartsWith("种", StringComparison.Ordinal))
                            && firstSet.All(b => b.Text != "收丹" && b.Text != "收器" && !b.Text.StartsWith("收获", StringComparison.Ordinal)),
                            "the cave panel opens with planting actions and no pending collect");
                        int gdiBefore = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                        int userBefore = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
                        for (int cycle = 0; cycle < 240; cycle++) { panel.RefreshData(); if ((cycle & 31) == 0) Application.DoEvents(); }
                        check(firstSet.SequenceEqual(panel.ActionControlsSnapshot()),
                            "an unchanged action set reuses the same button instances across 240 refreshes");
                        check(GetGuiResources(Process.GetCurrentProcess().Handle, 0) <= gdiBefore + 4
                            && GetGuiResources(Process.GetCurrentProcess().Handle, 1) <= userBefore + 4,
                            "a long-open panel leaks no GDI/USER objects across refreshes");
                        string signatureBeforeRipe = panel.ActionSignatureForTest;
                        // 灵田成熟：窗口保持打开，下一次低频刷新自动出现「收获田1」
                        g.Dongtian.Fields[0] = "linggu,1";
                        panel.RefreshData();
                        Control[] ripeSet = panel.ActionControlsSnapshot();
                        check(ripeSet.Any(b => b.Text == "收获田1") && panel.ActionSignatureForTest != signatureBeforeRipe,
                            "a ripe field surfaces its harvest action while the window stays open");
                        for (int cycle = 0; cycle < 30; cycle++) panel.RefreshData();
                        check(ripeSet.SequenceEqual(panel.ActionControlsSnapshot()), "the rebuilt set also stays instance-stable");
                        // 丹炉完成：收丹按钮自动出现，点击走真实控制器路径结算
                        check(g.DongtianStartPill("ninglu"), "brewing restarts for the collect-path check");
                        g.Dongtian.Furnace = "ninglu,1"; // 直接写成已成槽位，走真实收丹路径
                        panel.RefreshData();
                        Button collectPill = panel.ActionControlsSnapshot().FirstOrDefault(b => b.Text == "收丹") as Button;
                        check(collectPill != null, "a finished furnace surfaces the collect-pill action while the window stays open");
                        if (collectPill != null) { collectPill.PerformClick(); Application.DoEvents(); }
                        check(g.Dongtian.Furnace == null && g.Dongtian.BuffActive("buff-ninglu", DateTime.UtcNow),
                            "clicking the surfaced collect-pill action settles the pill for real");
                        // 器位完成：收器按钮自动出现并真实结算（器灵常驻生效）
                        g.Dongtian.Forge = "putuan,1";
                        panel.RefreshData();
                        Button collectGear = panel.ActionControlsSnapshot().FirstOrDefault(b => b.Text == "收器") as Button;
                        check(collectGear != null, "a finished forge surfaces the collect-gear action while the window stays open");
                        if (collectGear != null) { collectGear.PerformClick(); Application.DoEvents(); }
                        check(g.Dongtian.Forge == null && g.Dongtian.HasGear("putuan"),
                            "clicking the surfaced collect-gear action settles the gear for real");
                        panel.Hide();
                    }
                    g.Dongtian.Fields[0] = null; // 还原现场，后续故障注入段自带夹具
                    // —— 洞天存档故障注入：写盘失败不静默、待确认重试、库存与重复请求守恒 ——
                    string dtFixture = Path.Combine(Path.GetTempPath(), "dongtian-smoke-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dtFixture);
                    try
                    {
                        g.dongtianPathOverride = Path.Combine(dtFixture, "dongtian-v1.txt");
                        g.lingyuPathOverride = Path.Combine(dtFixture, "lingyu-v1.txt");
                        g.lingyu.Materials.Clear(); g.lingyu.Grants.Clear();
                        g.Dongtian.Bag.Clear(); g.Dongtian.Grants.Clear(); g.Dongtian.ImportSeq = 0;
                        g.lingyu.Materials["shuiyue-lingsui"] = 3;
                        check(g.DongtianImport("shuiyue-lingsui") && !g.DongtianPersistPending && File.Exists(g.DongtianWritePath) && File.Exists(g.LingyuWritePath),
                            "a committed import lands in both archives at once");
                        // 库存不足：不动任何台账与序号
                        check(!g.DongtianImport("lingshi-absent") && g.Dongtian.ImportSeq == 1 && !g.DongtianPersistPending
                            && g.lingyu.Grants.Count == 1 && g.Dongtian.Grants.Count == 1,
                            "insufficient stock refuses the import without touching either ledger");
                        // 重复请求：每次入库只移一份、序号唯一、总量守恒
                        check(g.DongtianImport("shuiyue-lingsui") && g.DongtianImport("shuiyue-lingsui")
                            && g.Dongtian.CountOf("shuiyue-lingsui") == 3 && g.LingyuMaterialCount("shuiyue-lingsui") == 0
                            && g.Dongtian.Grants.Count == 3 && g.lingyu.Grants.Count == 3,
                            "repeated imports move exactly one each with unique ledger ids");
                        // 同一材料：入库后脱离破境材料口径；炼丹恰消耗一份
                        check(g.DongtianStartPill("ninglu") && g.Dongtian.CountOf("shuiyue-lingsui") == 2, "brewing consumes the bag copy once");
                        g.cultivation.RealmIndex = 0; // 炼气视角：破境材料只认灵域库存，洞天袋内副本不算
                        check(g.BreakthroughMaterialCount == 0, "imported lingsui leaves the breakthrough material count");
                        g.lingyu.Materials["shuiyue-lingsui"] = 2;
                        check(g.BreakthroughMaterialCount == 2 && g.Dongtian.CountOf("shuiyue-lingsui") == 2,
                            "lingyu stock and cave bag stay separate ledgers");
                        g.cultivation.RealmIndex = 1; // 恢复筑基（洞天解锁门槛）
                        // 多次连续写盘失败（双档皆锁）：内存生效但明示待确认，绝不静默报成功
                        FileStream dtLock = new FileStream(g.DongtianWritePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                        FileStream lyLock = new FileStream(g.LingyuWritePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                        try
                        {
                            check(g.DongtianImport("shuiyue-lingsui") && g.DongtianPersistPending, "a failed write reports pending instead of silent success");
                            check(g.DongtianImport("shuiyue-lingsui") && g.DongtianPersistPending, "repeated failed writes stay pending");
                            g.TryFlushDongtianPersistence();
                            check(g.DongtianPersistPending, "the flush keeps waiting while the files stay locked");
                            check(g.Dongtian.CountOf("shuiyue-lingsui") == 4 && g.LingyuMaterialCount("shuiyue-lingsui") == 0,
                                "pending imports still apply in memory exactly once");
                        }
                        finally { dtLock.Dispose(); lyLock.Dispose(); }
                        g.TryFlushDongtianPersistence();
                        check(!g.DongtianPersistPending && DongtianProgress.Load(g.DongtianWritePath).CountOf("shuiyue-lingsui") == 4
                            && LingyuProgress.Load(g.LingyuWritePath).Grants.Count == 5,
                            "the retry commits every pending import once the files free up");
                        // 单档失败方向：只锁灵域档
                        g.lingyu.Materials["shuiyue-lingsui"] = 1;
                        FileStream lyLock2 = new FileStream(g.LingyuWritePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                        try { check(g.DongtianImport("shuiyue-lingsui") && g.DongtianPersistPending, "a lingyu-only write failure is also explicit"); }
                        finally { lyLock2.Dispose(); }
                        g.TryFlushDongtianPersistence();
                        check(!g.DongtianPersistPending, "the lingyu-side retry also commits");
                        // 操作入口同款语义：锁档开炉炼丹 → 待确认；解锁后提交成功
                        g.Dongtian.Furnace = null;
                        FileStream dtLock2 = new FileStream(g.DongtianWritePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                        try { check(g.DongtianStartPill("ninglu") && g.DongtianPersistPending && g.Dongtian.LastNotice.Contains("待落盘"), "brewing under a locked archive is marked pending too"); }
                        finally { dtLock2.Dispose(); }
                        g.TryFlushDongtianPersistence();
                        check(!g.DongtianPersistPending && g.Dongtian.Furnace != null, "the pending brew is committed after the retry");
                    }
                    finally
                    {
                        g.dongtianPathOverride = null; g.lingyuPathOverride = null;
                        try { Directory.Delete(dtFixture, true); } catch { }
                    }
                    check(g.connectivity == null && g.battle.Progress.FreedBytes == 0 && !Directory.Exists(dtFixture),
                        "growth simulation avoids real maintenance and removes its temporary archives");
                }
            }
            return "Growth integration: " + n + " checks passed (mortal tempering, awakening, breakthrough loop, combat linkage).";
        }
        // --perf-probe 运行器：隐藏宿主窗体 + 演示控制器（不写真实档案），
        // 合成一个忙碌战场（虚拟妖物 + 文件怪 + 双 Boss），按控制器当前节拍驱动 Tick，
        // 输出分模块 CPU 报表；随后附 30 秒静止场景（停战清场）对照自适应节拍与跳绘收益。
        // 测量与同机其他负载并存，由报告如实注明。
        private sealed class ProbeSnapshot
        {
            internal double Cpu, WorkingSetMb, PrivateMb; internal int Handles, Gdi, User; internal long Frames; internal int Ticks;
            internal static ProbeSnapshot Capture(AdventureController c, double cpuStart)
            {
                Process p = Process.GetCurrentProcess();
                return new ProbeSnapshot { Cpu = p.TotalProcessorTime.TotalSeconds - cpuStart, WorkingSetMb = p.WorkingSet64 / 1048576.0, PrivateMb = p.PrivateMemorySize64 / 1048576.0, Handles = p.HandleCount, Gdi = GetGuiResources(p.Handle, 0), User = GetGuiResources(p.Handle, 1), Frames = c.PresentedFrames };
            }
            internal string Describe()
            {
                return "CPU " + Cpu.ToString("0.0") + "s · 工作集 " + WorkingSetMb.ToString("0.0") + "MB · 私有 " + PrivateMb.ToString("0.0") + "MB · 句柄 " + Handles + " · GDI " + Gdi + " · USER " + User + " · 栅格化 " + Frames + " 帧 / " + Ticks + " 拍";
            }
        }
        internal static string RunPerfProbe(string assetDirectory, double minutes)
        {
            if (minutes <= 0 || minutes > 60) minutes = 3;
            using (Form owner = new Form())
            {
                IntPtr handle = owner.Handle;
                using (AdventureController c = new AdventureController(owner, assetDirectory, true))
                {
                    c.timer.Stop();
                    c.enabled = true; c.area = new Rectangle(-24000, -24000, 1600, 1000);
                    c.demoPending = false; c.demoStarted = -1;
                    c.cultivation.Mortal = false; c.cultivation.EnsureIdentity(new Random(7)); c.cultivation.Layer = 6;
                    Func<string, CleanupCandidate> file = delegate(string id) { return new CleanupCandidate { Path = @"C:\KnightAssistantSynthetic\" + id, Length = 4096, Created = 1, EligibleForCleanup = true }; };
                    Func<CleanupCandidate[], CleanupSnapshot> snap = delegate(CleanupCandidate[] f) { return new CleanupSnapshot { ObservedFilesAvailable = true, ObservedPathsAvailable = true, ObservedFiles = f.ToList(), ObservedPaths = new HashSet<string>(f.Select(x => x.Path), StringComparer.OrdinalIgnoreCase), InventoryComplete = true }; };
                    c.ApplyInventory(snap(new[] { file("p1"), file("p2"), file("p3") }), false);
                    for (int i = 0; i < 5; i++) { AdventureEnemy v = new AdventureEnemy { Id = "probe-fiend-" + i, IsVirtual = true, MonsterKind = i % 2 == 0 ? "imp" : "bat", Health = 90, MaxHealth = 90, X = c.x + 200 + i * 120, Y = c.y }; c.battle.Enemies.Add(v); c.Spawn(v); }
                    AdventureEnemy memBoss = EnemyBalance.MemoryBoss(c.battle.Progress, c.battle.Weapon, new Random(5)); c.battle.Enemies.Add(memBoss); c.Spawn(memBoss);
                    AdventureEnemy diskBoss = new AdventureEnemy { Id = "probe-disk", IsBoss = true, IsDiskBoss = true, Health = 700, MaxHealth = 700 }; c.battle.Enemies.Add(diskBoss); c.Spawn(diskBoss);
                    c.battle.SetAutomatic(true);
                    PerfProbe.Reset(); PerfProbe.Enabled = true; c.PresentedFrames = 0;
                    Stopwatch wall = Stopwatch.StartNew();
                    double cpuStart = Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds;
                    double end = wall.Elapsed.TotalSeconds + minutes * 60;
                    int fiendSeq = 5; double nextTopUp = 0; int busyTicks = 0;
                    while (wall.Elapsed.TotalSeconds < end)
                    {
                        if (wall.Elapsed.TotalSeconds >= nextTopUp)
                        {
                            nextTopUp = wall.Elapsed.TotalSeconds + 5;
                            int alive = c.battle.Enemies.Count(e => e.IsVirtual && !e.Defeated);
                            for (int i = alive; i < 5 && EnemyFieldBudget.CanAddOrdinary(c.battle.Enemies); i++)
                            {
                                AdventureEnemy v = new AdventureEnemy { Id = "probe-fiend-" + (fiendSeq++), IsVirtual = true, MonsterKind = "imp", Health = 90, MaxHealth = 90, X = c.x + 300, Y = c.y };
                                c.battle.Enemies.Add(v); c.Spawn(v);
                            }
                        }
                        c.Tick(null, EventArgs.Empty); busyTicks++;
                        Application.DoEvents();
                        Thread.Sleep(c.CurrentTickInterval);
                    }
                    double busyWall = wall.Elapsed.TotalSeconds;
                    ProbeSnapshot busySnap = ProbeSnapshot.Capture(c, cpuStart); busySnap.Ticks = busyTicks;
                    int busyActors = c.battle.Enemies.Count, busyInterval = c.CurrentTickInterval;
                    string busyReport = PerfProbe.Report(busyWall, busySnap.Cpu);
                    // —— 静止场景：停战清场、冻结主角移动，量 30 秒自适应节拍与跳绘 ——
                    c.battle.SetAutomatic(false); c.battle.CancelQueue(); c.battle.Enemies.Clear();
                    c.pauseMotionUntil = c.clock.Elapsed.TotalSeconds + 9999;
                    PerfProbe.Reset();
                    long framesBefore = c.PresentedFrames; double calmStart = wall.Elapsed.TotalSeconds; int calmTicks = 0;
                    while (wall.Elapsed.TotalSeconds < calmStart + 30)
                    {
                        c.Tick(null, EventArgs.Empty); calmTicks++;
                        Application.DoEvents();
                        Thread.Sleep(c.CurrentTickInterval);
                    }
                    double calmWall = wall.Elapsed.TotalSeconds - calmStart;
                    ProbeSnapshot calmSnap = ProbeSnapshot.Capture(c, cpuStart); calmSnap.Ticks = calmTicks; calmSnap.Frames = c.PresentedFrames - framesBefore;
                    PerfProbe.Enabled = false;
                    string calmReport = PerfProbe.Report(calmWall, calmSnap.Cpu - busySnap.Cpu);
                    return "== 忙碌战场（自动作战）==\r\n" + busyReport
                        + "资源: " + busySnap.Describe() + " · 节拍 " + busyInterval + "ms\r\n"
                        + "战场演员: " + busyActors + "   英雄等级 Lv." + c.battle.Progress.Level
                        + "\r\n\r\n== 静止场景（停战清场 · 自适应节拍）==\r\n" + calmReport
                        + "资源: " + calmSnap.Describe() + " · 节拍 " + c.CurrentTickInterval + "ms\r\n"
                        + "\r\n说明: 演示模式不写真实档案；cultivation/baiye 结算在演示模式跳过（近于零），正常模式为周期性轻量运算。静止场景期间若环境妖物自然刷新，计入真实负载。";
                }
            }
        }
        private static string LiveSmoke(string assetDirectory)
        {
            int n=0; Action<bool,string> check=delegate(bool ok,string label) { if(!ok) throw new Exception("Live field: "+label); n++; };
            Func<string,long,bool,CleanupCandidate> file=delegate(string id,long len,bool safe) { return new CleanupCandidate {Path=@"C:\KnightAssistantSynthetic\"+id,Length=len,Created=1,EligibleForCleanup=safe}; };
            Func<CleanupCandidate[],CleanupSnapshot> snap=delegate(CleanupCandidate[] f) { return new CleanupSnapshot {ObservedFilesAvailable=true,ObservedPathsAvailable=true,ObservedFiles=f.ToList(),ObservedPaths=new HashSet<string>(f.Select(x=>x.Path),StringComparer.OrdinalIgnoreCase),InventoryComplete=true}; };
            using(Form owner=new Form())
            {
                IntPtr handle=owner.Handle;
                using(AdventureController c=new AdventureController(owner,assetDirectory,true))
                {
                    c.timer.Stop(); c.enabled=true; c.area=new Rectangle(-24000,-24000,1600,1000); c.PositionLandmark(); c.x=-23600; c.y=-23500;
                    CleanupCandidate a=file("a",4096,true), b=file("new",0,false);
                    c.ApplyInventory(snap(new[]{a}),false); AdventureEnemy first=c.battle.Enemies.Single();
                    check(c.candidateBytes==4096 && c.liveTemp.EligibleFiles==1,"live processable count and bytes");
                    int oldMax=first.MaxHealth; first.Health=oldMax/2; c.battle.Progress.Level=5; c.ScaleOrdinaryEnemies();
                    check(first.MaxHealth>oldMax && first.Health<first.MaxHealth*.52 && first.Health>first.MaxHealth*.48,"living ordinary HP scales with hero while preserving damage ratio");
                    c.battle.Authorize(first); c.ApplyInventory(snap(new[]{a,b}),false);
                    check(c.battle.Enemies.Count==2 && c.battle.Enemies.Contains(first) && first.Authorized,"new zero-byte file spawns during an authorized queue");
                    check(c.candidateBytes==4096 && c.liveTemp.TotalFiles==2,"recent file not mislabeled as safe cleanup");
                    c.ApplyInventory(snap(new[]{a,b}),false); check(c.battle.Enemies.Count==2,"unchanged notifications never duplicate actors");
                    c.battle.CancelQueue(); c.ApplyInventory(snap(new[]{b}),false);
                    check(c.battle.Enemies.Count==1 && c.candidateBytes==0,"external deletion immediately updates castle and retires unselected actor");
                    c.simulatedCleaner=delegate(CleanupBatch batch,CancellationToken token) { return Task.FromResult(new CleanupResult {SkippedFiles=batch.FilesCount,RemainingCandidates=batch.Candidates.ToList(),Message="recent: retained"}); };
                    AdventureEnemy recent=c.battle.Enemies.Single(); c.battle.Authorize(recent); recent.Health=1; c.battle.Cooldown=0; c.battle.Strike(recent); c.BeginCleanup(recent); c.DrainCleanup(); c.battle.Tick(.31); c.battle.Enemies.Remove(recent);
                    c.ApplyInventory(snap(new[]{b}),false); check(c.battle.Enemies.Count==0 && c.recheckFiles==1,"killed recent file stays pending and never respawns unchanged");
                    CleanupCandidate grown=file("new",1,false); c.ApplyInventory(snap(new[]{grown}),false); check(c.battle.Enemies.Count==1,"one-byte growth immediately creates new actor");
                    c.battle.Enemies.Clear(); CleanupCandidate[] many=Enumerable.Range(0,200).Select(i=>file("many"+i,1024,true)).ToArray(); c.ApplyInventory(snap(many),true);
                    check(c.battle.Enemies.Count==9 && c.candidateBytes==200*1024,"nine ordinary slots without truncating live totals");
                    AdventureEnemy boss=EnemyBalance.MemoryBoss(c.battle.Progress,c.battle.Weapon,new Random(5)); c.Spawn(boss); c.battle.Enemies.Add(boss);
                    AdventureEnemy disk=new AdventureEnemy {Id="warning",IsBoss=true,IsDiskBoss=true,Health=700,MaxHealth=700}; c.Spawn(disk); c.battle.Enemies.Add(disk); c.RenderWorld(true);
                    check(c.enemyWindows.Count==11 && EnemyFieldBudget.IsWithinBudget(c.battle.Enemies),"eleven actors including both bosses");
                    List<AdventureEnemy> clear=c.battle.ClearFieldSnapshot(); check(!clear.Contains(boss) && !boss.Defeated && boss.Health==boss.MaxHealth,"memory boss cannot be bypassed by one-click clear");
                    foreach(AdventureEnemy target in clear) { target.Defeated=true; target.Cleaning=false; }
                    c.battle.CancelQueue(); c.battle.Enemies.RemoveAll(e=>!e.IsBoss);
                    // B4/PR#3 回归：蜃楼镜妖（含主镜 Boss 战）免疫一键清场——秘境战斗必须真实分出胜负。
                    c.battle.Enemies.Clear(); c.CancelPending();
                    c.LingyuCombatRequested("主镜蜃主", true);
                    AdventureEnemy mirrorBoss=c.battle.Enemies.Single(e=>e.Id.StartsWith("lingyu-",StringComparison.Ordinal));
                    AdventureEnemy stray=new AdventureEnemy {Id="stray",IsVirtual=true,MonsterKind="bat",Health=50,MaxHealth=50}; c.Spawn(stray); c.battle.Enemies.Add(stray);
                    List<AdventureEnemy> sweep=c.battle.ClearFieldSnapshot();
                    check(sweep.Contains(stray) && !sweep.Contains(mirrorBoss) && mirrorBoss.Health==mirrorBoss.MaxHealth && !mirrorBoss.Cleaning,
                        "the mirage boss never falls to one-click clear");
                    foreach(AdventureEnemy t2 in sweep) { t2.Defeated=true; t2.Cleaning=false; }
                    c.battle.CancelQueue(); c.battle.Enemies.Clear();
                    // B4/PR#3 回归：满员时镜妖仍能现身（退休闲置普通妖腾名额），且不突破战场预算。
                    for(int fill=0;fill<9;fill++){ AdventureEnemy filler=new AdventureEnemy {Id="fill-"+fill,IsVirtual=true,MonsterKind="bat",Health=50,MaxHealth=50}; c.battle.Enemies.Add(filler); }
                    check(!EnemyFieldBudget.CanAddOrdinary(c.battle.Enemies),"nine ordinary fill the field");
                    c.LingyuCombatRequested("镜妖", false);
                    check(c.battle.Enemies.Count(e2=>e2.Id.StartsWith("lingyu-",StringComparison.Ordinal))==1
                        && c.battle.Enemies.Count==9 && EnemyFieldBudget.IsWithinBudget(c.battle.Enemies),
                        "a full field retires one idle ordinary to admit the mirror fiend within budget");
                    c.LingyuCombatRequested("镜妖·再战", false);
                    check(c.battle.Enemies.Count(e2=>e2.Id.StartsWith("lingyu-",StringComparison.Ordinal))==1
                        && c.battle.Enemies.Count==9 && EnemyFieldBudget.IsWithinBudget(c.battle.Enemies),
                        "the next mirror fiend replaces the former one within budget");
                    c.battle.Enemies.Clear(); c.pendingLingyuEnemyId=null;
                    int calls=0; c.simulatedMemoryReclaimer=delegate(CancellationToken token) { calls++; return Task.FromResult(new MemoryReclaimResult {Before=new MemorySnapshot{Available=true,UsedPercent=75},After=new MemorySnapshot{Available=true,UsedPercent=73},Trimmed=2,WorkingSetReduction=1024*1024}); };
                    c.battle.Authorize(boss); boss.Health=1; c.battle.Cooldown=0; c.battle.Strike(boss); c.BeginCleanup(boss); c.BeginCleanup(boss); c.DrainMemoryReclaim();
                    check(calls==1 && boss.Defeated && c.memoryPercent==73 && c.battle.LastReward.Contains("75.0% → 73.0%"),"boss death triggers one measured reclaim and cannot duplicate it");
                    check(c.battle.Progress.FreedBytes==0 && c.connectivity==null && c.tempWatcher==null,"memory tests never fake disk bytes or open actual services");
                    c.battle.Enemies.Clear();
                    AdventureEnemy mixed=new AdventureEnemy {Id="mixed",Batch=new CleanupBatch(new List<CleanupCandidate>{a,b},1),Health=100,MaxHealth=100};
                    disk=new AdventureEnemy {Id="mixed-disk",IsBoss=true,IsDiskBoss=true,Health=700,MaxHealth=700};c.battle.Enemies.Add(mixed);c.battle.Enemies.Add(disk);c.BindDiskBossCandidates();
                    check(disk.Batch.FilesCount==1 && disk.Batch.Candidates[0]==a && mixed.Batch.FilesCount==1 && mixed.Batch.Candidates[0]==b,"disk boss takes only safe subset without losing recent-file actor ownership");
                    string fixtureRoot=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"watcher-fixture-"+Guid.NewGuid().ToString("N"));
                    string fixtureFile=Path.Combine(fixtureRoot,"created.tmp"); Directory.CreateDirectory(fixtureRoot);
                    try
                    {
                        c.InstallTempWatcher(fixtureRoot); int version=c.tempChangeVersion; File.WriteAllText(fixtureFile,"new file");
                        Stopwatch wait=Stopwatch.StartNew(); while(c.tempChangeVersion==version && wait.ElapsedMilliseconds<3000) Thread.Sleep(10);
                        check(c.tempChangeVersion!=version,"native creation notification reaches the same immediate-scan trigger");
                        version=c.tempChangeVersion; File.AppendAllText(fixtureFile,"growth"); wait.Restart(); while(c.tempChangeVersion==version && wait.ElapsedMilliseconds<3000)Thread.Sleep(10);
                        check(c.tempChangeVersion!=version,"native growth notification reaches immediate-scan trigger");
                    }
                    finally { if(c.tempWatcher!=null){c.tempWatcher.Dispose();c.tempWatcher=null;} if(File.Exists(fixtureFile))File.Delete(fixtureFile);Directory.Delete(fixtureRoot,false); }
                }
            }
            return "Live field integration: "+n+" checks passed (synthetic files and memory reclaim).";
        }
        private static string TacticsSmoke(string assetDirectory)
        {
            int checks = 0;
            Action<bool, string> check = delegate(bool okay, string label) { if (!okay) throw new InvalidOperationException("Automatic combat integration: " + label); checks++; };
            using (Form owner = new Form())
            {
                IntPtr handle = owner.Handle;
                using (AdventureController c = new AdventureController(owner, assetDirectory, true))
                {
                    c.timer.Stop(); c.area = new Rectangle(-24000, -24000, 1600, 1000);
                    foreach (AdventureWeapon weapon in AdventureWeapon.All)
                    {
                        c.battle.CancelQueue(); c.battle.Enemies.Clear(); c.combatTactics.Reset(); c.effects.Clear();
                        c.battle.Progress.WeaponId = weapon.Id; c.battle.Progress.Health = c.battle.Progress.MaxHealth;
                        c.battle.Cooldown = 0; c.x = -23700; c.y = -23550; c.ClampHero();
                        AdventureEnemy target = new AdventureEnemy { Id = "tactics-" + weapon.Id, MonsterKind = "skeleton", Health = 100000, MaxHealth = 100000 };
                        target.X = c.x + c.HeroSize.Width / 2.0 + 60 - c.EnemySize(target).Width / 2.0;
                        target.Y = c.y + c.HeroSize.Height - c.EnemySize(target).Height;
                        AdventureEnemy bystander = new AdventureEnemy { Id = "not-selected", MonsterKind = "slime", Health = 200, MaxHealth = 200, X = target.X + 25, Y = target.Y };
                        c.battle.Enemies.Add(target); c.battle.Enemies.Add(bystander); c.battle.Authorize(target); c.battle.Automatic = true;
                        c.AdvanceAutomaticCombat(target, 0, .05);
                        check(target.Health < 100000 && c.battle.Cooldown > 0, weapon.Id + " attacks from valid feet-anchor range");
                        double before = c.EnemyHeroDistance(target); c.AdvanceAutomaticCombat(target, .05, .05);
                        check(c.EnemyHeroDistance(target) > before && c.tacticalStatus == "拉开距离", weapon.Id + " opens space during cooldown");
                        int hits = 1, priorHealth = target.Health;
                        for (int frame = 1; frame <= 600 && hits < 3; frame++)
                        {
                            c.battle.Tick(.05); c.AdvanceEnemyPatrol(target, true, .05); c.AdvanceAutomaticCombat(target, (frame + 1) * .05, .05);
                            if (target.Health < priorHealth) hits++;
                            priorHealth = target.Health;
                        }
                        check(hits >= 3 && bystander.Health == 200 && !bystander.Authorized && c.cleanupTask == null, weapon.Id + " repeatedly counterattacks without authorizing bystander cleanup");
                    }
                    check(c.connectivity == null && !c.scanBusy && c.battle.Progress.FreedBytes == 0, "tactics simulation cannot touch network, files, or saved progress");
                }
            }
            return "Automatic combat integration: " + checks + " checks passed (seven weapons, synthetic enemies, no real cleanup or progress writes).";
        }
        public static void ExportEquipmentPreview(string assetDirectory, string output)
        {
            using (Form owner = new Form())
            {
                IntPtr ownerHandle = owner.Handle;
                using (AdventureController c = new AdventureController(owner, assetDirectory, true))
                {
                    c.battle.Progress.Level = 7; c.battle.Progress.Health = 150; c.battle.Progress.Experience = 62; c.battle.Progress.Score = 2840; c.battle.Progress.FreedBytes = 1375731712;
                    c.battle.Progress.Inventory = AdventureWeapon.All.Select(w => w.Id).ToList(); c.battle.Progress.WeaponId = "blackhole";
                    c.battle.LastReward = "预览示例：获得「星渊黑洞」· 不读取或清理真实文件";
                    // 成长体系预览画面：炼气十二层圆满、破境契机已至的修士（示范档案开局是凡人，需先觉醒）。
                    c.cultivation.Mortal = false; c.cultivation.EnsureIdentity(new Random(4));
                    c.cultivation.Layer = 12; c.cultivation.Qi = 0; c.cultivation.BreakthroughInsight = 500;
                    c.cultivation.GrantOpportunity("insight");
                    c.cultivation.LastNotice = "预览示例：炼气圆满，破境感悟 500 · 契机已至";
                    c.memoryAvailable = true; c.memoryPercent = 47.2; c.diskSnapshot = DiskSpaceSnapshot.FromValues(256L * 1024 * 1024 * 1024, 24L * 1024 * 1024 * 1024); c.scanStatus = "预览示例数据；真实临时文件只在正常启动后扫描。";
                    using (AdventureEquipmentForm panel = new AdventureEquipmentForm(c))
                    using (Bitmap capture = new Bitmap(panel.Width, panel.Height))
                    {
                        panel.StartPosition = FormStartPosition.Manual; panel.Location = new Point(-24000, -24000); panel.Show(); Application.DoEvents(); panel.RefreshData();
                        panel.DrawToBitmap(capture, new Rectangle(Point.Empty, panel.Size)); capture.Save(output, ImageFormat.Png);
                    }
                }
            }
        }
        // 百业道碑预览：全部使用合成数据，不读真实存档、不接触系统探针。
        public static void ExportBaiyeStelePreview(string assetDirectory, string output)
        {
            using (Form owner = new Form())
            {
                IntPtr ownerHandle = owner.Handle;
                using (AdventureController c = new AdventureController(owner, assetDirectory, true))
                {
                    DateTime stamp = new DateTime(2026, 10, 17, 6, 30, 0, DateTimeKind.Utc);
                    c.baiye.FirstRunExplained = true; c.baiye.Mode = 2; c.baiye.ManualMarkId = "manual-youli-learn";
                    c.baiye.AddYun("zhen", 186, stamp); c.baiye.AddYun("tianji", 132, stamp);
                    c.baiye.AddYun("wen", 64, stamp); c.baiye.AddYun("youli", 26, stamp);
                    c.baiye.AddLog("zhen", 6.0, "自动识别", stamp.AddMinutes(-42));
                    c.baiye.AddLog("tianji", 4.5, "自动识别", stamp.AddMinutes(-30));
                    c.baiye.AddLog("youli", 3.2, "手动标记", stamp.AddMinutes(-12));
                    BaiyeEncounters.Evaluate(c.baiye, stamp);
                    c.baiyeNotice = "机缘 · 天机演算：阵法与天机两道交相印证，悟得阵图「天机阵图」（修为 +120）";
                    c.baiyeTracker.ForegroundProcessName = delegate { return "code"; };
                    c.baiyeTracker.IdleSeconds = delegate { return 0; };
                    c.TickBaiyeForTest(3);
                    using (BaiyeSteleForm panel = new BaiyeSteleForm(c))
                    using (Bitmap capture = new Bitmap(panel.Width, panel.Height))
                    {
                        panel.StartPosition = FormStartPosition.Manual; panel.Location = new Point(-24000, -24000); panel.Show(); Application.DoEvents(); panel.RefreshData();
                        panel.DrawToBitmap(capture, new Rectangle(Point.Empty, panel.Size)); capture.Save(output, ImageFormat.Png);
                    }
                }
            }
        }
        // 灵域历练预览：全部使用合成数据，不读真实存档、不接触系统探针。
        public static void ExportLingyuStelePreview(string assetDirectory, string output)
        {
            using (Form owner = new Form())
            {
                IntPtr ownerHandle = owner.Handle;
                using (AdventureController c = new AdventureController(owner, assetDirectory, true))
                {
                    DateTime stamp = new DateTime(2026, 10, 17, 7, 20, 0, DateTimeKind.Utc);
                    c.baiye.AddYun("youli", 96, stamp); // 游历道 L1 → 蜃楼可进入
                    c.lingyu.Materials["jinghua-shuiyue"] = 5; c.lingyu.Materials["jinghua"] = 2;
                    c.lingyu.LogLine("踏入千面蜃楼 · 镜湖渡口");
                    c.lingyu.LogLine("来到镜廊深处");
                    c.lingyu.LogLine("开启镜像宝箱：镜花水月 ×2 · 修为 +12");
                    c.lingyu.LogLine("镜妖碎作流光（修为 +40 · 镜花水月 ×1）");
                    c.lingyu.LogLine("来到回声壁");
                    // 合成「回声壁机缘待定」画面：Enter 保留已存节点并评估机缘。
                    // 成长体系：示范档案开局是凡人，灵域门控面向修士，先完成觉醒。
                    c.cultivation.Mortal = false; c.cultivation.EnsureIdentity(new Random(4));
                    c.lingyu.ExpeditionActive = true; c.lingyu.ExpeditionNode = "echo"; c.lingyu.ExpeditionDepth = 2;
                    c.LingyuEnter("qianmian");
                    using (LingyuForm panel = new LingyuForm(c))
                    using (Bitmap capture = new Bitmap(panel.Width, panel.Height))
                    {
                        panel.StartPosition = FormStartPosition.Manual; panel.Location = new Point(-24000, -24000); panel.Show(); Application.DoEvents(); panel.RefreshData();
                        panel.DrawToBitmap(capture, new Rectangle(Point.Empty, panel.Size)); capture.Save(output, ImageFormat.Png);
                    }
                }
            }
        }
        // 破境仪式预览：全部使用合成数据，不读真实存档、不接触系统探针。
        public static void ExportBreakthroughPreview(string assetDirectory, string output)
        {
            using (Form owner = new Form())
            {
                IntPtr ownerHandle = owner.Handle;
                using (AdventureController c = new AdventureController(owner, assetDirectory, true))
                {
                    // 炼气十二层圆满、三契机在手、灵髓有库存的可破境画面。
                    c.cultivation.Mortal = false; c.cultivation.EnsureIdentity(new Random(4));
                    c.cultivation.Layer = 12; c.cultivation.Qi = 0; c.cultivation.BreakthroughInsight = 500;
                    c.cultivation.GrantOpportunity("insight"); c.cultivation.GrantOpportunity("realm"); c.cultivation.GrantOpportunity("treasure");
                    c.lingyu.Materials["shuiyue-lingsui"] = 1;
                    using (BreakthroughForm panel = new BreakthroughForm(c))
                    using (Bitmap capture = new Bitmap(panel.Width, panel.Height))
                    {
                        panel.StartPosition = FormStartPosition.Manual; panel.Location = new Point(-24000, -24000); panel.Show(); Application.DoEvents(); panel.RefreshData();
                        panel.DrawToBitmap(capture, new Rectangle(Point.Empty, panel.Size)); capture.Save(output, ImageFormat.Png);
                    }
                }
            }
        }
        public static void ExportPreview(string assetDirectory, string output)
        {
            using (AdventureRenderer r = new AdventureRenderer(assetDirectory))
            using (Bitmap canvas = new Bitmap(1280, 760))
            using (Graphics g = Graphics.FromImage(canvas))
            using (Font title = new Font("Microsoft YaHei UI", 27, FontStyle.Bold))
            using (Font body = new Font("Microsoft YaHei UI", 13))
            using (Brush ink = new SolidBrush(Color.FromArgb(236, 232, 255)))
            {
                g.Clear(Color.FromArgb(24, 25, 43)); g.DrawString("骑士助手 · 桌面冒险", title, ink, 36, 28);
                g.DrawString("精致 Q 版角色 / 五种随机怪物 / 自动规划 / 一键清场", body, ink, 40, 84);
                using (Bitmap pic = r.Render(new AdventureRenderState { Kind = "Landmark", Size = new Size(250, 280), ThemeCode = "OTHER", Time = 1, Label = "恶魔古堡", Opacity = 1 })) g.DrawImageUnscaled(pic, 30, 230);
                using (Bitmap pic = r.Render(new AdventureRenderState { Kind = "Hero", Size = new Size(300, 320), ThemeCode = "OTHER", Phase = "attack", WeaponId = "laser", Time = 1.2, MoveSpeed = 400, FacingRight = true, Level = 7, HealthRatio = .82, Label = "Lv.7 · 极光镭射枪", GlowProgress = .6, AttackProgress = .5, Opacity = 1 })) g.DrawImageUnscaled(pic, 290, 225);
                for (int i = 0; i < 6; i++) using (Bitmap pic = r.Render(new AdventureRenderState { Kind = i == 5 ? "Boss" : "Skeleton", MonsterKind = i == 5 ? "boss" : AdventureMonster.All[i].Id, Size = new Size(165, 195), ThemeCode = "OTHER", Phase = "run", Time = i * .6, FacingRight = false, HealthRatio = .7, QueueNumber = i + 1, Label = i == 5 ? "内存压力 Boss" : AdventureMonster.All[i].Name, Opacity = 1 })) g.DrawImageUnscaled(pic, 650 + (i % 3) * 190, 150 + (i / 3) * 205);
                g.DrawString("手动点选按点击顺序出击；自动规划综合距离、剩余血量与威胁，手动目标优先。", body, ink, 42, 604);
                g.DrawString("一键清场：锁定当前一批怪物，逐批清理临时文件；可随时取消。右键打开装备栏。", body, ink, 42, 642);
                g.DrawString("示意画面；临时文件占用的是磁盘空间，内存压力 Boss 不会强制清空 RAM。", body, ink, 42, 695);
                canvas.Save(output, ImageFormat.Png);
            }
        }
        // == --mortal-gui-accept：凡人主动战斗真实 GUI 验收（独立验收模式，非产品入口） ==
        // 与 --selftest/--adventure-smoke 同族：真实生产控制器、真实窗口与消息循环、
        // 本账户真实存档（验收以临时标准用户运行，该账户档案天然隔离，绝不动真实用户档案）。
        // 驱动方式：向真实敌人/角色窗口投递真实鼠标消息（与玩家点击同一事件链路），
        // 实时完成：选敌 → 出招 → 格挡 → 力竭调息 → 十次有效击杀 → 觉醒 → 自动作战证明。
        // 仅两处如实标注的加速：妖物自然生成过慢时触发与生产相同的 encounterDue 生成
        // 路径；以及一次强制力竭（验证调息恢复）。其余全部实时真实操作。
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr FindWindow(string className, string windowName);
        private const int WmLButtonDown = 0x201, WmLButtonUp = 0x202, WmKeyDown = 0x100;
        internal static int MortalGuiAccept(string assetDirectory, string logPath)
        {
            string shotDir = Path.Combine(Path.GetDirectoryName(logPath), "mortal-gui-shots");
            Directory.CreateDirectory(shotDir);
            StringBuilder log = new StringBuilder();
            List<string> failures = new List<string>();
            Action<string> step = delegate(string line) { log.AppendLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line); };
            Action<bool, string> expect = delegate(bool ok, string label)
            {
                step((ok ? "PASS  " : "FAIL  ") + label);
                if (!ok) failures.Add(label);
            };
            Action<Form, string> shot = delegate(Form w, string name)
            {
                try
                {
                    using (Bitmap b = DesktopFrameCapture.CaptureWindow(w))
                    {
                        b.Save(Path.Combine(shotDir, name + ".png"), ImageFormat.Png);
                        step("SHOT   " + name + " · desktop composition " + b.Width + "x" + b.Height);
                    }
                }
                catch (Exception e) { expect(false, "画面采集失败 " + name + "：" + e.Message); }
            };
            int exitCode;
            using (Form owner = new Form())
            {
                IntPtr ownerHandle = owner.Handle;
                using (AdventureController c = new AdventureController(owner, assetDirectory)) // 生产模式：真实存档（本账户隔离）
                {
                    c.enabled = true; // 与 MainForm 显示桌宠后的状态一致；否则 Tick 早退、世界不运转
                    expect(c.cultivation.Mortal && c.cultivation.MortalKills == 0 && !c.cultivation.CanAwaken, "开局为全新凡人档（0 次历练）");
                    expect(!c.cultivation.AutoBattleUnlocked && !c.battle.Automatic, "凡人期自动作战锁定且未开启");
                    expect(!c.consent.CleanupEnabled && !c.consent.MemoryEnabled && !c.consent.AutoMaintenanceEnabled, "真实文件清理/内存维护/无人值守自动维护授权全部关闭");
                    step("INFO   存档目录（隔离）：" + Path.GetDirectoryName(c.cultivationPath));

                    int clicksTotal = 0;
                    Action<Form> clickWindow = delegate(Form w)
                    {
                        if (w == null || w.IsDisposed) return;
                        IntPtr h = w.Handle;
                        int cx = Math.Max(1, w.Width / 2), cy = Math.Max(1, w.Height / 2);
                        IntPtr l = (IntPtr)((cy << 16) | (cx & 0xFFFF));
                        PostMessage(h, WmLButtonDown, (IntPtr)1, l); PostMessage(h, WmLButtonUp, IntPtr.Zero, l);
                        clicksTotal++;
                    };
                    Func<Control, string, Button> findButton = null;
                    findButton = delegate(Control root, string text)
                    {
                        if (root == null) return null;
                        Button self = root as Button;
                        if (self != null && self.Text == text) return self;
                        foreach (Control child in root.Controls) { Button found = findButton(child, text); if (found != null) return found; }
                        return null;
                    };

                    Stopwatch wall = Stopwatch.StartNew();
                    int phase = 0; // 0=手动十战 1=觉醒仪式 2=自动作战证明 3=完成
                    AdventureEnemy target = null;
                    string engagedId = null;
                    int strikesOnTarget = 0, distanceHints = 0, firstKillDistanceHints = -1, firstKillStrikes = -1;
                    double lastClickAt = -9, noTargetSince = -1, phase2Start = 0, phase2Exp = 0, lastTelemetryFeed = -9;
                    bool blockTested = false, deadLogged = false, exhaustTested = false, awakenClicked = false, feedChecked = false, sawRestNotice = false;
                    System.Windows.Forms.Timer driver = new System.Windows.Forms.Timer { Interval = 100 };
                    driver.Tick += delegate
                    {
                        if (failures.Count > 0 || phase == 3) return;
                        // 合成遥测喂给：生产中由 MainForm 的网络引擎调用 UpdateTelemetry 保持骑士存活；
                        // 验收不触网络，以恒定喂给代替（6 秒新鲜度窗口内每 2 秒一喂）。
                        if (wall.Elapsed.TotalSeconds - lastTelemetryFeed > 2)
                        {
                            lastTelemetryFeed = wall.Elapsed.TotalSeconds;
                            c.UpdateTelemetry(2048, true);
                        }
                        if (!feedChecked && wall.Elapsed.TotalSeconds > 6)
                        {
                            feedChecked = true;
                            expect(c.networkState.Phase != KnightPhase.Dying && c.networkState.Phase != KnightPhase.Grave && c.networkState.Phase != KnightPhase.Revive,
                                "遥测喂给保持骑士网络状态存活（生产由 MainForm 网络引擎喂给）");
                        }
                        // 调息文案尽力捕获：LastReward 会被世界节拍覆写（与格挡同款设计），
                        // 逐字确定性断言在 Growth 烟测，这里每拍采样只为留下玩家可见反馈的证据。
                        if (!sawRestNotice && c.battle.LastReward != null && c.battle.LastReward.Contains("凡躯调息完毕"))
                        {
                            sawRestNotice = true;
                            step("REST   玩家可见反馈：" + c.battle.LastReward);
                        }
                        if (phase == 0)
                        {
                            // 力竭/调息状态：静候生产自动调息链路，绝不动用复活按钮之外的手段
                            if (c.battle.Progress.Dead)
                            {
                                if (!deadLogged)
                                {
                                    deadLogged = true;
                                    expect(!c.battle.Automatic, "力竭后凡人不会转入自动作战");
                                    step("DOWN   凡人力竭，等待自动调息（约 60 秒）…");
                                    shot(c.heroWindow, "03-力竭调息");
                                }
                                return;
                            }
                            if (deadLogged)
                            {
                                deadLogged = false;
                                expect(!c.battle.Automatic, "调息恢复后凡人仍处于手动模式（不自动接续作战）");
                                expect(c.battle.DeathTime < 1, "调息恢复是全新的复活（死亡计时已归零，非他径清除死亡态）");
                                // 调息文案逐字断言见 Growth 烟测；GUI 侧 LastReward 会被世界节拍覆写属设计，尽力捕获。
                                step("UP     调息完毕，等待玩家再次操作（可见文案捕获：" + (sawRestNotice ? "已见「凡躯调息完毕」" : "被世界节拍覆写，以烟测逐字断言为准") + "）");
                                sawRestNotice = false;
                            }
                            if (c.cultivation.MortalKills >= CultivationBalance.MortalAwakenKills)
                            {
                                expect(c.cultivation.CanAwaken, "十次有效击杀达成，觉醒条件解锁");
                                step("AWAKEN 十战圆满（共点击 " + clicksTotal + " 次，首杀出招 " + firstKillStrikes + " 次/距离提示 " + firstKillDistanceHints + " 次），打开修炼面板…");
                                c.OpenEquipment();
                                phase = 1; return;
                            }
                            if (target == null || target.VisualDefeated || target.Defeated)
                            {
                                if (target != null && target.VisualDefeated && engagedId == target.Id)
                                {
                                    // 误触验证：点击已倒下的妖物窗口不产生任何效果
                                    Form deadWin; int killsBefore = c.cultivation.MortalKills;
                                    KnightLayeredWindow deadLayered;
                                    if (c.enemyWindows.TryGetValue(target.Id, out deadLayered)) { deadWin = deadLayered; clickWindow(deadWin); Application.DoEvents(); }
                                    expect(c.cultivation.MortalKills == killsBefore, "误触验证：点击已倒下妖物不产生击杀或出招");
                                    step("KILL   第 " + c.cultivation.MortalKills + " 次有效击杀（本目标出招 " + strikesOnTarget + " 次）");
                                    if (c.cultivation.MortalKills == 1) { firstKillStrikes = strikesOnTarget; firstKillDistanceHints = distanceHints; }
                                    engagedId = null;
                                }
                                target = c.battle.Enemies.FirstOrDefault(e => !e.IsBoss && !e.Defeated && !e.VisualDefeated && !e.Failed);
                                strikesOnTarget = 0;
                                if (target == null)
                                {
                                    if (noTargetSince < 0) noTargetSince = wall.Elapsed.TotalSeconds;
                                    if (wall.Elapsed.TotalSeconds - noTargetSince > 45)
                                    {
                                        noTargetSince = wall.Elapsed.TotalSeconds;
                                        c.encounterDue = true; // 加速点①：触发与生产完全相同的虚拟妖物生成路径
                                        step("ACCEL  45 秒无可战妖物，走生产同款 encounterDue 生成路径");
                                    }
                                    return;
                                }
                                noTargetSince = -1;
                                engagedId = target.Id;
                                step("TARGET 锁定 " + AdventureMonster.Find(target.MonsterKind).Name + "（HP " + target.Health + "/" + target.MaxHealth + "，距离 " + ((int)c.EnemyHeroDistance(target)) + "）");
                            }
                            // 格挡验证：首次接战时完成（状态断言为准——LastReward 会被世界节拍
                            // 周期性覆写，属设计；点击瞬间的文案尽力捕获并记入日志）
                            if (!blockTested)
                            {
                                blockTested = true;
                                double blockNow = c.clock.Elapsed.TotalSeconds;
                                clickWindow(c.heroWindow); Application.DoEvents();
                                string blockText = c.battle.LastReward;
                                expect(c.blockUntil > blockNow && c.blockCooldownUntil > blockNow,
                                    "点击角色触发格挡：减伤窗口与冷却倒计时同时置位" + (blockText.Contains("格挡！") ? "，文案「格挡！」已捕获" : "（状态断言为准）"));
                                if (blockText.Contains("格挡！")) step("BLOCK  玩家可见反馈：" + blockText);
                                double cooldownAt = c.blockCooldownUntil, untilAt = c.blockUntil;
                                clickWindow(c.heroWindow); Application.DoEvents();
                                double cooldownAfter = c.blockCooldownUntil;
                                step("BLOCK  冷却读数：首次 " + cooldownAt.ToString("F2") + " · 再次点击后 " + cooldownAfter.ToString("F2") + " · 格挡窗 " + untilAt.ToString("F2"));
                                expect(cooldownAfter <= cooldownAt + 0.001 || c.battle.LastReward.Contains("格挡"),
                                    "冷却期再次点击：要么倒计时不变，要么给出格挡系反馈（冷却语义确定性见 Growth 烟测）");
                                step("BLOCK  格挡链路验证完毕");
                                shot(c.heroWindow, "02-格挡反馈");
                            }
                            // 强制力竭验证：第五杀之后做一次
                            if (!exhaustTested && c.cultivation.MortalKills >= 5)
                            {
                                exhaustTested = true;
                                c.battle.Damage(99999); // 加速点②：强制力竭，验证调息恢复链路（非战斗操作）
                                step("ACCEL  强制力竭（验证自动调息恢复，非战斗操作）");
                                return;
                            }
                            if (wall.Elapsed.TotalSeconds - lastClickAt < .45) return; // 出招节流，接近人手节奏
                            KnightLayeredWindow layered; if (!c.enemyWindows.TryGetValue(target.Id, out layered)) return;
                            Form win = layered;
                            string rewardBefore = c.battle.LastReward;
                            clickWindow(win);
                            lastClickAt = wall.Elapsed.TotalSeconds;
                            Application.DoEvents();
                            if (c.battle.LastReward.Contains("距离太远") && c.battle.LastReward != rewardBefore)
                            {
                                distanceHints++;
                                if (distanceHints <= 5) step("HINT   " + c.battle.LastReward + "（角色自动接近中）");
                            }
                            strikesOnTarget++;
                            if (strikesOnTarget == 1) shot(win, "01-选敌出招");
                        }
                        else if (phase == 1)
                        {
                            if (!awakenClicked)
                            {
                                Button btn = findButton(c.equipment, "灵根觉醒");
                                expect(btn != null && btn.Visible && btn.Enabled, "十战后觉醒按钮可见可用");
                                if (btn == null) { phase = 3; return; }
                                awakenClicked = true;
                                btn.PerformClick();
                                return;
                            }
                            IntPtr dlg = FindWindow(null, "灵根觉醒");
                            if (dlg != IntPtr.Zero) { PostMessage(dlg, WmKeyDown, (IntPtr)13, IntPtr.Zero); return; } // Enter=确认（确认框+结果框）
                            if (c.cultivation.Mortal) return; // 等待觉醒结算
                            expect(CultivationIdentity.IsValidRoot(c.cultivation.SpiritRoot), "觉醒测出有效灵根：" + c.cultivation.SpiritRoot);
                            expect(!c.cultivation.Mortal && c.cultivation.RealmIndex == 0 && c.cultivation.Layer == 1, "觉醒后进入炼气一层");
                            expect(c.cultivation.AutoBattleUnlocked && c.battle.Automatic, "觉醒后自动作战解锁并默认开启");
                            expect(c.consent.CleanupEnabled == false && c.consent.MemoryEnabled == false && c.consent.AutoMaintenanceEnabled == false, "觉醒后真实维护授权保持关闭");
                            string archiveText = File.Exists(c.cultivationPath) ? File.ReadAllText(c.cultivationPath) : "";
                            expect(archiveText.Contains("mortal=0") && archiveText.Contains("root="), "修仙档案已落盘（脱凡+灵根）");
                            step("AWOKEN 灵根「" + c.cultivation.RootName + "」· 进入炼气一层 · 自动作战已开启");
                            shot(c.heroWindow, "04-觉醒炼气");
                            phase = 2; phase2Start = wall.Elapsed.TotalSeconds; phase2Exp = c.battle.Progress.Experience; noTargetSince = -1;
                        }
                        else if (phase == 2)
                        {
                            if (c.battle.Progress.Experience > phase2Exp)
                            {
                                expect(true, "觉醒后零点击自动作战完成击杀（经验 " + phase2Exp + " → " + c.battle.Progress.Experience + "）");
                                step("AUTO   自动作战证明完成");
                                phase = 3; return;
                            }
                            bool anyTarget = c.battle.Enemies.Any(e => !e.IsBoss && !e.Defeated && !e.VisualDefeated && !e.Failed);
                            if (!anyTarget)
                            {
                                if (noTargetSince < 0) noTargetSince = wall.Elapsed.TotalSeconds;
                                if (wall.Elapsed.TotalSeconds - noTargetSince > 45)
                                {
                                    noTargetSince = wall.Elapsed.TotalSeconds;
                                    c.encounterDue = true; // 加速点①同款
                                    step("ACCEL  自动证明阶段触发生产同款生成路径");
                                }
                            }
                            if (wall.Elapsed.TotalSeconds - phase2Start > 300) { expect(false, "自动作战证明超时（300 秒无击杀）"); phase = 3; }
                        }
                    };
                    driver.Start();
                    while (phase != 3 && wall.Elapsed < TimeSpan.FromMinutes(16) && failures.Count == 0) { Application.DoEvents(); Thread.Sleep(20); }
                    driver.Stop(); driver.Dispose();
                    if (phase != 3 && failures.Count == 0) expect(false, "验收总时长超过 16 分钟未完成");
                    expect(c.battle.Enemies.Count(e => e.Id.StartsWith("lingyu-", StringComparison.Ordinal)) == 0, "验收全程未触发灵域镜妖（无关系统零干扰）");
                    step("STATS  总点击 " + clicksTotal + " 次 · 距离提示 " + distanceHints + " 次 · 用时 " + wall.Elapsed.ToString(@"mm\:ss"));
                    step(failures.Count == 0 ? "RESULT 全部通过" : "RESULT 存在失败项 " + failures.Count);
                    exitCode = failures.Count == 0 ? 0 : 1;
                }
            }
            File.WriteAllText(logPath, log.ToString(), Encoding.UTF8);
            return exitCode;
        }
    }
}

















