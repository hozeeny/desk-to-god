using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace NetBunny
{
    // Hidden real controller, stopped timer and private archives only. No message loop,
    // visible actors, native input, live maintenance, process targets or fake geometry API.
    internal static class DesktopEncounterSpacingTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int checks, failures;
        private static readonly MethodInfo spawn = typeof(AdventureController).GetMethod("Spawn", Hidden);
        private static readonly MethodInfo patrol = typeof(AdventureController).GetMethod("AdvanceEnemyPatrol", Hidden);
        private static readonly MethodInfo enemySize = typeof(AdventureController).GetMethod("EnemySize", Hidden);
        private static readonly MethodInfo boundEnemy = typeof(AdventureController).GetMethod("BoundEnemy", Hidden);
        private sealed class FixedRandom : Random
        {
            internal int Doubles, Integers;
            public override double NextDouble() { Doubles++; return .5; }
            public override int Next(int maxValue) { Integers++; return 0; }
        }
        private static void Check(bool okay, string message)
        { checks++; if (!okay) failures++; Console.WriteLine((okay ? "PASS " : "FAIL ") + message); }
        private static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, Hidden).GetValue(target); }
        private static void Set(object target, string name, object value) { target.GetType().GetField(name, Hidden).SetValue(target, value); }
        private static Size SizeOf(AdventureController g, AdventureEnemy e) { return (Size)enemySize.Invoke(g, new object[] { e }); }
        private static Size HeroSize(float s) { return new Size((int)(220 * s), (int)(240 * s)); }
        private static PointF Feet(AdventureController g, AdventureEnemy e)
        { Size z = SizeOf(g, e); return new PointF((float)(e.X + z.Width / 2.0), (float)(e.Y + z.Height)); }
        private static PointF HeroFeet(AdventureController g, float s)
        { Size z = HeroSize(s); return new PointF((float)(Field<double>(g, "x") + z.Width / 2.0), (float)(Field<double>(g, "y") + z.Height)); }
        private static double Distance(PointF a, PointF b) { return AdventureBattle.Distance(a.X, a.Y, b.X, b.Y); }
        private static RectangleF Bounds(AdventureController g, AdventureEnemy e)
        { Size z = SizeOf(g, e); return new RectangleF((float)e.X, (float)e.Y, z.Width, z.Height); }
        private static bool Finite(double x) { return !Double.IsNaN(x) && !Double.IsInfinity(x); }
        private static bool InBounds(AdventureController g, AdventureEnemy e, Rectangle area)
        {
            RectangleF r = Bounds(g, e);
            return Finite(e.X) && Finite(e.Y) && r.Left >= area.Left - .01 && r.Top >= area.Top - .01
                && r.Right <= area.Right + .01 && r.Bottom <= area.Bottom + .01;
        }
        private static void Place(AdventureController g, AdventureEnemy e, PointF feet)
        { Size z = SizeOf(g, e); e.X = feet.X - z.Width / 2.0; e.Y = feet.Y - z.Height; e.TargetX = e.X + 200; e.TargetY = e.Y; }
        private static AdventureEnemy Enemy(string id)
        { return new AdventureEnemy { Id = id, MonsterKind = "skeleton", IsVirtual = true, Health = 200, MaxHealth = 200, SpawnLevel = 1 }; }
        private static FixedRandom Scene(AdventureController g, float s, Rectangle area, PointF hero)
        {
            g.Battle.CancelQueue(); g.Battle.Enemies.Clear(); g.Battle.SetAutomatic(false);
            g.Cultivation.Mortal = false; g.Battle.Progress.Dead = false; g.Battle.Progress.Health = 100;
            Set(g, "displayScale", s); Set(g, "area", area); Size z = HeroSize(s);
            Set(g, "x", (double)hero.X - z.Width / 2.0); Set(g, "y", (double)hero.Y - z.Height);
            Set(g, "blackholeUntil", 0d);
            FixedRandom random = new FixedRandom(); Set(g, "random", random); return random;
        }
        private static void Advance(AdventureController g, AdventureEnemy e, bool canFight, double dt)
        { e.MoveSpeed = 0; patrol.Invoke(g, new object[] { e, canFight, dt }); }
        private static double SegmentDistance(PointF point, PointF a, PointF b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, den = dx * dx + dy * dy;
            double t = den <= .00000001 ? 0 : Math.Max(0, Math.Min(1, ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / den));
            return AdventureBattle.Distance(point.X, point.Y, a.X + dx * t, a.Y + dy * t);
        }

        private static void SpawnChecks(AdventureController g)
        {
            Rectangle area = new Rectangle(0, 0, 1600, 900);
            foreach (float s in new[] { .8f, 1f, 1.2f })
            {
                // The unchanged .5 random proposal is (720,360); collide with its actual
                // post-clamp rectangle, while a large free screen supplies alternatives.
                FixedRandom random = Scene(g, s, area, new PointF(785, 530));
                AdventureEnemy born = Enemy("spawn-hero"); spawn.Invoke(g, new object[] { born });
                RectangleF hero = new RectangleF((float)Field<double>(g, "x"), (float)Field<double>(g, "y"), HeroSize(s).Width, HeroSize(s).Height);
                Check(!Bounds(g, born).IntersectsWith(hero), "spawn avoids the full hero window when free space exists at scale " + s);
                Check(random.Doubles == 4 && random.Integers == 1, "spawn retains four double draws and one initial patrol-sector draw at scale " + s);
                Check(InBounds(g, born, area), "hero-avoidance spawn remains inside its working area at scale " + s);

                random = Scene(g, s, area, new PointF(1400, 800));
                AdventureEnemy existing = Enemy("existing-visible"); existing.X = 720; existing.Y = 360; g.Battle.Enemies.Add(existing);
                born = Enemy("spawn-neighbor"); spawn.Invoke(g, new object[] { born });
                Check(!Bounds(g, born).IntersectsWith(Bounds(g, existing)), "spawn avoids an existing visible ordinary actor window at scale " + s);
                Check(random.Doubles == 4 && random.Integers == 1 && InBounds(g, born, area), "neighbor avoidance uses no extra randomness and stays bounded at scale " + s);
                PointF first = new PointF((float)born.X, (float)born.Y);
                Scene(g, s, area, new PointF(1400, 800));
                existing = Enemy("existing-visible"); existing.X = 720; existing.Y = 360; g.Battle.Enemies.Add(existing);
                AdventureEnemy repeat = Enemy("spawn-neighbor"); spawn.Invoke(g, new object[] { repeat });
                Check(Distance(first, new PointF((float)repeat.X, (float)repeat.Y)) < .001, "the same proposal and actors produce a deterministic safe spawn at scale " + s);

                random = Scene(g, s, area, new PointF(1400, 800));
                born = Enemy("self"); born.PatrolSector = 3; spawn.Invoke(g, new object[] { born });
                PointF noSelf = new PointF((float)born.X, (float)born.Y);
                Scene(g, s, area, new PointF(1400, 800));
                AdventureEnemy self = Enemy("self"); self.PatrolSector = 3; g.Battle.Enemies.Add(self);
                FixedRandom withSelf = Field<FixedRandom>(g, "random"); spawn.Invoke(g, new object[] { self });
                Check(Distance(noSelf, new PointF((float)self.X, (float)self.Y)) < .001,
                    "a respawned object does not count its own actor as an obstacle at scale " + s);
                Check(withSelf.Doubles == 4 && withSelf.Integers == 0, "an existing patrol sector retains its original random-call contract at scale " + s);
            }
        }

        private static void RingChecks(AdventureController g, int count, float s)
        {
            Rectangle area = new Rectangle(0, 0, 1600, 900); PointF hero = new PointF(800, 500);
            Scene(g, s, area, hero);
            AdventureEnemy[] actors = Enumerable.Range(0, count).Select(i => Enemy("ring-" + i)).ToArray();
            foreach (AdventureEnemy e in actors) { Place(g, e, new PointF(800 - 80 * s, 500)); g.Battle.Enemies.Add(e); g.Battle.Authorize(e); }
            int experience = g.Battle.Progress.Experience, score = g.Battle.Progress.Score; long qi = g.Cultivation.LifetimeQi, freed = g.Battle.Progress.FreedBytes;
            bool speed = true, inside = true, noCross = true, inReach = true; double latePath = 0; double minAtEight = Double.MaxValue;
            for (int frame = 0; frame < 240; frame++)
            {
                foreach (AdventureEnemy e in actors)
                {
                    PointF old = Feet(g, e); Advance(g, e, true, .05); PointF current = Feet(g, e);
                    double step = Distance(old, current);
                    speed &= step <= 68 * s * .05 + .002 && e.MoveSpeed <= 68 * s + .02;
                    inside &= InBounds(g, e, area); noCross &= SegmentDistance(hero, old, current) >= 75 * s - .02;
                    inReach &= Distance(hero, current) <= 86 * s + .05 && Distance(hero, current) <= g.Battle.Weapon.Range * .8 * s + .05;
                    if (frame >= 160) latePath += step;
                }
                if (frame == 159)
                    for (int i = 0; i < count; i++) for (int j = i + 1; j < count; j++) minAtEight = Math.Min(minAtEight, Distance(Feet(g, actors[i]), Feet(g, actors[j])));
            }
            string label = count + " actors scale " + s;
            Check(minAtEight >= 60 * s - .1, "already coincident 80-scale-ring actors gain a visible 60-scale separation within eight seconds: " + label);
            Check(speed, "ring separation respects the original enemy speed and every frame's dt: " + label);
            Check(noCross, "each actual movement segment stays outside the hero's inner circle: " + label);
            Check(inReach && actors.All(e => Math.Abs(Distance(hero, Feet(g, e)) - 80 * s) <= 2 * s),
                "separated actors remain on the melee ring inside existing attack and short-sword reaches: " + label);
            Check(latePath <= 4 * s, "settled actors do not keep oscillating during seconds eight through twelve: " + label);
            Check(inside, "ring movement remains inside the original working area: " + label);
            Check(g.Battle.QueueCount == count && actors.All(e => e.Authorized && e.Authorization == AdventureAuthorization.Manual
                && e.QueueNumber == Array.IndexOf(actors, e) + 1 && e.Health == 200 && !e.Defeated && !e.KillRewarded),
                "spacing preserves the actual queue, manual authorizations and living combat states: " + label);
            Check(g.Battle.Progress.Experience == experience && g.Battle.Progress.Score == score && g.Cultivation.LifetimeQi == qi
                && g.Battle.Progress.FreedBytes == freed && g.Battle.Progress.Health == 100,
                "spacing grants no experience, cultivation, maintenance bytes or damage: " + label);
        }

        private static void BoundsAndFallbackChecks(AdventureController g)
        {
            foreach (float s in new[] { .8f, 1f, 1.2f })
            {
                Rectangle[] areas = { new Rectangle(-1920, -260, 1920, 1080), new Rectangle(20, 30, (int)(245 * s), (int)(260 * s)) };
                foreach (Rectangle area in areas)
                foreach (bool right in new[] { false, true })
                foreach (bool bottom in new[] { false, true })
                {
                    Size heroSize = HeroSize(s);
                    PointF hero = new PointF(right ? area.Right - heroSize.Width / 2f - 4 : area.Left + heroSize.Width / 2f + 4,
                        bottom ? area.Bottom - 4 : area.Top + heroSize.Height + 2);
                    FixedRandom random = Scene(g, s, area, hero);
                    AdventureEnemy[] actors = { Enemy("edge-0"), Enemy("edge-1"), Enemy("edge-2") };
                    foreach (AdventureEnemy e in actors)
                    {
                        Place(g, e, new PointF(hero.X + (right ? -80 : 80) * s, hero.Y)); boundEnemy.Invoke(g, new object[] { e }); g.Battle.Enemies.Add(e);
                    }
                    AdventureEnemy born = Enemy("crowded-spawn"); spawn.Invoke(g, new object[] { born }); g.Battle.Enemies.Add(born);
                    bool valid = InBounds(g, born, area), boundedSteps = true;
                    for (int frame = 0; frame < 60; frame++) foreach (AdventureEnemy e in actors)
                    {
                        PointF old = Feet(g, e); Advance(g, e, true, .1);
                        valid &= InBounds(g, e, area); boundedSteps &= Distance(old, Feet(g, e)) <= 68 * s * .1 + .003;
                    }
                    string label = "scale " + s + " area " + area + " right=" + right + " bottom=" + bottom;
                    Check(valid && boundedSteps, "corner, negative-coordinate and crowded-area soft fallback stays finite, bounded and speed-limited: " + label);
                    Check(random.Doubles == 4 && random.Integers == 1, "crowded spawn fallback consumes no additional random draws: " + label);
                }
                Scene(g, s, new Rectangle(0, 0, 1600, 900), new PointF(800, 500));
                AdventureEnemy first = Enemy("long-frame-0"), second = Enemy("long-frame-1");
                Place(g, first, new PointF(800 - 80 * s, 500)); Place(g, second, Feet(g, first)); g.Battle.Enemies.Add(first); g.Battle.Enemies.Add(second);
                PointF before = Feet(g, second); Advance(g, second, true, 1.5); PointF after = Feet(g, second);
                Check(Distance(before, after) <= 68 * s * 1.5 + .01 && SegmentDistance(new PointF(800, 500), before, after) >= 75 * s - .05,
                    "a long frame cannot cut through the hero while separating coincident actors at scale " + s);
                before = Feet(g, second); Advance(g, second, true, 0);
                Check(Distance(before, Feet(g, second)) < .001, "zero dt cannot move an encounter actor at scale " + s);
            }
        }

        private static void ProtectedAndLegacyChecks(AdventureController g)
        {
            Action<AdventureEnemy>[] protect = { e => e.Dragging = true, e => e.Cleaning = true, e => e.VisualDefeated = true,
                e => e.Defeated = true, e => e.Failed = true, e => { e.IsBoss = true; e.IsDiskBoss = true; } };
            string[] names = { "dragging", "cleaning", "shattering", "defeated", "failed", "disk warning" };
            for (int i = 0; i < protect.Length; i++)
            {
                Scene(g, 1, new Rectangle(0, 0, 1600, 900), new PointF(800, 500));
                AdventureEnemy early = Enemy("protected-peer"), guarded = Enemy("protected-later"); protect[i](guarded);
                Place(g, early, new PointF(720, 500)); Place(g, guarded, new PointF(720, 500));
                g.Battle.Enemies.Add(early); g.Battle.Enemies.Add(guarded); PointF before = Feet(g, guarded);
                Advance(g, guarded, true, .5);
                Check(Distance(before, Feet(g, guarded)) < .001 && guarded.Health == 200 && !guarded.Authorized,
                    "encounter separation respects " + names[i] + " actors");
            }
            PointF[] idleResults = new PointF[2];
            for (int i = 0; i < 2; i++)
            {
                Scene(g, 1, new Rectangle(0, 0, 1600, 900), new PointF(800, 500)); g.Cultivation.Mortal = true;
                AdventureEnemy peer = Enemy("mortal-peer"), untouched = Enemy("mortal-unselected");
                Place(g, peer, new PointF(720, 500)); Place(g, untouched, new PointF(720, 500));
                g.Battle.Enemies.Add(peer); g.Battle.Enemies.Add(untouched); Advance(g, untouched, i == 0, .1); idleResults[i] = Feet(g, untouched);
                Check(!untouched.Authorized && g.Battle.Target == null && g.Battle.Progress.Health == 100, "unselected mortal scenery stays unengaged for canFight=" + (i == 0));
            }
            Check(Distance(idleResults[0], idleResults[1]) < .001, "unselected mortal actors retain the same ordinary idle patrol rather than encounter separation");
            Scene(g, 1, new Rectangle(0, 0, 1600, 900), new PointF(800, 500));
            AdventureEnemy a = Enemy("blackhole-early"), b = Enemy("blackhole-later");
            Place(g, a, new PointF(720, 500)); Place(g, b, new PointF(720, 500)); g.Battle.Enemies.Add(a); g.Battle.Enemies.Add(b);
            g.Battle.Authorize(a); g.Battle.Authorize(b); Set(g, "blackholeUntil", Field<Stopwatch>(g, "clock").Elapsed.TotalSeconds + 100);
            PointF pulling = Feet(g, b); for (int i = 0; i < 20; i++) { Advance(g, a, true, .1); Advance(g, b, true, .1); }
            Check(Distance(pulling, Feet(g, b)) < .001 && Distance(Feet(g, a), Feet(g, b)) < .001,
                "active black-hole gathering is not counteracted by lateral encounter dispersion");

            Scene(g, 1, new Rectangle(0, 0, 1600, 900), new PointF(785, 530));
            string privateRoot = Field<string>(g, "isolatedProfileRoot");
            try
            {
                // No Tick, save, refresh, renderer or worker entry is called while exercising legacy geometry.
                Set(g, "isolatedProfileRoot", null); FixedRandom random = Field<FixedRandom>(g, "random");
                AdventureEnemy oldSpawn = Enemy("legacy-spawn"); spawn.Invoke(g, new object[] { oldSpawn });
                Check(oldSpawn.X == 720 && oldSpawn.Y == 360 && random.Doubles == 4 && random.Integers == 1,
                    "non-isolated spawn retains its original random proposal and call count");
                Set(g, "x", 690d); Set(g, "y", 260d);
                AdventureEnemy oldChase = Enemy("legacy-chase"), oldPeer = Enemy("legacy-peer");
                Place(g, oldChase, new PointF(720, 500)); Place(g, oldPeer, new PointF(720, 500));
                g.Battle.Enemies.Add(oldPeer); g.Battle.Enemies.Add(oldChase); Advance(g, oldChase, true, .05);
                Check(Feet(g, oldChase).X > 721 && Math.Abs(Feet(g, oldChase).Y - 500) < .001 && Distance(Feet(g, oldChase), new PointF(800, 500)) < 79,
                    "non-isolated ordinary chasing keeps its legacy 70-unit approach instead of the desktop stopping ring");
            }
            finally { Set(g, "isolatedProfileRoot", privateRoot); }
        }

        [STAThread] private static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                string evidence = Path.GetFullPath(args[1]); Directory.CreateDirectory(evidence);
                using (Form owner = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-25000, -25000) })
                using (AdventureController g = new AdventureController(owner, Path.GetFullPath(args[0]), false, null, false, Path.Combine(evidence, "private-profile")))
                {
                    Field<Timer>(g, "timer").Stop(); g.ConfigureDesktopDemo(false); Field<Timer>(g, "timer").Stop();
                    SpawnChecks(g);
                    foreach (float s in new[] { .8f, 1f, 1.2f }) { RingChecks(g, 2, s); RingChecks(g, 3, s); }
                    BoundsAndFallbackChecks(g); ProtectedAndLegacyChecks(g);
                    Check(!owner.Visible && !g.DesktopHeroWindow.Visible && !g.DesktopActorsVisible && g.DesktopEnemyWindows.Values.All(w => !w.Visible),
                        "all controller and actor windows remain hidden throughout the stopped-timer fixture");
                    Check(g.DesktopRealWorkIdle, "no real scan, process target, cleanup, memory task or system worker is started");
                }
                Console.WriteLine("Desktop encounter spacing: " + checks + " checks, " + failures + " failures. Hidden controller/private archives, no native input or natural-time claim.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
    }
}
