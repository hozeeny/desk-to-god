using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace NetBunny
{
    // Stopped-timer/private controller fixtures and injected display rectangles.
    // No visible forms, native input, display-setting changes or real archives.
    internal static class DesktopDisplayLayoutTests
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static int checks, failures;
        static Type snapshotType;
        sealed class CountedRandom : Random
        {
            internal int Calls;
            public override double NextDouble() { Calls++; return .5; }
            public override int Next(int max) { Calls++; return 0; }
        }
        static void Check(bool okay, string message) { checks++; if (!okay) failures++; Console.WriteLine((okay ? "PASS " : "FAIL ") + message); }
        static T Field<T>(object value, string name) { return (T)value.GetType().GetField(name, Hidden).GetValue(value); }
        static void Set(object value, string name, object next) { value.GetType().GetField(name, Hidden).SetValue(value, next); }
        static object Snapshot(string name, Rectangle work, bool primary)
        { return Activator.CreateInstance(snapshotType, Hidden, null, new object[] { name, work, primary }, null); }
        static bool Refresh(AdventureController game, params object[] displays)
        {
            Array values = Array.CreateInstance(snapshotType, displays.Length);
            for (int i = 0; i < displays.Length; i++) values.SetValue(displays[i], i);
            return (bool)typeof(AdventureController).GetMethod("RefreshDesktopDisplayLayout", Hidden, null, new[] { values.GetType() }, null).Invoke(game, new object[] { values });
        }
        static PointF Position(AdventureController game) { return new PointF((float)Field<double>(game, "x"), (float)Field<double>(game, "y")); }
        static PointF Target(AdventureController game) { return new PointF((float)Field<double>(game, "targetX"), (float)Field<double>(game, "targetY")); }
        static bool Same(PointF a, PointF b) { return Math.Abs(a.X - b.X) < .01 && Math.Abs(a.Y - b.Y) < .01; }
        static PointF Map(PointF point, Rectangle old, Rectangle next, Size size)
        {
            double px = point.X + next.Left - old.Left, py = point.Y + next.Top - old.Top;
            return new PointF((float)Math.Max(next.Left + 4, Math.Min(next.Right - size.Width - 4, px)),
                (float)Math.Max(next.Top + 2, Math.Min(next.Bottom - size.Height - 4, py)));
        }
        static AdventureEnemy Enemy(string id, Rectangle area, bool boss)
        { return new AdventureEnemy { Id = id, IsVirtual = true, IsBoss = boss, X = area.Left + 310, Y = area.Top + 190, TargetX = area.Left + 620, TargetY = area.Top + 370, Health = 70, MaxHealth = 100, PatrolSector = 4 }; }
        static void Prepare(AdventureController game, Rectangle work)
        {
            game.Battle.CancelQueue(); game.Battle.Enemies.Clear(); Set(game, "area", work);
            Set(game, "x", work.Left + 160d); Set(game, "y", work.Top + 80d);
            Set(game, "targetX", work.Left + 650d); Set(game, "targetY", work.Top + 390d);
            Set(game, "blackholeX", work.Left + 700d); Set(game, "blackholeY", work.Top + 450d);
            Set(game, "lastSave", -4321d);
        }
        static void ExistingChangeScreenRegression(AdventureController game)
        {
            Screen screen = Screen.PrimaryScreen;
            Prepare(game, screen.WorkingArea);
            AdventureEnemy enemy = Enemy("same-screen-live-order", screen.WorkingArea, false);
            game.Battle.Enemies.Add(enemy); game.Battle.Authorize(enemy);
            CountedRandom random = new CountedRandom(); Set(game, "random", random);
            double tx = enemy.TargetX, ty = enemy.TargetY; int sector = enemy.PatrolSector;
            game.ChangeScreen(screen); // Real existing entry, not a replacement fixture implementation.
            Check(enemy.TargetX == tx && enemy.TargetY == ty && enemy.PatrolSector == sector,
                "desktop ChangeScreen keeps the existing patrol target and sector instead of rerolling them");
            Check(random.Calls == 0, "desktop ChangeScreen consumes no story or patrol random draws");
            Check(game.Battle.Target == enemy && enemy.Authorized && enemy.Authorization == AdventureAuthorization.Manual,
                "desktop ChangeScreen keeps the current combat order and authorization");
        }
        static void InjectedRecoveryChecks(AdventureController game)
        {
            Rectangle old = new Rectangle(-1920, -100, 1920, 1040), other = new Rectangle(0, 0, 1600, 900);
            Prepare(game, old); CountedRandom random = new CountedRandom(); Set(game, "random", random);
            object a = Snapshot("A", old, false), b = Snapshot("B", other, true);
            Refresh(game, a, b);
            Check(Field<string>(game, "desktopDisplayDeviceName") == "A" && game.WorkingArea == old,
                "initial tracking identifies the currently selected working area rather than array order or primary screen");
            AdventureEnemy ordinary = Enemy("ordinary", old, false), started = Enemy("started-maintenance", old, true);
            game.Battle.Enemies.Add(ordinary); game.Battle.Authorize(ordinary);
            game.Battle.Enemies.Add(started); game.Battle.Authorize(started); started.Cleaning = started.CleanupStarted = true;
            started.VisualDefeated = started.FellInCombat = true; started.Health = 0;
            game.Battle.SetAutomatic(true);
            object loot = Activator.CreateInstance(typeof(AdventureController).GetNestedType("LootView", BindingFlags.NonPublic), true);
            var lootFields = loot.GetType(); lootFields.GetField("X", Hidden).SetValue(loot, old.Left + 800d); lootFields.GetField("Y", Hidden).SetValue(loot, old.Top + 550d);
            lootFields.GetField("Window", Hidden).SetValue(loot, new KnightLayeredWindow());
            ((System.Collections.IList)typeof(AdventureController).GetField("lootViews", Hidden).GetValue(game)).Add(loot);
            PointF hero = Position(game), target = Target(game), op = new PointF((float)ordinary.X, (float)ordinary.Y), ot = new PointF((float)ordinary.TargetX, (float)ordinary.TargetY);
            PointF sp = new PointF((float)started.X, (float)started.Y), st = new PointF((float)started.TargetX, (float)started.TargetY);
            PointF pull = new PointF((float)Field<double>(game, "blackholeX"), (float)Field<double>(game, "blackholeY"));
            Rectangle next = new Rectangle(-1100, -240, 1100, 760);
            Check(Refresh(game, b, Snapshot("A", next, false)), "the same device is followed when its origin and working area change");
            Check(game.WorkingArea == next && Field<string>(game, "desktopDisplayDeviceName") == "A", "same-device recovery wins over a different primary display");
            Check(Same(Position(game), Map(hero, old, next, new Size(220, 240))) && Same(Target(game), Map(target, old, next, new Size(220, 240))),
                "hero and roaming target translate by origin delta and clamp with the hero size");
            Check(Same(new PointF((float)ordinary.X, (float)ordinary.Y), Map(op, old, next, new Size(130, 170)))
                && Same(new PointF((float)ordinary.TargetX, (float)ordinary.TargetY), Map(ot, old, next, new Size(130, 170))),
                "ordinary actor and its patrol target use the same bounded translation");
            Check(Same(new PointF((float)started.X, (float)started.Y), Map(sp, old, next, new Size(190, 220)))
                && Same(new PointF((float)started.TargetX, (float)started.TargetY), Map(st, old, next, new Size(190, 220))),
                "a started maintenance actor and both target coordinates rebase without retiring its order");
            Check(Same(new PointF((float)Field<double>(game, "blackholeX"), (float)Field<double>(game, "blackholeY")), Map(pull, old, next, Size.Empty)),
                "the live field-pull point cannot remain on a removed monitor");
            PointF expectedLoot = Map(new PointF(old.Left + 800, old.Top + 550), old, next, new Size(160, 155));
            Check(Same(new PointF((float)(double)lootFields.GetField("X", Hidden).GetValue(loot), (float)(double)lootFields.GetField("Y", Hidden).GetValue(loot)), expectedLoot),
                "transient loot coordinates move with the selected working area");
            Check(random.Calls == 0 && ordinary.PatrolSector == 4 && started.PatrolSector == 4 && game.Battle.QueueCount == 2
                && game.Battle.Target == ordinary && ordinary.QueueNumber == 1 && started.QueueNumber == 2 && game.AutoPlanning
                && ordinary.Health == 70 && started.Cleaning && started.CleanupStarted && started.VisualDefeated && started.FellInCombat,
                "display recovery preserves the queue, automatic preference, actor health and started-work state");
            Check(Field<double>(game, "lastSave") == -4321 && game.Battle.Progress.FreedBytes == 0 && !ordinary.KillRewarded,
                "geometry recovery neither saves progress nor invents a combat or maintenance result");
            PointF stable = Position(game), stableTarget = Target(game);
            Check(!Refresh(game, Snapshot("A", next, false), b) && Same(stable, Position(game)) && Same(stableTarget, Target(game)) && random.Calls == 0,
                "an identical repeated display snapshot is inert");
            Check(!Refresh(game) && !Refresh(game, Snapshot("bad", Rectangle.Empty, true)) && game.WorkingArea == next,
                "no valid display snapshot defers recovery instead of applying an empty working area");
            Rectangle fallback = new Rectangle(20, 30, 850, 620);
            Set(game, "dragging", true); PointF beforeDrag = Position(game);
            Check(!Refresh(game, Snapshot("B", fallback, true)) && game.WorkingArea == next && Same(beforeDrag, Position(game)) && Field<string>(game, "desktopDisplayDeviceName") == "A",
                "an active hero drag defers the entire migration and retains its tracked display for retry");
            Set(game, "dragging", false); ordinary.Dragging = true;
            Check(!Refresh(game, Snapshot("B", fallback, true)) && game.WorkingArea == next && Same(beforeDrag, Position(game)),
                "an active enemy drag also defers migration without invalidating its native grab offset");
            ordinary.Dragging = false;
            Check(Refresh(game, Snapshot("B", fallback, true)) && game.WorkingArea == fallback && Field<string>(game, "desktopDisplayDeviceName") == "B",
                "disconnecting the tracked device retries after drag release and falls back to a surviving display");
            // Existing drag code may already change area and hero position. Adopt that
            // selection without applying a second monitor-origin translation to the hero.
            PointF beforeCrossDragEnemy = new PointF((float)ordinary.X, (float)ordinary.Y);
            Rectangle dragged = new Rectangle(-1800, 0, 1600, 900); Set(game, "area", dragged); Set(game, "x", -1500d); Set(game, "y", 120d);
            Refresh(game, Snapshot("B", fallback, true), Snapshot("C", dragged, false));
            Check(Field<string>(game, "desktopDisplayDeviceName") == "C" && Same(Position(game), new PointF(-1500, 120)),
                "an already completed cross-monitor drag updates device tracking without double-moving the hero");
            Check(Same(new PointF((float)ordinary.X, (float)ordinary.Y), Map(beforeCrossDragEnemy, dragged, dragged, new Size(130, 170))),
                "completed drag recovery only bounds actors because ordinary ticks may already have moved them into the new area");
            Rectangle tickedDrag = new Rectangle(2000, 0, 1600, 900);
            Set(game, "area", tickedDrag); Set(game, "x", 2500d); Set(game, "y", 120d);
            ordinary.X = 2400; ordinary.Y = 160; ordinary.TargetX = 2700; ordinary.TargetY = 300;
            Refresh(game, Snapshot("D", tickedDrag, false));
            Check(ordinary.X == 2400 && ordinary.Y == 160 && ordinary.TargetX == 2700 && ordinary.TargetY == 300,
                "an actor already bounded by the original drag-time tick is not shifted again on drag release");
            Set(game, "enabled", false);
            Rectangle disabled = new Rectangle(-1700, 40, 1000, 700);
            Check(Refresh(game, Snapshot("D", disabled, false)) && !game.Enabled && !game.DesktopActorsVisible && !game.DesktopHeroWindow.Visible
                && game.DesktopEnemyWindows.Values.All(w => !w.Visible), "disabled or hidden actors recover geometry without becoming enabled or visible");
            Check(random.Calls == 0 && Field<double>(game, "lastSave") == -4321 && game.DesktopRealWorkIdle,
                "hidden recovery starts no save, observer, maintenance worker or random draw");
        }
        static void GeometryChecks(AdventureController game)
        {
            Type geometry = typeof(AdventureController).Assembly.GetType("NetBunny.DesktopDisplayLayout");
            MethodInfo fit = geometry.GetMethod("FitPanelBounds", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo visible = geometry.GetMethod("ShouldRestorePanel", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo select = geometry.GetMethod("Select", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo panel = geometry.GetMethod("PanelDisplay", BindingFlags.Static | BindingFlags.NonPublic);
            Rectangle work = new Rectangle(-1280, -120, 1280, 800), outside = new Rectangle(2200, 1000, 720, 880);
            Rectangle result = (Rectangle)fit.Invoke(null, new object[] { outside, work });
            Check(work.Contains(result) && result.Size == new Size(720, 800), "a disconnected feature panel is fitted inside the surviving negative-coordinate work area");
            Check((Rectangle)fit.Invoke(null, new object[] { result, work }) == result, "already fitted feature-panel bounds are stable");
            Array screens = Array.CreateInstance(snapshotType, 3);
            screens.SetValue(Snapshot("far-primary", new Rectangle(1500, 900, 1000, 800), true), 0);
            screens.SetValue(Snapshot("near", new Rectangle(-1600, -900, 1280, 700), false), 1);
            screens.SetValue(Snapshot("upper", new Rectangle(0, -1100, 1400, 700), false), 2);
            object nearest = select.Invoke(null, new object[] { screens, "removed", Rectangle.Empty, new PointF(-400, 80) });
            Check((string)snapshotType.GetField("DeviceName", Hidden).GetValue(nearest) == "near",
                "fallback uses distance to live rectangles across a virtual-desktop hole rather than primary order");
            Rectangle existingPanel = new Rectangle(100, -1000, 720, 500);
            object panelScreen = panel.Invoke(null, new object[] { screens, existingPanel });
            Check((string)snapshotType.GetField("DeviceName", Hidden).GetValue(panelScreen) == "upper"
                && (Rectangle)fit.Invoke(null, new object[] { existingPanel, new Rectangle(0, -1100, 1400, 700) }) == existingPanel,
                "a feature panel on another surviving monitor keeps its current bounds independently of the hero");
            Check((Rectangle)fit.Invoke(null, new object[] { outside, Rectangle.Empty }) == outside,
                "invalid work-area geometry cannot collapse a feature panel");
            Check(!(bool)visible.Invoke(null, new object[] { false, false, FormWindowState.Normal })
                && !(bool)visible.Invoke(null, new object[] { true, true, FormWindowState.Normal })
                && !(bool)visible.Invoke(null, new object[] { true, false, FormWindowState.Minimized })
                && !(bool)visible.Invoke(null, new object[] { true, false, FormWindowState.Maximized })
                && (bool)visible.Invoke(null, new object[] { true, false, FormWindowState.Normal }),
                "hidden, minimized, native-maximized and disposed panels are skipped until normal visible restoration");
            PointF old = Position(game); Rectangle area = game.WorkingArea;
            game.Dispose();
            Check(!Refresh(game, Snapshot("after-dispose", new Rectangle(0, 0, 800, 600), true)) && game.WorkingArea == area && Same(Position(game), old),
                "a disposed controller ignores later display snapshots safely");
        }
        static void LegacyChecks(Form owner, string assets)
        {
            using (AdventureController legacy = new AdventureController(owner, assets, true))
            {
                Field<Timer>(legacy, "timer").Stop(); Set(legacy, "enabled", false);
                Prepare(legacy, Screen.PrimaryScreen.WorkingArea);
                AdventureEnemy enemy = Enemy("legacy", legacy.WorkingArea, false); legacy.Battle.Enemies.Add(enemy);
                CountedRandom random = new CountedRandom(); Set(legacy, "random", random);
                legacy.ChangeScreen(Screen.PrimaryScreen);
                Check(random.Calls > 0 && enemy.PatrolSector != 4, "legacy ChangeScreen still uses its original patrol retargeting behavior");
                Rectangle before = legacy.WorkingArea;
                Check(!Refresh(legacy, Snapshot("ignored", new Rectangle(-5000, -5000, 700, 500), true)) && legacy.WorkingArea == before,
                    "the injected recovery entry cannot change the legacy controller");
            }
        }
        static void PanelRecoveryChecks()
        {
            Type geometry = typeof(AdventureController).Assembly.GetType("NetBunny.DesktopDisplayLayout");
            MethodInfo decide = geometry.GetMethod("ShouldRestorePanelBounds", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo topology = geometry.GetMethod("TopologyKey", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo feature = geometry.GetMethod("IsFeaturePanel", BindingFlags.Static | BindingFlags.NonPublic);
            Array displays = Array.CreateInstance(snapshotType, 2);
            displays.SetValue(Snapshot("left", new Rectangle(-1280, 0, 1280, 800), false), 0);
            displays.SetValue(Snapshot("right", new Rectangle(0, 0, 1600, 900), true), 1);
            // Before the change the visible-normal loop always fitted bounds.
            // Exercise that old decision when its new pure seam is absent.
            Func<bool, bool, Rectangle, bool> restore = (changed, newlyNormal, bounds) => decide == null || (bool)decide.Invoke(null, new object[] { changed, newlyNormal, bounds, displays });
            Rectangle partial = new Rectangle(-40, 70, 720, 700);
            Check(!restore(false, false, partial), "an unchanged display layout preserves a reachable manually straddled panel");
            Check(!restore(false, false, new Rectangle(1300, 200, 720, 700)), "an ordinary partial edge placement is not pulled back during subsequent polls or dragging");
            Check(restore(true, false, partial), "an actual topology or working-area change authorizes one panel fit");
            Check(restore(false, true, partial), "a newly visible or restored-normal panel is fitted once");
            Check(restore(false, false, new Rectangle(8000, -3000, 720, 700)), "a wholly unreachable panel remains eligible for fallback even without a topology change");
            Array reordered = Array.CreateInstance(snapshotType, 2); reordered.SetValue(displays.GetValue(1), 0); reordered.SetValue(displays.GetValue(0), 1);
            Check(topology != null && (string)topology.Invoke(null, new object[] { displays }) == (string)topology.Invoke(null, new object[] { reordered }),
                "screen array reorder does not manufacture a topology change");
            Array shrunken = Array.CreateInstance(snapshotType, 2); shrunken.SetValue(displays.GetValue(0), 0); shrunken.SetValue(Snapshot("right", new Rectangle(0, 20, 1600, 880), true), 1);
            Check(topology != null && (string)topology.Invoke(null, new object[] { displays }) != (string)topology.Invoke(null, new object[] { shrunken }),
                "a taskbar-sized working-area change is detected in the panel topology signature");
            Check(feature != null && !(bool)feature.Invoke(null, new object[] { typeof(Form) }) && !(bool)feature.Invoke(null, new object[] { typeof(KnightLayeredWindow) })
                && !(bool)feature.Invoke(null, new object[] { typeof(ArtDemoForm) }), "generic acceptance underlays, input probes and actor windows are excluded from panel recovery");
            Type[] allowed = { typeof(DesktopPracticePanel), typeof(DesktopEcologyPanel), typeof(BaiyeSteleForm), typeof(BreakthroughForm), typeof(LingyuForm), typeof(DongtianForm), typeof(AdventureEquipmentForm) };
            Check(feature != null && allowed.All(type => (bool)feature.Invoke(null, new object[] { type })), "only the program's explicit feature-form types are eligible for panel recovery");
        }
        static void PausedWrapperCheck(AdventureController game)
        {
            Rectangle before = new Rectangle(-9000, -5000, 1200, 800); Set(game, "area", before);
            Set(game, "x", -8800d); Set(game, "y", -4800d); PointF position = Position(game);
            string tracked = Field<string>(game, "desktopDisplayDeviceName");
            game.RefreshDesktopDisplayLayout();
            Check(!Field<Timer>(game, "timer").Enabled && game.WorkingArea == before && Same(position, Position(game))
                && Field<string>(game, "desktopDisplayDeviceName") == tracked,
                "the live-screen wrapper does not rebase a deliberately paused controller during acceptance capture message pumping");
        }
        [STAThread] static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[2]));
                using (Form owner = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-25000, -25000) })
                using (AdventureController game = new AdventureController(owner, Path.GetFullPath(args[0]), false, null, false, Path.Combine(output, "private-profile")))
                {
                    Field<Timer>(game, "timer").Stop(); game.ConfigureDesktopDemo(false); Field<Timer>(game, "timer").Stop();
                    ExistingChangeScreenRegression(game);
                    snapshotType = typeof(AdventureController).Assembly.GetType("NetBunny.DesktopDisplaySnapshot");
                    Check(snapshotType != null, "an injectable display snapshot exists for recovery without changing system settings");
                    if (snapshotType != null) { InjectedRecoveryChecks(game); PanelRecoveryChecks(); PausedWrapperCheck(game); GeometryChecks(game); LegacyChecks(owner, Path.GetFullPath(args[0])); }
                    Check(!owner.Visible && !game.DesktopHeroWindow.Visible, "all regression fixtures remain hidden without running a GUI message loop");
                }
                Console.WriteLine("Desktop display layout: " + checks + " checks, " + failures + " failures. Private/hidden fixtures; no display changes, native input, real archives or hot-plug claim.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
    }
}
