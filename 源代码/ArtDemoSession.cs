using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetBunny
{
    // Explicit opt-in only. The production constructor and archives retain their
    // existing behavior; this playable host uses demonstrationOnly memory models.
    internal sealed partial class AdventureController
    {
        private readonly bool artDemoRuntime;
        private double artDemoSpeed = 1;
        private bool artDemoCave;
        private bool artDemoHostHidden;
        private int artDemoSpawnSequence;
        private int artDemoLineup;
        internal string ArtDemoHeroStage { get { return cultivation.Mortal ? "mortal" : cultivation.RealmIndex == 0 ? "qishi" : "zhuji"; } }
        internal string ArtDemoFailure { get { return failure; } }
        internal Rectangle ArtDemoHeroBounds { get { return heroWindow.Bounds; } }
        internal bool PauseArtDemoCapture()
        {
            bool wasRunning = timer.Enabled; timer.Stop(); return wasRunning;
        }
        internal void ResumeArtDemoCapture(bool wasRunning)
        {
            lastTick = clock.Elapsed.TotalSeconds; lastCultivationAt = lastTick;
            if (wasRunning && !disposed) timer.Start();
        }
        internal List<ArtDemoActorFrame> SnapshotArtDemoActors()
        {
            List<ArtDemoActorFrame> frames = new List<ArtDemoActorFrame>();
            FieldInfo current = typeof(KnightLayeredWindow).GetField("current", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (KnightLayeredWindow window in Application.OpenForms.OfType<KnightLayeredWindow>().Where(w => w.Visible && !w.IsDisposed).ToArray())
            {
                Bitmap frame = current.GetValue(window) as Bitmap;
                if (frame == null) continue;
                string role = window == heroWindow ? "hero" : enemyWindows.Any(p => p.Value == window && battle.Enemies.Any(e => e.Id == p.Key && !e.VisualDefeated && !e.Defeated)) ? "enemy" : "decoration";
                frames.Add(new ArtDemoActorFrame { Window = window, Bounds = window.Bounds, Frame = (Bitmap)frame.Clone(), Role = role });
            }
            ArtDemoPixelVerifier.SortByDesktopZOrder(frames);
            return frames;
        }
        internal static string ArtDemoMonsterLabel(string id)
        {
            switch (id)
            {
                case "skeleton": return "山中石傀";
                case "slime": return "苔露灵团";
                case "imp": return "木芽小妖";
                case "bat": return "夜翼蝠妖";
                case "mushroom": return "赤伞菇妖";
                default: return "山中妖物";
            }
        }
        internal bool ArtDemoBringActorsForward()
        {
            if (!artDemoRuntime || disposed || artDemoHostHidden) return true;
            List<ArtDemoActorFrame> actors = SnapshotArtDemoActors(); bool ok = true;
            try
            {
                foreach (ArtDemoActorFrame actor in actors.OrderByDescending(a => a.ZOrder))
                    ok &= ArtDemoWindowOrdering.RaiseWithoutActivation(actor.Window.Handle);
            }
            finally { foreach (ArtDemoActorFrame actor in actors) actor.Dispose(); }
            return ok;
        }
        internal void ArtDemoSetHostVisible(bool visible)
        {
            if (!artDemoRuntime || disposed || artDemoHostHidden == !visible) return;
            artDemoHostHidden = !visible;
            if (!visible)
            {
                effects.Clear();
                List<ArtDemoActorFrame> actors = SnapshotArtDemoActors();
                foreach (ArtDemoActorFrame actor in actors) { actor.Window.Hide(); actor.Dispose(); }
            }
            else { RenderWorld(true); ArtDemoBringActorsForward(); }
        }
        internal int ArtDemoAttackCount;
        internal int ArtDemoSettledKills;
        private readonly HashSet<string> artDemoSettled = new HashSet<string>();

        internal void ConfigureArtDemo(Rectangle bounds, bool cave)
        {
            if (!artDemoRuntime || !demonstrationOnly) throw new InvalidOperationException("需要隔离美术 Demo 控制器。");
            artDemoCave = cave; roaming = false; area = bounds;
            consent.CleanupEnabled = consent.MemoryEnabled = consent.AutoMaintenanceEnabled = false;
            displayScale = .85f; heroWindow.Size = HeroSize; landmarkWindow.Size = LandmarkSize;
            x = area.Left + area.Width * .27; y = area.Bottom - HeroSize.Height - 45;
            if (cave)
            {
                cultivation.Mortal = false; cultivation.RealmIndex = 1; cultivation.Layer = 1;
                cultivation.EnsureIdentity(new Random(17)); cultivation.AutoBattle = false;
                dongtian.Bag["linggu"] = 4; dongtian.Bag["ningshen"] = 2;
                dongtian.Plant(0, "linggu", DateTime.UtcNow);
                battle.LastReward = "筑基预置档 · 独立内存展示，可实际播种、开炉与修行";
            }
            else
            {
                for (int i = 0; i < 5; i++) AddArtDemoEnemy(i);
                battle.LastReward = "点击妖物，或按「出招」主动攻击；每一招都需点击。十次有效击杀后觉醒。";
            }
            Enabled = true; PositionLandmark(); ClampHero(); RenderWorld(true);
        }
        internal void MoveArtDemoStage(Rectangle bounds)
        {
            if (!artDemoRuntime || area == bounds) return;
            int dx = bounds.X - area.X, dy = bounds.Y - area.Y;
            x += dx; y += dy;
            foreach (AdventureEnemy enemy in battle.Enemies) { enemy.X += dx; enemy.Y += dy; enemy.TargetX += dx; enemy.TargetY += dy; }
            area = bounds; ClampHero(); foreach (AdventureEnemy enemy in battle.Enemies) BoundEnemy(enemy);
            PositionLandmark(); RenderWorld(true);
        }
        private void AddArtDemoEnemy(int fixture)
        {
            AdventureMonster kind = AdventureMonster.All[fixture == 3 && artDemoLineup % 2 != 0 ? 4 : fixture % AdventureMonster.All.Length];
            AdventureEnemy enemy = new AdventureEnemy { Id = "art-demo-" + (++artDemoSpawnSequence), IsVirtual = true, MonsterKind = kind.Id, SpawnLevel = battle.Progress.Level };
            enemy.MaxHealth = EnemyBalance.OrdinaryHealth(enemy.SpawnLevel, kind.HealthFactor, 0);
            if (fixture == 3) { enemy.Elite = true; enemy.MaxHealth = (int)(enemy.MaxHealth * CultivationBalance.EliteHealthFactor); }
            if (fixture == 4) { enemy.IsBoss = true; enemy.NoFieldClear = true; enemy.EvilTheme = "CN"; enemy.BossWeapon = "wave"; enemy.MaxHealth *= 2; }
            if (fixture == 5) { enemy.NoFieldClear = true; }
            enemy.Health = enemy.MaxHealth;
            // Fixed opening composition is a combat fixture. Health, damage,
            // cooldowns and rewards all use the same production battle model.
            enemy.X = area.Left + area.Width * (.47 + (fixture % 3) * .16);
            enemy.Y = area.Top + area.Height * (fixture < 3 ? .28 : .59);
            BoundEnemy(enemy); PickEnemyTarget(enemy); battle.Enemies.Add(enemy);
        }
        internal void ArtDemoMaintainField()
        {
            if (!artDemoRuntime || !enabled) return;
            foreach (AdventureEnemy enemy in battle.Enemies)
                if (enemy.KillRewarded && enemy.FellInCombat && artDemoSettled.Add(enemy.Id)) ArtDemoSettledKills++;
            if (!artDemoCave && battle.Enemies.Count(e => !e.Defeated && !e.VisualDefeated) < 4 && EnemyFieldBudget.CanAddOrdinary(battle.Enemies))
                AddArtDemoEnemy(artDemoSpawnSequence % 3);
        }
        internal void ArtDemoStrike()
        {
            if (!artDemoRuntime || battle.Progress.Dead) return;
            AdventureEnemy target = battle.Target ?? battle.Enemies.Where(e => !e.Defeated && !e.VisualDefeated && !e.Cleaning).OrderBy(e => e.IsBoss ? 2 : e.Elite ? 1 : 0).ThenBy(e => e.Health).FirstOrDefault();
            if (target == null) return;
            battle.Authorize(target);
            double cooldown = battle.Cooldown;
            TryMortalStrike(target);
            if (cooldown <= 0 && battle.Cooldown > 0) ArtDemoAttackCount++;
            RenderWorld(true);
        }
        internal void ArtDemoBlock() { if (artDemoRuntime) { TryBlock(); RenderWorld(true); } }
        internal void ArtDemoSetSpeed(int speed) { if (artDemoRuntime) artDemoSpeed = speed == 6 ? 6 : speed == 4 ? 4 : 1; }
        internal void ArtDemoSwapLineup()
        {
            if (!artDemoRuntime || artDemoCave) return;
            battle.CancelQueue(); battle.Enemies.Clear(); effects.Clear(); artDemoLineup++;
            for (int i = 0; i < 5; i++) AddArtDemoEnemy(i == 4 && artDemoLineup % 2 != 0 ? 5 : i);
            RestoreAutoBattlePreference();
            battle.LastReward = "演示对练阵容已切换；保留人物进度，替换妖物不计击杀。"; RenderWorld(true);
        }
        internal void ArtDemoSelectWeapon(string id)
        {
            if (!artDemoRuntime || cultivation.Mortal) return;
            // Clearly marked demo equipment rack: does not grant any real archive.
            if (!battle.Progress.Inventory.Contains(id)) battle.Progress.Inventory.Add(id);
            ChooseWeapon(id); RenderWorld(true);
        }
        internal static string ArtDemoSmoke(string assets)
        {
            int checks = 0;
            Action<bool, string> expect = delegate(bool ok, string detail) { if (!ok) throw new InvalidOperationException("ArtDemo smoke: " + detail); checks++; };
            using (Form owner = new Form())
            using (AdventureController c = new AdventureController(owner, assets, true, null, true))
            {
                c.timer.Stop(); c.ConfigureArtDemo(new Rectangle(-22000, -22000, 1100, 700), false); c.timer.Stop(); c.ArtDemoSetSpeed(6);
                expect(c.demonstrationOnly && c.artDemoRuntime && !c.IsDemo, "playable memory mode never enters the old scripted demo");
                expect(c.cultivation.Mortal && c.cultivation.MortalKills == 0 && !c.AutoPlanning, "fresh mortal and manual combat");
                expect(!c.consent.CleanupEnabled && !c.consent.MemoryEnabled && !c.consent.AutoMaintenanceEnabled && c.connectivity == null, "all real maintenance and connectivity disabled");
                expect(!c.AwakenSpiritRoot(), "no early awakening before ten kills");
                long framesBeforeOrdering = c.PresentedFrames;
                expect(c.ArtDemoBringActorsForward() && c.PresentedFrames == framesBeforeOrdering, "native actor reorder does not redraw, activate or reopen windows");
                c.ArtDemoSetHostVisible(false);
                expect(!c.heroWindow.Visible && !c.landmarkWindow.Visible && c.enemyWindows.Values.All(w => !w.Visible), "minimized host hides its own actors");
                c.lastTick = c.clock.Elapsed.TotalSeconds - .1; c.Tick(null, EventArgs.Empty);
                expect(!c.heroWindow.Visible && c.enemyWindows.Values.All(w => !w.Visible), "background game tick cannot redisplay minimized actors");
                c.ArtDemoSetHostVisible(true);
                expect(c.heroWindow.Visible && c.enemyWindows.Values.Any(w => w.Visible), "restoring the host resumes its existing actor windows");
                c.ArtDemoSwapLineup();
                expect(c.cultivation.MortalKills == 0 && c.battle.Enemies.Any(e => e.NoFieldClear && !e.IsBoss) && c.battle.Enemies.Any(e => e.Elite && e.MonsterKind == "mushroom"), "demo mirror/elite lineup swap gives zero kill credit");
                for (int i = 0; i < 2200 && !c.cultivation.CanAwaken; i++)
                {
                    c.lastTick = c.clock.Elapsed.TotalSeconds - .1;
                    c.Tick(null, EventArgs.Empty); c.ArtDemoMaintainField(); c.ArtDemoStrike();
                }
                expect(c.cultivation.MortalKills == 10 && c.cultivation.CanAwaken, "ten production combat settlements unlock awakening");
                expect(c.ArtDemoAttackCount >= 10, "kills required actual production attack cooldowns and damage");
                expect(c.AwakenSpiritRoot() && !c.cultivation.Mortal && c.battle.Automatic && c.ArtDemoHeroStage == "qishi", "awakening enters qi-refining and automatic combat");
                int before = c.battle.Progress.Score;
                for (int i = 0; i < 1200 && c.battle.Progress.Score <= before; i++)
                {
                    c.lastTick = c.clock.Elapsed.TotalSeconds - .1; c.Tick(null, EventArgs.Empty); c.ArtDemoMaintainField();
                }
                expect(c.battle.Progress.Score > before, "production auto combat wins a fight without manual attacks");
                expect(c.battle.Progress.FreedBytes == 0 && c.cleanupTask == null && c.memoryReclaimTask == null, "combat never creates real maintenance or freed-byte claims");
            }
            return "ArtDemo memory smoke: " + checks + " checks passed; ten genuine combat kills, awakening, automatic combat; no real game archive access or maintenance.";
        }
    }

    internal sealed class ArtDemoActorFrame : IDisposable
    {
        internal KnightLayeredWindow Window;
        internal Bitmap Frame;
        internal Rectangle Bounds;
        internal string Role;
        internal int ZOrder;
        public void Dispose() { if (Frame != null) Frame.Dispose(); }
    }

    internal static class ArtDemoWindowOrdering
    {
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        internal static bool RaiseWithoutActivation(IntPtr window)
        {
            // NOSIZE | NOMOVE | NOACTIVATE | NOOWNERZORDER. No show flag:
            // activation only reorders already-visible windows in this Demo.
            return SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, 0x213);
        }
    }

    // Acceptance only: compare the *already presented* frames with real desktop
    // RGB pixels. A detailed scenery bitmap alone cannot satisfy this verifier.
    internal static class ArtDemoPixelVerifier
    {
        [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr parent);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        internal static void SortByDesktopZOrder(List<ArtDemoActorFrame> actors)
        {
            Dictionary<IntPtr, int> order = new Dictionary<IntPtr, int>();
            IntPtr window = GetTopWindow(IntPtr.Zero);
            for (int i = 0; window != IntPtr.Zero && i < 20000; i++, window = GetWindow(window, 2)) order[window] = i;
            foreach (ArtDemoActorFrame actor in actors)
            { int index; actor.ZOrder = actor.Window != null && order.TryGetValue(actor.Window.Handle, out index) ? index : Int32.MaxValue; }
        }
        internal static Dictionary<string, object> VerifyActor(ArtDemoActorFrame actor, IList<ArtDemoActorFrame> actors, Bitmap actual, Bitmap background, Bitmap expected, Point origin)
        {
            int samples = 0, matches = 0, sourceOpaque = 0;
            HashSet<int> colors = new HashSet<int>();
            for (int y = 0; y < actor.Frame.Height; y += 2)
                for (int x = 0; x < actor.Frame.Width; x += 2)
                {
                    Color source = actor.Frame.GetPixel(x, y); if (source.A < 245) continue;
                    sourceOpaque++;
                    Point desktop = new Point(actor.Bounds.X + x, actor.Bounds.Y + y);
                    int sx = desktop.X - origin.X, sy = desktop.Y - origin.Y;
                    if (sx < 0 || sy < 0 || sx >= actual.Width || sy >= actual.Height) continue;
                    bool covered = false;
                    foreach (ArtDemoActorFrame above in actors)
                        if (above != actor && above.ZOrder < actor.ZOrder && above.Bounds.Contains(desktop)
                            && above.Frame.GetPixel(desktop.X - above.Bounds.X, desktop.Y - above.Bounds.Y).A > 20) { covered = true; break; }
                    if (covered) continue;
                    Color p = expected.GetPixel(sx, sy), ground = background.GetPixel(sx, sy);
                    // Insist on pixels actually contributed by the actor, rather
                    // than accepting a pale costume that happens to match scenery.
                    if (Distance(p, ground) < 36) continue;
                    Color q = actual.GetPixel(sx, sy); samples++;
                    colors.Add((p.R >> 4) << 8 | (p.G >> 4) << 4 | (p.B >> 4));
                    if (Distance(p, q) <= 45) matches++;
                }
            double ratio = samples == 0 ? 0 : matches / (double)samples;
            if (sourceOpaque < 100 || samples < 100 || colors.Count < 8 || ratio < .90)
                throw new InvalidOperationException("真实桌面角色像素缺失、遮挡、透明或坐标不符：" + actor.Role + " samples=" + samples + " opaque=" + sourceOpaque + " colors=" + colors.Count + " match=" + ratio.ToString("F3"));
            return new Dictionary<string, object> { { "role", actor.Role }, { "bounds", actor.Bounds.ToString() }, { "sourceOpaqueSamples", sourceOpaque }, { "visibleDistinctSamples", samples }, { "matchingSamples", matches }, { "matchRatio", ratio }, { "distinctColors", colors.Count }, { "rgbToleranceSum", 45 }, { "minimumRatio", .90 } };
        }
        private static int Distance(Color a, Color b) { return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B); }
        internal static Bitmap ExpectedComposite(Bitmap background, IEnumerable<ArtDemoActorFrame> actors, Point origin)
        {
            Bitmap expected = new Bitmap(background);
            using (Graphics g = Graphics.FromImage(expected))
                foreach (ArtDemoActorFrame actor in actors.OrderByDescending(a => a.ZOrder)) g.DrawImageUnscaled(actor.Frame, actor.Bounds.X - origin.X, actor.Bounds.Y - origin.Y);
            return expected;
        }
        internal static void TransparentNegativeCheck()
        {
            using (Bitmap blank = new Bitmap(100, 120, PixelFormat.Format32bppArgb))
            using (Bitmap desktop = new Bitmap(100, 120))
            {
                bool rejected = false;
                try { VerifyActor(new ArtDemoActorFrame { Frame = blank, Bounds = new Rectangle(0, 0, 100, 120), Role = "transparent-negative" }, new ArtDemoActorFrame[0], desktop, desktop, desktop, Point.Empty); }
                catch (InvalidOperationException) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("纯透明帧被像素校验误接受。");
            }
        }
    }

    internal static class ArtDemoSession
    {
        internal static int Run(string[] args)
        {
            string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            if (args[0] == "--art-demo-smoke")
            {
                Environment.SetEnvironmentVariable("DESK_TO_GOD_ART_DEMO_ASSETS", null, EnvironmentVariableTarget.Process);
                string result = AdventureController.ArtDemoSmoke(assets);
                File.WriteAllText(args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "art-demo-smoke.log"), result, new UTF8Encoding(false));
                return 0;
            }
            string pack = Environment.GetEnvironmentVariable("DESK_TO_GOD_ART_DEMO_ASSETS");
            if (String.IsNullOrWhiteSpace(pack))
            {
                pack = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArtDemoAssets");
                Environment.SetEnvironmentVariable("DESK_TO_GOD_ART_DEMO_ASSETS", pack, EnvironmentVariableTarget.Process);
            }
            if (!Directory.Exists(pack)) throw new DirectoryNotFoundException("找不到独立美术包：" + pack + "；请先运行美术Demo/build-demo.ps1。");
            string output = args[0] == "--art-demo-accept" ? (args.Length > 1 ? Path.GetFullPath(args[1]) : null) : null;
            if (args[0] == "--art-demo-accept" && output == null) throw new ArgumentException("--art-demo-accept 需要一个全新的截图输出目录。");
            if (output != null && Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("验收输出目录必须为空，既有证据不会覆盖。");
            using (ArtDemoForm form = new ArtDemoForm(assets, pack, output)) Application.Run(form);
            return ArtDemoForm.LastExitCode;
        }
    }

    internal sealed class ArtDemoForm : Form
    {
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        internal static int LastExitCode;
        private readonly string assets, pack, output;
        private readonly Timer pulse = new Timer(), driver = new Timer();
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private AdventureController game;
        private readonly Label realm = new Label(), stats = new Label(), notice = new Label(), mode = new Label();
        private readonly Button strike = new Button(), awaken = new Button(), automatic = new Button(), speed = new Button(), cave = new Button();
        private readonly Button lineup = new Button();
        private readonly Panel sidebar = new Panel();
        private readonly List<string> evidence = new List<string>();
        private readonly List<object> pixelChecks = new List<object>(), layoutChecks = new List<object>();
        private readonly List<Image> icons = new List<Image>();
        private Image battleBackground, caveBackground;
        private bool caveMode, fast, closing, accepting, capturing, acceptanceComplete;
        private int acceptPhase, lastKills = -1, scoreBeforeAuto, attacksBeforeAuto;
        private double nextPhase;
        private readonly Dictionary<string, string> realArchives = new Dictionary<string, string>();
        private Rectangle Stage { get { return new Rectangle(22, 134, Math.Max(500, ClientSize.Width - 354), ClientSize.Height - 178); } }
        private Rectangle StageOnScreen { get { Rectangle actors = Stage; actors.Height = Math.Max(200, actors.Height - 76); return RectangleToScreen(actors); } }
        internal ArtDemoForm(string assets, string pack, string output)
        {
            this.assets = assets; this.pack = pack; this.output = output;
            Text = "桌面修仙传 · 美术可玩 Demo"; StartPosition = FormStartPosition.CenterScreen; TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            Rectangle work = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(1040, work.Width), Math.Min(690, work.Height));
            Size = new Size(Math.Min(1256, work.Width), Math.Min(799, work.Height));
            StartPosition = FormStartPosition.Manual; Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);
            BackColor = Color.FromArgb(236, 241, 237); ForeColor = Color.FromArgb(38, 65, 62);
            Font = new Font("Microsoft YaHei UI", 10); DoubleBuffered = true;
            if (output != null) { Directory.CreateDirectory(output); HashRealArchives(); }
            realm.Font = new Font("Microsoft YaHei UI", 18, FontStyle.Bold); realm.AutoSize = false;
            stats.AutoSize = false; notice.AutoSize = false; notice.Font = new Font("Microsoft YaHei UI", 10); mode.AutoSize = false;
            sidebar.AutoScroll = true; sidebar.AutoScrollMargin = new Size(0, 12); sidebar.BackColor = BackColor;
            Controls.Add(sidebar); sidebar.Controls.AddRange(new Control[] { realm, stats, mode }); Controls.Add(notice);
            AddButton(strike, "出招 · 点击一次一招", delegate { game.ArtDemoStrike(); });
            AddButton(new Button(), "格挡 · 卸去来势", delegate { game.ArtDemoBlock(); });
            AddButton(awaken, "觉醒灵根 · 十次历练后", delegate { bool ok = game.AwakenSpiritRoot(); if (ok) Log("AWAKEN real CultivationProgress.Awaken: " + game.Cultivation.RootName); UpdateStatus(); });
            AddButton(automatic, "自动作战 · 尚未觉醒", delegate { game.SetAutomatic(!game.AutoPlanning); UpdateStatus(); });
            AddButton(speed, "演示快进 ×1", delegate { fast = !fast; game.ArtDemoSetSpeed(fast ? 4 : 1); UpdateStatus(); });
            AddButton(cave, "切换 · 筑基洞天预置档", delegate { StartGame(!caveMode); });
            Button weapon = new Button(); AddButton(weapon, "演示装备架 · 切换法器", delegate {
                if (game.Cultivation.Mortal) { notice.Text = "凡人先用初始长枪完成历练，觉醒后可试用演示法器。"; return; }
                string[] ids = new[] { "spear", "wave", "laser", "boomerang", "blackhole" }; int index = Array.IndexOf(ids, game.Battle.Weapon.Id); game.ArtDemoSelectWeapon(ids[(index + 1) % ids.Length]);
            });
            Button plant = new Button(); AddButton(plant, "洞天 · 播种 / 开炉", delegate {
                if (!game.DongtianUnlocked) return;
                if (game.Dongtian.Fields[1] == null) game.DongtianPlant(1, "ningshen"); else game.DongtianStartPill("juqi");
                notice.Text = game.Dongtian.LastNotice;
            });
            AddButton(lineup, "镜妖对练 · 切换阵容", delegate { game.ArtDemoSwapLineup(); Log("FIXTURE swapped combat lineup; no kill or progression credit"); });
            LoadImages();
            pulse.Interval = 100; pulse.Tick += delegate { if (game == null || capturing) return; game.ArtDemoMaintainField(); UpdateStatus(); if (game.ArtDemoFailure != null) Fail(new InvalidOperationException(game.ArtDemoFailure)); };
            Shown += delegate {
                // A hidden launcher suppresses the inherited first Show command.
                // This user-requested interactive demo must explicitly show its
                // own host before any topmost desktop actors are presented.
                WindowState = FormWindowState.Normal; ShowWindow(Handle, 1); BringToFront(); Activate();
                StartGame(false); pulse.Start();
                if (output != null) { driver.Interval = 90; driver.Tick += AcceptanceTick; nextPhase = elapsed.Elapsed.TotalSeconds + 1.2; driver.Start(); }
            };
            Move += delegate { if (game != null && WindowState != FormWindowState.Minimized) game.MoveArtDemoStage(StageOnScreen); };
            Resize += delegate {
                if (WindowState == FormWindowState.Minimized) { if (game != null) game.ArtDemoSetHostVisible(false); return; }
                LayoutControls(); if (game != null) { game.MoveArtDemoStage(StageOnScreen); game.ArtDemoSetHostVisible(true); } Invalidate();
            };
            Activated += delegate { if (game != null && WindowState != FormWindowState.Minimized && !game.ArtDemoBringActorsForward()) Log("WINDOW native actor reorder failed"); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (output != null && !acceptanceComplete && LastExitCode == 0)
                {
                    LastExitCode = 1; string error = "自动验收完成前窗口被关闭：" + e.CloseReason;
                    Log("ABORT " + error); WriteReport(false, false, error);
                }
                closing = true; driver.Stop(); pulse.Stop(); if (game != null) { game.Dispose(); game = null; }
            };
            FormClosed += delegate { foreach (Image icon in icons) icon.Dispose(); if (battleBackground != null) battleBackground.Dispose(); if (caveBackground != null) caveBackground.Dispose(); pulse.Dispose(); driver.Dispose(); };
            LayoutControls();
        }
        private void AddButton(Button button, string text, Action action)
        {
            button.Text = text; button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Color.FromArgb(176, 197, 186);
            button.BackColor = Color.FromArgb(248, 249, 242); button.ForeColor = ForeColor; button.Cursor = Cursors.Hand;
            button.Click += delegate { if (game != null) action(); }; sidebar.Controls.Add(button);
        }
        private void LayoutControls()
        {
            int width = 264;
            sidebar.SetBounds(ClientSize.Width - 310, 81, 286, Math.Max(180, ClientSize.Height - 120));
            sidebar.AutoScrollPosition = Point.Empty;
            realm.SetBounds(0, 64, width, 42); stats.SetBounds(0, 114, width, 93); mode.SetBounds(0, 2, width, 38);
            int y = 218;
            foreach (Button button in sidebar.Controls.OfType<Button>()) { button.SetBounds(0, y, width, 36); y += 43; }
            notice.SetBounds(38, ClientSize.Height - 116, Stage.Width - 34, 62);
        }
        private void StartGame(bool cave)
        {
            if (game != null) game.Dispose(); caveMode = cave;
            game = new AdventureController(this, assets, true, null, true); game.ConfigureArtDemo(StageOnScreen, cave);
            game.ArtDemoSetSpeed(fast ? 4 : 1); UpdateStatus(); Invalidate();
            Log(cave ? "PRESET independent zhuji memory fixture; not a progression claim" : "NEW mortal memory session; 0 kills; no persisted game profile");
        }
        private void UpdateStatus()
        {
            if (game == null) return; CultivationProgress c = game.Cultivation;
            realm.Text = c.Mortal ? "凡人 · 红尘历练" : c.RealmName + " · " + c.Layer + " 层";
            int marrow; game.Lingyu.Materials.TryGetValue("shuiyue-lingsui", out marrow);
            stats.Text = "气血  " + game.Battle.Progress.Health + " / " + game.Battle.Progress.MaxHealth + "\n" + (c.Mortal ? "有效击杀  " + c.MortalKills + " / 10" : "灵根  " + c.RootName + " · 击杀 " + game.ArtDemoSettledKills) + "\n修为  " + c.Qi + "    灵髓  " + marrow + "\n兵器  " + (game.ArtDemoHeroStage == "mortal" ? "灵铁短剑" : game.ArtDemoHeroStage == "qishi" ? "青钢剑" : "凝玉剑");
            mode.Text = caveMode ? "筑基预置档 · 独立内存" : "战斗成长档 · 内存游玩";
            awaken.Enabled = c.CanAwaken; strike.Enabled = !game.Battle.Progress.Dead && !caveMode;
            automatic.Enabled = c.AutoBattleUnlocked && !caveMode;
            automatic.Text = !c.AutoBattleUnlocked ? "自动作战 · 尚未觉醒" : game.AutoPlanning ? "自动作战 · 已开启" : "自动作战 · 已关闭";
            speed.Text = "战斗 / 吐纳 ×" + (fast ? "4" : "1") + " · 演示快进";
            cave.Text = caveMode ? "返回 · 全新凡人游玩档" : "切换 · 筑基洞天预置档";
            notice.Text = caveMode ? game.Dongtian.LastNotice.Length > 0 ? game.Dongtian.LastNotice : game.Battle.LastReward : game.Battle.LastReward;
        }
        private void LoadImages()
        {
            string battle = Path.Combine(pack, "scene-battle.png"), cave = Path.Combine(pack, "scene-dongtian.png");
            if (File.Exists(battle)) battleBackground = Image.FromFile(battle);
            if (File.Exists(cave)) caveBackground = Image.FromFile(cave);
            foreach (string name in new[] { "item-spirit-stone", "item-pill", "item-material", "item-scroll", "item-marrow" })
            { string path = Path.Combine(pack, name + ".png"); if (File.Exists(path)) icons.Add(Image.FromFile(path)); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Font title = new Font("Microsoft YaHei UI", 27, FontStyle.Bold)) using (Brush ink = new SolidBrush(ForeColor)) g.DrawString("桌 面 修 仙 传", title, ink, 27, 22);
            using (Font small = new Font("Microsoft YaHei UI", 10)) using (Brush muted = new SolidBrush(Color.FromArgb(90, 116, 105)))
            {
                g.DrawString("可玩美术纵切  /  凡人十战，灵根初醒，一隅洞天", small, muted, 32, 82);
                g.DrawString("独立 Demo · 游戏状态仅存内存 · 网络、文件清理、内存维护全部关闭", small, muted, 31, ClientSize.Height - 29);
            }
            Rectangle stage = Stage;
            using (LinearGradientBrush wash = new LinearGradientBrush(stage, Color.FromArgb(218, 230, 219), Color.FromArgb(242, 244, 225), 90)) g.FillRectangle(wash, stage);
            Image background = caveMode ? caveBackground : battleBackground;
            if (background != null) g.DrawImage(background, stage);
            else
            {
                using (Brush mountain = new SolidBrush(Color.FromArgb(73, 132, 151, 130))) g.FillPolygon(mountain, new[] { new Point(stage.Left, stage.Bottom - 140), new Point(stage.Left + 100, stage.Top + 70), new Point(stage.Left + 230, stage.Bottom - 170), new Point(stage.Left + 370, stage.Top + 42), new Point(stage.Right, stage.Bottom - 120), new Point(stage.Right, stage.Bottom), new Point(stage.Left, stage.Bottom) });
                using (Brush mist = new SolidBrush(Color.FromArgb(170, 236, 243, 227))) g.FillEllipse(mist, stage.Left - 50, stage.Bottom - 230, stage.Width + 100, 330);
            }
            using (Pen border = new Pen(Color.FromArgb(155, 181, 164))) g.DrawRectangle(border, stage);
            using (Font f = new Font("Microsoft YaHei UI", 12, FontStyle.Bold)) using (Brush b = new SolidBrush(Color.FromArgb(62, 95, 78))) g.DrawString(caveMode ? "筑基洞天  ·  预置展示" : "青岚山麓  ·  点妖出招 / 点主角格挡", f, b, stage.Left + 18, stage.Top + 15);
            for (int i = 0; i < icons.Count; i++) g.DrawImage(icons[i], new Rectangle(stage.Right - 58 - i * 51, stage.Top + 48, 42, 42));
            if (icons.Count > 0) using (Font f = new Font("Microsoft YaHei UI", 8)) using (Brush b = new SolidBrush(ForeColor)) g.DrawString("道具图鉴 · 示意图，无虚构余额", f, b, stage.Right - 267, stage.Top + 94);
        }
        private void Log(string text) { evidence.Add(DateTime.UtcNow.ToString("o") + " " + text); }
        private void HashRealArchives()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant");
            foreach (string name in new[] { "adventure-v1.txt", "cultivation-v2.txt", "baiye-v1.txt", "lingyu-v1.txt", "dongtian-v1.txt", "maintenance-consent.txt" })
            { string path = Path.Combine(root, name); realArchives[name] = Hash(path); }
        }
        private static string Hash(string path)
        {
            if (!File.Exists(path)) return "missing";
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        }
        private void CaptureFrame(string name)
        {
            List<object> checks = CaptureVerifiedPixels(name);
            pixelChecks.Add(new Dictionary<string, object> { { "image", name + ".png" }, { "actors", checks.ToArray() }, { "method", "frozen presented frame + real desktop background + native Z-order alpha composite" } });
            Log("CAPTURE verified real hero" + (caveMode ? "" : "+enemy") + " opaque visible pixels at native coordinates: " + name + "; hero=" + game.ArtDemoHeroBounds);
        }
        private List<object> CaptureVerifiedPixels(string name)
        {
            bool wasRunning = game.PauseArtDemoCapture(); capturing = true;
            List<ArtDemoActorFrame> actors = null;
            Bitmap actual = null, background = null, expected = null;
            try
            {
                if (IsIconic(Handle))
                {
                    // Acceptance owns this process. Restore only its own host
                    // after a desktop-wide minimize; never manipulate another
                    // application or dismiss an occluding window.
                    WindowState = FormWindowState.Normal; ShowWindow(Handle, 9);
                    List<ArtDemoActorFrame> restoreOrder = game.SnapshotArtDemoActors();
                    foreach (ArtDemoActorFrame actor in restoreOrder.OrderByDescending(a => a.ZOrder)) actor.Window.BringToFront();
                    foreach (ArtDemoActorFrame actor in restoreOrder) actor.Dispose();
                    Log("RESTORE own minimized Demo host before native capture; gameplay state unchanged");
                }
                Refresh(); Application.DoEvents(); actors = game.SnapshotArtDemoActors();
                Point header = PointToScreen(new Point(40, 40));
                if (!IsWindowVisible(Handle) || GetAncestor(WindowFromPoint(header), 2) != Handle)
                    throw new InvalidOperationException("Demo 主展示窗口未实际显示，或标题区域被其他窗口遮挡。nativeVisible=" + IsWindowVisible(Handle) + "; header=" + header + "; root=" + GetAncestor(WindowFromPoint(header), 2) + "; host=" + Handle + "; bounds=" + Bounds);
                ArtDemoActorFrame hero = actors.FirstOrDefault(a => a.Role == "hero");
                if (hero == null) throw new InvalidOperationException("截图缺少已显示的真实角色窗口。");
                actual = DesktopFrameCapture.CaptureWindow(this);
                if (name != null) actual.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
                try
                {
                    foreach (ArtDemoActorFrame actor in actors) actor.Window.Hide();
                    // The host is a static, paused scene during these two native
                    // captures; no DrawToBitmap or invented background is used.
                    background = DesktopFrameCapture.CaptureWindow(this);
                }
                finally
                {
                    foreach (ArtDemoActorFrame actor in actors.OrderByDescending(a => a.ZOrder)) if (!actor.Window.IsDisposed) actor.Window.Show();
                }
                expected = ArtDemoPixelVerifier.ExpectedComposite(background, actors, Bounds.Location);
                List<object> checks = new List<object>();
                checks.Add(ArtDemoPixelVerifier.VerifyActor(hero, actors, actual, background, expected, Bounds.Location));
                if (!caveMode)
                {
                    object verifiedEnemy = null; List<string> failures = new List<string>();
                    foreach (ArtDemoActorFrame enemy in actors.Where(a => a.Role == "enemy"))
                        try { verifiedEnemy = ArtDemoPixelVerifier.VerifyActor(enemy, actors, actual, background, expected, Bounds.Location); break; }
                        catch (InvalidOperationException error) { failures.Add(error.Message); }
                    if (verifiedEnemy == null) throw new InvalidOperationException("截图没有任何可验证的存活妖物像素：" + String.Join("; ", failures.ToArray()));
                    checks.Add(verifiedEnemy);
                }
                return checks;
            }
            finally
            {
                if (expected != null) expected.Dispose(); if (background != null) background.Dispose(); if (actual != null) actual.Dispose();
                if (actors != null) foreach (ArtDemoActorFrame actor in actors) actor.Dispose();
                capturing = false; game.ResumeArtDemoCapture(wasRunning);
            }
        }
        private void VerifyCaptureNegativeCases()
        {
            ArtDemoPixelVerifier.TransparentNegativeCheck();
            bool wasRunning = game.PauseArtDemoCapture(); capturing = true;
            try
            {
                using (Form cover = new Form())
                {
                    cover.FormBorderStyle = FormBorderStyle.None; cover.ShowInTaskbar = false; cover.TopMost = true;
                    cover.StartPosition = FormStartPosition.Manual; cover.Bounds = game.ArtDemoHeroBounds; cover.BackColor = Color.Magenta;
                    cover.Show(); cover.BringToFront(); cover.Refresh(); Application.DoEvents();
                    bool rejected = false;
                    try { CaptureVerifiedPixels(null); }
                    catch (InvalidOperationException error)
                    {
                        if (!error.Message.StartsWith("真实桌面角色像素", StringComparison.Ordinal)) throw;
                        rejected = true; Log("PIXEL NEGATIVE real topmost magenta occluder rejected: " + error.Message);
                    }
                    cover.Close();
                    if (!rejected) throw new InvalidOperationException("真实置顶遮挡窗口被像素校验误接受。");
                }
                Log("PIXEL NEGATIVE fully transparent frame rejected before color matching");
            }
            finally { capturing = false; game.ResumeArtDemoCapture(wasRunning); }
        }
        private void VerifySmallScreenLayout()
        {
            Rectangle previous = Bounds, work = Screen.FromControl(this).WorkingArea;
            foreach (Size requested in new[] { new Size(1256, 728), new Size(1040, 690) })
            {
                Bounds = new Rectangle(work.Location, new Size(Math.Min(requested.Width, work.Width), Math.Min(requested.Height, work.Height)));
                Application.DoEvents();
                if (!work.Contains(Bounds)) throw new InvalidOperationException("小屏初始窗口超出 WorkingArea。");
                int visibleButtons = 0;
                foreach (Button button in sidebar.Controls.OfType<Button>())
                {
                    sidebar.ScrollControlIntoView(button); Application.DoEvents();
                    if (!sidebar.RectangleToScreen(sidebar.ClientRectangle).Contains(button.RectangleToScreen(button.ClientRectangle)))
                        throw new InvalidOperationException("侧栏按钮滚动后仍被裁切：" + button.Text);
                    visibleButtons++;
                }
                layoutChecks.Add(new Dictionary<string, object> { { "requestedWindow", requested.ToString() }, { "actualBounds", Bounds.ToString() }, { "clientSize", ClientSize.ToString() }, { "workingArea", work.ToString() }, { "buttonsReachableByScroll", visibleButtons }, { "lastButton", lineup.Text }, { "lastButtonVisible", true } });
                Log("LAYOUT actual window=" + Size + ", client=" + ClientSize + "; all " + visibleButtons + " buttons visibly reachable by scroll");
            }
            sidebar.ScrollControlIntoView(lineup); CaptureFrame("05-small-screen-scroll");
            Bounds = previous;
        }
        private void AcceptanceTick(object sender, EventArgs e)
        {
            if (closing || accepting || elapsed.Elapsed.TotalSeconds < nextPhase) return;
            accepting = true;
            try
            {
                if (elapsed.Elapsed.TotalSeconds > 180) throw new TimeoutException("真实战斗验收超时；不会通过直接修改击杀数完成验收。");
                if (acceptPhase == 0)
                {
                    if (!game.Cultivation.Mortal || game.Cultivation.MortalKills != 0 || game.AutoPlanning) throw new InvalidOperationException("初始凡人状态不正确。");
                    if (!Screen.FromControl(this).WorkingArea.Contains(Bounds)) throw new InvalidOperationException("初始窗体超出可用桌面。");
                    CaptureFrame("01-mortal-playable"); VerifyCaptureNegativeCases(); lineup.PerformClick(); CaptureFrame("01b-mortal-mirror-shard"); speed.PerformClick(); game.ArtDemoSetSpeed(6); fast = true;
                    Log("ACCEPT explicit demo time acceleration x6; enemy HP, damage and ten-kill rule unchanged"); acceptPhase = 1;
                }
                else if (acceptPhase == 1)
                {
                    if (game.Cultivation.MortalKills != lastKills) { lastKills = game.Cultivation.MortalKills; Log("COMBAT mortal settled kills=" + lastKills + "; real strikes=" + game.ArtDemoAttackCount); }
                    if (game.Cultivation.MortalKills >= 10)
                    {
                        if (!game.Cultivation.CanAwaken) throw new InvalidOperationException("十杀未解锁觉醒。");
                        CaptureFrame("02-ten-real-kills"); awaken.PerformClick();
                        if (game.Cultivation.Mortal || !game.AutoPlanning) throw new InvalidOperationException("觉醒未开启炼气自动战斗。");
                        scoreBeforeAuto = game.Battle.Progress.Score; attacksBeforeAuto = game.ArtDemoAttackCount;
                        acceptPhase = 2; nextPhase = elapsed.Elapsed.TotalSeconds + 2;
                    }
                    else { strike.PerformClick(); if (!game.Battle.Progress.Dead) game.ArtDemoBlock(); }
                }
                else if (acceptPhase == 2)
                {
                    if (game.Battle.Progress.Score <= scoreBeforeAuto) return;
                    if (game.ArtDemoAttackCount != attacksBeforeAuto) throw new InvalidOperationException("自动战斗验收期间出现了手动出招。");
                    Log("AUTO real automatic combat increased score " + scoreBeforeAuto + " -> " + game.Battle.Progress.Score + "; no manual attack button events");
                    CaptureFrame("03-qishi-automatic");
                    // Recompose the explicitly labelled demo lineup after the
                    // automatic victory was already proved; no progress is paid.
                    lineup.PerformClick(); lineup.PerformClick();
                    CaptureFrame("03b-qishi-mirror-wraith"); cave.PerformClick(); acceptPhase = 3; nextPhase = elapsed.Elapsed.TotalSeconds + 1.2;
                }
                else
                {
                    if (game.ArtDemoHeroStage != "zhuji" || !game.DongtianUnlocked) throw new InvalidOperationException("筑基预置档未就绪。");
                    CaptureFrame("04-zhuji-dongtian-preset");
                    VerifySmallScreenLayout();
                    string archiveRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant");
                    bool unchanged = realArchives.All(p => Hash(Path.Combine(archiveRoot, p.Key)) == p.Value);
                    Log("ISOLATION startup/finish archive hash comparison=" + unchanged + "; existing production instance may independently change its own archive");
                    acceptanceComplete = true; WriteReport(true, unchanged, null); LastExitCode = 0; Close();
                }
            }
            catch (Exception error) { Fail(error); }
            finally { accepting = false; }
        }
        private void WriteReport(bool passed, bool unchanged, string error)
        {
            if (output == null) return;
            File.WriteAllLines(Path.Combine(output, "playthrough.log"), evidence.ToArray(), new UTF8Encoding(false));
            var report = new Dictionary<string, object> {
                { "passed", passed }, { "error", error }, { "renderer", "AdventureRenderer + real WS_EX_LAYERED windows" },
                { "gameplay", "AdventureController / AdventureBattle / CultivationProgress; real button events; ten real kills before awakening" },
                { "isolation", "demonstrationOnly=true; game models in memory; no production game profile reads/writes; acceptance only reads hashes; no network/maintenance" },
                { "realArchivesUnchangedDuringSample", unchanged }, { "presetIsProgressionProof", false }, { "capture", "DesktopFrameCapture desktop pixels, never DrawToBitmap" },
                { "pixelChecks", pixelChecks.ToArray() }, { "layoutChecks", layoutChecks.ToArray() }, { "captureNegativeCases", "fully transparent frame and real topmost occluder must be rejected" },
                { "output", output }, { "assets", pack }, { "utc", DateTime.UtcNow.ToString("o") }, { "evidence", evidence.ToArray() }
            };
            File.WriteAllText(Path.Combine(output, "acceptance-report.json"), new JavaScriptSerializer().Serialize(report), new UTF8Encoding(false));
        }
        private void Fail(Exception error)
        {
            if (closing) return; LastExitCode = 1; Log("FAIL " + error);
            WriteReport(false, false, error.ToString());
            if (output == null) MessageBox.Show(error.Message, "美术 Demo 未能运行"); Close();
        }
    }
}
