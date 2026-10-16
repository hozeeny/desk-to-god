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
    internal sealed class AdventureController : IDisposable
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
        private readonly string consentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant", "maintenance-consent.txt");
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
        private double memoryPercent, attackAnimation, renderTime, runCycle, actualMoveSpeed, blackholeUntil, blackholeX, blackholeY;
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
        public AdventureController(Form owner, string assetDirectory, bool demonstrationOnly = false, Func<double> waveClock = null)
        {
            if (owner == null) throw new ArgumentNullException("owner"); this.owner = owner; this.demonstrationOnly = demonstrationOnly;

            renderer = new AdventureRenderer(assetDirectory);
            region = new RegionThemeService();
            connectivity = demonstrationOnly ? null : new KnightConnectivityReader();
            // Freeze the historical v5.4 archive once before anything can rewrite
            // it; adventure-v1.txt itself stays the live v1-format runtime archive.
            if (!demonstrationOnly) AdventureProgress.EnsureLegacyBackup(progressPath);
            battle = new AdventureBattle(demonstrationOnly ? new AdventureProgress() : AdventureProgress.Load(progressPath));
            // Demonstration and smoke runs never touch the real cultivation archive.
            cultivation = demonstrationOnly ? new CultivationProgress() : CultivationProgress.Load(cultivationPath);
            cultivation.EnsureIdentity(random);
            encounters = new CultivationEncounterScheduler(random);
            deviceGrade = CultivationHardware.Classify(Environment.ProcessorCount, ReadTotalRamBytes());
            if (demonstrationOnly) { consent.CleanupEnabled = consent.MemoryEnabled = true; }
            else
            {
                MaintenanceConsentStore persisted = MaintenanceConsentStore.Load(consentPath);
                consent.CleanupEnabled = persisted.CleanupEnabled; consent.MemoryEnabled = persisted.MemoryEnabled;
                // Unattended self-battle is the default and independent from
                // real maintenance consent; a dead hero first finishes resting.
                if (cultivation.AutoBattle && !battle.Progress.Dead) battle.SetAutomatic(true);
            }
            if (!demonstrationOnly) region.SelectMode(battle.Progress.LastThemeCode);
            region.SelectMode(demonstrationOnly ? "OFF" : battle.Progress.ThemeMode);
            region.Changed += RegionChanged;
            area = Screen.FromControl(owner).WorkingArea;
            heroWindow = NewWindow("骑士助手 · 角色"); landmarkWindow = NewWindow("骑士助手 · 地区地标");
            heroWindow.Size = HeroSize; landmarkWindow.Size = LandmarkSize;
            x = area.Right - HeroSize.Width - 40; y = area.Bottom - HeroSize.Height - 30; ClampHero(); PickTarget();
            heroWindow.ContextRequested = OpenEquipment;
            heroWindow.DragChanged = delegate(bool active) { dragging = active; if (!active) { area = Screen.FromPoint(heroWindow.Location).WorkingArea; pauseMotionUntil = clock.Elapsed.TotalSeconds + 2; ClampHero(); PickTarget(); PositionLandmark(); } };
            heroWindow.DragMoved = delegate(Point p) { x = p.X; y = p.Y; heroWindow.Location = p; };
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
            battle.SetAutomatic(cultivation.AutoBattle && !battle.Progress.Dead);
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
        private bool consentRetryCleanup, consentRetryMemory;
        private double consentRetryAt = -1;
        internal bool SetCleanupConsent(bool value)
        {
            if (disposed) return false;
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
                return persisted;
            }
            if (!persisted) { battle.LastReward = "授权文件写入失败，为安全起见文件清理授权保持关闭"; return false; }
            battle.LastReward = "文件清理授权已开启：击败文件怪将安全清理其绑定的、超过 24 小时的本用户临时文件";
            return true;
        }
        internal bool SetMemoryConsent(bool value)
        {
            if (disposed) return false;
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
                return persisted;
            }
            if (!persisted) { battle.LastReward = "授权文件写入失败，为安全起见内存维护授权保持关闭"; return false; }
            battle.LastReward = "内存维护授权已开启：击败内存 Boss 将尝试回收同会话后台进程的工作集";
            return true;
        }
        // Third, separately confirmed permission: unattended real maintenance.
        // Auto battle without it fights virtually even where a scope consent is on.
        internal bool MaintenanceConsentAutoMaintenance { get { return consent.AutoMaintenanceEnabled; } }
        internal bool SetAutoMaintenanceConsent(bool value)
        {
            if (disposed) return false;
            string error;
            bool persisted = consent.SetAutoMaintenance(ConsentWritePath, value, out error);
            if (!persisted)
            {
                scanStatus = "授权状态暂未保存：" + error;
                battle.LastReward = value ? "授权文件写入失败，为安全起见无人值守自动维护保持关闭" : "无人值守自动维护已在本机关闭，但授权文件未能更新；若重启后仍显示已授权请手动删除 " + consentPath;
                if (!value) RetireAutomaticRealEffectJobs();
                return false;
            }
            if (!value)
            {
                // Queued-but-unstarted automatic targets with real effects retire;
                // auto battle itself continues against virtual fiends.
                RetireAutomaticRealEffectJobs();
                battle.LastReward = "无人值守自动维护已关闭：自动作战继续，但只结算虚拟奖励；真实清理/回收需要手动击杀或重新开启";
            }
            else battle.LastReward = "无人值守自动维护已开启：自动作战击败文件怪/内存 Boss 时将执行对应的真实清理或回收（仍须对应授权保持开启）";
            return true;
        }
        private void RetireAutomaticRealEffectJobs()
        {
            // Cycle automatic mode: unstarted automatic targets (including defeated
            // ones awaiting the cleanup worker) are retired; manual orders survive.
            bool resume = battle.Automatic || cultivation.AutoBattle;
            battle.SetAutomatic(false); RemoveCancelledAutomaticJobs();
            if (resume) battle.SetAutomatic(!battle.Progress.Dead);
        }
        // Automatic planning must never start unattended real maintenance on its
        // own: real-effect targets require the separate auto-maintenance consent.
        private bool AutoMaintenanceEligibility(AdventureEnemy enemy)
        {
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
            if (!consentRetryCleanup && !consentRetryMemory) return;
            string ignored;
            if (consentRetryCleanup && consent.SetCleanup(ConsentWritePath, false, out ignored)) consentRetryCleanup = false;
            if (consentRetryMemory && consent.SetMemory(ConsentWritePath, false, out ignored)) consentRetryMemory = false;
            if (!consentRetryCleanup && !consentRetryMemory) scanStatus = "授权关闭状态已重试保存成功";
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
        internal bool TryBreakthrough()
        {
            if (disposed || IsDemo || demonstrationOnly) return false;
            bool success = cultivation.TryBreakthrough(random);
            Save();
            if (success && enabled) RenderWorld(true);
            return success;
        }
        // Real elapsed seconds feed cultivation exactly once per tick, independent of
        // the render-frame dt clamp, the pet's visibility and the network state.
        private void AdvanceCultivation(double now)
        {
            if (demonstrationOnly || cultivation == null) return;
            if (lastCultivationAt < 0 || cultivationPaused || IsDemo) { lastCultivationAt = now; return; }
            double elapsed = now - lastCultivationAt; lastCultivationAt = now;
            if (elapsed <= 0) return;
            if (cultivation.AdvanceActiveTime(elapsed, deviceGrade) > 0) Save();
            if (encounters.Observe(elapsed)) encounterDue = true;
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
            fiend.Health = fiend.MaxHealth;
            Spawn(fiend); battle.Enemies.Add(fiend);
            battle.LastReward = "一只" + kind.Name + "被洞天灵气吸引而来 · 击败可获得修为";
        }
        private void SettleVirtual(AdventureEnemy enemy)
        {
            int qi = encounters.RollKillQi(enemy.IsBoss);
            cultivation.AddQi(qi);
            battle.CompleteVirtual(enemy, qi);
            Save();
        }
        private void SettleHeld(AdventureEnemy enemy)
        {
            int qi = encounters.RollKillQi(enemy.IsBoss);
            cultivation.AddQi(qi);
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
                if (battle.Progress.Dead) return "修士力竭 · 入定调息约 " + Math.Max(1, (int)(CultivationBalance.ReviveRestSeconds - battle.DeathTime)) + " 秒后自行恢复";
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
            if (disposed) return;
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
            if (equipment == null || equipment.IsDisposed) equipment = new AdventureEquipmentForm(this);
            equipment.RefreshData(); equipment.Show(); equipment.BringToFront(); equipment.Activate();
        }
        public void RefreshWorld() { RefreshWorld(true); }
        private void RefreshWorld(bool manualRequest)
        {
            if (disposed || demonstrationOnly || scanBusy || IsDemo) return;
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
            if (tempWatcher != null || demonstrationOnly || disposed) return;
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
            if (diskBusy || demonstrationOnly || disposed) return;
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
        private void Tick(object sender, EventArgs args)
        {
            if (disposed) return;
            double now = clock.Elapsed.TotalSeconds, dt = Math.Max(0, Math.Min(.1, now - lastTick)); lastTick = now;
            // Cultivation uses the raw elapsed time, never the render-clamped dt.
            AdvanceCultivation(now);
            // A consent revocation whose file update failed is retried until the
            // stale grant is gone — even while the pet is hidden.
            if ((consentRetryCleanup || consentRetryMemory) && now >= consentRetryAt + 5) { consentRetryAt = now; TryFlushConsentRevocation(); }
            DrainCleanup(); DrainMemoryReclaim(); if (demoPending && cleanupTask == null) StartDemo();
            if (!demonstrationOnly && now - lastDisk >= 5 && !diskBusy) ReadDisk(now);
            if (!enabled) return; effects.Tick(dt);
            if (demoPending) { RenderWorld(false); return; }
            if (demoStarted >= 0)
            {
                if (demonstrationOnly || now - demoStarted < 24) { renderTime += dt; RenderWorld(now - lastEnemyPaint >= .065); return; }
                StopDemo();
            }
            KnightConnectivity online = connectivity == null ? KnightConnectivity.Online : connectivity.Read(out connectivityDetail);
            lastConnectivity = online;
            // Cultivation is local-first: losing the network never kills the
            // cultivator or stops local battle; the pet meditates (Pant) instead.
            networkState.Tick(dt, bytesPerSecond, monitoringAvailable && now - lastTelemetry < 6,
                online == KnightConnectivity.Offline ? KnightConnectivity.Unknown : online);
            battle.Tick(dt);
            // Unattended recovery: a defeated cultivator always meditates back to
            // full health; no manual revival and no fixed online schedule needed.
            if (battle.Progress.Dead && !IsDemo && battle.DeathTime >= CultivationBalance.ReviveRestSeconds)
            {
                battle.Revive();
                if (cultivation.AutoBattle) battle.SetAutomatic(true);
                battle.LastReward = "修士调息完毕，伤势尽复，继续自主修行";
                Save();
            }
            ScaleOrdinaryEnemies(); foreach (LootView loot in lootViews.ToArray()) { loot.Life -= dt; if (loot.Life <= 0) { loot.Window.Dispose(); lootViews.Remove(loot); } } renderTime += dt; attackAnimation = Math.Max(0, attackAnimation - dt * 3);
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
                    else
                    {
                    double tx = target.X + (target.IsBoss ? 30 : -35), ty = target.Y - (HeroSize.Height - EnemySize(target).Height);
                    double distance = AdventureBattle.Distance(x, y, tx, ty);
                    if (distance > battle.Weapon.Range * displayScale * .8) MoveToward(tx, ty, Math.Max(200, networkState.PixelsPerSecond), dt);
                    else
                    {
                        heroFacing = target.X >= x;
                        if (battle.Cooldown <= 0) StrikeTarget(target, now);
                    }
                    }
                }
                else if (canFight && roaming && networkState.Phase == KnightPhase.Run)
                { if (AdventureBattle.Distance(x, y, targetX, targetY) < 18) PickTarget(); MoveToward(targetX, targetY, networkState.PixelsPerSecond, dt); }
            }
            actualMoveSpeed = dragging || dt <= 0 ? 0 : AdventureBattle.Distance(moveStartX, moveStartY, x, y) / dt;
            runCycle = (runCycle + actualMoveSpeed * dt / (displayScale * HeroPoseRig.TravelPerCycle)) % 1;
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
            PumpCleanup();
            RefillEnemies();
            if (now - lastScan >= InventoryIntervalSeconds || (now - lastScan >= .35 && scannedChangeVersion != Interlocked.CompareExchange(ref tempChangeVersion,0,0))) RefreshWorld(false);
            if (!demonstrationOnly && now - lastMemory >= 1 && !memoryBusy) ReadMemory(now);
            if (!demonstrationOnly && now - lastRegion >= 60) { lastRegion = now; region.RefreshAsync(); }
            if (now - lastSave >= 20) Save();
            RenderWorld(false);
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
            if (choice.Move) MoveToward(choice.Destination.X - HeroSize.Width / 2.0, choice.Destination.Y - HeroSize.Height, Math.Max(200, networkState.PixelsPerSecond), dt);
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
        private void StrikeTarget(AdventureEnemy target, double now)
        {
            attackAnimation = 1;
            if (battle.Weapon.Id == "blackhole") { blackholeUntil = now + 1.1; blackholeX = target.X; blackholeY = target.Y; }
            effects.Fire(new PointF((float)x + 110 * displayScale, (float)y + 125 * displayScale), new PointF((float)target.X + EnemySize(target).Width / 2f, (float)target.Y + EnemySize(target).Height * .47f), battle.Weapon.Id, displayScale);
            int inventoryBefore = battle.Progress.Inventory.Count;
            List<AdventureEnemy> killed = battle.Strike(target);
            foreach (AdventureEnemy defeated in killed) BeginCleanup(defeated);
            if (battle.Progress.Inventory.Count > inventoryBefore) ShowLoot(battle.Progress.Inventory.Last(), target.X, target.Y);
            if (killed.Count > 0) Save();
        }
        private void ReadMemory(double now)
        {
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
            if (disposed || IsDemo || !enemy.Authorized || enemy.Defeated) return;
            // Pure cultivation mode (default): virtual fiends and, without explicit
            // maintenance consent, file monsters and bosses all settle virtually.
            // No kill ever deletes real files or trims another process on its own.
            if (enemy.IsVirtual) { SettleVirtual(enemy); return; }
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
            CleanupResult result = cleanupTask.Result; AdventureEnemy enemy = activeCleanupEnemy;
            cleanupTask = null; activeCleanupEnemy = null; cleanupEpoch++; workCancellation.Dispose(); workCancellation = null;
            int priorWeapons = battle.Progress.Inventory.Count;
            battle.Complete(enemy, result.FreedBytes, result.DeletedFiles, result.Message, random.NextDouble(), result.Cancelled);
            if (battle.Progress.Inventory.Count > priorWeapons && enabled && !IsDemo) ShowLoot(battle.Progress.Inventory.Last(), enemy.X, enemy.Y);
            // File failure is an accounting result, never a reason to resurrect a dead mob.
            if (result.RemainingCandidates.Count > 0) RecordRecheck(result.RemainingCandidates);
            else if (result.DeletedFiles == 0 && enemy.Batch != null) RecordRecheck(enemy.Batch.Candidates);
            candidateBytes = Math.Max(0, candidateBytes - result.FreedBytes);
            cleanupNotice = "已清理 " + result.DeletedFiles + " 个文件 / " + AdventureBattle.FormatBytes(result.FreedBytes) + (recheckFiles > 0 ? "；待重扫 " + recheckFiles + " 个 / " + AdventureBattle.FormatBytes(recheckBytes) : "");
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
            enemy.Y = area.Top + 30 + random.NextDouble() * Math.Max(1, area.Height - 240); BoundEnemy(enemy); PickEnemyTarget(enemy);
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
            double heroDistance = EnemyHeroDistance(enemy);
            if (canFight)
            {
                Size size = EnemySize(enemy);
                double chaseX = x + HeroSize.Width / 2.0 - size.Width / 2.0;
                double chaseY = y + HeroSize.Height - size.Height;
                double reach = (!String.IsNullOrEmpty(enemy.EvilTheme) ? Math.Min(210,AdventureWeapon.Find(enemy.BossWeapon).Range*.5) : enemy.IsBoss ? 88 : 70) * displayScale;
                if (heroDistance > reach) MoveEnemy(enemy, chaseX, chaseY, (enemy.IsBoss ? 56 : AdventureMonster.Find(enemy.MonsterKind).WalkSpeed) * displayScale, dt);
                else FaceEnemy(enemy, chaseX - enemy.X);
                return;
            }
            if (AdventureBattle.Distance(enemy.X, enemy.Y, enemy.TargetX, enemy.TargetY) < 10) PickEnemyTarget(enemy);
            double speed = enemy.IsBoss ? 55 : AdventureMonster.Find(enemy.MonsterKind).WalkSpeed;
            // During death/disconnection, idle patrol remains independent of hero roaming.
            if (enemy.Authorized) speed *= heroDistance < 180 * displayScale ? .18 : .42;
            MoveEnemy(enemy, enemy.TargetX, enemy.TargetY, speed * displayScale, dt);
        }
        private double EnemyHeroDistance(AdventureEnemy enemy)
        {
            Size size = EnemySize(enemy);
            return AdventureBattle.Distance(enemy.X + size.Width / 2.0, enemy.Y + size.Height, x + HeroSize.Width / 2.0, y + HeroSize.Height);
        }
        private void AttackHero(AdventureEnemy enemy, bool canFight, double dt)
        {
            if (!canFight || enemy.Dragging || enemy.Cleaning || enemy.VisualDefeated || enemy.Defeated || enemy.Failed || enemy.IsDiskWarning) { enemy.AttackTime = 0; return; }
            double cooldown = !String.IsNullOrEmpty(enemy.EvilTheme) ? 2.4 + AdventureWeapon.Find(enemy.BossWeapon).Cooldown : enemy.IsBoss ? 1.8 : AdventureMonster.Find(enemy.MonsterKind).AttackCooldown;
            if (EnemyHeroDistance(enemy) > (!String.IsNullOrEmpty(enemy.EvilTheme) ? Math.Min(300,AdventureWeapon.Find(enemy.BossWeapon).Range*.65) : enemy.IsBoss ? 105 : 86) * displayScale) { enemy.AttackTime = 0; return; }
            enemy.AttackTime += dt;
            if (enemy.AttackTime < cooldown) return;
            enemy.AttackTime = 0; enemy.VisualDirty = true; enemy.AttackFlash = 1;
            battle.Damage(!String.IsNullOrEmpty(enemy.EvilTheme) ? 2 + enemy.SpawnLevel / 12 : enemy.IsBoss ? 12 : AdventureMonster.Find(enemy.MonsterKind).Damage);
            if (!String.IsNullOrEmpty(enemy.EvilTheme) && enabled) effects.Fire(new PointF((float)enemy.X+EnemySize(enemy).Width/2f,(float)enemy.Y+EnemySize(enemy).Height/2f),new PointF((float)x+HeroSize.Width/2f,(float)y+HeroSize.Height/2f),enemy.BossWeapon,displayScale);
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
        private void PositionLandmark() { landmarkWindow.Location = new Point(area.Left + 16, area.Bottom - LandmarkSize.Height - 16); }
        private void PaintWindow(KnightLayeredWindow window, AdventureRenderState state)
        {
            window.Present(renderer.Render(state)); if (!window.Visible) window.Show();
        }
        private AdventureRenderState HeroVisual(double now)
        {
            string phase = networkState.Phase.ToString().ToLowerInvariant();
            if (battle.Progress.Dead) phase = battle.DeathTime < 3.4 ? "dying" : "grave";
            else if (attackAnimation > 0) phase = "attack";
            else if (actualMoveSpeed > 1 && phase != "grave" && phase != "dying" && phase != "revive") phase = "run";
            else if (phase == "run") phase = "idle";
            return new AdventureRenderState { Kind = "Hero", Size = HeroSize, ThemeCode = demonstrationOnly ? demoTheme : region.CurrentCode, Phase = phase, WeaponId = battle.Weapon.Id, Time = now, Cycle = runCycle, MoveSpeed = actualMoveSpeed / displayScale, FacingRight = heroFacing, Level = battle.Progress.Level, HealthRatio = (double)battle.Progress.Health / battle.Progress.MaxHealth, Label = battle.Progress.Dead ? "入定调息 · 稍后自行恢复" : cultivation.RealmName + " · 第" + cultivation.Layer + "层", GlowProgress = battle.LevelGlow > 0 ? 1 - battle.LevelGlow / 4.5 : -1, AttackProgress = attackAnimation, Opacity = 1, PhaseTime = battle.Progress.Dead ? battle.DeathTime : networkState.PhaseTime };
        }
        private void RenderWorld(bool enemies)
        {
            if (disposed || !enabled) return;
            try
            {
                if (demoStarted >= 0) { RenderDemo(enemies || clock.Elapsed.TotalSeconds - lastEnemyPaint >= .066); return; }
                heroWindow.Location = new Point((int)x, (int)y); PaintWindow(heroWindow, HeroVisual(renderTime));
                double paintNow = clock.Elapsed.TotalSeconds;
                if (enemies || !landmarkWindow.Visible || paintNow - lastLandmarkPaint >= .25)
                {
                    lastLandmarkPaint = paintNow;
                    PaintWindow(landmarkWindow, new AdventureRenderState { Kind = "Landmark", Size = LandmarkSize, ThemeCode = demonstrationOnly ? demoTheme : region.CurrentCode, Time = renderTime, Label = CastleAmountText, GrowthLabel = GrowthStatusText, GrowthRatio = ResourceGrowthRatio, DiskLabel = diskSnapshot.Available ? "C盘剩余 " + AdventureBattle.FormatBytes(diskSnapshot.AvailableBytes) : "C盘剩余：等待采样", DiskFreeRatio = diskSnapshot.Available ? diskSnapshot.FreeRatio : -1, DiskCritical = diskSnapshot.Available && diskSnapshot.AvailableBytes < LowDiskBossPolicy.LowThresholdBytes, Opacity = 1 });
                }
                HashSet<string> visible = new HashSet<string>();
                // Dispose retired windows BEFORE showing replacements, including demo transitions.
                RemoveUnusedWindows(new HashSet<string>(battle.Enemies.Where(e => !e.IsVisuallyHidden).Take(EnemyFieldBudget.MaxEnemies).Select(e => e.Id)));
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
                        window.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressedAt = Cursor.Position; dragged = false; } };
                        window.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left && !dragged && !IsDemo && !captured.VisualDefeated) { if (battle.Progress.Dead) OpenEquipment(); else battle.Authorize(captured); RenderWorld(true); } };
                        window.ContextRequested = OpenEquipment;
                        window.DragChanged = delegate(bool active) { captured.Dragging = active; };
                        window.DragMoved = delegate(Point p) { if (AdventureBattle.Distance(pressedAt.X, pressedAt.Y, Cursor.Position.X, Cursor.Position.Y) > 6) dragged = true; captured.X = p.X; captured.Y = p.Y; BoundEnemy(captured); };
                    }
                    window.Location = new Point((int)enemy.X, (int)enemy.Y);
                    if (!enemies && !newWindow && !enemy.VisualDirty && !enemy.VisualDefeated && (enemyIndex - paintStart + enemyCount) % enemyCount >= 4) continue;
                    string label = enemy.Failed ? "暂不能清理 · 重新扫描" : enemy.IsDiskBoss ? (enemy.IsDiskWarning ? "C 盘不足 · 无可清理文件" : "磁盘 Boss · " + AdventureBattle.FormatBytes(enemy.Bytes)) : enemy.IsBoss ? "邪恶分身 · " + AdventureWeapon.Find(enemy.BossWeapon).Name : enemy.Cleaning ? (enemy == activeCleanupEnemy ? "正在清理…" : "等待清理…") : enemy.Authorized ? "#" + enemy.QueueNumber + " · " + AdventureBattle.FormatBytes(enemy.Bytes) : AdventureMonster.Find(enemy.MonsterKind).Name + (enemy.Batch != null && enemy.Batch.Candidates.Any(f=>!f.EligibleForCleanup) ? " · 近期文件" : "");
                    double deathProgress = enemy.VisualDefeated ? Math.Min(1, enemy.DeathElapsed / AdventureEnemy.ShatterSeconds) : 0;
                    PaintWindow(window, new AdventureRenderState { Kind = !String.IsNullOrEmpty(enemy.EvilTheme) ? "EvilBoss" : enemy.IsBoss ? "Boss" : "Skeleton", WeaponId = enemy.BossWeapon ?? "spear", MonsterKind = enemy.MonsterKind, Size = EnemySize(enemy), ThemeCode = enemy.EvilTheme ?? (demonstrationOnly ? demoTheme : region.CurrentCode), Time = enemy.AnimationTime, PhaseTime = enemy.DeathElapsed, DeathProgress = deathProgress, Phase = enemy.VisualDefeated ? "shatter" : enemy.AttackFlash > .05 ? "attack" : enemy.MoveSpeed > 1 ? "run" : "idle", Label = enemy.VisualDefeated ? "" : label, QueueNumber = enemy.QueueNumber, FacingRight = enemy.FacingRight, MoveSpeed = enemy.MoveSpeed / displayScale, HealthRatio = (double)enemy.Health / Math.Max(1, enemy.MaxHealth), HealthCurrent = enemy.Health, HealthMaximum = enemy.MaxHealth, AttackProgress = !String.IsNullOrEmpty(enemy.EvilTheme) ? enemy.AttackFlash : enemy.HitFlash * 4, HitFlash = enemy.HitFlash * 4, Opacity = 1 });
                    enemy.VisualDirty = false;
                }
                enemyPaintCursor = enemyCount == 0 ? 0 : (paintStart + 4) % enemyCount;
                RemoveUnusedWindows(visible);
                foreach (LootView loot in lootViews) { loot.Window.Location = new Point((int)loot.X, (int)(loot.Y - (3.2 - loot.Life) * 18)); PaintWindow(loot.Window, new AdventureRenderState { Kind = "Loot", Size = new Size(160, 155), ThemeCode = region.CurrentCode, WeaponId = loot.WeaponId, Time = 3.2 - loot.Life, Label = loot.Label, Opacity = Math.Min(1, loot.Life) }); }
            }
            catch (Exception e) { failure = e.Message; Enabled = false; }
        }
        private void ShowLoot(string weaponId, double px, double py)
        {
            while (lootViews.Count >= 2) { lootViews[0].Window.Dispose(); lootViews.RemoveAt(0); }
            KnightLayeredWindow window = NewWindow("装备掉落 · 已放入背包"); window.PassThrough = true;
            lootViews.Add(new LootView { Window = window, WeaponId = weaponId, Label = "获得 " + AdventureWeapon.Find(weaponId).Name, X = Math.Min(area.Right - 165, Math.Max(area.Left + 5, px)), Y = Math.Min(area.Bottom - 160, Math.Max(area.Top + 70, py)) });
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
            PaintWindow(heroWindow, new AdventureRenderState { Kind = "Hero", Size = HeroSize, ThemeCode = demonstrationOnly ? demoTheme : region.CurrentCode, Phase = phase, WeaponId = t < 5 ? "spear" : t < 10 ? "laser" : "blackhole", Time = t, PhaseTime = phaseTime, MoveSpeed = 400, FacingRight = true, Level = 7, HealthRatio = phase == "grave" ? 0 : 1, Label = "纯演示 · 不清理文件", GlowProgress = t >= 10 && t < 14 ? (t - 10) / 4 : -1, AttackProgress = phase == "attack" ? (t * 2) % 1 : 0, Opacity = 1 });
            if (!enemies) return; lastEnemyPaint = clock.Elapsed.TotalSeconds;
            PaintWindow(landmarkWindow, new AdventureRenderState { Kind = "Landmark", Size = LandmarkSize, ThemeCode = demonstrationOnly ? demoTheme : region.CurrentCode, Time = t, Label = "演示城堡 · 虚拟敌人", Opacity = 1 });
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
            lastSave = clock.Elapsed.TotalSeconds;
            try { battle.Progress.Save(progressPath); } catch (Exception e) { scanStatus = "进度暂未保存：" + e.Message; }
            // adventure-v1.txt stays the live runtime combat archive in the
            // unchanged v1 format (v5.4 can still read it); the frozen
            // *.v54-backup copy made at first launch preserves history.
            // Cultivation data lives in its own separate versioned archive.
            try { cultivation.Save(cultivationPath); } catch (Exception e) { scanStatus = "修炼档案暂未保存：" + e.Message; }
        }
        public void Dispose()
        {
            if (disposed) return;
            timer.Stop(); CancelPending(); cancellation.Cancel();
            // Final best-effort flush of any consent revocation whose file update
            // failed during the session, so a restart cannot resurrect it.
            if (consentRetryCleanup || consentRetryMemory) TryFlushConsentRevocation();
            if (tempWatcher != null) { tempWatcher.Dispose(); tempWatcher = null; }
            if (memoryReclaimTask != null) { try { memoryReclaimTask.Wait(6000); DrainMemoryReclaim(); } catch { } }
            // Clean only works under the local temporary directory and checks the
            // token between files. Finish the active result before saving progress.
            if (cleanupTask != null) { try { cleanupTask.Wait(); DrainCleanup(); } catch { } }
            demoStarted = -1; demoPending = false; Save(); disposed = true;
            timer.Dispose(); if (connectivity != null) connectivity.Dispose(); region.Changed -= RegionChanged; region.Dispose();
            if (equipment != null) equipment.Dispose(); heroWindow.Dispose(); landmarkWindow.Dispose(); foreach (KnightLayeredWindow w in enemyWindows.Values) w.Dispose(); enemyWindows.Clear(); ClearLoot(); effects.Dispose(); renderer.Dispose(); cancellation.Dispose();
        }        internal AdventureBattle Battle { get { return battle; } }
        internal string DetailText { get { return cleanupNotice + "\r\n" + scanStatus + "\r\n" + (diskSnapshot.Available ? "C 盘剩余 " + AdventureBattle.FormatBytes(diskSnapshot.AvailableBytes) + " / " + AdventureBattle.FormatBytes(diskSnapshot.TotalBytes) : diskSnapshot.Error) + "\r\n" + connectivityDetail + "\r\n" + region.Status; } }
        internal string ThemeMode { get { return battle.Progress.ThemeMode; } }
        internal bool IsDemo { get { return demoStarted >= 0 || demoPending; } }
        internal Rectangle WorkingArea { get { return area; } }
        internal string MemoryText { get { return memoryAvailable ? memoryPercent.ToString("0.0") + "%" : "等待采样"; } }
        internal bool IsScanning { get { return scanBusy; } }
        internal void ChooseTheme(string code) { if (demonstrationOnly) { demoTheme = code; RenderWorld(true); return; } if (IsDemo) return; battle.Progress.ThemeMode = code; region.SelectMode(code); battle.Progress.LastThemeCode = region.CurrentCode; region.RefreshAsync(); Save(); RenderWorld(true); }
        internal void RefreshRegion() { if (!demonstrationOnly && !IsDemo) region.RequestRefresh(); }
        internal void ChooseWeapon(string id) { if (IsDemo || !battle.Progress.Inventory.Contains(id)) return; battle.Progress.WeaponId = id; Save(); }
        internal void SetAutomatic(bool value) { SetAutoPlan(value); }
        public void SetAutoPlan(bool value)
        {
            if (disposed || IsDemo || demonstrationOnly) return;
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
        internal void ChangeScreen(Screen screen) { area = screen.WorkingArea; ClampHero(); PositionLandmark(); foreach (AdventureEnemy e in battle.Enemies) { BoundEnemy(e); PickEnemyTarget(e); } RenderWorld(true); }
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
                    check(c.heroWindow.Owner == null && !c.heroWindow.ShowInTaskbar && c.heroWindow.TopMost, "desktop overlay independent of main window");
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
                    for (int tick = 0; tick < 4; tick++)
                    {
                        Dictionary<string, object> frames = c.enemyWindows.ToDictionary(pair => pair.Key, pair => currentFrame.GetValue(pair.Value));
                        foreach (AdventureEnemy enemy in c.battle.Enemies) { enemy.X += 2; enemy.AnimationTime += .016; }
                        c.renderTime += .016; c.RenderWorld(false); int changed = 0;
                        foreach (AdventureEnemy enemy in c.battle.Enemies)
                        {
                            KnightLayeredWindow window = c.enemyWindows[enemy.Id];
                            if (!Object.ReferenceEquals(frames[enemy.Id], currentFrame.GetValue(window))) { changed++; repainted.Add(enemy.Id); }
                            positionsCurrent &= window.Left == (int)enemy.X && window.Top == (int)enemy.Y;
                        }
                        boundedPaint &= changed == 4;
                    }
                    check(boundedPaint, "normal rendering spreads eight monsters across four rasterizations per tick");
                    check(positionsCurrent, "all monster positions follow movement even when their pose is cached");
                    check(repainted.Count == 8, "round-robin rendering never starves a monster pose");
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
                    // A failed revocation write still applies in memory, cancels the
                    // in-flight worker at once, and retries the stale grant removal.
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
                        check(!c.SetCleanupConsent(false) && !c.consent.CleanupEnabled && c.consentRetryCleanup, "failed revocation write reports failure yet still applies in memory and schedules a retry");
                        check(c.workCancellation.IsCancellationRequested, "in-flight cleanup is cancelled even when the consent file update fails");
                        c.DrainCleanup();
                        check(inFlight.Failed && c.battle.Progress.FreedBytes == freed && c.battle.LastReward.Contains("取消"), "cancelled worker reports its actual result without inventing freed bytes");
                    }
                    finally
                    {
                        c.consentPathOverride = null; c.simulatedCleaner = null;
                        consentLock.Dispose();
                        try { File.Delete(blockedConsent); } catch { }
                    }
                    c.TryFlushConsentRevocation();
                    check(!c.consentRetryCleanup, "stale consent removal is retried until the file can no longer resurrect it");
                    // Hide/show and demo transitions never detach runtime state from
                    // the persisted unattended-battle preference.
                    c.cultivation.AutoBattle = true; c.battle.SetAutomatic(true);
                    c.Enabled = false; check(!c.battle.Automatic, "hiding stops runtime auto battle");
                    c.Enabled = true; check(c.battle.Automatic, "showing restores the persisted auto-battle preference");
                    c.cultivation.AutoBattle = false; c.Enabled = false; c.Enabled = true;
                    check(!c.battle.Automatic, "a saved manual preference survives hide/show too");
                    c.cultivation.AutoBattle = true; c.RestoreAutoBattlePreference(); c.battle.CancelQueue();
                    c.consent.CleanupEnabled = false;
                    // Unattended recovery: a defeated cultivator always rests back.
                    c.battle.Enemies.Clear(); c.CancelPending(); c.battle.SetAutomatic(false);
                    c.battle.Progress.Dead = true; c.battle.Progress.Health = 0; c.battle.DeathTime = CultivationBalance.ReviveRestSeconds + 1;
                    c.enabled = true; c.demoStarted = -1; c.demoPending = false;
                    c.Tick(null, EventArgs.Empty);
                    check(!c.battle.Progress.Dead && c.battle.Progress.Health == c.battle.Progress.MaxHealth && c.battle.Automatic && c.battle.LastReward.Contains("调息"), "an unattended cultivator recovers from defeat and resumes auto battle");
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
            }
            return "Adventure native smoke: " + count + " checks passed; no real cleanup, network query, or progress save.\r\n" + ResourceSmoke(assetDirectory);
        }
        private static string ResourceSmoke(string assetDirectory)
        {
            return LiveSmoke(assetDirectory) + "\r\n" + TacticsSmoke(assetDirectory);
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
                    c.memoryAvailable = true; c.memoryPercent = 47.2; c.diskSnapshot = DiskSpaceSnapshot.FromValues(256L * 1024 * 1024 * 1024, 24L * 1024 * 1024 * 1024); c.scanStatus = "预览示例数据；真实临时文件只在正常启动后扫描。";
                    using (AdventureEquipmentForm panel = new AdventureEquipmentForm(c))
                    using (Bitmap capture = new Bitmap(panel.Width, panel.Height))
                    {
                        panel.StartPosition = FormStartPosition.Manual; panel.Location = new Point(-24000, -24000); panel.Show(); Application.DoEvents(); panel.RefreshData();
                        panel.DrawToBitmap(capture, new Rectangle(Point.Empty, panel.Size)); capture.Save(output, ImageFormat.Png);
                    }
                }
            }
        }        public static void ExportPreview(string assetDirectory, string output)
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
    }
}

















