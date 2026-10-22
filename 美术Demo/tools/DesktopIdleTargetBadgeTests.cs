using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NetBunny
{
    // Real RenderWorld/Present frames in a stopped, private Virtual controller.
    // Actor windows stay outside every physical screen; no input or game ticks.
    internal static class DesktopIdleTargetBadgeTests
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        static int checks, failures;
        static T Field<T>(object item, string name) { return (T)item.GetType().GetField(name, Hidden).GetValue(item); }
        static void Set(object item, string name, object value) { item.GetType().GetField(name, Hidden).SetValue(item, value); }
        static void Check(bool okay, string detail) { checks++; if (!okay) failures++; Console.WriteLine((okay ? "PASS " : "FAIL ") + detail); }
        static void Render(AdventureController game)
        { typeof(AdventureController).GetMethod("RenderWorld", Hidden).Invoke(game, new object[] { true }); }
        static Bitmap Frame(AdventureController game, AdventureEnemy enemy)
        { return Field<Bitmap>(game.DesktopEnemyWindows[enemy.Id], "current"); }
        static bool Above(Form a, Form b)
        {
            IntPtr current = GetWindow(b.Handle, 3);
            for (int i = 0; i < 1024 && current != IntPtr.Zero; i++, current = GetWindow(current, 3))
                if (current == a.Handle) return true;
            return false;
        }
        static object FirstShot(AdventureEffects effects)
        { return ((System.Collections.IList)Field<object>(effects, "shots"))[0]; }
        static double EffectCoverage(KnightLayeredWindow hero, KnightLayeredWindow effect, Rectangle logical)
        {
            Bitmap h = Field<Bitmap>(hero, "current"), e = Field<Bitmap>(effect, "current"); int total = 0, covered = 0;
            float s = Math.Min(h.Width / 220f, h.Height / 220f), dx = (h.Width - 220 * s) / 2, dy = (h.Height - 220 * s) / 2;
            Rectangle region = Rectangle.Intersect(new Rectangle(Point.Empty, h.Size), Rectangle.FromLTRB((int)(dx + logical.Left * s), (int)(dy + logical.Top * s), (int)(dx + logical.Right * s), (int)(dy + logical.Bottom * s)));
            bool higher = Above(effect, hero);
            for (int y = region.Top; y < region.Bottom; y++) for (int x = region.Left; x < region.Right; x++)
            {
                if (h.GetPixel(x, y).A < 20) continue; total++;
                int ex = hero.Left + x - effect.Left, ey = hero.Top + y - effect.Top;
                if (higher && ex >= 0 && ey >= 0 && ex < e.Width && ey < e.Height && e.GetPixel(ex, ey).A >= 20) covered++;
            }
            return total == 0 ? 1 : covered / (double)total;
        }
        static PointF LowerAnchor(Form actor)
        {
            float s = Math.Min(actor.Width / 220f, actor.Height / 220f);
            return new PointF(actor.Left + actor.Width / 2f, actor.Top + (actor.Height - 220 * s) / 2 + 150 * s);
        }
        static bool Near(PointF a, PointF b) { return Math.Abs(a.X - b.X) < 1.1 && Math.Abs(a.Y - b.Y) < 1.1; }
        static void EffectChecks(string assets, string output)
        {
            float[] scales = { .8f, 1f, 1.2f };
            for (int i = 0; i < scales.Length; i++) using (Host h = new Host(assets, Path.Combine(output, "private-effect-" + i)))
            {
                AdventureController game = h.Game; float scale = scales[i]; Set(game, "displayScale", scale);
                game.Cultivation.Mortal = i == 0; game.Cultivation.RealmIndex = i == 2 ? 1 : 0;
                AdventureEnemy boss = EnemyBalance.MemoryBoss(game.Battle.Progress, game.Battle.Weapon, Field<Random>(game, "random"));
                Size size = (Size)typeof(AdventureController).GetMethod("EnemySize", Hidden).Invoke(game, new object[] { boss });
                boss.X = Field<double>(game, "x") + 110 * scale - 24 * scale - size.Width / 2f;
                boss.Y = Field<double>(game, "y") + 240 * scale - size.Height;
                game.Battle.Enemies.Add(boss); game.Battle.Authorize(boss); Render(game);
                KnightLayeredWindow hero = game.DesktopHeroWindow, target = game.DesktopEnemyWindows[boss.Id];
                AdventureEffects effects = Field<AdventureEffects>(game, "effects");
                typeof(AdventureController).GetMethod("StrikeTarget", Hidden).Invoke(game, new object[] { boss, 0d });
                object shot = FirstShot(effects); KnightLayeredWindow window = Field<KnightLayeredWindow>(shot, "Window");
                Check(Near(Field<PointF>(shot, "From"), LowerAnchor(hero)) && Near(Field<PointF>(shot, "To"), LowerAnchor(target)), "player strike uses the actual rendered lower-body anchors at scale " + scale);
                Check(window.Width == (int)(144 * scale) && window.Height == (int)(144 * scale), "new illustrated projectile has the bounded 144-unit footprint at scale " + scale);
                Render(game);
                Check(Above(hero, window) && Above(window, target), "real relative order puts hero above effect above current target at scale " + scale);
                Check(window.PassThrough && EffectCoverage(hero, window, new Rectangle(55, 15, 110, 100)) == 0 && EffectCoverage(hero, window, new Rectangle(8, 0, 204, 25)) == 0 && EffectCoverage(hero, window, new Rectangle(34, 194, 152, 29)) == 0,
                    "fixed original hero alpha in face, action hint and realm HUD is never covered by that effect at scale " + scale);
                double duration = Field<double>(shot, "Duration"); effects.Tick(.2); Render(game);
                Check(Math.Abs(Field<double>(shot, "Age") - .2) < .000001 && Math.Abs(duration - .5) < .000001 && effects.ActiveCount == 1,
                    "visual refinement preserves the normal projectile age and duration at scale " + scale);
                Check(Above(hero, window) && window.Visible && Field<Bitmap>(window, "current").Width == (int)(144 * scale), "moving projectile remains visible and below hero without hiding or clearing at scale " + scale);
                effects.Clear(); int health = game.Battle.Progress.Health;
                double cooldown = (double)typeof(AdventureController).GetMethod("EnemyAttackCooldown", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { boss });
                typeof(AdventureController).GetMethod("AttackHero", Hidden).Invoke(game, new object[] { boss, true, cooldown + .01 });
                Check(effects.ActiveCount == 1 && game.Battle.Progress.Health < health, "real Boss attack still damages through its existing path and emits an effect at scale " + scale);
                shot = FirstShot(effects); window = Field<KnightLayeredWindow>(shot, "Window"); Render(game);
                Check(Near(Field<PointF>(shot, "From"), LowerAnchor(target)) && Near(Field<PointF>(shot, "To"), LowerAnchor(hero)), "Boss response uses the same lower-body mapping in the reverse direction at scale " + scale);
                using (var layers = new DesktopPanelLayerCoordinator()) using (var panel = new PassivePanel(new Point(hero.Left - 600, hero.Top)))
                {
                    panel.Show(); layers.Register(panel, true); effects.Fire(LowerAnchor(target), LowerAnchor(hero), "wave", scale); Render(game);
                    var shots = (System.Collections.IList)Field<object>(effects, "shots");
                    Check(shots.Cast<object>().All(s => Above(panel, Field<KnightLayeredWindow>(s, "Window")) && Above(hero, Field<KnightLayeredWindow>(s, "Window"))),
                        "later effect windows preserve the protected panel and hero ceiling at scale " + scale);
                }
                for (int tick = 0; tick < 5; tick++) effects.Tick(.2);
                Check(effects.ActiveCount == 0 && game.DesktopRealWorkIdle, "original effect lifetime disposes its windows with no real worker at scale " + scale);
                h.VerifyScope();
            }
        }
        sealed class PassivePanel : Form
        {
            internal PassivePanel(Point point) { ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Location = point; Size = new Size(300, 180); }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams { get { CreateParams p = base.CreateParams; p.ExStyle |= 8 | 0x08000000 | 0x80; return p; } }
        }
        static void OrderChecks(string assets, string output)
        {
            using (Host h = new Host(assets, Path.Combine(output, "private-order")))
            {
                h.Game.Battle.Authorize(h.A); Render(h.Game); h.VerifyScope();
                KnightLayeredWindow hero = h.Game.DesktopHeroWindow, a = h.Game.DesktopEnemyWindows[h.A.Id], b = h.Game.DesktopEnemyWindows[h.B.Id];
                Check(Above(hero, a) && Above(hero, b), "actual native actor order keeps the hero and its HUD above all enemies");
                Check(Above(a, b), "the current target stays above other enemies independent of creation order");
                Bitmap aFrame = Frame(h.Game, h.A), bFrame = Frame(h.Game, h.B); Render(h.Game);
                Check(Object.ReferenceEquals(aFrame, Frame(h.Game, h.A)) && Object.ReferenceEquals(bFrame, Frame(h.Game, h.B)), "actor ordering does not rerasterize unchanged stationary enemies");
                // A real later Show triggers the existing feature-panel protection.
                using (var layers = new DesktopPanelLayerCoordinator())
                using (var panel = new PassivePanel(new Point(hero.Left - 600, hero.Top)))
                {
                    panel.Show(); layers.Register(panel, true);
                    var later = new AdventureEnemy { Id = "later-window", MonsterKind = "bat", IsVirtual = true, Health = 100, MaxHealth = 100, X = b.Left + 100, Y = b.Top };
                    h.Game.Battle.Enemies.Add(later); Render(h.Game);
                    KnightLayeredWindow c = h.Game.DesktopEnemyWindows[later.Id];
                    Check(Above(panel, hero) && Above(panel, a) && Above(panel, b) && Above(panel, c), "new actor priority remains below the existing protected feature panel");
                    Check(Above(hero, a) && Above(a, c), "later enemies cannot cover the hero HUD or the current target under a protected panel");
                    later.Dragging = true; Render(h.Game);
                    Check(Above(c, hero) && Above(hero, a) && Above(panel, c), "a dragged actor remains visible while the feature panel still outranks it");
                    later.Dragging = false; Render(h.Game);
                    Check(Above(hero, a) && Above(a, c) && Above(panel, hero), "ending an actor drag restores hero and current-target visibility without raising past the panel");
                }
                SetWindowPos(b.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x213);
                using (var unrelated = new PassivePanel(new Point(hero.Left - 800, hero.Top)))
                {
                    unrelated.Show(); Check(Above(unrelated, b), "the fixture starts its unrelated own topmost window above the disturbed actor group");
                    Render(h.Game);
                    Check(Above(unrelated, hero) && Above(hero, a) && Above(a, b), "relative repair preserves a window already above the group instead of globally raising the game");
                }
                int queue = h.Game.Battle.QueueCount, health = h.Game.Battle.Progress.Health;
                Check(queue == 1 && health > 0 && h.A.Authorization == AdventureAuthorization.Manual && h.A.Health == h.A.MaxHealth && h.Game.DesktopRealWorkIdle,
                    "native ordering changes no combat authorization, HP, queue or real worker");
            }
        }
        static Rectangle Badge(Bitmap frame)
        {
            float scale = Math.Min(frame.Width / 220f, frame.Height / 220f), dx = (frame.Width - 220 * scale) / 2, dy = (frame.Height - 220 * scale) / 2;
            return Rectangle.Intersect(new Rectangle(0, 0, frame.Width, frame.Height), Rectangle.FromLTRB((int)Math.Floor(dx + 159 * scale) - 1,
                (int)Math.Floor(dy + 41 * scale) - 1, (int)Math.Ceiling(dx + 210 * scale) + 1, (int)Math.Ceiling(dy + 69 * scale) + 1));
        }
        static int Difference(Bitmap before, Bitmap after, bool badgeOnly)
        {
            Rectangle region = badgeOnly ? Badge(after) : new Rectangle(Point.Empty, after.Size); int changed = 0;
            for (int y = region.Top; y < region.Bottom; y++) for (int x = region.Left; x < region.Right; x++)
                if (before.GetPixel(x, y).ToArgb() != after.GetPixel(x, y).ToArgb()) changed++;
            return changed;
        }
        sealed class Host : IDisposable
        {
            internal readonly Form Owner;
            internal readonly AdventureController Game;
            internal readonly AdventureEnemy A, B;
            internal Host(string assets, string root)
            {
                Owner = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-25000, -25000) };
                Game = new AdventureController(Owner, assets, false, null, false, root);
                Field<Timer>(Game, "timer").Stop(); Game.ConfigureDesktopDemo(false); Field<Timer>(Game, "timer").Stop();
                A = Game.Battle.Enemies[0]; B = Game.Battle.Enemies[1];
                Rectangle screenBounds = Screen.AllScreens.Select(s => s.Bounds).Aggregate(Rectangle.Union);
                Rectangle area = new Rectangle(screenBounds.Left - 20000, screenBounds.Top - 20000, 1800, 1000);
                Set(Game, "area", area); Set(Game, "x", area.Left + 80d); Set(Game, "y", area.Top + 120d);
                A.X = area.Left + 700; A.Y = area.Top + 120; B.X = area.Left + 1200; B.Y = area.Top + 120;
                Set(Game, "desktopActorsVisible", true);
            }
            internal void VerifyScope()
            {
                Check(!Field<Timer>(Game, "timer").Enabled && !Game.DesktopObservesSystem && Game.DesktopRealWorkIdle && !Owner.Visible
                    && Game.DesktopEnemyWindows.Values.Concat(new[] { Game.DesktopHeroWindow }).All(window => Screen.AllScreens.All(screen => !screen.Bounds.IntersectsWith(window.Bounds))),
                    "stopped Virtual fixture owns only screen-external actor windows, with no real worker, visible host or game tick");
            }
            public void Dispose() { Game.Dispose(); Owner.Dispose(); }
        }
        [STAThread] static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                string assets = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[2]));
                IntPtr foreground = GetForegroundWindow();
                using (Host h = new Host(assets, Path.Combine(output, "private-select")))
                {
                    Render(h.Game); h.VerifyScope(); Bitmap original = Frame(h.Game, h.A);
                    using (Bitmap before = (Bitmap)original.Clone())
                    {
                        before.Save(Path.Combine(output, "01-idle-unselected-frame.png"), ImageFormat.Png);
                        int hp = h.A.Health; double cooldown = h.Game.Battle.Cooldown;
                        Check(h.Game.DesktopChooseEnemy(h.A.Id, false) && h.A.QueueNumber == 1 && h.Game.Battle.Target == h.A
                            && h.A.Health == hp && h.Game.Battle.Cooldown == cooldown, "out-of-range selection uses the production entry and changes only combat focus, without a strike");
                        Bitmap after = Frame(h.Game, h.A); after.Save(Path.Combine(output, "02-idle-selected-frame.png"), ImageFormat.Png);
                        Check(!Object.ReferenceEquals(original, after) && Difference(before, after, true) > 20,
                            "a stationary merely-selected enemy immediately gains target-badge pixels in the real presented frame");
                    }
                }
                using (Host h = new Host(assets, Path.Combine(output, "private-preempt")))
                {
                    h.Game.Battle.SetAutomatic(true); Check(h.Game.Battle.PlanNext(Field<double>(h.Game, "x"), Field<double>(h.Game, "y")) == h.A,
                        "real planner establishes the nearest automatic target before a manual preemption");
                    Render(h.Game);
                    using (Bitmap beforeA = (Bitmap)Frame(h.Game, h.A).Clone()) using (Bitmap beforeB = (Bitmap)Frame(h.Game, h.B).Clone())
                    {
                        Check(h.Game.Battle.Authorize(h.B) && h.B.QueueNumber == 1 && h.A.QueueNumber == 2 && h.Game.Battle.Target == h.B,
                            "real manual authorization renumbers the old automatic target instead of cancelling its queue");
                        Render(h.Game);
                        Check(Difference(beforeA, Frame(h.Game, h.A), true) > 20, "stationary old automatic target replaces its target word with its new queue-number pixels");
                        Check(Difference(beforeB, Frame(h.Game, h.B), true) > 20, "stationary new manual target immediately receives target-badge pixels");
                    }
                }
                using (Host h = new Host(assets, Path.Combine(output, "private-cancel")))
                {
                    h.Game.Battle.Authorize(h.A); Render(h.Game);
                    using (Bitmap before = (Bitmap)Frame(h.Game, h.A).Clone())
                    {
                        h.Game.Battle.CancelQueue(); Render(h.Game);
                        Check(h.A.QueueNumber == 0 && h.Game.Battle.Target == null && Difference(before, Frame(h.Game, h.A), true) > 20,
                            "cancelling a stationary target removes its badge immediately through the existing invalidation path");
                    }
                }
                using (Host h = new Host(assets, Path.Combine(output, "private-stable")))
                {
                    h.Game.Battle.Authorize(h.A); Render(h.Game); Bitmap original = Frame(h.Game, h.A);
                    h.Game.Battle.Authorize(h.B); Render(h.Game);
                    Check(h.A.QueueNumber == 1 && h.B.QueueNumber == 2 && Object.ReferenceEquals(original, Frame(h.Game, h.A)),
                        "renumbering another actor does not rerasterize an unchanged stationary target");
                    Bitmap second = Frame(h.Game, h.B); Render(h.Game);
                    Check(Object.ReferenceEquals(original, Frame(h.Game, h.A)) && Object.ReferenceEquals(second, Frame(h.Game, h.B)),
                        "a second frame with unchanged stationary semantics retains both original Bitmap instances");
                }
                OrderChecks(assets, output);
                EffectChecks(assets, output);
                Check(GetForegroundWindow() == foreground, "off-screen production rendering and disposal never change foreground focus");
                Console.WriteLine("Idle target badge: " + checks + " checks, " + failures + " failures. Real source frames; no native screenshot, input, HP edit, Advance or production-archive write.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
    }
}
