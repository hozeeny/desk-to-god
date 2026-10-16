using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
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
        private readonly ResourceWaveState resourceWaves = new ResourceWaveState();
        private readonly DiskSpaceService diskSpace = new DiskSpaceService();
        private readonly LowDiskBossPolicy diskBossPolicy = new LowDiskBossPolicy();
        private DiskSpaceSnapshot diskSnapshot = DiskSpaceSnapshot.Unavailable("等待 C 盘容量采样");
        private bool diskBusy, diskWaveRequested;
        private double lastDisk = -100;
        private readonly AdventureEffects effects = new AdventureEffects();
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
        public AdventureController(Form owner, string assetDirectory, bool demonstrationOnly = false)
        {
            if (owner == null) throw new ArgumentNullException("owner"); this.owner = owner; this.demonstrationOnly = demonstrationOnly;
            renderer = new AdventureRenderer(assetDirectory);
            region = new RegionThemeService();
            connectivity = demonstrationOnly ? null : new KnightConnectivityReader();
            battle = new AdventureBattle(demonstrationOnly ? new AdventureProgress() : AdventureProgress.Load(progressPath));
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
        private Size LandmarkSize { get { return new Size((int)(200 * displayScale), (int)(250 * displayScale)); } }
        private KnightLayeredWindow NewWindow(string name) { KnightLayeredWindow w = new KnightLayeredWindow(); w.Text = name; return w; }
        public bool Enabled
        {
            get { return enabled; }
            set
            {
                if (disposed) return; if (!value) CancelPending(); if (enabled == value) return; enabled = value;
                if (enabled) { failure = null; lastTick = clock.Elapsed.TotalSeconds; RenderWorld(true); if (!scanBusy && demoStarted < 0 && !demonstrationOnly) RefreshWorld(false); }
                else { effects.Clear(); ClearLoot(); heroWindow.Hide(); landmarkWindow.Hide(); foreach (KnightLayeredWindow w in enemyWindows.Values) w.Hide(); }
            }
        }
        public bool Roaming { get { return roaming; } set { roaming = value; if (value) PickTarget(); } }
        public bool AutoPlanning { get { return battle.Automatic; } }
        public DiskSpaceSnapshot DiskSnapshot { get { return diskSnapshot; } }
        public bool CanClearField { get { return !disposed && !demonstrationOnly && enabled && !IsDemo && !battle.Progress.Dead; } }
        public string StateText
        {
            get
            {
                if (failure != null) return "桌面显示异常：" + failure;
                if (!enabled) return "骑士与怪物已隐藏";
                if (demoPending) return "正在停止清理，准备安全演示";
                if (demoStarted >= 0) return "冒险演示 · 不清理文件，不改变经验和装备";
                if (battle.Progress.Dead) return "骑士战死 · 右键墓碑复活";
                if (networkState.Phase == KnightPhase.Dying || networkState.Phase == KnightPhase.Grave) return "网络断开 · 灵魂升天，墓碑等待重连";
                if (networkState.Phase == KnightPhase.Revive) return "网络恢复 · 从坟地爬出";
                if (battle.QueueCount > 0) return (battle.Target == null ? "后台清理 · " : battle.Automatic ? "自动规划 · " : "清理队列 · ") + battle.QueueCount + " 个目标 · " + battle.Weapon.Name + " · Lv." + battle.Progress.Level;
                return "Lv." + battle.Progress.Level + " · " + (actualMoveSpeed > 1 ? "随网速奔跑" : networkState.Phase == KnightPhase.Pant ? "正在大喘气" : "原地待命") + " · " + CleanupAmountText;
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
            // Preview must not silently resume an automatic deletion mode afterwards.
            resumeAutomaticAfterDemo = false; battle.SetAutomatic(false); RemoveCancelledAutomaticJobs();
            if (workCancellation != null) workCancellation.Cancel(); demoPending = true;
            if (cleanupTask == null) StartDemo();
        }
        private void StartDemo()
        {
            demoPending = false; effects.Clear(); ClearLoot(); demoStarted = clock.Elapsed.TotalSeconds; Enabled = true; RenderWorld(true);
        }        internal void StopDemo() { if (demonstrationOnly) return; effects.Clear(); demoStarted = -1; demoPending = false; battle.Automatic = resumeAutomaticAfterDemo && !battle.Progress.Dead; resumeAutomaticAfterDemo = false; lastTick = clock.Elapsed.TotalSeconds; RenderWorld(true); }
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
            if (disposed || demonstrationOnly || scanBusy || IsDemo || battle.QueueCount > 0 || cleanupTask != null) return;
            scanBusy = true; int scanEpoch = cleanupEpoch; lastScan = clock.Elapsed.TotalSeconds; scanStatus = "正在扫描 24 小时以上、可安全检查的临时文件…";
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
            resourceWaves.ObserveInventory(snapshot.ObservedTempBytes, snapshot.InventoryComplete);
            if (battle.QueueCount > 0 || cleanupTask != null || IsDemo) return;
            // A scan is an observation, not permission to refill the field. Old files
            // beyond the twelve-batch display budget are already in the census baseline.
            if (manualRequest) resourceWaves.CompleteWave();
            CompleteResourceWaveIfSettled();
            bool diskRequest = diskWaveRequested && !resourceWaves.WaveActive;
            if (!resourceWaves.TryBeginWave(manualRequest || diskRequest))
            {
                scanStatus = resourceWaves.WaveActive ? "本轮怪物尚未清完；扫描不会补回已击败的怪物。" : "本轮已结束；等待临时文件或内存占用增长，也可手动重新扫描。";
                if (!snapshot.InventoryComplete) scanStatus += " 本次总量检查不完整，不据此判断临时文件增长。";
                return;
            }
            diskWaveRequested = false;
            // No queued/active jobs exist here. Replace only the previous file wave;
            // a passive low-disk warning and independent RAM-pressure boss can remain.
            battle.Enemies.RemoveAll(e => !e.IsBoss || (e.IsDiskBoss && e.Defeated));
            foreach (AdventureEnemy existingDiskBoss in battle.Enemies.Where(e => e.IsDiskBoss))
            {
                existingDiskBoss.Batch = null; existingDiskBoss.Failed = false;
                existingDiskBoss.Health = existingDiskBoss.MaxHealth; existingDiskBoss.RestoreVisual(AdventureBattle.DiskWarningMessage);
            }
            List<CleanupBatch> fieldBatches = EnemyFieldBudget.CoalesceBatches(snapshot.Batches, EnemyFieldBudget.AvailableOrdinarySlots(battle.Enemies));
            HashSet<string> reboundPaths = new HashSet<string>(fieldBatches.SelectMany(batch => batch.Candidates).Select(file => file.Path), StringComparer.OrdinalIgnoreCase);
            foreach (string path in recheckCandidates.Keys.ToArray())
                if (reboundPaths.Contains(path) || (snapshot.InventoryComplete && snapshot.ObservedPathsAvailable && !snapshot.ObservedPaths.Contains(path))) recheckCandidates.Remove(path);
            recheckBytes = recheckCandidates.Values.Sum(file => file.Length); recheckFiles = recheckCandidates.Count;
            candidateBytes = fieldBatches.Sum(batch => batch.Bytes) + recheckBytes;
            scanStatus = "第 " + resourceWaves.WaveNumber + " 轮 · " + (diskRequest ? "C 盘新增低空间压力" : resourceWaves.LastWaveReason) + "；" + snapshot.Message;
            foreach (CleanupBatch batch in fieldBatches)
            {
                AdventureEnemy enemy = AdventureMonster.Create(batch, random);
                Spawn(enemy); battle.Enemies.Add(enemy);
            }
            BindDiskBossCandidates();
            CompleteResourceWaveIfSettled(); lastLandmarkPaint = -100;
        }
        private void CompleteResourceWaveIfSettled()
        {
            if (cleanupTask == null && pendingCleanup.Count == 0 && !battle.Enemies.Any(e => e.Batch != null && !e.Defeated))
                resourceWaves.CompleteWave();
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
                        AdventureEnemy ordinary = AdventureMonster.Create(batch, random);
                        ordinary.Failed = existing.Failed; Spawn(ordinary); battle.Enemies.Add(ordinary);
                    }
                existing = null; diskWaveRequested = false;
            }
            if (diskBossPolicy.Observe(diskSnapshot, now, existing != null || !EnemyFieldBudget.CanAddBoss(battle.Enemies, true)))
            {
                AdventureEnemy boss = new AdventureEnemy { Id = "disk-boss-" + Guid.NewGuid().ToString("N"), IsBoss = true, IsDiskBoss = true, MonsterKind = "boss", MaxHealth = 800 + battle.Progress.Level * 40 };
                boss.Health = boss.MaxHealth; Spawn(boss); battle.Enemies.Add(boss);
                diskWaveRequested = true;
                BindDiskBossCandidates();
                if (boss.IsDiskWarning) { battle.LastReward = AdventureBattle.DiskWarningMessage; lastScan = -100; }
                else battle.LastReward = "C 盘剩余空间低于 1 GiB：磁盘危机 Boss 出现！击败后清理它对应的临时文件。";
            }
            else BindDiskBossCandidates();
        }
        private void BindDiskBossCandidates()
        {
            AdventureEnemy boss = battle.Enemies.FirstOrDefault(e => e.IsDiskBoss && e.IsDiskWarning && !e.Defeated);
            if (boss == null) return;
            AdventureEnemy[] donors = battle.Enemies.Where(e => !e.IsBoss && e.Batch != null && !e.Authorized && !e.Cleaning && !e.Defeated && !e.Failed && e.Batch.Candidates.All(f => String.Equals(Path.GetPathRoot(f.Path), @"C:\", StringComparison.OrdinalIgnoreCase))).ToArray();
            if (donors.Length == 0) return;
            // Transfer ownership, never duplicate it. Both normal mobs and the disk
            // boss enter the same exclusive, identity-checked cleanup worker.
            List<CleanupCandidate> files = donors.SelectMany(e => e.Batch.Candidates).GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            if (files.Count == 0) return;
            boss.Batch = new CleanupBatch(files, 1); boss.VisualDirty = true;
            foreach (AdventureEnemy donor in donors) battle.Enemies.Remove(donor);
            diskWaveRequested = false;
            battle.LastReward = "磁盘危机 Boss 已绑定 " + files.Count + " 个临时文件 · 可清理 " + AdventureBattle.FormatBytes(boss.Bytes);
        }
        private void Tick(object sender, EventArgs args)
        {
            if (disposed) return;
            double now = clock.Elapsed.TotalSeconds, dt = Math.Max(0, Math.Min(.1, now - lastTick)); lastTick = now;
            DrainCleanup(); if (demoPending && cleanupTask == null) StartDemo();
            if (!demonstrationOnly && now - lastDisk >= 5 && !diskBusy) ReadDisk(now);
            if (!enabled) return; effects.Tick(dt);
            if (demoPending) { RenderWorld(false); return; }
            if (demoStarted >= 0)
            {
                if (demonstrationOnly || now - demoStarted < 24) { renderTime += dt; RenderWorld(now - lastEnemyPaint >= .065); return; }
                StopDemo();
            }
            KnightConnectivity online = connectivity == null ? KnightConnectivity.Online : connectivity.Read(out connectivityDetail);
            networkState.Tick(dt, bytesPerSecond, monitoringAvailable && now - lastTelemetry < 6, online);
            battle.Tick(dt); foreach (LootView loot in lootViews.ToArray()) { loot.Life -= dt; if (loot.Life <= 0) { loot.Window.Dispose(); lootViews.Remove(loot); } } renderTime += dt; attackAnimation = Math.Max(0, attackAnimation - dt * 3);
            bool canFight = !battle.Progress.Dead && networkState.Phase != KnightPhase.Dying && networkState.Phase != KnightPhase.Grave && networkState.Phase != KnightPhase.Revive;
            if (canFight && battle.Automatic) battle.PlanNext(x, y);
            AdventureEnemy target = canFight ? battle.Target : null;
            double moveStartX = x, moveStartY = y;
            if (!dragging && now > pauseMotionUntil)
            {
                if (target != null)
                {
                    double tx = target.X + (target.IsBoss ? 30 : -35), ty = target.Y - (HeroSize.Height - EnemySize(target).Height);
                    double distance = AdventureBattle.Distance(x, y, tx, ty);
                    if (distance > battle.Weapon.Range * displayScale * .8) MoveToward(tx, ty, Math.Max(200, networkState.PixelsPerSecond), dt);
                    else
                    {
                        heroFacing = target.X >= x;
                        if (battle.Cooldown <= 0) { attackAnimation = 1; if (battle.Weapon.Id == "blackhole") { blackholeUntil = now + 1.1; blackholeX = target.X; blackholeY = target.Y; } effects.Fire(new PointF((float)x + 110 * displayScale, (float)y + 125 * displayScale), new PointF((float)target.X + 65 * displayScale, (float)target.Y + 80 * displayScale), battle.Weapon.Id, displayScale); int inventoryBefore = battle.Progress.Inventory.Count; List<AdventureEnemy> killed = battle.Strike(target); foreach (AdventureEnemy defeated in killed) BeginCleanup(defeated); if (battle.Progress.Inventory.Count > inventoryBefore) ShowLoot(battle.Progress.Inventory.Last(), target.X, target.Y); if (killed.Count > 0) Save(); }
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
            if (now - lastScan >= 60) RefreshWorld(false);
            if (!demonstrationOnly && now - lastMemory >= 5 && !memoryBusy) ReadMemory(now);
            if (!demonstrationOnly && now - lastRegion >= 60) { lastRegion = now; region.RefreshAsync(); }
            if (now - lastSave >= 20) Save();
            RenderWorld(false);
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
                    resourceWaves.ObserveMemory(memoryPercent, true);
                    if (!IsDemo && resourceWaves.AutomaticWaveAllowed && !resourceWaves.WaveActive) lastScan = -100;
                    if (!IsDemo && EnemyFieldBudget.CanAddBoss(battle.Enemies, false) && battle.ObserveMemory(value.UsedPercent, elapsed, now))
                    {
                        AdventureEnemy boss = new AdventureEnemy { Id = "boss-" + Guid.NewGuid().ToString("N"), IsBoss = true, MonsterKind = "boss", MaxHealth = 550 + battle.Progress.Level * 40 };
                        boss.Health = boss.MaxHealth; Spawn(boss); battle.Enemies.Add(boss);
                        battle.LastReward = "系统内存持续超过 85%：内存压力 Boss 出现！击败它不会强制清空内存";
                    }
                });
            });
        }
        private void BeginCleanup(AdventureEnemy enemy)
        {
            if (disposed || (demonstrationOnly && simulatedCleaner == null) || IsDemo || !enemy.Authorized) return;
            if (enemy.IsBoss && !enemy.IsDiskBoss) { battle.Complete(enemy, 0, 0, null, 1); Save(); return; }
            if (enemy.Batch == null) { battle.Complete(enemy, 0, 0, "没有真实文件批次", 1); return; }
            if (!pendingCleanup.Contains(enemy)) pendingCleanup.Enqueue(enemy);
            // The read-only census yields its filesystem gate to a newly killed target.
            if (scanCancellation != null) scanCancellation.Cancel();
            cleanupNotice = "怪物已击败 · 正在清理对应临时文件";
            PumpCleanup();
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
            resourceWaves.RecordDeletion(result.FreedBytes);
            CompleteResourceWaveIfSettled();
            lastLandmarkPaint = -100; lastDisk = -100;
            if (resourceWaves.AutomaticWaveAllowed) lastScan = -100;
            Save();
            PumpCleanup();
            // Completion reduces the current field's displayed bytes immediately.
            // Do not force an instant rescan that refills it from the same old files.
        }        private void Post(Action action)
        {
            if (disposed || owner.IsDisposed || !owner.IsHandleCreated) return;
            try { owner.BeginInvoke(new Action(delegate { if (!disposed) action(); })); } catch (InvalidOperationException) { }
        }
        private string CleanupAmountText { get { return recheckFiles > 0 ? "可处理 " + AdventureBattle.FormatBytes(Math.Max(0, candidateBytes - recheckBytes)) + " · 待重扫 " + AdventureBattle.FormatBytes(recheckBytes) : "本轮可清理 " + AdventureBattle.FormatBytes(candidateBytes); } }
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
                double reach = (enemy.IsBoss ? 88 : 70) * displayScale;
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
            double cooldown = enemy.IsBoss ? 1.8 : AdventureMonster.Find(enemy.MonsterKind).AttackCooldown;
            if (EnemyHeroDistance(enemy) > (enemy.IsBoss ? 105 : 86) * displayScale) { enemy.AttackTime = 0; return; }
            enemy.AttackTime += dt;
            if (enemy.AttackTime < cooldown) return;
            enemy.AttackTime = 0; enemy.VisualDirty = true;
            battle.Damage(enemy.IsBoss ? 12 : AdventureMonster.Find(enemy.MonsterKind).Damage);
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
            return new AdventureRenderState { Kind = "Hero", Size = HeroSize, ThemeCode = demonstrationOnly ? demoTheme : region.CurrentCode, Phase = phase, WeaponId = battle.Weapon.Id, Time = now, Cycle = runCycle, MoveSpeed = actualMoveSpeed / displayScale, FacingRight = heroFacing, Level = battle.Progress.Level, HealthRatio = (double)battle.Progress.Health / battle.Progress.MaxHealth, Label = battle.Progress.Dead ? "右键墓碑 · 复活" : "Lv." + battle.Progress.Level, GlowProgress = battle.LevelGlow > 0 ? 1 - battle.LevelGlow / 4.5 : -1, AttackProgress = attackAnimation, Opacity = 1, PhaseTime = battle.Progress.Dead ? battle.DeathTime : networkState.PhaseTime };
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
                    PaintWindow(landmarkWindow, new AdventureRenderState { Kind = "Landmark", Size = LandmarkSize, ThemeCode = demonstrationOnly ? demoTheme : region.CurrentCode, Time = renderTime, Label = CleanupAmountText, DiskLabel = diskSnapshot.Available ? "C盘剩余 " + AdventureBattle.FormatBytes(diskSnapshot.AvailableBytes) : "C盘剩余：等待采样", DiskFreeRatio = diskSnapshot.Available ? diskSnapshot.FreeRatio : -1, DiskCritical = diskSnapshot.Available && diskSnapshot.AvailableBytes < LowDiskBossPolicy.LowThresholdBytes, Opacity = 1 });
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
                        AdventureEnemy captured = enemy; window = NewWindow(enemy.IsDiskBoss ? "C 盘空间危机 Boss" : enemy.IsBoss ? "内存压力 Boss" : AdventureMonster.Find(enemy.MonsterKind).Name + " · 临时文件怪物"); enemyWindows.Add(enemy.Id, window);
                        Point pressedAt = Point.Empty; bool dragged = false;
                        window.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressedAt = Cursor.Position; dragged = false; } };
                        window.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left && !dragged && !IsDemo && !captured.VisualDefeated) { if (battle.Progress.Dead) OpenEquipment(); else battle.Authorize(captured); RenderWorld(true); } };
                        window.ContextRequested = OpenEquipment;
                        window.DragChanged = delegate(bool active) { captured.Dragging = active; };
                        window.DragMoved = delegate(Point p) { if (AdventureBattle.Distance(pressedAt.X, pressedAt.Y, Cursor.Position.X, Cursor.Position.Y) > 6) dragged = true; captured.X = p.X; captured.Y = p.Y; BoundEnemy(captured); };
                    }
                    window.Location = new Point((int)enemy.X, (int)enemy.Y);
                    if (!enemies && !newWindow && !enemy.VisualDirty && !enemy.VisualDefeated && (enemyIndex - paintStart + enemyCount) % enemyCount >= 4) continue;
                    string label = enemy.Failed ? "暂不能清理 · 重新扫描" : enemy.IsDiskBoss ? (enemy.IsDiskWarning ? "C 盘不足 · 无可清理文件" : "磁盘 Boss · " + AdventureBattle.FormatBytes(enemy.Bytes)) : enemy.IsBoss ? "内存压力 " + memoryPercent.ToString("0") + "%" : enemy.Cleaning ? (enemy == activeCleanupEnemy ? "正在清理…" : "等待清理…") : enemy.Authorized ? "#" + enemy.QueueNumber + " · " + AdventureBattle.FormatBytes(enemy.Bytes) : AdventureMonster.Find(enemy.MonsterKind).Name;
                    double deathProgress = enemy.VisualDefeated ? Math.Min(1, enemy.DeathElapsed / AdventureEnemy.ShatterSeconds) : 0;
                    PaintWindow(window, new AdventureRenderState { Kind = enemy.IsBoss ? "Boss" : "Skeleton", MonsterKind = enemy.MonsterKind, Size = EnemySize(enemy), ThemeCode = demonstrationOnly ? demoTheme : region.CurrentCode, Time = enemy.AnimationTime, PhaseTime = enemy.DeathElapsed, DeathProgress = deathProgress, Phase = enemy.VisualDefeated ? "shatter" : enemy.MoveSpeed > 1 ? "run" : "idle", Label = enemy.VisualDefeated ? "" : label, QueueNumber = enemy.QueueNumber, FacingRight = enemy.FacingRight, MoveSpeed = enemy.MoveSpeed / displayScale, HealthRatio = (double)enemy.Health / Math.Max(1, enemy.MaxHealth), HealthCurrent = enemy.Health, HealthMaximum = enemy.MaxHealth, AttackProgress = enemy.HitFlash * 4, HitFlash = enemy.HitFlash * 4, Opacity = 1 });
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
        }
        public void Dispose()
        {
            if (disposed) return;
            timer.Stop(); CancelPending(); cancellation.Cancel();
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
            battle.LastReward = battle.Automatic ? "自动规划已开启：优先处理近处、残血与高威胁怪物，手动点击始终优先；会清理对应临时文件" : "自动规划已关闭；未开始的自动清理已撤销，手动队列保留";
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
            demoPending = false; demoStarted = -1; resumeAutomaticAfterDemo = false;
            blackholeUntil = 0; effects.Clear();
            if (workCancellation != null) workCancellation.Cancel();
            while (pendingCleanup.Count > 0) { AdventureEnemy enemy = pendingCleanup.Dequeue(); if (enemy.VisualDefeated && enemy.Batch != null) RecordRecheck(enemy.Batch.Candidates); enemy.CancelBeforeCleanup("清理已取消 · 对应文件待重扫"); }
            battle.CancelQueue();
            CompleteResourceWaveIfSettled();
        }
        internal void Resurrect() { if (IsDemo) return; battle.Revive(); Save(); }
        internal void AbsorbAll() { ClearField(); }
        public void ClearField()
        {
            if (!CanClearField) return;
            int inventoryBefore = battle.Progress.Inventory.Count;
            List<AdventureEnemy> selected = battle.ClearFieldSnapshot();
            if (battle.Progress.Inventory.Count > inventoryBefore) ShowLoot(battle.Progress.Inventory.Last(), x, y);
            if (selected.Count == 0) { battle.LastReward = "当前没有可清场的怪物；可以重新扫描临时文件"; return; }
            blackholeUntil = clock.Elapsed.TotalSeconds + 1.7; blackholeX = x + HeroSize.Width / 2; blackholeY = y + HeroSize.Height / 2; attackAnimation = 1;
            PointF origin = new PointF((float)blackholeX, (float)blackholeY);
            effects.FireField(origin, selected.Select(enemy => new PointF((float)enemy.X + 65 * displayScale, (float)enemy.Y + 80 * displayScale)), displayScale);
            foreach (AdventureEnemy enemy in selected) BeginCleanup(enemy);
            battle.LastReward = "一键清场：击败 " + selected.Count + " 个目标 · 经验 +" + selected.Sum(enemy => enemy.KillExperience) + "；对应临时文件逐批安全清理，可随时取消待办清理";
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
            int count = 0;
            Action<bool, string> check = delegate(bool ok, string label) { if (!ok) throw new InvalidOperationException("Adventure resource smoke: " + label); count++; };
            const long MiB = 1024L * 1024, GiB = 1024L * MiB;
            Func<string, long, CleanupBatch> batch = delegate(string id, long size) { return new CleanupBatch(new List<CleanupCandidate> { new CleanupCandidate { Path = @"C:\KnightAssistantSynthetic\" + id + ".tmp", Length = size } }, 1); };
            Func<long, CleanupBatch[], CleanupSnapshot> inventory = delegate(long total, CleanupBatch[] batches) { return new CleanupSnapshot { ObservedTempBytes = total, InventoryComplete = true, Batches = batches.ToList(), CandidateBytes = batches.Sum(b => b.Bytes), Message = "synthetic inventory" }; };
            using (Form owner = new Form())
            {
                IntPtr ownerHandle = owner.Handle;
                using (AdventureController c = new AdventureController(owner, assetDirectory, true))
                {
                    c.timer.Stop(); c.enabled = true; c.area = new Rectangle(-24000, -24000, 1600, 1000); c.PositionLandmark(); c.x = -23600; c.y = -23500; c.ClampHero();
                    CleanupBatch first = batch("one", 15 * MiB), second = batch("two", 20 * MiB), backlog = batch("old-backlog", 30 * MiB);
                    c.resourceWaves.ObserveMemory(40, true);
                    c.ApplyInventory(inventory(100 * MiB, new[] { first, second }), false);
                    check(c.battle.Enemies.Count == 2 && c.candidateBytes == 35 * MiB && c.resourceWaves.WaveNumber == 1, "initial file wave owns exactly its safe batches");
                    c.ApplyInventory(inventory(100 * MiB, new[] { second, backlog }), false);
                    check(c.battle.Enemies.Count == 2 && c.battle.Enemies.Any(e => e.Id == first.Id) && c.candidateBytes == 35 * MiB, "background scan cannot replace live wave or inflate castle bytes");
                    TaskCompletionSource<CleanupResult> delayed = new TaskCompletionSource<CleanupResult>(); int starts = 0;
                    c.simulatedCleaner = delegate(CleanupBatch ignored, CancellationToken token) { starts++; return delayed.Task; };
                    AdventureEnemy enemy = c.battle.Enemies.First(e => e.Id == first.Id); c.battle.Authorize(enemy); enemy.Health = 1; c.battle.Strike(enemy); c.BeginCleanup(enemy); c.PumpCleanup();
                    c.battle.Tick(.31); c.RenderWorld(true);
                    check(starts == 1 && !c.enemyWindows.ContainsKey(enemy.Id) && c.candidateBytes == 35 * MiB && c.battle.Progress.FreedBytes == 0, "visual death precedes pending real-result accounting");
                    delayed.SetResult(new CleanupResult { FreedBytes = 15 * MiB, DeletedFiles = 1 }); c.DrainCleanup();
                    check(c.candidateBytes == 20 * MiB && c.resourceWaves.BaselineTempBytes == 85 * MiB && c.lastLandmarkPaint < 0, "successful result immediately reduces castle and census baseline");
                    check(!c.diskSnapshot.Available, "file logical bytes cannot fabricate a disk free-space sample");
                    c.ApplyInventory(inventory(85 * MiB, new[] { second, backlog }), false);
                    check(c.candidateBytes == 20 * MiB && c.resourceWaves.WaveNumber == 1 && !c.battle.Enemies.Any(e => e.Id == backlog.Id), "post-cleanup old backlog is not new growth");
                    c.simulatedCleaner = delegate(CleanupBatch item, CancellationToken token) { return Task.FromResult(new CleanupResult { FreedBytes = item.Bytes, DeletedFiles = item.FilesCount }); };
                    enemy = c.battle.Enemies.First(e => e.Id == second.Id); c.battle.Authorize(enemy); enemy.Health = 1; c.battle.Cooldown = 0; c.battle.Strike(enemy); c.BeginCleanup(enemy); c.PumpCleanup(); c.DrainCleanup();
                    check(c.candidateBytes == 0 && !c.resourceWaves.WaveActive && c.battle.Progress.FreedBytes == 35 * MiB, "cleared wave settles once and castle reaches zero");
                    for (int i = 0; i < 5; i++) c.ApplyInventory(inventory(65 * MiB, new[] { backlog }), false);
                    check(c.resourceWaves.WaveNumber == 1 && !c.battle.Enemies.Any(e => !e.Defeated), "five unchanged resource scans cannot refill a cleared field");
                    CleanupSnapshot partial = inventory(200 * MiB, new[] { backlog }); partial.InventoryComplete = false; c.ApplyInventory(partial, false);
                    check(c.resourceWaves.WaveNumber == 1, "partial census cannot manufacture temporary-file growth");
                    c.ApplyInventory(inventory(67 * MiB, new[] { backlog }), false);
                    check(c.resourceWaves.WaveNumber == 2 && c.candidateBytes == 30 * MiB && c.battle.Enemies.Count == 1, "trusted new temporary-file growth admits one new finite wave");
                    c.ObserveDisk(DiskSpaceSnapshot.FromValues(100 * GiB, GiB), 1);
                    check(!c.battle.Enemies.Any(e => e.IsDiskBoss), "one GiB exactly does not satisfy less-than threshold");
                    c.ObserveDisk(DiskSpaceSnapshot.FromValues(100 * GiB, GiB / 2), 2);
                    AdventureEnemy boss = c.battle.Enemies.Single(e => e.IsDiskBoss);
                    check(boss.IsBoss && boss.Batch != null && boss.Bytes == 30 * MiB && !c.battle.Enemies.Any(e => !e.IsBoss), "low-disk boss takes exclusive ownership of current safe C-drive file batches");
                    check(c.candidateBytes == 30 * MiB && c.DiskSnapshot.AvailableBytes == GiB / 2, "boss transfer neither changes castle total nor invents free space");
                    c.ApplyInventory(inventory(67 * MiB, new[] { backlog }), true);
                    check(c.battle.Enemies.Single() == boss && boss.Batch != null && boss.Bytes == 30 * MiB, "manual rescan preserves a low-disk boss and rebinds candidates without duplication");
                    delayed = new TaskCompletionSource<CleanupResult>();
                    c.simulatedCleaner = delegate(CleanupBatch item, CancellationToken token) { starts++; return delayed.Task; };
                    long freedBeforeBoss = c.battle.Progress.FreedBytes; int scoreBeforeBoss = c.battle.Progress.Score, xpBeforeBoss = c.battle.Progress.Experience;
                    c.battle.Authorize(boss); boss.Health = 1; c.battle.Cooldown = 0; c.battle.Strike(boss); c.BeginCleanup(boss); c.PumpCleanup(); c.battle.Tick(.31); c.RenderWorld(true);
                    bool bossPending = c.cleanupTask == delayed.Task && boss.Cleaning && !c.enemyWindows.ContainsKey(boss.Id) && c.battle.Progress.Score == scoreBeforeBoss + boss.KillExperience * 5;
                    delayed.SetResult(new CleanupResult { FreedBytes = 30 * MiB, DeletedFiles = 1 }); c.DrainCleanup();
                    check(bossPending, "disk boss pays only kill XP while real deletion remains pending");
                    check(c.candidateBytes == 0 && c.battle.Progress.FreedBytes == freedBeforeBoss + 30 * MiB && c.battle.Progress.Score == scoreBeforeBoss + 100 + boss.KillExperience * 5 && c.battle.Progress.Experience == xpBeforeBoss + boss.KillExperience, "disk-boss real deletion reduces castle and awards combat score once without duplicate XP");
                    check(c.DiskSnapshot.AvailableBytes == GiB / 2 && c.lastDisk < 0, "free-space bar awaits fresh filesystem sample after cleanup");
                    c.ObserveDisk(DiskSpaceSnapshot.FromValues(100 * GiB, GiB / 2 + 12 * MiB), 3);
                    check(c.DiskSnapshot.AvailableBytes == GiB / 2 + 12 * MiB && !c.battle.Enemies.Any(e => e.IsDiskBoss && !e.Defeated), "reported capacity uses new measurement; continuous low space cannot farm bosses");
                    c.ApplyInventory(inventory(37 * MiB, new[] { first }), false);
                    check(!c.battle.Enemies.Any(e => !e.Defeated) && c.candidateBytes == 0, "boss defeat does not replenish old files");
                    c.resourceWaves.ObserveMemory(45, true); c.ApplyInventory(inventory(37 * MiB, new[] { first }), false);
                    check(c.battle.Enemies.Count == 1 && c.candidateBytes == first.Bytes, "five-point RAM rise admits a finite real-file wave");
                    c.ApplyInventory(inventory(37 * MiB, new CleanupBatch[0]), true);
                    c.ObserveDisk(DiskSpaceSnapshot.FromValues(100 * GiB, 2 * GiB), 200);
                    c.ObserveDisk(DiskSpaceSnapshot.FromValues(100 * GiB, GiB / 2), 201);
                    boss = c.battle.Enemies.Single(e => e.IsDiskBoss && !e.Defeated);
                    double bossX = boss.X, bossY = boss.Y; c.AdvanceEnemyPatrol(boss, true, .2);
                    check(boss.IsDiskWarning && !c.battle.Authorize(boss) && c.battle.ClearFieldSnapshot().Count == 0 && boss.X == bossX && boss.Y == bossY, "no-candidate warning boss is passive and cannot authorize fake cleanup");
                    c.ObserveDisk(DiskSpaceSnapshot.Unavailable("synthetic failure"), 202);
                    check(!c.DiskSnapshot.Available && c.battle.Enemies.Contains(boss), "unavailable capacity is not zero and does not invent recovery");
                    c.ObserveDisk(DiskSpaceSnapshot.FromValues(100 * GiB, 2 * GiB), 203);
                    check(!c.battle.Enemies.Contains(boss), "passive warning disappears after measured disk recovery");
                    CleanupBatch[] dozen = Enumerable.Range(0, 12).Select(i => new CleanupBatch(new List<CleanupCandidate> { new CleanupCandidate { Path = @"D:\KnightAssistantSynthetic\budget-" + i + ".tmp", Length = (i + 1) * MiB, IndexLow = (uint)(i + 1), Written = 100 + i } }, i + 1)).ToArray();
                    c.ApplyInventory(inventory(200 * MiB, dozen), true); c.RenderWorld(true);
                    check(c.battle.Enemies.Count == 6 && c.enemyWindows.Count == 6 && c.candidateBytes == dozen.Sum(b => b.Bytes), "twelve scan batches occupy six ordinary windows with unchanged total bytes");
                    List<CleanupCandidate> fieldFiles = c.battle.Enemies.SelectMany(e => e.Batch.Candidates).ToList();
                    check(fieldFiles.Count == 12 && fieldFiles.Distinct().Count() == 12 && dozen.All(b => fieldFiles.Contains(b.Candidates[0])), "controller coalescing retains every original candidate object exactly once");
                    AdventureEnemy memoryBoss = new AdventureEnemy { Id = "budget-memory-boss", IsBoss = true, MonsterKind = "boss", MaxHealth = 600, Health = 600 };
                    c.Spawn(memoryBoss); c.battle.Enemies.Add(memoryBoss);
                    c.ObserveDisk(DiskSpaceSnapshot.FromValues(100 * GiB, GiB / 2), 403); c.RenderWorld(true);
                    check(c.battle.Enemies.Count == 8 && c.enemyWindows.Count == 8 && EnemyFieldBudget.IsWithinBudget(c.battle.Enemies), "six ordinary monsters plus memory and passive disk bosses never exceed eight actors");
                    for (int repeat = 0; repeat < 4; repeat++) { c.ObserveDisk(DiskSpaceSnapshot.FromValues(100 * GiB, GiB / 2), 404 + repeat); c.RenderWorld(true); }
                    check(c.battle.Enemies.Count == 8 && c.enemyWindows.Count == 8, "repeated low-space observations cannot append duplicate bosses");
                    KnightLayeredWindow[] oldOrdinaryWindows = c.battle.Enemies.Where(e => !e.IsBoss).Select(e => c.enemyWindows[e.Id]).ToArray();
                    int replacementHighWater = c.enemyWindows.Count, retiredWindows = 0;
                    foreach (KnightLayeredWindow oldWindow in oldOrdinaryWindows) oldWindow.Disposed += delegate { retiredWindows++; replacementHighWater = Math.Max(replacementHighWater, c.enemyWindows.Count); };
                    CleanupBatch[] replacement = Enumerable.Range(0, 12).Select(i => new CleanupBatch(new List<CleanupCandidate> { new CleanupCandidate { Path = @"D:\KnightAssistantSynthetic\replacement-" + i + ".tmp", Length = MiB, IndexLow = (uint)(i + 101), Written = 500 + i } }, i + 1)).ToArray();
                    c.ApplyInventory(inventory(220 * MiB, replacement), true); c.RenderWorld(true);
                    check(retiredWindows == 6 && oldOrdinaryWindows.All(w => w.IsDisposed) && replacementHighWater <= 8 && c.enemyWindows.Count == 8 && EnemyFieldBudget.IsWithinBudget(c.battle.Enemies), "same-frame rescan disposes old windows before creating their six replacements");
                    AdventureEnemy fading = c.battle.Enemies.First(e => !e.IsBoss); c.battle.Authorize(fading); fading.Health = 1; c.battle.Cooldown = 0; c.battle.Strike(fading);
                    delayed = new TaskCompletionSource<CleanupResult>();
                    c.simulatedCleaner = delegate(CleanupBatch item, CancellationToken token) { return delayed.Task; };
                    c.BeginCleanup(fading); c.battle.Tick(.31); c.RenderWorld(false);
                    c.ApplyInventory(inventory(230 * MiB, dozen), true);
                    bool delayedBudget = c.battle.Enemies.Count == 8 && c.enemyWindows.Count == 7 && !EnemyFieldBudget.CanAddOrdinary(c.battle.Enemies) && c.cleanupTask == delayed.Task;
                    delayed.SetResult(new CleanupResult { Message = "synthetic locked residue", RemainingCandidates = fading.Batch.Candidates.ToList() }); c.DrainCleanup(); c.RenderWorld(false);
                    check(delayedBudget, "an invisible in-flight death still owns its slot and blocks scan replacement");
                    check(fading.Defeated && fading.IsVisuallyHidden && c.recheckFiles == fading.Batch.FilesCount && c.recheckBytes == fading.Bytes && c.enemyWindows.Count == 7 && c.battle.Enemies.Count == 8, "failed residue stays in explicit rescan accounting without creating or reviving an enemy");
                    c.ObserveDisk(DiskSpaceSnapshot.FromValues(100 * GiB, 2 * GiB), 500); c.RenderWorld(true);
                    check(c.battle.Enemies.Count == 7 && c.enemyWindows.Count == 6 && EnemyFieldBudget.IsWithinBudget(c.battle.Enemies), "disk warning recovery frees its own slot while the fading ordinary object stays counted");
                    int unresolvedCount = c.recheckFiles; long unresolvedBytes = c.recheckBytes;
                    CleanupSnapshot incompleteRescan = inventory(0, new CleanupBatch[0]); incompleteRescan.InventoryComplete = false; incompleteRescan.ObservedPathsAvailable = true;
                    c.ApplyInventory(incompleteRescan, true);
                    check(c.recheckFiles == unresolvedCount && c.recheckBytes == unresolvedBytes && c.candidateBytes == unresolvedBytes, "incomplete empty scan preserves unverified failed-file accounting without respawning a monster");
                    CleanupSnapshot knownPresent = inventory(unresolvedBytes, new CleanupBatch[0]); knownPresent.ObservedPathsAvailable = true;
                    foreach (CleanupCandidate file in fading.Batch.Candidates) knownPresent.ObservedPaths.Add(file.Path);
                    c.ApplyInventory(knownPresent, true);
                    check(c.recheckFiles == unresolvedCount && !c.battle.Enemies.Any(e => !e.IsBoss), "complete scan retains known-existing residue that is not yet a safe candidate");
                    CleanupSnapshot knownAbsent = inventory(0, new CleanupBatch[0]); knownAbsent.ObservedPathsAvailable = true;
                    c.ApplyInventory(knownAbsent, true);
                    check(c.recheckFiles == 0 && c.recheckBytes == 0 && c.candidateBytes == 0, "only a complete path census can retire externally removed pending files");
                    c.RecordRecheck(first.Candidates); c.ApplyInventory(inventory(first.Bytes, new[] { first }), true);
                    check(c.recheckFiles == 0 && c.candidateBytes == first.Bytes && c.battle.Enemies.Single(e => !e.IsBoss).Batch == first, "revalidated candidate transfers out of pending accounting exactly once");
                    check(c.connectivity == null && !c.scanBusy && c.region.Mode == "OFF", "resource smoke never starts real network or file services");
                }
            }
            return "Adventure resource smoke: " + count + " checks passed (synthetic inventories, disk samples and cleanup results only).";
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

















