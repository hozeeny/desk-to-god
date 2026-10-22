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
        static int AlphaInRegion(Bitmap frame, Rectangle effect, Rectangle hero, Rectangle logical, out int overlapPixels)
        {
            float scale = Math.Min(hero.Width / 220f, hero.Height / 220f), dx = (hero.Width - 220 * scale) / 2, dy = (hero.Height - 220 * scale) / 2;
            Rectangle region = Rectangle.FromLTRB(hero.Left + (int)Math.Floor(dx + logical.Left * scale), hero.Top + (int)Math.Floor(dy + logical.Top * scale),
                hero.Left + (int)Math.Ceiling(dx + logical.Right * scale), hero.Top + (int)Math.Ceiling(dy + logical.Bottom * scale));
            Rectangle overlap = Rectangle.Intersect(region, effect); overlapPixels = overlap.Width * overlap.Height; int nonzero = 0;
            for (int y = overlap.Top; y < overlap.Bottom; y++) for (int x = overlap.Left; x < overlap.Right; x++)
                if (frame.GetPixel(x - effect.Left, y - effect.Top).A != 0) nonzero++;
            return nonzero;
        }
        static bool Protected(KnightLayeredWindow effect, Rectangle hero, Rectangle logical)
        { int overlap; return AlphaInRegion(Field<Bitmap>(effect, "current"), effect.Bounds, hero, logical, out overlap) == 0 && overlap > 0; }
        static bool AllProtectionClear(KnightLayeredWindow effect, Rectangle hero)
        {
            int overlap; Bitmap frame = Field<Bitmap>(effect, "current");
            return AlphaInRegion(frame, effect.Bounds, hero, new Rectangle(0, 0, 220, 115), out overlap) == 0
                && AlphaInRegion(frame, effect.Bounds, hero, new Rectangle(0, 190, 220, 30), out overlap) == 0;
        }
        static void MovingProtectionChecks(AdventureController game, AdventureEffects effects, KnightLayeredWindow hero, float scale)
        {
            effects.Clear(); Point original = hero.Location;
            PointF remote = new PointF(hero.Left + 600 * scale, hero.Top + 200 * scale);
            effects.Fire(remote, remote, "wave", scale);
            object shot = FirstShot(effects); KnightLayeredWindow effect = Field<KnightLayeredWindow>(shot, "Window");
            PointF from = Field<PointF>(shot, "From"), to = Field<PointF>(shot, "To");
            double age = Field<double>(shot, "Age"); Rectangle effectBounds = effect.Bounds;
            using (Bitmap unmasked = (Bitmap)Field<Bitmap>(effect, "current").Clone())
            {
                Rectangle[] zones = { new Rectangle(0, 0, 220, 115), new Rectangle(0, 190, 220, 30) };
                float[] centers = { 60, 205 };
                for (int i = 0; i < zones.Length; i++)
                {
                    float actualScale = Math.Min(hero.Width / 220f, hero.Height / 220f), dy = (hero.Height - 220 * actualScale) / 2;
                    Set(game, "x", (double)(effectBounds.Left + effectBounds.Width / 2 - hero.Width / 2));
                    Set(game, "y", (double)(effectBounds.Top + effectBounds.Height / 2 - dy - centers[i] * actualScale));
                    if (i == 0)
                    {
                        typeof(AdventureController).GetMethod("ClampHero", Hidden).Invoke(game, null);
                        Check(Protected(effect, hero.Bounds, zones[i]) && Field<double>(shot, "Age") == age && effect.Bounds == effectBounds,
                            "ClampHero alone refreshes the moved head mask before any Render or Tick, without aging the shot at scale " + scale);
                    }
                    Render(game); int overlap;
                    Check(AlphaInRegion(unmasked, effectBounds, hero.Bounds, zones[i], out overlap) > 0 && overlap > 0,
                        "moving fixture puts actual previously visible effect pixels inside the new " + (i == 0 ? "whole head" : "HUD") + " zone at scale " + scale);
                    Check(Protected(effect, hero.Bounds, zones[i]) && Field<double>(shot, "Age") == age && effect.Bounds == effectBounds,
                        "Render immediately clears every alpha in the moved " + (i == 0 ? "whole head" : "HUD") + " zone without advancing or moving the shot at scale " + scale);
                    if (i == 0)
                    {
                        effects.Clear(); effects.Fire(remote, remote, "wave", scale);
                        shot = FirstShot(effects); effect = Field<KnightLayeredWindow>(shot, "Window");
                        Check(Protected(effect, hero.Bounds, zones[i]) && Field<double>(shot, "Age") == 0,
                            "a new Fire first frame is already masked where the real unmasked reference had visible head-zone pixels at scale " + scale);
                    }
                }
                Set(game, "x", (double)original.X); Set(game, "y", (double)original.Y); Render(game);
                Bitmap restored = Field<Bitmap>(effect, "current");
                Check(Difference(unmasked, restored, false) == 0 && Field<double>(shot, "Age") == age && Near(Field<PointF>(shot, "From"), from) && Near(Field<PointF>(shot, "To"), to),
                    "leaving the protection area restores the original visible effect at unchanged age/endpoints at scale " + scale);
                Render(game);
                Check(Object.ReferenceEquals(restored, Field<Bitmap>(effect, "current")), "unchanged hero position does not rerasterize a stationary effect at scale " + scale);
            }
            Set(game, "dragging", true); Render(game);
            Check(Above(hero, effect), "a currently dragged hero stays above the effect at scale " + scale);
            Set(game, "dragging", false); Render(game); effects.Clear();
        }
        static void TransitionProtectionChecks(AdventureEffects effects, KnightLayeredWindow hero, float scale)
        {
            MethodInfo update = typeof(AdventureEffects).GetMethod("UpdateHeroProtection", Hidden), commit = typeof(AdventureEffects).GetMethod("CommitHeroProtection", Hidden);
            PointF remote = new PointF(hero.Left + 600 * scale, hero.Top + 200 * scale);
            effects.Fire(remote, remote, "wave", scale);
            object shot = FirstShot(effects); KnightLayeredWindow effect = Field<KnightLayeredWindow>(shot, "Window");
            Rectangle bounds = effect.Bounds; float s = Math.Min(hero.Width / 220f, hero.Height / 220f), dy = (hero.Height - 220 * s) / 2;
            int centerY = bounds.Top + bounds.Height / 2, left = bounds.Left + bounds.Width / 2 - hero.Width / 2;
            Rectangle oldHero = new Rectangle(left, (int)(centerY - dy - 119 * s), hero.Width, hero.Height);
            Rectangle newHero = new Rectangle(left, (int)(centerY - dy - 186 * s), hero.Width, hero.Height);
            Rectangle head = new Rectangle(0, 0, 220, 115), footer = new Rectangle(0, 190, 220, 30);
            double age = Field<double>(shot, "Age"); PointF from = Field<PointF>(shot, "From"), to = Field<PointF>(shot, "To");
            using (Bitmap original = (Bitmap)Field<Bitmap>(effect, "current").Clone())
            {
                if (update != null) update.Invoke(effects, new object[] { oldHero });
                if (commit != null) commit.Invoke(effects, null);
                if (update != null) update.Invoke(effects, new object[] { newHero });
                int count;
                Check(AlphaInRegion(original, bounds, oldHero, head, out count) > 0 && AlphaInRegion(original, bounds, newHero, footer, out count) > 0
                    && Protected(effect, oldHero, head) && Protected(effect, newHero, footer),
                    "prepare phase protects both old head and new HUD with nonzero unmasked reference pixels at scale " + scale);
                if (commit != null) commit.Invoke(effects, null);
                Check(commit != null && AlphaInRegion(Field<Bitmap>(effect, "current"), bounds, oldHero, head, out count) > 0 && Protected(effect, newHero, footer)
                    && Field<double>(shot, "Age") == age && Near(Field<PointF>(shot, "From"), from) && Near(Field<PointF>(shot, "To"), to),
                    "commit releases the old zone while retaining the new zone without advancing age or endpoints at scale " + scale);
                Bitmap stable = Field<Bitmap>(effect, "current");
                if (update != null) update.Invoke(effects, new object[] { newHero });
                if (commit != null) commit.Invoke(effects, null);
                Check(Object.ReferenceEquals(stable, Field<Bitmap>(effect, "current")), "repeated same-bounds prepare/commit reuses the effect bitmap at scale " + scale);
            }
            if (update != null) update.Invoke(effects, new object[] { hero.Bounds });
            if (commit != null) commit.Invoke(effects, null);
            effects.Clear();
        }
        static void ScalePresentationChecks(AdventureController game, AdventureEffects effects, KnightLayeredWindow hero, float oldScale, float newScale)
        {
            Render(game); Bitmap oldFrame = Field<Bitmap>(hero, "current"); Rectangle oldBounds = new Rectangle(hero.Location, oldFrame.Size);
            float effectScale = Math.Max(oldScale, newScale), s = Math.Min(oldFrame.Width / 220f, oldFrame.Height / 220f), dy = (oldFrame.Height - 220 * s) / 2;
            effects.Fire(new PointF(hero.Left + 800, hero.Top + 500), new PointF(hero.Left + 800, hero.Top + 500), "wave", effectScale);
            using (Bitmap unmasked = (Bitmap)Field<Bitmap>(Field<KnightLayeredWindow>(FirstShot(effects), "Window"), "current").Clone())
            {
                effects.Clear(); PointF center = new PointF(oldBounds.Left + oldBounds.Width / 2f, oldBounds.Top + dy + 205 * s);
                effects.Fire(center, center, "wave", effectScale);
                object shot = FirstShot(effects); KnightLayeredWindow effect = Field<KnightLayeredWindow>(shot, "Window");
                double age = Field<double>(shot, "Age"); int overlap;
                int oldReferenceAlpha = AlphaInRegion(unmasked, effect.Bounds, oldBounds, new Rectangle(0, 190, 220, 30), out overlap);
                Set(game, "displayScale", newScale);
                Size newSize = (Size)typeof(AdventureController).GetProperty("HeroSize", Hidden).GetValue(game, null);
                hero.Size = newSize;
                typeof(AdventureController).GetMethod("PositionHeroWindow", Hidden).Invoke(game, null);
                Rectangle newBounds = hero.Bounds;
                Check(oldReferenceAlpha > 0 && oldFrame.Size != newSize && Object.ReferenceEquals(oldFrame, Field<Bitmap>(hero, "current")),
                    "scale transition " + oldScale + " to " + newScale + " retains the actual old presented bitmap and has visible unmasked reference pixels in its old HUD");
                Check(AllProtectionClear(effect, oldBounds) && AllProtectionClear(effect, newBounds) && Field<double>(shot, "Age") == age,
                    "before new Present, resized PositionHeroWindow protects both actual old-frame and new-size zones without advancing age for " + oldScale + " to " + newScale);
                Render(game);
                Check(Field<Bitmap>(hero, "current").Size == newSize && AllProtectionClear(effect, newBounds)
                    && AlphaInRegion(Field<Bitmap>(effect, "current"), effect.Bounds, oldBounds, new Rectangle(0, 190, 220, 30), out overlap) == oldReferenceAlpha
                    && Field<double>(shot, "Age") == age,
                    "successful new Present releases old HUD pixels while keeping the new zones protected at unchanged age for " + oldScale + " to " + newScale);
            }
            effects.Clear(); Set(game, "displayScale", oldScale); Render(game);
        }
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
                int beforeStrikeHealth = boss.Health;
                typeof(AdventureController).GetMethod("StrikeTarget", Hidden).Invoke(game, new object[] { boss, 0d });
                string strikeHint = (string)typeof(AdventureController).GetProperty("DesktopHudHint", Hidden).GetValue(game, null);
                Check(boss.Health < beforeStrikeHealth && strikeHint.Contains("命中") && strikeHint.Contains((beforeStrikeHealth - boss.Health).ToString()),
                    "actual StrikeTarget reports its real health delta on the closed-panel desktop hint at scale " + scale);
                object shot = FirstShot(effects); KnightLayeredWindow window = Field<KnightLayeredWindow>(shot, "Window");
                Check(Near(Field<PointF>(shot, "From"), LowerAnchor(hero)) && Near(Field<PointF>(shot, "To"), LowerAnchor(target)), "player strike uses the actual rendered lower-body anchors at scale " + scale);
                Check(window.Width == (int)(144 * scale) && window.Height == (int)(144 * scale), "new illustrated projectile has the bounded 144-unit footprint at scale " + scale);
                Check(Protected(window, hero.Bounds, new Rectangle(0, 0, 220, 115)) && Protected(window, hero.Bounds, new Rectangle(0, 190, 220, 30)),
                    "the very first Fire frame has zero alpha across the whole head and lower HUD protection zones at scale " + scale);
                Render(game);
                Check(Above(window, hero) && Above(hero, target), "real relative order puts visible effect above protected hero above current target at scale " + scale);
                Check(window.PassThrough && EffectCoverage(hero, window, new Rectangle(55, 15, 110, 100)) == 0 && EffectCoverage(hero, window, new Rectangle(8, 0, 204, 25)) == 0 && EffectCoverage(hero, window, new Rectangle(34, 194, 152, 29)) == 0,
                    "fixed original hero alpha in face, action hint and realm HUD is never covered by that effect at scale " + scale);
                double duration = Field<double>(shot, "Duration"); effects.Tick(.2); Render(game);
                Check(Math.Abs(Field<double>(shot, "Age") - .2) < .000001 && Math.Abs(duration - .5) < .000001 && effects.ActiveCount == 1,
                    "visual refinement preserves the normal projectile age and duration at scale " + scale);
                Check(Above(window, hero) && window.Visible && Field<Bitmap>(window, "current").Width == (int)(144 * scale), "moving projectile remains visible above protected hero without hiding or clearing at scale " + scale);
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
                    Check(shots.Cast<object>().All(s => Above(panel, Field<KnightLayeredWindow>(s, "Window")) && Above(Field<KnightLayeredWindow>(s, "Window"), hero)),
                        "later effect windows stay visible above protected hero and below the existing panel at scale " + scale);
                    h.A.Dragging = true; Render(game);
                    Check(shots.Cast<object>().All(s => Above(game.DesktopEnemyWindows[h.A.Id], Field<KnightLayeredWindow>(s, "Window"))) && Above(panel, game.DesktopEnemyWindows[h.A.Id]),
                        "a dragged enemy stays above every effect while remaining below the feature panel at scale " + scale);
                    h.A.Dragging = false;
                }
                for (int tick = 0; tick < 5; tick++) effects.Tick(.2);
                Check(effects.ActiveCount == 0 && game.DesktopRealWorkIdle, "original effect lifetime disposes its windows with no real worker at scale " + scale);
                MovingProtectionChecks(game, effects, hero, scale);
                TransitionProtectionChecks(effects, hero, scale);
                ScalePresentationChecks(game, effects, hero, scale, scales[(i + 1) % scales.Length]);
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
