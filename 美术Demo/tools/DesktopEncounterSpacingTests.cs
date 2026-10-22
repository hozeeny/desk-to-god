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
        private static RectangleF HeroBounds(AdventureController g, float s)
        { Size z = HeroSize(s); return new RectangleF((float)Field<double>(g, "x"), (float)Field<double>(g, "y"), z.Width, z.Height); }
        private static double OverlapArea(RectangleF a, RectangleF b)
        { return Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left)) * Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top)); }
        private static bool SweepsHero(RectangleF before, RectangleF after, RectangleF hero, float s)
        {
            // Check the actual movement segment, including long frames whose endpoints
            // can both be outside. This observes pixels occupied, not route-planner angles.
            int samples = Math.Max(1, (int)Math.Ceiling(AdventureBattle.Distance(before.X, before.Y, after.X, after.Y) / Math.Max(.25, s)));
            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                RectangleF at = new RectangleF(before.X + (after.X - before.X) * t, before.Y + (after.Y - before.Y) * t, before.Width, before.Height);
                if (OverlapArea(at, hero) > .02 * s * s) return true;
            }
            return false;
        }
        private static bool IdleUnengaged(AdventureController g, AdventureEnemy e, int experience, int score, long qi, long freed)
        {
            return g.Battle.Target == null && g.Battle.QueueCount == 0 && !e.Authorized && e.Authorization == AdventureAuthorization.None
                && e.Health == 200 && !e.KillRewarded && !e.Defeated && g.Battle.Progress.Health == 100
                && g.Battle.Progress.Experience == experience && g.Battle.Progress.Score == score
                && g.Cultivation.LifetimeQi == qi && g.Battle.Progress.FreedBytes == freed;
        }
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
            Check(Distance(idleResults[0], idleResults[1]) < .001, "unselected mortal actors stay on the unengaged patrol branch regardless of the caller's canFight flag");
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

        private static void IdleRouteChecks(AdventureController g, float s, Point offset)
        {
            Rectangle area = new Rectangle(offset.X, offset.Y, 1800, 1100);
            PointF heroFeet = new PointF(offset.X + 900, offset.Y + 650);
            FixedRandom random = Scene(g, s, area, heroFeet); g.Cultivation.Mortal = true;
            RectangleF hero = HeroBounds(g, s); AdventureEnemy idle = Enemy("idle-route-through-hero");
            idle.X = hero.Left - 330 * s; idle.Y = hero.Top + 70 * s; idle.PatrolSector = 4;
            idle.TargetX = area.Right - 140 * s; idle.TargetY = idle.Y; g.Battle.Enemies.Add(idle);
            double targetX = idle.TargetX, targetY = idle.TargetY; int sector = idle.PatrolSector;
            int experience = g.Battle.Progress.Experience, score = g.Battle.Progress.Score; long qi = g.Cultivation.LifetimeQi, freed = g.Battle.Progress.FreedBytes;
            bool clear = true, limited = true, bounded = true, plan = true, unengaged = true;
            // The final patrol target is farther away than fourteen seconds of original
            // walking speed, so a sector refresh cannot be mistaken for avoidance work.
            for (int frame = 0; frame < 280; frame++)
            {
                RectangleF before = Bounds(g, idle); Advance(g, idle, true, .05); RectangleF after = Bounds(g, idle);
                clear &= !SweepsHero(before, after, hero, s);
                limited &= AdventureBattle.Distance(before.X, before.Y, after.X, after.Y) <= 68 * s * .05 + .003 && idle.MoveSpeed <= 68 * s + .02;
                bounded &= InBounds(g, idle, area);
                plan &= idle.TargetX == targetX && idle.TargetY == targetY && idle.PatrolSector == sector;
                unengaged &= IdleUnengaged(g, idle, experience, score, qi, freed);
            }
            string label = "scale " + s + " origin " + offset;
            Check(clear, "an unselected mortal patrol routed through the hero detours around the entire hero/HUD window: " + label);
            Check(idle.X >= hero.Right - .1, "idle avoidance makes forward progress to the far side rather than freezing before the hero: " + label);
            Check(limited && bounded, "idle detours retain original walk speed, frame limits and screen bounds: " + label);
            Check(plan && random.Doubles == 0 && random.Integers == 0, "visual idle waypoints preserve the original target, patrol sector and random stream: " + label);
            Check(unengaged, "idle detouring cannot select, authorize, attack, grant rewards or perform maintenance: " + label);
        }

        private static void HeroMovesOntoIdleChecks(AdventureController g, float s)
        {
            Rectangle area = new Rectangle(0, 0, 1800, 1100);
            FixedRandom random = Scene(g, s, area, new PointF(1400, 850)); g.Cultivation.Mortal = true;
            AdventureEnemy idle = Enemy("hero-moved-onto-idle"); idle.X = 560; idle.Y = 340; idle.PatrolSector = 7;
            idle.TargetX = 1600; idle.TargetY = idle.Y; g.Battle.Enemies.Add(idle);
            Check(OverlapArea(Bounds(g, idle), HeroBounds(g, s)) == 0, "moving-hero fixture starts with an ordinary non-overlapping idle actor at scale " + s);
            // Move only the hero. The original target would now pull this actor deeper
            // through the portrait; safe routing must first exit at walking speed.
            Set(g, "x", idle.X + 110 * s); Set(g, "y", idle.Y + 20 * s);
            RectangleF hero = HeroBounds(g, s); double overlap = OverlapArea(Bounds(g, idle), hero);
            double originalX = idle.TargetX, originalY = idle.TargetY; int sector = idle.PatrolSector;
            int experience = g.Battle.Progress.Experience, score = g.Battle.Progress.Score; long qi = g.Cultivation.LifetimeQi, freed = g.Battle.Progress.FreedBytes;
            bool limited = true, deeper = false, reentered = false, plan = true; double exitAt = Double.PositiveInfinity;
            for (int frame = 0; frame < 160; frame++)
            {
                RectangleF before = Bounds(g, idle); Advance(g, idle, true, .05); RectangleF after = Bounds(g, idle);
                double nextOverlap = OverlapArea(after, hero);
                limited &= AdventureBattle.Distance(before.X, before.Y, after.X, after.Y) <= 68 * s * .05 + .003 && InBounds(g, idle, area);
                if (Double.IsPositiveInfinity(exitAt))
                {
                    deeper |= nextOverlap > overlap + .05 * s * s;
                    if (nextOverlap <= .02 * s * s) exitAt = (frame + 1) * .05;
                }
                else reentered |= SweepsHero(before, after, hero, s);
                overlap = nextOverlap;
                plan &= idle.TargetX == originalX && idle.TargetY == originalY && idle.PatrolSector == sector;
            }
            Check(exitAt <= 4 && !deeper && !reentered, "an idle actor overlapped by a moved hero exits within four seconds without moving deeper or returning through the HUD at scale " + s);
            Check(limited, "overlap escape is gradual, finite and bounded by the original speed at scale " + s);
            Check(plan && random.Doubles == 0 && random.Integers == 0 && IdleUnengaged(g, idle, experience, score, qi, freed),
                "escaping the moved hero preserves the patrol plan, randomness and noncombat state at scale " + s);
        }

        private static void IdleEdgeAndLongFrameChecks(AdventureController g)
        {
            foreach (float s in new[] { .8f, 1f, 1.2f })
            {
                Rectangle[] areas = { new Rectangle(-1920, -260, 1920, 1080), new Rectangle(20, 30, (int)(245 * s), (int)(260 * s)) };
                foreach (Rectangle area in areas)
                foreach (bool right in new[] { false, true })
                foreach (bool bottom in new[] { false, true })
                {
                    Size hz = HeroSize(s);
                    PointF hero = new PointF(right ? area.Right - hz.Width / 2f - 4 : area.Left + hz.Width / 2f + 4,
                        bottom ? area.Bottom - 4 : area.Top + hz.Height + 2);
                    FixedRandom random = Scene(g, s, area, hero); g.Cultivation.Mortal = true;
                    AdventureEnemy idle = Enemy("idle-corner-fallback"); Place(g, idle, hero); boundEnemy.Invoke(g, new object[] { idle });
                    idle.PatrolSector = 4;
                    // Deliberately unreachable final target: tests soft fallback without
                    // a legitimate reached-target reroll obscuring its random-call count.
                    idle.TargetX = area.Right + 500; idle.TargetY = area.Bottom + 500; g.Battle.Enemies.Add(idle);
                    double targetX = idle.TargetX, targetY = idle.TargetY; bool okay = true;
                    for (int frame = 0; frame < 40; frame++)
                    {
                        PointF before = Feet(g, idle); Advance(g, idle, true, .1);
                        okay &= InBounds(g, idle, area) && Distance(before, Feet(g, idle)) <= 68 * s * .1 + .003;
                    }
                    Check(okay && idle.TargetX == targetX && idle.TargetY == targetY && idle.PatrolSector == 4 && random.Doubles == 0
                        && random.Integers == 0 && !idle.Authorized && g.Battle.Target == null,
                        "idle corner/negative/tight-area soft fallback stays bounded, speed-limited and unengaged without rerolls: scale " + s + " area " + area + " right=" + right + " bottom=" + bottom);
                }
                foreach (double dt in new[] { 1.5, 8.0 })
                {
                    Rectangle area = new Rectangle(0, 0, 1800, 1100);
                    FixedRandom random = Scene(g, s, area, new PointF(900, 650)); g.Cultivation.Mortal = true;
                    RectangleF hero = HeroBounds(g, s); AdventureEnemy idle = Enemy("idle-long-frame");
                    idle.X = hero.Left - 250 * s; idle.Y = hero.Top + 70 * s; idle.PatrolSector = 2;
                    idle.TargetX = 1600; idle.TargetY = idle.Y; g.Battle.Enemies.Add(idle);
                    RectangleF before = Bounds(g, idle); Advance(g, idle, true, dt); RectangleF after = Bounds(g, idle);
                    Check(!SweepsHero(before, after, hero, s), "a long idle step cannot sweep through the hero even if its endpoint passes the portrait: scale " + s + " dt=" + dt);
                    Check(InBounds(g, idle, area) && AdventureBattle.Distance(before.X, before.Y, after.X, after.Y) <= 68 * s * dt + .003
                        && random.Doubles == 0 && random.Integers == 0 && idle.PatrolSector == 2,
                        "long-dt idle avoidance keeps existing speed, bounds and random cadence: scale " + s + " dt=" + dt);
                }
            }
        }

        private static void IdleVerticalAndOccupiedTargetChecks(AdventureController g)
        {
            foreach (float s in new[] { .8f, 1f, 1.2f })
            {
                foreach (bool downward in new[] { true, false })
                {
                    Rectangle area = new Rectangle(0, 0, 2200, 2000);
                    FixedRandom random = Scene(g, s, area, new PointF(1100, 1050)); g.Cultivation.Mortal = true;
                    RectangleF hero = HeroBounds(g, s); AdventureEnemy idle = Enemy("idle-vertical-crossing"); Size size = SizeOf(g, idle);
                    idle.X = hero.Left + 45 * s;
                    idle.Y = downward ? hero.Top - size.Height - 180 * s : hero.Bottom + 180 * s;
                    idle.TargetX = idle.X;
                    idle.TargetY = downward ? hero.Bottom + 400 * s : hero.Top - size.Height - 400 * s;
                    idle.PatrolSector = 5; g.Battle.Enemies.Add(idle);
                    double targetX = idle.TargetX, targetY = idle.TargetY; bool clear = true, limited = true, plan = true, crossed = false;
                    // Both endpoints sit in the hero's horizontal span. A safe route
                    // must pass around a side and continue past the far edge; a single
                    // diagonal to the destination cannot satisfy the swept-window check.
                    for (int frame = 0; frame < 480; frame++)
                    {
                        RectangleF before = Bounds(g, idle); Advance(g, idle, true, .05); RectangleF after = Bounds(g, idle);
                        clear &= !SweepsHero(before, after, hero, s);
                        limited &= AdventureBattle.Distance(before.X, before.Y, after.X, after.Y) <= 68 * s * .05 + .003 && InBounds(g, idle, area);
                        plan &= idle.TargetX == targetX && idle.TargetY == targetY && idle.PatrolSector == 5;
                        crossed = downward ? after.Top >= hero.Bottom + 60 * s : after.Bottom <= hero.Top - 60 * s;
                        if (crossed) break; // Keep well outside the legitimate <10px arrival/reroll threshold.
                    }
                    string label = "scale " + s + " direction=" + (downward ? "down" : "up");
                    Check(clear && crossed, "a centered vertical idle crossing goes around the full hero window and reaches the opposite side: " + label);
                    Check(limited, "vertical detouring respects each frame's original speed and work-area bounds: " + label);
                    Check(plan && random.Doubles == 0 && random.Integers == 0 && !idle.Authorized && g.Battle.Target == null,
                        "vertical visual detours leave the patrol destination, sector, random stream and unengaged state intact: " + label);
                }

                Rectangle waitArea = new Rectangle(0, 0, 1800, 1100);
                FixedRandom waitRandom = Scene(g, s, waitArea, new PointF(900, 650)); g.Cultivation.Mortal = true;
                RectangleF occupiedHero = HeroBounds(g, s); AdventureEnemy waiting = Enemy("idle-occupied-target");
                waiting.X = occupiedHero.Left - 330 * s; waiting.Y = occupiedHero.Top + 70 * s;
                waiting.TargetX = occupiedHero.Left + 60 * s; waiting.TargetY = waiting.Y; waiting.PatrolSector = 3; g.Battle.Enemies.Add(waiting);
                double originalX = waiting.TargetX, originalY = waiting.TargetY;
                double initialDistance = AdventureBattle.Distance(waiting.X, waiting.Y, originalX, originalY), lateTravel = 0;
                int experience = g.Battle.Progress.Experience, score = g.Battle.Progress.Score; long qi = g.Cultivation.LifetimeQi, freed = g.Battle.Progress.FreedBytes;
                bool safe = true, limitedWait = true, preserved = true, unengaged = true;
                for (int frame = 0; frame < 160; frame++)
                {
                    RectangleF before = Bounds(g, waiting); Advance(g, waiting, true, .05); RectangleF after = Bounds(g, waiting);
                    double moved = AdventureBattle.Distance(before.X, before.Y, after.X, after.Y);
                    safe &= !SweepsHero(before, after, occupiedHero, s);
                    limitedWait &= moved <= 68 * s * .05 + .003 && InBounds(g, waiting, waitArea);
                    if (frame >= 120) lateTravel += moved;
                    preserved &= waiting.TargetX == originalX && waiting.TargetY == originalY && waiting.PatrolSector == 3;
                    unengaged &= IdleUnengaged(g, waiting, experience, score, qi, freed);
                }
                double waitingDistance = AdventureBattle.Distance(waiting.X, waiting.Y, originalX, originalY);
                Check(safe && waitingDistance < initialDistance - 100 * s && waitingDistance > 10 && lateTravel <= s,
                    "an occupied patrol destination is approached safely and waits without late oscillation or entering the hero/HUD: scale " + s);
                Check(limitedWait && preserved && waitRandom.Doubles == 0 && waitRandom.Integers == 0 && unengaged,
                    "waiting at an occupied target preserves original speed, patrol plan, random cadence and noncombat state: scale " + s);

                // Free the same destination by moving only the hero; do not supply a
                // replacement target or invoke the planner. Stop before real arrival.
                Set(g, "x", (double)(waitArea.Right - HeroSize(s).Width - 30)); Set(g, "y", 30d);
                RectangleF movedHero = HeroBounds(g, s); bool resumedSafe = true, resumedLimited = true;
                double remaining = waitingDistance;
                for (int frame = 0; frame < 240 && remaining > 30 * s; frame++)
                {
                    RectangleF before = Bounds(g, waiting); Advance(g, waiting, true, .05); RectangleF after = Bounds(g, waiting);
                    resumedSafe &= !SweepsHero(before, after, movedHero, s);
                    resumedLimited &= AdventureBattle.Distance(before.X, before.Y, after.X, after.Y) <= 68 * s * .05 + .003 && InBounds(g, waiting, waitArea);
                    preserved &= waiting.TargetX == originalX && waiting.TargetY == originalY && waiting.PatrolSector == 3;
                    unengaged &= IdleUnengaged(g, waiting, experience, score, qi, freed);
                    remaining = AdventureBattle.Distance(waiting.X, waiting.Y, originalX, originalY);
                }
                Check(resumedSafe && resumedLimited && remaining <= 30 * s && remaining > 10 && remaining < waitingDistance - 100 * s,
                    "moving the hero away releases the safe wait and resumes the original target before its arrival threshold: scale " + s);
                Check(preserved && waitRandom.Doubles == 0 && waitRandom.Integers == 0 && unengaged,
                    "resuming an occupied patrol plan needs no new target, random draws, authorization, combat or rewards: scale " + s);
            }
        }

        private static void IdleWallOverlapEscapeChecks(AdventureController g)
        {
            foreach (float s in new[] { .8f, 1f, 1.2f })
            foreach (int placement in new[] { 0, 1, 2 })
            {
                Rectangle area = new Rectangle(0, 0, (int)(720 * s), (int)(480 * s)); Size hz = HeroSize(s);
                float heroX = placement == 1 ? area.Right - hz.Width - 16 * s : placement == 2 ? 4 : 16 * s;
                float heroY = placement == 2 ? 2 + 12 * s : 2;
                FixedRandom random = Scene(g, s, area, new PointF(heroX + hz.Width / 2f, heroY + hz.Height)); g.Cultivation.Mortal = true;
                AdventureEnemy idle = Enemy("idle-wall-overlap"); Size size = SizeOf(g, idle);
                idle.X = placement == 1 ? area.Right - size.Width - 4 : 4; idle.Y = 2;
                idle.TargetX = area.Right + 400; idle.TargetY = area.Bottom + 400; idle.PatrolSector = 8; g.Battle.Enemies.Add(idle);
                double targetX = idle.TargetX, targetY = idle.TargetY; RectangleF hero = HeroBounds(g, s);
                double overlap = OverlapArea(Bounds(g, idle), hero); bool monotone = overlap > 0, limited = true, plan = true;
                int experience = g.Battle.Progress.Experience, score = g.Battle.Progress.Score; long qi = g.Cultivation.LifetimeQi, freed = g.Battle.Progress.FreedBytes;
                bool unengaged = true; double exitAt = Double.PositiveInfinity;
                // These legal wall placements have an escape direction that does not
                // first increase overlap. Observe the actor's actual successive bounds;
                // neither a shortest-exit choice nor any particular waypoint is assumed.
                for (int frame = 0; frame < 160; frame++)
                {
                    RectangleF before = Bounds(g, idle); Advance(g, idle, true, .05); RectangleF after = Bounds(g, idle);
                    double nextOverlap = OverlapArea(after, hero);
                    monotone &= nextOverlap <= overlap + .05 * s * s;
                    limited &= AdventureBattle.Distance(before.X, before.Y, after.X, after.Y) <= 68 * s * .05 + .003 && InBounds(g, idle, area);
                    plan &= idle.TargetX == targetX && idle.TargetY == targetY && idle.PatrolSector == 8;
                    unengaged &= IdleUnengaged(g, idle, experience, score, qi, freed);
                    overlap = nextOverlap;
                    if (overlap <= .02 * s * s) { exitAt = (frame + 1) * .05; break; }
                }
                string label = "scale " + s + " wall=" + (placement == 0 ? "top-left" : placement == 1 ? "top-right" : "left");
                Check(monotone && exitAt <= 8, "a wall-overlapped idle actor exits without increasing its full-window overlap on any frame: " + label);
                Check(limited && plan && random.Doubles == 0 && random.Integers == 0 && unengaged,
                    "wall-overlap escape stays within original speed and bounds while preserving the patrol plan and noncombat state: " + label);
            }
        }

        private static void IdleProtectedAndLegacyChecks(AdventureController g)
        {
            Action<AdventureEnemy>[] protect = { e => e.Dragging = true, e => e.Cleaning = true, e => e.VisualDefeated = true,
                e => e.Defeated = true, e => e.Failed = true, e => { e.IsBoss = true; e.IsDiskBoss = true; } };
            string[] names = { "dragging", "cleaning", "shattering", "defeated", "failed", "disk warning" };
            for (int i = 0; i < protect.Length; i++)
            {
                Scene(g, 1, new Rectangle(0, 0, 1600, 900), new PointF(800, 500)); g.Cultivation.Mortal = true;
                AdventureEnemy idle = Enemy("protected-idle"); protect[i](idle); Place(g, idle, new PointF(800, 400));
                idle.PatrolSector = 6; g.Battle.Enemies.Add(idle); PointF before = Feet(g, idle);
                double targetX = idle.TargetX, targetY = idle.TargetY; Advance(g, idle, false, .5);
                Check(Distance(before, Feet(g, idle)) < .001 && idle.TargetX == targetX && idle.TargetY == targetY && idle.PatrolSector == 6,
                    "idle avoidance leaves " + names[i] + " actors and their patrol plans untouched");
            }
            Scene(g, 1, new Rectangle(0, 0, 1800, 1100), new PointF(900, 650)); g.Cultivation.Mortal = true;
            AdventureEnemy legacy = Enemy("legacy-idle-straight"); legacy.X = 400; legacy.Y = 450; legacy.TargetX = 1600; legacy.TargetY = 450; legacy.PatrolSector = 7;
            g.Battle.Enemies.Add(legacy); string privateRoot = Field<string>(g, "isolatedProfileRoot"); FixedRandom random = Field<FixedRandom>(g, "random");
            bool crossed = false;
            try
            {
                Set(g, "isolatedProfileRoot", null);
                for (int i = 0; i < 100; i++) { Advance(g, legacy, false, .1); crossed |= OverlapArea(Bounds(g, legacy), HeroBounds(g, 1)) > 1; }
                Check(Math.Abs(legacy.Y - 450) < .001 && Math.Abs(legacy.X - 1080) < .02 && crossed,
                    "non-isolated idle patrol keeps its original straight-line movement through the same geometry");
                Check(legacy.TargetX == 1600 && legacy.TargetY == 450 && legacy.PatrolSector == 7 && random.Doubles == 0 && random.Integers == 0,
                    "legacy idle geometry preserves its original target, sector and random cadence");
            }
            finally { Set(g, "isolatedProfileRoot", privateRoot); }
        }

        private sealed class SourceActor : IDisposable
        {
            internal Bitmap Frame; internal Point Origin;
            public void Dispose() { Frame.Dispose(); }
        }
        private static SourceActor SourceFrame(AdventureController g, AdventureEnemy enemy)
        {
            AdventureRenderState state = enemy == null
                ? (AdventureRenderState)typeof(AdventureController).GetMethod("HeroVisual", Hidden).Invoke(g, new object[] { 0d })
                : new AdventureRenderState { Kind = enemy.IsBoss ? "EvilBoss" : "Skeleton", HeroStage = "qishi", Phase = "idle", Time = 0,
                    Size = SizeOf(g, enemy), MonsterKind = enemy.MonsterKind, WeaponId = enemy.BossWeapon ?? "spear", ThemeCode = enemy.EvilTheme ?? "OFF",
                    Label = enemy.IsBoss ? "内存压力 Boss" : AdventureController.ArtDemoMonsterLabel(enemy.MonsterKind), QueueNumber = enemy.QueueNumber,
                    HealthRatio = 1, HealthCurrent = enemy.Health, HealthMaximum = enemy.MaxHealth, Opacity = 1 };
            return new SourceActor { Frame = Field<AdventureRenderer>(g, "renderer").Render(state), Origin = enemy == null
                ? new Point((int)Field<double>(g, "x"), (int)Field<double>(g, "y")) : new Point((int)enemy.X, (int)enemy.Y) };
        }
        private static double Covered(SourceActor victim, SourceActor[] others, Rectangle region)
        {
            int total = 0, covered = 0; region.Intersect(new Rectangle(Point.Empty, victim.Frame.Size));
            for (int y = region.Top; y < region.Bottom; y++) for (int x = region.Left; x < region.Right; x++)
            {
                if (victim.Frame.GetPixel(x, y).A < 20) continue;
                total++;
                foreach (SourceActor other in others)
                {
                    int ox = victim.Origin.X + x - other.Origin.X, oy = victim.Origin.Y + y - other.Origin.Y;
                    if (ox >= 0 && oy >= 0 && ox < other.Frame.Width && oy < other.Frame.Height && other.Frame.GetPixel(ox, oy).A >= 20) { covered++; break; }
                }
            }
            return total == 0 ? 0 : covered / (double)total; // Original opaque denominator, never the surviving visible subset.
        }
        private static void DenseBossChecks(AdventureController g, string evidence, bool bottom)
        {
            Rectangle area = new Rectangle(0, 0, 1800, 1100); PointF hero = new PointF(900, bottom ? 1096 : 700);
            FixedRandom random = Scene(g, 1, area, hero);
            AdventureEnemy[] ordinary = Enumerable.Range(0, 6).Select(i => Enemy("dense-" + i)).ToArray();
            foreach (AdventureEnemy e in ordinary) { Place(g, e, new PointF(hero.X - 80, hero.Y)); g.Battle.Enemies.Add(e); }
            AdventureEnemy boss = Enemy("late-current-boss"); boss.IsBoss = true; boss.EvilTheme = "HK"; boss.BossWeapon = "automaton";
            Place(g, boss, new PointF(hero.X - 80, hero.Y)); g.Battle.Enemies.Add(boss); g.Battle.Authorize(boss);
            double[] tx = ordinary.Select(e => e.TargetX).ToArray(), ty = ordinary.Select(e => e.TargetY).ToArray();
            bool speed = true, bounded = true; double lateMovement = 0;
            for (int frame = 0; frame < 240; frame++)
            {
                foreach (AdventureEnemy e in ordinary)
                {
                    PointF before = Feet(g, e); Advance(g, e, true, .05); double moved = Distance(before, Feet(g, e));
                    speed &= moved <= 68 * .05 + .003; bounded &= InBounds(g, e, area); if (frame >= 220) lateMovement += moved;
                }
                if (frame == 159)
                {
                    SourceActor earlyHero = SourceFrame(g, null), earlyBoss = SourceFrame(g, boss);
                    SourceActor[] earlyPeers = ordinary.Select(e => SourceFrame(g, e)).ToArray();
                    try
                    {
                        double head = Covered(earlyHero, earlyPeers, new Rectangle(0, 0, 220, 132)), hud = Covered(earlyHero, earlyPeers, new Rectangle(0, 200, 220, 40));
                        double bossCover = Covered(earlyBoss, earlyPeers, new Rectangle(Point.Empty, earlyBoss.Frame.Size));
                        double pairs = earlyPeers.Select((actor, i) => Covered(actor, earlyPeers.Where((other, j) => i != j).ToArray(), new Rectangle(Point.Empty, actor.Frame.Size))).Max();
                        Check(head <= .05 && hud <= .05 && bossCover <= .1 && pairs <= .2,
                            "eight-second fixed original-alpha observation is already clear before the separate settled check: " + (bottom ? "bottom" : "center")
                            + " head=" + head.ToString("F3") + " hud=" + hud.ToString("F3") + " boss=" + bossCover.ToString("F3") + " peers=" + pairs.ToString("F3"));
                        using (Bitmap picture = new Bitmap(area.Width, area.Height))
                        {
                            using (Graphics draw = Graphics.FromImage(picture)) { draw.Clear(Color.FromArgb(245, 242, 232)); foreach (SourceActor actor in earlyPeers.Concat(new[] { earlyBoss, earlyHero })) draw.DrawImageUnscaled(actor.Frame, actor.Origin); }
                            picture.Save(Path.Combine(evidence, bottom ? "dense-bottom-8s-source-render.png" : "dense-center-8s-source-render.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    finally { earlyHero.Dispose(); earlyBoss.Dispose(); foreach (SourceActor actor in earlyPeers) actor.Dispose(); }
                }
            }
            SourceActor h = SourceFrame(g, null), b = SourceFrame(g, boss); SourceActor[] peers = ordinary.Select(e => SourceFrame(g, e)).ToArray();
            Console.WriteLine("Dense feet " + (bottom ? "bottom" : "center") + ": " + String.Join(";", ordinary.Select(e => e.Id + "=" + Feet(g, e))));
            try
            {
                double head = Covered(h, peers, new Rectangle(0, 0, 220, 132)), hud = Covered(h, peers, new Rectangle(0, 200, 220, 40));
                double bossCover = Covered(b, peers, new Rectangle(Point.Empty, b.Frame.Size));
                double pairCover = peers.Select((actor, i) => Covered(actor, peers.Where((other, j) => i != j).ToArray(), new Rectangle(Point.Empty, actor.Frame.Size))).Max();
                string label = bottom ? "bottom edge" : "free central area";
                Check(head <= .05 && hud <= .05, "six ordinary actors leave at least 95% of original hero head/upper text and lower HUD opaque samples uncovered: " + label + " head=" + head.ToString("F3") + " hud=" + hud.ToString("F3"));
                Check(bossCover <= .10, "ordinary actors yield to the current Boss even when it was appended last; at least 90% original Boss alpha remains clear: " + label + " covered=" + bossCover.ToString("F3"));
                Check(pairCover <= .20, "actual ordinary source-frame alpha overlap remains below twenty percent after dispersal: " + label + " covered=" + pairCover.ToString("F3"));
                Check(ordinary.Any(e => Distance(Feet(g, e), hero) > 100) && lateMovement <= .1,
                    "a crowded melee ring uses outer waiting positions and the final one-second observation has no orbit/jitter: " + label + " lateMove=" + lateMovement.ToString("F2"));
                using (Bitmap picture = new Bitmap(area.Width, area.Height))
                {
                    using (Graphics draw = Graphics.FromImage(picture)) { draw.Clear(Color.FromArgb(245, 242, 232)); foreach (SourceActor actor in peers.Concat(new[] { b, h })) draw.DrawImageUnscaled(actor.Frame, actor.Origin); }
                    picture.Save(Path.Combine(evidence, bottom ? "dense-bottom-source-render.png" : "dense-center-source-render.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            finally { h.Dispose(); b.Dispose(); foreach (SourceActor actor in peers) actor.Dispose(); }
            Check(speed && bounded && random.Doubles == 0 && random.Integers == 0 && ordinary.Select((e, i) => e.TargetX == tx[i] && e.TargetY == ty[i] && !e.Authorized && e.Health == 200).All(ok => ok)
                && g.Battle.Target == boss && g.Battle.QueueCount == 1, "dense visual waiting preserves original speed, bounds, random stream, patrol targets and combat authorizations");
            AdventureEnemy selected = ordinary[5]; Check(g.Battle.Authorize(selected), "an outer waiting actor can receive an ordinary manual order");
            // A real manual selection, rather than appending behind the already manual Boss.
            Check(g.Battle.SelectDesktopTarget(selected) && g.Battle.Target == selected, "the formerly last ordinary actor becomes the actual current target");
            for (int frame = 0; frame < 200; frame++) foreach (AdventureEnemy e in ordinary) Advance(g, e, true, .05);
            Check(Distance(Feet(g, selected), hero) <= 86.05 && selected.Health == 200 && !selected.KillRewarded,
                "the newly selected outer actor reaches the true existing 86-unit attack distance without starvation or a fabricated hit: " + Feet(g, selected) + " distance=" + Distance(Feet(g, selected), hero).ToString("F2"));
        }

        [STAThread] private static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                string evidence = Path.GetFullPath(args[1]); Directory.CreateDirectory(evidence);
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])), "ArtDemoAssets"));
                using (Form owner = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-25000, -25000) })
                using (AdventureController g = new AdventureController(owner, Path.GetFullPath(args[0]), false, null, false, Path.Combine(evidence, "private-profile")))
                {
                    Field<Timer>(g, "timer").Stop(); g.ConfigureDesktopDemo(false); Field<Timer>(g, "timer").Stop();
                    DenseBossChecks(g, evidence, false); DenseBossChecks(g, evidence, true);
                    SpawnChecks(g);
                    foreach (float s in new[] { .8f, 1f, 1.2f }) { RingChecks(g, 2, s); RingChecks(g, 3, s); }
                    BoundsAndFallbackChecks(g); ProtectedAndLegacyChecks(g);
                    foreach (float s in new[] { .8f, 1f, 1.2f })
                    {
                        IdleRouteChecks(g, s, Point.Empty); IdleRouteChecks(g, s, new Point(-1920, -260)); HeroMovesOntoIdleChecks(g, s);
                    }
                    IdleEdgeAndLongFrameChecks(g); IdleVerticalAndOccupiedTargetChecks(g); IdleWallOverlapEscapeChecks(g); IdleProtectedAndLegacyChecks(g);
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
