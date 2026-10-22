using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace NetBunny
{
    // The desktop host owns lifetime and lightweight panels only. All combat,
    // progression, archive formats and optional feature panels remain the same
    // AdventureController used by the original game.
    internal sealed partial class AdventureController
    {
        private readonly string isolatedProfileRoot;
        private readonly DesktopEnvironmentPolicy desktopEnvironment;
        private bool desktopActorsVisible = true, desktopTestAdvancing;
        private bool desktopLastSaveSucceeded = true;
        private double desktopLastRefill;
        private int desktopAcceptanceSpeed = 1;
        internal bool IsolatedDesktop { get { return isolatedProfileRoot != null; } }
        internal bool DesktopObservesSystem { get { return IsolatedDesktop && desktopEnvironment.MayObserve; } }
        internal DesktopEnvironmentMode DesktopMode { get { return desktopEnvironment.ReadMode(consent.CleanupEnabled, consent.MemoryEnabled); } }
        private bool MayObserveLocalResources { get { return !IsolatedDesktop || DesktopObservesSystem; } }
        internal string IsolatedProfileRoot { get { return isolatedProfileRoot; } }
        internal Action DesktopPanelRequested;
        internal KnightLayeredWindow DesktopHeroWindow { get { return heroWindow; } }
        internal Dictionary<string, KnightLayeredWindow> DesktopEnemyWindows { get { return new Dictionary<string, KnightLayeredWindow>(enemyWindows); } }
        internal bool DesktopActorsVisible { get { return desktopActorsVisible; } }
        internal string DesktopNotice { get { return (desktopAcceptanceSpeed == 6 ? "验收快进×6 · " : "") + (IsolatedDesktop ? ReadDesktopPlayerView().Notice : failure ?? (desktopLastSaveSucceeded ? battle.LastReward : scanStatus)); } }
        internal string[] DesktopArchivePaths { get { return new[] { progressPath, CultivationWritePath, BaiyeWritePath, LingyuWritePath, DongtianWritePath }; } }
        internal bool DesktopRealWorkIdle { get { return connectivity == null && tempWatcher == null && cleanupTask == null && memoryReclaimTask == null && !scanBusy && !diskBusy && !memoryBusy; } }
        internal void DesktopSetAcceptanceSpeed(int speed)
        {
            if (!IsolatedDesktop || (speed != 1 && speed != 6)) throw new ArgumentException("验收时间倍率仅允许1或6。");
            desktopAcceptanceSpeed = speed;
        }

        internal void ConfigureDesktopDemo(bool showActors = true)
        {
            if (!IsolatedDesktop || disposed) throw new InvalidOperationException("透明桌面 Demo 必须使用独立持久化存档。");
            desktopActorsVisible = showActors;
            if (!DesktopObservesSystem) consent.CleanupEnabled = consent.MemoryEnabled = consent.AutoMaintenanceEnabled = false;
            roaming = true;
            battle.Progress.ThemeMode = "OFF";
            scanStatus = DesktopObservesSystem ? "电脑生态只读观察 · 真实维护须分别授权" : "隔离桌面修行 · 纯虚拟妖物 · 系统维护始终关闭";
            connectivityDetail = "本地修行 · 不探测网络";
            heroWindow.Text = "桌面修仙传 · 修士";
            // Two ordinary opponents are enough to start immediately without
            // covering the desktop in a staged battle composition.
            if (!DesktopObservesSystem && !(lingyu.ExpeditionActive && lingyu.CombatPending) && !battle.Enemies.Any(e => !e.Defeated))
            { SpawnVirtualFiend(); SpawnVirtualFiend(); }
            desktopLastRefill = clock.Elapsed.TotalSeconds;
            Enabled = true;
            landmarkWindow.Hide();
            Save();
        }

        private void MaintainDesktopField()
        {
            if (!IsolatedDesktop || !enabled || IsDemo || (lingyu.ExpeditionActive && lingyu.CombatPending)) return;
            if (battle.Enemies.Count(e => !e.Defeated && !e.VisualDefeated) >= 2 || clock.Elapsed.TotalSeconds - desktopLastRefill < 2) return;
            desktopLastRefill = clock.Elapsed.TotalSeconds;
            if (EnemyFieldBudget.CanAddOrdinary(battle.Enemies)) SpawnVirtualFiend();
        }

        internal void DesktopStrikeSelected()
        {
            if (!IsolatedDesktop || disposed || battle.Progress.Dead) return;
            AdventureEnemy target = battle.Target;
            if (target != null) TryMortalStrike(target);
            RenderWorld(true);
        }

        // Feet share a baseline, but the illustrated bodies need horizontal
        // breathing room. This is a movement destination only: hit range,
        // damage, cooldown and the tactics planner's attack decision stay intact.
        private PointF DesktopCombatStandPoint(AdventureEnemy target)
        {
            Size enemySize = EnemySize(target);
            double enemyFeetX = target.X + enemySize.Width / 2.0;
            double enemyFeetY = target.Y + enemySize.Height;
            double heroFeetX = x + HeroSize.Width / 2.0;
            double gap = Math.Min(80 * displayScale, battle.Weapon.Range * displayScale * .8 - 4 * displayScale);
            double minimum = area.Left + HeroSize.Width / 2.0, maximum = area.Right - HeroSize.Width / 2.0;
            int side = heroFeetX < enemyFeetX ? -1 : heroFeetX > enemyFeetX ? 1 : heroFacing ? -1 : 1;
            double desired = enemyFeetX + side * gap;
            if (desired < minimum || desired > maximum) desired = enemyFeetX - side * gap;
            desired = Math.Max(minimum, Math.Min(maximum, desired));
            double feetY = Math.Max(area.Top + HeroSize.Height, Math.Min(area.Bottom, enemyFeetY));
            return new PointF((float)desired, (float)feetY);
        }

        private bool DesktopActorsOverlap(AdventureEnemy target)
        {
            Size enemySize = EnemySize(target);
            return Math.Abs(x + HeroSize.Width / 2.0 - target.X - enemySize.Width / 2.0) < 70 * displayScale
                && Math.Abs(y + HeroSize.Height - target.Y - enemySize.Height) < 150 * displayScale;
        }

        private void PlaceDesktopSpawn(AdventureEnemy enemy)
        {
            Size size = EnemySize(enemy); PointF proposal = new PointF((float)enemy.X, (float)enemy.Y);
            var candidates = new List<PointF> { proposal };
            float gapX = size.Width + 12 * displayScale, gapY = size.Height + 12 * displayScale;
            for (int row = -1; row <= 1; row++) for (int column = -1; column <= 1; column++)
                if (row != 0 || column != 0) candidates.Add(new PointF(proposal.X + column * gapX, proposal.Y + row * gapY));
            for (int row = 0; row < 5; row++) for (int column = 0; column < 5; column++)
                candidates.Add(new PointF(area.Left + Math.Max(0, area.Width - size.Width) * column / 4f,
                    area.Top + Math.Max(0, area.Height - size.Height) * row / 4f));
            PointF best = proposal; double bestScore = Double.MaxValue;
            foreach (PointF candidate in candidates)
            {
                PointF bounded = KnightPetController.BoundPosition(candidate.X, candidate.Y, size, area);
                RectangleF body = new RectangleF(bounded, size);
                double overlap = DesktopIntersectionArea(body, new RectangleF((float)x, (float)y, HeroSize.Width, HeroSize.Height));
                foreach (AdventureEnemy other in battle.Enemies)
                    if (other != enemy && !other.IsVisuallyHidden)
                        overlap += DesktopIntersectionArea(body, new RectangleF((float)other.X, (float)other.Y, EnemySize(other).Width, EnemySize(other).Height));
                double score = overlap * 1000 / Math.Max(1, size.Width * size.Height)
                    + AdventureBattle.Distance(proposal.X, proposal.Y, bounded.X, bounded.Y) / Math.Max(1, area.Width + area.Height);
                if (score + .000001 < bestScore) { best = bounded; bestScore = score; }
            }
            // Fixed candidates only: visual placement must not consume the
            // shared story/random stream or postpone a mandatory mirror spawn.
            enemy.X = best.X; enemy.Y = best.Y;
        }
        private static double DesktopIntersectionArea(RectangleF a, RectangleF b)
        { RectangleF intersection = RectangleF.Intersect(a, b); return Math.Max(0, intersection.Width) * Math.Max(0, intersection.Height); }

        private PointF DesktopBoundEnemyFeet(AdventureEnemy enemy, double feetX, double feetY)
        {
            Size size = EnemySize(enemy);
            PointF origin = KnightPetController.BoundPosition(feetX - size.Width / 2.0, feetY - size.Height, size, area);
            return new PointF(origin.X + size.Width / 2f, origin.Y + size.Height);
        }
        private static bool DesktopInside(PointF point, RectangleF bounds)
        { return point.X > bounds.Left && point.X < bounds.Right && point.Y > bounds.Top && point.Y < bounds.Bottom; }
        private static bool DesktopIdleSegmentBlocked(PointF a, PointF b, RectangleF obstacle)
        {
            double enter = 0, leave = 1;
            for (int axis = 0; axis < 2; axis++)
            {
                double start = axis == 0 ? a.X : a.Y, delta = axis == 0 ? b.X - a.X : b.Y - a.Y;
                double low = axis == 0 ? obstacle.Left : obstacle.Top, high = axis == 0 ? obstacle.Right : obstacle.Bottom;
                if (Math.Abs(delta) < .000001) { if (start <= low || start >= high) return false; continue; }
                double first = (low - start) / delta, last = (high - start) / delta;
                enter = Math.Max(enter, Math.Min(first, last)); leave = Math.Min(leave, Math.Max(first, last));
                if (enter >= leave - .0000001) return false;
            }
            return leave > 0 && enter < 1;
        }
        private static void DesktopConsiderIdlePath(PointF current, PointF a, PointF b, PointF goal, RectangleF obstacle,
            double remainder, ref double bestLength, ref PointF best)
        {
            if (DesktopIdleSegmentBlocked(current, a, obstacle) || DesktopIdleSegmentBlocked(a, b, obstacle)
                || DesktopIdleSegmentBlocked(b, goal, obstacle)) return;
            double first = AdventureBattle.Distance(current.X, current.Y, a.X, a.Y), middle = AdventureBattle.Distance(a.X, a.Y, b.X, b.Y);
            double length = first + middle + AdventureBattle.Distance(b.X, b.Y, goal.X, goal.Y) + remainder;
            if (length + .0001 >= bestLength) return;
            bestLength = length; best = first > .1 ? a : middle > .1 ? b : goal;
        }
        private PointF DesktopIdleWaypoint(AdventureEnemy enemy)
        {
            Size size = EnemySize(enemy); float margin = 8 * displayScale, cornerGap = displayScale;
            PointF current = new PointF((float)enemy.X, (float)enemy.Y);
            PointF destination = KnightPetController.BoundPosition(enemy.TargetX, enemy.TargetY, size, area);
            // Move the actor's origin around the hero expanded by its full
            // window, including the HUD. Actual sprite transparency stays intact.
            RectangleF obstacle = new RectangleF((float)x - size.Width - margin, (float)y - size.Height - margin,
                HeroSize.Width + size.Width + 2 * margin, HeroSize.Height + size.Height + 2 * margin);
            if (DesktopInside(current, obstacle))
            {
                PointF[] exits = { new PointF(current.X, obstacle.Top - cornerGap), new PointF(obstacle.Right + cornerGap, current.Y),
                    new PointF(current.X, obstacle.Bottom + cornerGap), new PointF(obstacle.Left - cornerGap, current.Y),
                    new PointF(area.Left, area.Top), new PointF(area.Right, area.Top), new PointF(area.Right, area.Bottom), new PointF(area.Left, area.Bottom), current };
                PointF exit = current; double bestScore = Double.MaxValue;
                RectangleF hero = new RectangleF((float)x, (float)y, HeroSize.Width, HeroSize.Height);
                double initialOverlap = DesktopIntersectionArea(new RectangleF(current, size), hero);
                foreach (PointF proposal in exits)
                {
                    PointF bounded = KnightPetController.BoundPosition(proposal.X, proposal.Y, size, area);
                    // Each axis reaches its greatest overlap nearest the hero's
                    // center. Their product bounds the complete escape segment;
                    // reject paths that would first push deeper through the HUD.
                    float nearestX = Math.Max(Math.Min(current.X, bounded.X), Math.Min(Math.Max(current.X, bounded.X), hero.Left + (hero.Width - size.Width) / 2));
                    float nearestY = Math.Max(Math.Min(current.Y, bounded.Y), Math.Min(Math.Max(current.Y, bounded.Y), hero.Top + (hero.Height - size.Height) / 2));
                    if (DesktopIntersectionArea(new RectangleF(nearestX, nearestY, size.Width, size.Height), hero) > initialOverlap + .001) continue;
                    // When a tiny work area cannot hold two windows, minimize
                    // overlap or wait; never teleport or enlarge the work area.
                    double overlap = DesktopIntersectionArea(new RectangleF(bounded, size), hero);
                    double score = (DesktopInside(bounded, obstacle) ? 1000 : 0) + overlap * 1000 / Math.Max(1, size.Width * size.Height)
                        + AdventureBattle.Distance(current.X, current.Y, bounded.X, bounded.Y) / Math.Max(1, area.Width + area.Height);
                    if (score + .000001 < bestScore) { exit = bounded; bestScore = score; }
                }
                return exit;
            }
            if (!DesktopInside(destination, obstacle) && !DesktopIdleSegmentBlocked(current, destination, obstacle)) return destination;
            PointF[] corners = { new PointF(obstacle.Left - cornerGap, obstacle.Top - cornerGap), new PointF(obstacle.Right + cornerGap, obstacle.Top - cornerGap),
                new PointF(obstacle.Right + cornerGap, obstacle.Bottom + cornerGap), new PointF(obstacle.Left - cornerGap, obstacle.Bottom + cornerGap) };
            for (int i = 0; i < corners.Length; i++) corners[i] = KnightPetController.BoundPosition(corners[i].X, corners[i].Y, size, area);
            PointF[] goals = DesktopInside(destination, obstacle)
                ? new[] { new PointF(obstacle.Left - cornerGap, destination.Y), new PointF(obstacle.Right + cornerGap, destination.Y),
                    new PointF(destination.X, obstacle.Top - cornerGap), new PointF(destination.X, obstacle.Bottom + cornerGap) }
                : new[] { destination };
            PointF best = current; double bestLength = Double.MaxValue;
            foreach (PointF proposedGoal in goals)
            {
                PointF goal = KnightPetController.BoundPosition(proposedGoal.X, proposedGoal.Y, size, area);
                if (DesktopInside(goal, obstacle)) continue;
                double remainder = AdventureBattle.Distance(goal.X, goal.Y, destination.X, destination.Y);
                if (!DesktopIdleSegmentBlocked(current, goal, obstacle))
                { DesktopConsiderIdlePath(current, goal, goal, goal, obstacle, remainder, ref bestLength, ref best); continue; }
                foreach (PointF a in corners)
                {
                    if (DesktopInside(a, obstacle)) continue;
                    DesktopConsiderIdlePath(current, a, a, goal, obstacle, remainder, ref bestLength, ref best);
                    foreach (PointF b in corners)
                        if (!DesktopInside(b, obstacle)) DesktopConsiderIdlePath(current, a, b, goal, obstacle, remainder, ref bestLength, ref best);
                }
            }
            // The patrol target/sector and random stream are untouched. If its
            // destination is occupied, wait at the safe proxy until the hero moves.
            return best;
        }
        private static double DesktopBodyOverlap(PointF feet, Size size, PointF otherFeet, Size otherSize)
        {
            // Estimate the illustrated body, excluding transparent atlas padding.
            double dx = (feet.X - otherFeet.X) / Math.Max(1, (size.Width + otherSize.Width) * .28);
            double dy = (feet.Y - size.Height * .45 - otherFeet.Y + otherSize.Height * .45)
                / Math.Max(1, (size.Height + otherSize.Height) * .28);
            double overlap = Math.Max(0, 1 - Math.Sqrt(dx * dx + dy * dy)); return overlap * overlap;
        }
        private double DesktopStandScore(AdventureEnemy enemy, PointF feet, PointF current)
        {
            Size size = EnemySize(enemy);
            double score = 2 * DesktopBodyOverlap(feet, size, new PointF((float)x + HeroSize.Width / 2f, (float)y + HeroSize.Height), HeroSize);
            // Older actors keep their place; later arrivals yield. Symmetric
            // per-frame pushing would make settled targets perpetually jitter.
            foreach (AdventureEnemy other in battle.Enemies)
            {
                if (other == enemy) break;
                if (other.IsVisuallyHidden) continue;
                Size otherSize = EnemySize(other);
                score += 8 * DesktopBodyOverlap(feet, size, new PointF((float)other.X + otherSize.Width / 2f, (float)other.Y + otherSize.Height), otherSize);
            }
            return score + .12 * AdventureBattle.Distance(current.X, current.Y, feet.X, feet.Y) / (80 * displayScale);
        }

        private RectangleF DesktopCombatBody(AdventureEnemy enemy, PointF feet)
        {
            Size size = EnemySize(enemy); float scale = Math.Min(size.Width / 220f, size.Height / 220f);
            // The illustration plus its existing name/health strip, excluding
            // transparent window padding. Use the renderer's uniform mapping.
            RectangleF logical = enemy.IsBoss ? new RectangleF(19, 7, 182, 191) : new RectangleF(28, 8, 164, 188);
            RectangleF body = new RectangleF(feet.X - size.Width / 2f + (size.Width - 220 * scale) / 2 + logical.X * scale,
                feet.Y - size.Height + (size.Height - 220 * scale) / 2 + logical.Y * scale, logical.Width * scale, logical.Height * scale);
            body.Inflate(2 * displayScale, 2 * displayScale); return body;
        }
        private List<RectangleF> DesktopCombatBlockers(AdventureEnemy enemy)
        {
            float s = displayScale;
            var blockers = new List<RectangleF> {
                new RectangleF((float)x + 8 * s, (float)y + 8 * s, 204 * s, 24 * s),
                new RectangleF((float)x + 68 * s, (float)y + 34 * s, 88 * s, 108 * s),
                new RectangleF((float)x + 34 * s, (float)y + 199 * s, 152 * s, 34 * s) };
            foreach (AdventureEnemy boss in battle.Enemies)
                if (boss != enemy && boss.IsBoss && !boss.IsVisuallyHidden && !boss.IsDiskWarning)
                {
                    Size bossSize = EnemySize(boss);
                    blockers.Add(DesktopCombatBody(boss, new PointF((float)boss.X + bossSize.Width / 2f, (float)boss.Y + bossSize.Height)));
                }
            bool earlier = true;
            foreach (AdventureEnemy other in battle.Enemies)
            {
                if (other == enemy) { earlier = false; continue; }
                if (other.IsVisuallyHidden || other.IsDiskWarning || other.IsBoss) continue;
                // The selected actor and every Boss reserve their actual visual
                // position even when appended after ordinary scenery.
                if (other != battle.Target && (!earlier || enemy == battle.Target)) continue;
                Size size = EnemySize(other);
                blockers.Add(DesktopCombatBody(other, new PointF((float)other.X + size.Width / 2f, (float)other.Y + size.Height)));
            }
            return blockers;
        }
        private double DesktopDenseStandScore(AdventureEnemy enemy, PointF feet, PointF current, List<RectangleF> blockers)
        {
            RectangleF body = DesktopCombatBody(enemy, feet); double score = 0;
            for (int i = 0; i < blockers.Count; i++)
                score += (i < 3 ? 2000 : 800) * DesktopIntersectionArea(body, blockers[i])
                    / Math.Max(1, Math.Min(body.Width * body.Height, blockers[i].Width * blockers[i].Height));
            // A clear waiting place stays clear and still. Only the current
            // target must enter the melee ring; idle peers need not orbit it.
            return score + .12 * AdventureBattle.Distance(current.X, current.Y, feet.X, feet.Y) / (80 * displayScale);
        }
        private bool DesktopDenseSegmentSafe(AdventureEnemy enemy, PointF from, PointF to, List<RectangleF> blockers)
        {
            RectangleF start = DesktopCombatBody(enemy, from), end = DesktopCombatBody(enemy, to);
            int protectedCount = 3 + battle.Enemies.Count(e => e != enemy && e.IsBoss && !e.IsVisuallyHidden && !e.IsDiskWarning);
            // Peers reserve destinations by stable priority. They are not hard
            // walls: coincident actors must be able to walk out of a crowd.
            for (int i = enemy == battle.Target ? 3 : 0; i < protectedCount; i++)
            {
                RectangleF block = blockers[i]; double overlap = DesktopIntersectionArea(start, block);
                if (overlap <= .001)
                {
                    RectangleF expanded = new RectangleF(block.Left - (start.Right - from.X), block.Top - (start.Bottom - from.Y), block.Width + start.Width, block.Height + start.Height);
                    if (DesktopIdleSegmentBlocked(from, to, expanded)) return false;
                }
                else
                {
                    // Same conservative whole-segment overlap bound as idle
                    // escape: an actor already inside must not first dig deeper.
                    float nearestX = Math.Max(Math.Min(start.X, end.X), Math.Min(Math.Max(start.X, end.X), block.Left + (block.Width - start.Width) / 2));
                    float nearestY = Math.Max(Math.Min(start.Y, end.Y), Math.Min(Math.Max(start.Y, end.Y), block.Top + (block.Height - start.Height) / 2));
                    if (DesktopIntersectionArea(new RectangleF(nearestX, nearestY, start.Width, start.Height), block) > overlap + .001) return false;
                }
            }
            return true;
        }
        private PointF DesktopDenseWaypoint(AdventureEnemy enemy, PointF current, PointF goal, List<RectangleF> blockers)
        {
            if (DesktopDenseSegmentSafe(enemy, current, goal, blockers)) return goal;
            RectangleF body = DesktopCombatBody(enemy, current); var corners = new List<PointF>(); bool inside = false;
            int protectedCount = 3 + battle.Enemies.Count(e => e != enemy && e.IsBoss && !e.IsVisuallyHidden && !e.IsDiskWarning);
            for (int i = enemy == battle.Target ? 3 : 0; i < protectedCount; i++)
            {
                RectangleF block = blockers[i];
                RectangleF expanded = new RectangleF(block.Left - (body.Right - current.X), block.Top - (body.Bottom - current.Y), block.Width + body.Width, block.Height + body.Height);
                inside |= DesktopIntersectionArea(body, block) > .001;
                float gap = displayScale;
                corners.Add(new PointF(expanded.Left - gap, expanded.Top - gap)); corners.Add(new PointF(expanded.Right + gap, expanded.Top - gap));
                corners.Add(new PointF(expanded.Right + gap, expanded.Bottom + gap)); corners.Add(new PointF(expanded.Left - gap, expanded.Bottom + gap));
                if (inside)
                {
                    corners.Add(new PointF(current.X, expanded.Top - gap)); corners.Add(new PointF(current.X, expanded.Bottom + gap));
                    corners.Add(new PointF(expanded.Left - gap, current.Y)); corners.Add(new PointF(expanded.Right + gap, current.Y));
                }
            }
            PointF best = current; double bestScore = inside ? DesktopDenseStandScore(enemy, current, current, blockers) : Double.MaxValue;
            // At most two corners around the fixed protected footprints. This
            // also reaches the far side of a Boss without crossing its body.
            PointF[] bounded = corners.Select(p => DesktopBoundEnemyFeet(enemy, p.X, p.Y)).Distinct().ToArray();
            foreach (PointF candidate in bounded)
            {
                if (AdventureBattle.Distance(current.X, current.Y, candidate.X, candidate.Y) < .1 || !DesktopDenseSegmentSafe(enemy, current, candidate, blockers)) continue;
                double score;
                if (inside) score = DesktopDenseStandScore(enemy, candidate, current, blockers);
                else
                {
                    double remainder = DesktopDenseSegmentSafe(enemy, candidate, goal, blockers)
                        ? AdventureBattle.Distance(candidate.X, candidate.Y, goal.X, goal.Y) : Double.MaxValue;
                    foreach (PointF second in bounded)
                        if (DesktopDenseSegmentSafe(enemy, candidate, second, blockers) && DesktopDenseSegmentSafe(enemy, second, goal, blockers))
                            remainder = Math.Min(remainder, AdventureBattle.Distance(candidate.X, candidate.Y, second.X, second.Y)
                                + AdventureBattle.Distance(second.X, second.Y, goal.X, goal.Y));
                    if (remainder == Double.MaxValue) continue;
                    score = AdventureBattle.Distance(current.X, current.Y, candidate.X, candidate.Y) + remainder;
                }
                if (score + .001 < bestScore) { bestScore = score; best = candidate; }
            }
            return best;
        }
        private PointF DesktopDenseStandPoint(AdventureEnemy enemy)
        {
            Size size = EnemySize(enemy); PointF current = new PointF((float)enemy.X + size.Width / 2f, (float)enemy.Y + size.Height);
            double heroX = x + HeroSize.Width / 2.0, heroY = y + HeroSize.Height;
            List<RectangleF> blockers = DesktopCombatBlockers(enemy); bool selected = enemy == battle.Target;
            PointF best = current; double score = selected ? Double.MaxValue : DesktopDenseStandScore(enemy, current, current, blockers);
            double[] radii = selected ? new[] { 80d } : new[] { 80d, 140d, 200d, 260d, 320d };
            foreach (double radius in radii) for (int i = 0; i < 16; i++)
            {
                double angle = i * Math.PI / 8;
                PointF candidate = DesktopBoundEnemyFeet(enemy, heroX + Math.Cos(angle) * radius * displayScale, heroY + Math.Sin(angle) * radius * displayScale);
                if (selected && AdventureBattle.Distance(heroX, heroY, candidate.X, candidate.Y) > EnemyAttackReach(enemy) * displayScale) continue;
                double next = DesktopDenseStandScore(enemy, candidate, current, blockers);
                if (next + .004 >= score) continue;
                PointF waypoint = DesktopDenseWaypoint(enemy, current, candidate, blockers);
                if (AdventureBattle.Distance(current.X, current.Y, waypoint.X, waypoint.Y) < .1
                    && AdventureBattle.Distance(current.X, current.Y, candidate.X, candidate.Y) >= .1) continue;
                score = next; best = waypoint;
            }
            return best;
        }
        private PointF DesktopEnemyStandPoint(AdventureEnemy enemy)
        {
            if (battle.Enemies.Any(e => e.IsBoss && !e.IsVisuallyHidden && !e.IsDiskWarning)
                || battle.Enemies.Count(e => !e.IsBoss && !e.IsVisuallyHidden) > 3) return DesktopDenseStandPoint(enemy);
            Size size = EnemySize(enemy);
            double heroX = x + HeroSize.Width / 2.0, heroY = y + HeroSize.Height, radius = 80 * displayScale;
            PointF current = new PointF((float)enemy.X + size.Width / 2f, (float)enemy.Y + size.Height);
            double distance = AdventureBattle.Distance(current.X, current.Y, heroX, heroY);
            double angle = distance > .001 ? Math.Atan2(current.Y - heroY, current.X - heroX) : Math.PI;
            PointF direct = DesktopBoundEnemyFeet(enemy, heroX + Math.Cos(angle) * radius, heroY + Math.Sin(angle) * radius);
            if (distance > radius + 4 * displayScale) return direct;
            PointF best = direct; double directScore = DesktopStandScore(enemy, direct, current), bestScore = directScore;
            for (int i = 0; i < 16; i++)
            {
                double candidateAngle = i * Math.PI / 8;
                PointF candidate = DesktopBoundEnemyFeet(enemy, heroX + Math.Cos(candidateAngle) * radius, heroY + Math.Sin(candidateAngle) * radius);
                if (AdventureBattle.Distance(heroX, heroY, candidate.X, candidate.Y) > EnemyAttackReach(enemy) * displayScale) continue;
                double score = DesktopStandScore(enemy, candidate, current);
                if (score < bestScore) { best = candidate; bestScore = score; }
            }
            if (directScore - bestScore < .004) return direct;
            double turn = Math.Atan2(best.Y - heroY, best.X - heroX) - angle;
            while (turn > Math.PI) turn -= 2 * Math.PI;
            while (turn < -Math.PI) turn += 2 * Math.PI;
            // Small arc waypoints keep the straight movement segment outside
            // the hero, even on a long frame. No teleport or speed override.
            turn = Math.Max(-Math.PI / 12, Math.Min(Math.PI / 12, turn));
            return DesktopBoundEnemyFeet(enemy, heroX + Math.Cos(angle + turn) * radius, heroY + Math.Sin(angle + turn) * radius);
        }

        private void AdvanceDesktopManualApproach(AdventureEnemy target, double now, double dt)
        {
            double range = battle.Weapon.Range * displayScale * .8;
            if (EnemyHeroDistance(target) > range || DesktopActorsOverlap(target))
            {
                PointF stand = DesktopCombatStandPoint(target);
                MoveToward(stand.X - HeroSize.Width / 2.0, stand.Y - HeroSize.Height, Math.Max(200, networkState.PixelsPerSecond), dt);
            }
            heroFacing = target.X + EnemySize(target).Width / 2.0 >= x + HeroSize.Width / 2.0;
            // Preserve the original non-mortal manual-mode behavior. A mortal
            // never attacks here; every strike still needs a mouse click.
            if (!cultivation.Mortal && battle.Cooldown <= 0 && EnemyHeroDistance(target) <= range + .01) StrikeTarget(target, now);
        }

        internal void DesktopSetActorsVisible(bool visible)
        {
            if (!IsolatedDesktop || disposed) return;
            desktopActorsVisible = visible;
            if (!visible)
            {
                effects.Clear(); ClearLoot(); heroWindow.Hide(); landmarkWindow.Hide();
                foreach (KnightLayeredWindow window in enemyWindows.Values) window.Hide();
            }
            else { lastHeroSignature = null; RenderWorld(true); }
        }

        internal bool DesktopSaveNow()
        {
            if (!IsolatedDesktop || disposed) return false;
            Save();
            return desktopLastSaveSucceeded;
        }

        // Deterministic logical verification only: no visible windows, no new
        // balancing rules, no health/kill injection. Every combat step is <= .1s
        // and uses the original Tick. The GUI acceptance uses the real timer.
        internal void DesktopAdvanceForTest(double seconds)
        {
            if (!IsolatedDesktop || Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds <= 0) return;
            bool previous = desktopTestAdvancing; desktopTestAdvancing = true;
            try
            {
                while (seconds > .000001)
                {
                    double step = Math.Min(.1, seconds), now = clock.Elapsed.TotalSeconds;
                    lastTick = now - step; lastCultivationAt = now - step;
                    desktopLastRefill -= step;
                    Tick(null, EventArgs.Empty);
                    seconds -= step;
                }
            }
            finally { desktopTestAdvancing = previous; }
        }

        internal static string DesktopStateSmoke(string assets, string profilesRoot)
        {
            profilesRoot = DesktopDemoProfiles.ValidateRoot(profilesRoot);
            string run = Path.Combine(profilesRoot, "state-smoke-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            string player = Path.Combine(run, "player"), full = Path.Combine(run, "qishi-full"), cave = Path.Combine(run, "zhuji");
            int checks = 0;
            Action<bool, string> check = delegate(bool ok, string message) { if (!ok) throw new InvalidOperationException("Desktop state smoke: " + message); checks++; };
            DesktopDemoProfiles.CreateSeed(player, "mortal");
            using (Form owner = new Form())
            {
                foreach (float scale in new[] { 1f, 1.5f, 2f })
                {
                    string spacing = Path.Combine(run, "spacing-" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    DesktopDemoProfiles.CreateSeed(spacing, "mortal");
                    using (AdventureController c = new AdventureController(owner, assets, false, null, false, spacing))
                    {
                        c.timer.Stop(); c.ConfigureDesktopDemo(false); c.timer.Stop();
                        if (scale == 1)
                        {
                            // Freeze only this offscreen fixture clock so frame
                            // identity can be tested without scheduler timing.
                            c.clock.Stop();
                            AdventureRenderState visual = new AdventureRenderState { Phase = "run", WeaponId = "spear", HeroStage = "mortal", Size = new Size(220, 240), MoveSpeed = 200, FacingRight = true, HealthRatio = 1, Level = 1, Label = "frame-regression", Cycle = .1 };
                            string firstFrame = c.HeroSignature(visual);
                            visual.Time = .032; visual.Cycle = .8; visual.PhaseTime = .027; visual.MoveSpeed = 220;
                            check(c.HeroSignature(visual) == firstFrame, "one desktop frame bucket ignores independently advancing cycle, render time, phase time and speed bins");
                            visual.Phase = "guard";
                            check(c.HeroSignature(visual) != firstFrame, "input phase changes refresh immediately inside a frame bucket");
                            visual.Phase = "run"; visual.FacingRight = false;
                            check(c.HeroSignature(visual) != firstFrame, "facing changes refresh immediately inside a frame bucket");
                            visual.FacingRight = true; visual.HealthRatio = .90;
                            check(c.HeroSignature(visual) != firstFrame, "health changes refresh immediately inside a frame bucket");
                            visual.HealthRatio = 1; visual.HeroStage = "qishi";
                            check(c.HeroSignature(visual) != firstFrame, "growth appearance changes refresh immediately inside a frame bucket");
                            visual.HeroStage = "mortal";
                            AdventureEnemy animated = c.battle.Enemies[0]; animated.MoveSpeed = 100;
                            string firstEnemyFrame = c.EnemySignature(animated, "frame-regression", 0);
                            animated.AnimationTime += .15; animated.DeathElapsed += .15; animated.MoveSpeed = 119;
                            check(c.EnemySignature(animated, "frame-regression", .3) == firstEnemyFrame, "one enemy frame bucket ignores independent animation, death and speed bins");
                            animated.FacingRight = !animated.FacingRight;
                            check(c.EnemySignature(animated, "frame-regression", .3) != firstEnemyFrame, "enemy facing changes refresh immediately inside a frame bucket");
                            animated.FacingRight = !animated.FacingRight;
                            c.clock.Start(); System.Threading.Thread.Sleep(120); c.clock.Stop();
                            check(c.HeroSignature(visual) != firstFrame && c.EnemySignature(animated, "frame-regression", .3) != firstEnemyFrame,
                                "crossing the wall-clock bucket advances hero and enemy frames even without another pose clock changing");
                            c.clock.Start();
                        }
                        c.displayScale = scale; c.area = new Rectangle(-22000, -22000, (int)(1500 * scale), (int)(1100 * scale));
                        c.x = c.area.Left + 300 * scale; c.y = c.area.Top + 300 * scale; c.ClampHero();
                        c.battle.Enemies.Clear(); c.desktopLastRefill = c.clock.Elapsed.TotalSeconds + 10000;
                        AdventureEnemy opponent = new AdventureEnemy { Id = "spacing-opponent", IsVirtual = true, MonsterKind = "skeleton", Health = 100, MaxHealth = 100, SpawnLevel = 1 };
                        opponent.X = c.x + c.HeroSize.Width / 2.0 + 10 * scale - c.EnemySize(opponent).Width / 2.0;
                        opponent.Y = c.y + c.HeroSize.Height - c.EnemySize(opponent).Height;
                        c.battle.Enemies.Add(opponent); c.battle.Authorize(opponent);
                        c.DesktopAdvanceForTest(3);
                        double gap = c.EnemyHeroDistance(opponent);
                        check(gap >= 70 * scale && gap <= c.battle.Weapon.Range * scale * .8 + .01,
                            "overlapping mortal actors separate to a readable, attackable foot gap at scale " + scale + "; actual=" + gap);
                        double heroHold = c.x, enemyHold = opponent.X; int untouched = opponent.Health;
                        c.DesktopAdvanceForTest(2);
                        check(Math.Abs(c.x - heroHold) < 2 * scale && Math.Abs(opponent.X - enemyHold) < 2 * scale && opponent.Health == untouched,
                            "manual stand-off settles without endless pursuit or any automatic mortal strike at scale " + scale);
                        c.DesktopStrikeSelected();
                        check(opponent.Health < untouched, "the same unmodified weapon range permits the next explicit manual strike at scale " + scale);
                        // The automatic planner still decides attacks and evasion;
                        // an overlap needs only a presentation destination guard.
                        c.cultivation.Mortal = false; c.battle.SetAutomatic(true); c.battle.Cooldown = 0;
                        opponent.X = c.x + c.HeroSize.Width / 2.0 + 10 * scale - c.EnemySize(opponent).Width / 2.0;
                        opponent.Y = c.y + c.HeroSize.Height - c.EnemySize(opponent).Height;
                        double beforeGap = c.EnemyHeroDistance(opponent); c.desktopTestAdvancing = true;
                        c.AdvanceAutomaticCombat(opponent, 0, .05);
                        check(c.EnemyHeroDistance(opponent) > beforeGap + scale,
                            "a ready automatic attack also opens an overlapping silhouette at scale " + scale);
                        for (int frame = 0; frame < 18; frame++) { c.battle.Tick(.05); c.AdvanceAutomaticCombat(opponent, frame * .05, .05); }
                        c.desktopTestAdvancing = false;
                        check(c.EnemyHeroDistance(opponent) >= 70 * scale && c.battle.Cooldown > 0,
                            "automatic spacing still makes real attacks without permanent chase at scale " + scale);
                    }
                }
                using (AdventureController c = new AdventureController(owner, assets, false, null, false, player))
                {
                    c.timer.Stop(); c.area = new Rectangle(-22000, -22000, 1250, 850);
                    c.x = c.area.Left + 150; c.y = c.area.Top + 300; c.ClampHero(); c.ConfigureDesktopDemo(false); c.timer.Stop();
                    check(!c.demonstrationOnly && !c.artDemoRuntime && c.IsolatedDesktop, "persistent desktop uses the formal controller, not an in-memory alternate game");
                    check(c.DesktopArchivePaths.All(p => p.StartsWith(player + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)), "all five archives resolve to the private player root");
                    check(c.ConsentWritePath.StartsWith(player + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "even the disabled maintenance consent path resolves inside the isolated profile");
                    check(c.cultivation.Mortal && !c.battle.Automatic && c.battle.Enemies.Count == 2 && c.battle.Enemies.All(e => e.IsVirtual), "new player begins mortal with ordinary virtual enemies and no automatic battle");
                    check(!c.SetCleanupConsent(true) && !c.SetMemoryConsent(true) && !c.SetAutoMaintenanceConsent(true)
                        && !c.consent.CleanupEnabled && !c.consent.MemoryEnabled && !c.consent.AutoMaintenanceEnabled,
                        "all three real maintenance scopes refuse enable requests");
                    c.RefreshWorld(); c.RefreshRegion(); c.ReadMemory(0); c.ReadDisk(0);
                    check(c.DesktopRealWorkIdle && !File.Exists(Path.Combine(player, "maintenance-consent.txt")), "desktop mode never starts network, temp scan, disk/RAM jobs or writes consent files");
                    check(!c.AwakenSpiritRoot() && !c.cultivation.CanAwaken, "awakening cannot bypass the ten effective kills rule");
                    AdventureEnemy target = c.battle.Enemies[0]; c.battle.Authorize(target);
                    c.DesktopAdvanceForTest(4);
                    int health = target.Health; c.DesktopAdvanceForTest(3);
                    check(target.Health == health && c.cultivation.MortalKills == 0, "selecting an enemy only approaches it; mortal combat never attacks autonomously");
                    c.DesktopStrikeSelected();
                    check(target.Health < health || target.FellInCombat, "manual strike uses the original range, cooldown and damage");
                    check(c.TryBlock() && !c.TryBlock(), "guard works and retains its original cooldown");
                    int attacks = 1;
                    for (int i = 0; i < 18000 && !c.cultivation.CanAwaken; i++)
                    {
                        target = c.battle.Target ?? c.battle.Enemies.Where(e => !e.Defeated && !e.VisualDefeated).FirstOrDefault();
                        if (target != null && !c.battle.Progress.Dead) { c.battle.Authorize(target); c.DesktopStrikeSelected(); attacks++; }
                        c.DesktopAdvanceForTest(.1);
                    }
                    check(c.cultivation.MortalKills == 10 && c.battle.Progress.Score >= 10 && attacks > 10, "ten genuine combat settlements unlock awakening without kill/health injection");
                    check(c.AwakenSpiritRoot() && c.ArtDemoHeroStage == "qishi" && c.battle.Automatic, "awakening switches the actual state to qi refining and enables automatic combat");
                    int score = c.battle.Progress.Score; long qi = c.cultivation.LifetimeQi;
                    c.DesktopSetActorsVisible(false);
                    for (int i = 0; i < 12000 && c.battle.Progress.Score <= score; i++) c.DesktopAdvanceForTest(.1);
                    check(c.battle.Progress.Score > score && c.cultivation.LifetimeQi > qi && !c.heroWindow.Visible, "automatic combat and cultivation continue with all panels and actors hidden");
                    check(c.DesktopRealWorkIdle && c.battle.Progress.FreedBytes == 0, "combat cannot create real maintenance jobs or invented freed bytes");
                    c.baiye.ManualMarkId = ""; c.lingyu.Materials["state-smoke"] = 2; c.dongtian.Bag["linggu"] = 3;
                    check(c.DesktopSaveNow() && c.DesktopArchivePaths.All(File.Exists), "all five archives are saved at the isolated paths");
                    FileStream locked = new FileStream(c.CultivationWritePath, FileMode.Open, FileAccess.Read, FileShare.None);
                    try { check(!c.DesktopSaveNow() && c.DesktopNotice.Contains("暂未保存"), "an unwritable archive reports failure rather than claiming durable success"); }
                    finally { locked.Dispose(); }
                    check(c.DesktopSaveNow(), "save recovers after the archive becomes writable");
                }
                using (AdventureController restored = new AdventureController(owner, assets, false, null, false, player))
                {
                    restored.timer.Stop();
                    check(!restored.cultivation.Mortal && restored.ArtDemoHeroStage == "qishi" && restored.cultivation.MortalKills == 10
                        && restored.lingyu.Materials.ContainsKey("state-smoke") && restored.dongtian.CountOf("linggu") == 3,
                        "normal Dispose and restart restore actual growth appearance and the independent feature archives");
                    check(restored.battle.Automatic && restored.DesktopRealWorkIdle, "saved automatic combat preference restores without maintenance/network");
                }
                bool refused = false;
                try { DesktopDemoProfiles.CreateSeed(player, "zhuji"); } catch (InvalidOperationException) { refused = true; }
                check(refused, "creating a test fixture never overwrites an existing player");
                DesktopDemoProfiles.CreateSeed(full, "qishi-full");
                using (AdventureController c = new AdventureController(owner, assets, false, null, false, full))
                {
                    c.timer.Stop();
                    check(c.cultivation.CanAttemptBreakthrough && c.ArtDemoHeroStage == "qishi", "independent qi-full fixture meets the original breakthrough requirements");
                    check(c.TryBreakthrough(true) && c.cultivation.RealmIndex == 1 && c.ArtDemoHeroStage == "zhuji", "the original guaranteed-insight breakthrough changes actual realm and appearance");
                    check(c.DesktopSaveNow(), "breakthrough writes only the private fixture");
                }
                using (AdventureController c = new AdventureController(owner, assets, false, null, false, full))
                { c.timer.Stop(); check(c.ArtDemoHeroStage == "zhuji" && c.DongtianUnlocked, "foundation appearance and cave unlock survive restart"); }
                DesktopDemoProfiles.CreateSeed(cave, "zhuji");
                DongtianForm lifetimePanel = null;
                using (AdventureController c = new AdventureController(owner, assets, false, null, false, cave))
                {
                    c.timer.Stop();
                    check(c.DongtianPlant(0, "linggu") && c.DongtianImport("shuiyue-lingsui") && c.DongtianStartPill("ninglu"), "original cave planting, material transfer and alchemy persist in the independent foundation fixture");
                    check(c.LingyuEnter("qianmian") && c.Lingyu.ExpeditionActive, "original mirage entry works from the foundation fixture");
                    for (int i = 0; i < 300 && !c.Lingyu.CombatPending; i++)
                    {
                        if (c.Mirage.PendingJiyuan != null) c.LingyuChoose(0);
                        else c.LingyuChoose(c.Lingyu.ExpeditionNode == "ferry" || c.Lingyu.ExpeditionNode == "corridor" ? 0 : 1);
                    }
                    AdventureEnemy mirror = c.battle.Enemies.FirstOrDefault(e => e.Id == c.pendingLingyuEnemyId);
                    check(c.Lingyu.CombatPending && mirror != null && mirror.NoFieldClear && mirror.IsVirtual, "real exploration choices spawn a virtual interactive mirage enemy");
                    check(c.DesktopSaveNow(), "cave and expedition state save through the same five-archive controller");
                    lifetimePanel = c.dongtianForm = new DongtianForm(c);
                }
                check(lifetimePanel.IsDisposed, "controller disposal also disposes its cave panel and refresh timer");
                using (AdventureController c = new AdventureController(owner, assets, false, null, false, cave))
                {
                    c.timer.Stop(); c.ConfigureDesktopDemo(false); c.timer.Stop(); c.DesktopAdvanceForTest(.1);
                    check(c.Dongtian.Fields[0] != null && c.Dongtian.Furnace != null && c.Lingyu.ExpeditionActive, "cave timers and active expedition survive restart");
                    AdventureEnemy mirror = c.battle.Enemies.FirstOrDefault(e => e.Id == c.pendingLingyuEnemyId);
                    check(c.Lingyu.CombatPending && mirror != null && mirror.NoFieldClear, "pending mirror combat is restored as an actual enemy after restart");
                    for (int i = 0; i < 16000 && c.Lingyu.CombatPending; i++) c.DesktopAdvanceForTest(.1);
                    check(!c.Lingyu.CombatPending && c.DesktopRealWorkIdle, "restored mirror fight resolves through original automatic combat without any real maintenance");
                }
            }
            bool liveRefused = false;
            try { DesktopDemoProfiles.ValidateRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant")); } catch (ArgumentException) { liveRefused = true; }
            check(liveRefused, "the controller refuses the live profile directory as an isolated root");
            return "Desktop state smoke: " + checks + " checks passed. Formal controller, five isolated archives, ten real combat kills, awakening/auto combat, safe save failure/restart, breakthrough/cave/mirage. Logical time advanced for test only; this is not GUI acceptance.\r\nEvidence profiles: " + run;
        }
    }

    internal static class DesktopDemoProfiles
    {
        internal static string Smoke(string assets, string profilesRoot) { return AdventureController.DesktopStateSmoke(assets, profilesRoot); }
        internal static string ValidateRoot(string root)
        {
            if (String.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root)) throw new ArgumentException("隔离存档必须是绝对路径。");
            string full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string live = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant")).TrimEnd(Path.DirectorySeparatorChar);
            if (full.Length < 4 || full.Equals(live, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(live + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || live.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("隔离存档不得指向真实存档或其父目录。");
            for (DirectoryInfo dir = new DirectoryInfo(full); dir != null; dir = dir.Parent)
                if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("隔离存档路径不能经过目录联接或符号链接。");
            return full;
        }

        internal static void CreateSeed(string root, string seed)
        {
            root = ValidateRoot(root);
            if (seed != "qishi-full" && seed != "zhuji" && seed != "mortal" && !String.IsNullOrEmpty(seed))
                throw new ArgumentException("未知测试档：" + seed);
            if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
                throw new InvalidOperationException("测试档只能建立在新的空目录，不能重置已有修士。");
            Directory.CreateDirectory(root);
            CultivationProgress cultivation = new CultivationProgress();
            AdventureProgress battle = new AdventureProgress { ThemeMode = "OFF" };
            DongtianProgress cave = new DongtianProgress();
            LingyuProgress lingyu = new LingyuProgress();
            if (seed == "qishi-full" || seed == "zhuji")
            {
                cultivation.Mortal = false; cultivation.RealmIndex = seed == "zhuji" ? 1 : 0;
                cultivation.Layer = seed == "zhuji" ? 1 : cultivation.LayersMax;
                cultivation.EnsureIdentity(new Random(17));
                cultivation.LastNotice = "独立测试档 · " + (seed == "zhuji" ? "筑基洞天" : "炼气圆满破境");
                if (seed == "qishi-full")
                {
                    cultivation.BreakthroughInsight = cultivation.RealmDef.GuaranteedInsight;
                    cultivation.GrantOpportunity("insight", "desktop-seed");
                    lingyu.Materials["shuiyue-lingsui"] = 2;
                }
                else { cave.Bag["linggu"] = 4; cave.Bag["ningshen"] = 2; lingyu.Materials["shuiyue-lingsui"] = 2; }
            }
            battle.Save(Path.Combine(root, "adventure-v1.txt"));
            cultivation.Save(Path.Combine(root, "cultivation-v2.txt"));
            new BaiyeProgress().Save(Path.Combine(root, "baiye-v1.txt"));
            lingyu.Save(Path.Combine(root, "lingyu-v1.txt"));
            cave.Save(Path.Combine(root, "dongtian-v1.txt"));
            File.WriteAllText(Path.Combine(root, "desktop-demo-seed.txt"),
                "独立桌面 Demo 存档\r\nseed=" + (String.IsNullOrEmpty(seed) ? "mortal" : seed) + "\r\n测试档预置资源和境界，不代表自然成长。真实维护始终关闭。\r\n", new UTF8Encoding(true));
        }
    }
}
