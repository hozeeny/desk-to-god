using System;
using System.Drawing;

namespace NetBunny
{
    // Pure movement advice. Coordinates and bounds share a caller-selected anchor
    // (the controller uses feet); this class never authorizes combat or cleanup.
    internal sealed class CombatTacticsInput
    {
        internal bool Automatic;
        internal string TargetId;
        internal PointF Hero, Target;
        internal RectangleF MovementBounds;
        internal double Scale = 1, WeaponRange = 88, Cooldown, HealthRatio = 1, Time;
        internal PointF[] Threats;
    }

    internal sealed class CombatTacticsDecision
    {
        internal bool Move, Attack;
        internal PointF Destination;
        internal string Mode;
    }

    internal sealed class CombatTactics
    {
        private string currentTarget;
        private int orbitSide = 1;
        private double nextTurnAllowed;

        internal void Reset()
        {
            currentTarget = null; orbitSide = 1; nextTurnAllowed = 0;
        }

        internal CombatTacticsDecision Plan(CombatTacticsInput input)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (!Finite(input.Hero) || !Finite(input.Target) || !Finite(input.MovementBounds.X) ||
                !Finite(input.MovementBounds.Y) || !Finite(input.MovementBounds.Width) || !Finite(input.MovementBounds.Height) ||
                input.MovementBounds.Width < 0 || input.MovementBounds.Height < 0)
                return new CombatTacticsDecision { Destination = input.Hero, Mode = "hold" };
            double scale = Finite(input.Scale) && input.Scale > 0 ? input.Scale : 1;
            double range = Finite(input.WeaponRange) ? Math.Max(1, input.WeaponRange) : 88 * scale;
            double now = Finite(input.Time) ? input.Time : 0;
            double health = Finite(input.HealthRatio) ? Math.Max(0, Math.Min(1, input.HealthRatio)) : 1;
            bool ready = Finite(input.Cooldown) && input.Cooldown <= 0;
            double dx = input.Hero.X - input.Target.X, dy = input.Hero.Y - input.Target.Y;
            double distance = Length(dx, dy);
            if (!input.Automatic)
            {
                Reset();
                // Manual targeting preserves the original straight pursuit.
                return Decision(input, distance > range ? Clamp(input.Target, input.MovementBounds) : input.Hero,
                    ready && distance <= range, distance > range ? "approach" : "hold", scale);
            }
            if (!String.Equals(currentTarget, input.TargetId, StringComparison.Ordinal))
            {
                currentTarget = input.TargetId;
                // A small stable hash avoids randomized hash implementation or per-frame RNG.
                int hash = 0;
                if (currentTarget != null) foreach (char c in currentTarget) hash = unchecked(hash * 31 + c);
                orbitSide = (hash & 1) == 0 ? 1 : -1;
                nextTurnAllowed = now + .7;
            }
            // A ready, valid hit always wins over flight. Otherwise low health or
            // a large crowd could cause endless retreat without any counterattack.
            if (ready && distance <= range)
                return Decision(input, input.Hero, true, "counter", scale);
            if (ready)
                return Decision(input, Clamp(input.Target, input.MovementBounds), false, "approach", scale);

            double awayX, awayY;
            if (distance > .001) { awayX = dx / distance; awayY = dy / distance; }
            else
            {
                // Deterministic escape when actors have the same anchor.
                awayX = orbitSide; awayY = 0;
            }
            double pushX = 0, pushY = 0;
            int nearby = 0;
            if (input.Threats != null) foreach (PointF threat in input.Threats)
            {
                if (!Finite(threat)) continue;
                double ex = input.Hero.X - threat.X, ey = input.Hero.Y - threat.Y;
                double separation = Length(ex, ey);
                if (separation > 190 * scale) continue;
                nearby++;
                if (separation > .001)
                {
                    double weight = Math.Max(0, 1 - separation / (190 * scale));
                    pushX += ex / separation * weight; pushY += ey / separation * weight;
                }
            }
            bool pressure = health <= .35 || nearby >= 3;
            bool melee = range <= 160 * scale;
            double preferred = melee ? range + (pressure ? 58 : 28) * scale : range * (pressure ? .86 : .70);
            double gap = preferred - distance;
            double tangentX = -awayY * orbitSide, tangentY = awayX * orbitSide;
            double directionX, directionY;
            string mode;
            if (gap > 10 * scale)
            {
                // Keep a lateral component so an approaching group is not pulled
                // along one straight line and edge escape has a stable side.
                directionX = awayX + tangentX * .35;
                directionY = awayY + tangentY * .35;
                mode = "retreat";
            }
            else
            {
                double correction = Math.Max(-.85, Math.Min(.85, gap / (60 * scale)));
                directionX = tangentX + awayX * correction;
                directionY = tangentY + awayY * correction;
                mode = pressure ? "retreat" : "flank";
            }
            if (pressure) { directionX += pushX * .75; directionY += pushY * .75; }
            double norm = Length(directionX, directionY);
            if (norm < .001) { directionX = tangentX; directionY = tangentY; norm = 1; }
            directionX /= norm; directionY /= norm;
            PointF destination = SafeWaypoint(input, directionX, directionY, 110 * scale, preferred, pressure);

            // Stay on the chosen flank until an edge actually blocks that flank.
            // The time gate prevents rapid left/right changes along corners.
            double actualX = destination.X - input.Hero.X, actualY = destination.Y - input.Hero.Y;
            if (now >= nextTurnAllowed && actualX * tangentX + actualY * tangentY < -10 * scale)
            { orbitSide = -orbitSide; nextTurnAllowed = now + .7; }
            return Decision(input, destination, false, mode, scale);
        }

        private static PointF SafeWaypoint(CombatTacticsInput input, double vx, double vy, double step, double preferred, bool pressure)
        {
            // Evaluate traversable directions, not only a clamped outward point:
            // at a wall or corner clamping alone would repeatedly yield zero movement.
            double[] angles = { 0, .45, -.45, .9, -.9, Math.PI / 2, -Math.PI / 2, Math.PI };
            PointF best = Clamp(input.Hero, input.MovementBounds);
            double bestScore = Double.NegativeInfinity;
            foreach (double angle in angles)
            {
                double tx = vx * Math.Cos(angle) - vy * Math.Sin(angle);
                double ty = vx * Math.Sin(angle) + vy * Math.Cos(angle);
                PointF p = Clamp(new PointF((float)(input.Hero.X + tx * step), (float)(input.Hero.Y + ty * step)), input.MovementBounds);
                double ax = p.X - input.Hero.X, ay = p.Y - input.Hero.Y;
                double moved = Length(ax, ay);
                if (moved < .001) continue;
                double alignment = (ax * vx + ay * vy) / moved;
                double separation = Length(p.X - input.Target.X, p.Y - input.Target.Y);
                double spacing = Math.Min(1, separation / Math.Max(1, preferred));
                double score = alignment * 32 + Math.Min(1, moved / step) * 34 + spacing * (pressure ? 30 : 15);
                if (input.Threats != null) foreach (PointF threat in input.Threats)
                {
                    if (!Finite(threat)) continue;
                    double enemyDistance = Length(p.X - threat.X, p.Y - threat.Y);
                    // Penalize paths ending in another nearby enemy.
                    if (enemyDistance < step * .75) score -= (1 - enemyDistance / (step * .75)) * (pressure ? 30 : 12);
                }
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        private static CombatTacticsDecision Decision(CombatTacticsInput input, PointF destination, bool attack, string mode, double scale)
        {
            return new CombatTacticsDecision { Destination = destination, Attack = attack,
                Move = !attack && Length(destination.X - input.Hero.X, destination.Y - input.Hero.Y) > .5 * scale, Mode = mode };
        }
        private static PointF Clamp(PointF point, RectangleF bounds)
        {
            return new PointF(Math.Max(bounds.Left, Math.Min(bounds.Right, point.X)), Math.Max(bounds.Top, Math.Min(bounds.Bottom, point.Y)));
        }
        private static double Length(double x, double y) { return Math.Sqrt(x * x + y * y); }
        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static bool Finite(PointF value) { return Finite(value.X) && Finite(value.Y); }

        internal static string SelfTest()
        {
            int checks = 0;
            Action<bool, string> check = delegate(bool okay, string label)
            { if (!okay) throw new InvalidOperationException("Combat tactics: " + label); checks++; };
            Func<CombatTacticsInput> fresh = delegate
            {
                return new CombatTacticsInput { Automatic = true, TargetId = "synthetic-target", Hero = new PointF(500, 300),
                    Target = new PointF(560, 300), MovementBounds = new RectangleF(0, 0, 1000, 600), WeaponRange = 88, Cooldown = .5,
                    Threats = new[] { new PointF(560, 300) } };
            };
            CombatTactics tactics = new CombatTactics(); CombatTacticsInput input = fresh();
            CombatTacticsDecision decision = tactics.Plan(input);
            check(decision.Move && !decision.Attack && decision.Mode == "retreat", "melee cooldown causes deliberate retreat");
            check(Length(decision.Destination.X - input.Target.X, decision.Destination.Y - input.Target.Y) > 60, "retreat opens distance from target");
            PointF first = decision.Destination;
            for (int i = 0; i < 20; i++)
            {
                input.Time = i * .015; decision = tactics.Plan(input);
                check(decision.Destination == first, "identical situation keeps its flank without per-frame random jitter");
            }
            input.Cooldown = 0; decision = tactics.Plan(input);
            check(decision.Attack && !decision.Move && decision.Mode == "counter", "ready melee counterattack has priority over evasion");
            input.HealthRatio = .1; input.Threats = new[] { new PointF(560, 300), new PointF(450, 300), new PointF(500, 350) };
            decision = tactics.Plan(input);
            check(decision.Attack && !decision.Move, "low health and a crowd do not cause endless flight");
            input.Target = new PointF(720, 300); decision = tactics.Plan(input);
            check(decision.Move && !decision.Attack && decision.Mode == "approach" && decision.Destination == input.Target, "after cooldown returns to attack range");
            input = fresh(); input.Cooldown = .3; input.HealthRatio = .2; decision = tactics.Plan(input);
            check(decision.Move && decision.Mode == "retreat", "low health increases evasive spacing during reload");
            input.HealthRatio = 1; input.Threats = new[] { new PointF(560, 300), new PointF(450, 300), new PointF(500, 350) };
            decision = tactics.Plan(input);
            check(decision.Move && decision.Mode == "retreat", "three nearby enemies trigger crowd avoidance");
            input = fresh(); input.WeaponRange = 520; input.Target = new PointF(800, 300); decision = tactics.Plan(input);
            check(decision.Move && !decision.Attack && decision.Mode == "retreat", "ranged weapon preserves shooting distance");
            input.Target = new PointF(864, 300); decision = tactics.Plan(input);
            check(decision.Move && decision.Mode == "flank" && Math.Abs(decision.Destination.Y - input.Hero.Y) > 25, "at preferred range strafes instead of standing still");
            input.Cooldown = 0; decision = tactics.Plan(input);
            check(decision.Attack && !decision.Move, "ranged counterattack fires without crossing outside range");
            input = fresh(); input.Automatic = false; decision = tactics.Plan(input);
            check(!decision.Move && !decision.Attack, "manual close targeting keeps its original cooldown hold");
            input.Cooldown = 0; decision = tactics.Plan(input);
            check(decision.Attack && !decision.Move, "manual attack remains ready inside range");
            input.Target = new PointF(800, 300); decision = tactics.Plan(input);
            check(decision.Move && !decision.Attack && decision.Destination == input.Target, "manual targeting keeps direct pursuit");

            PointF[] edges = { new PointF(0, 0), new PointF(1000, 0), new PointF(0, 600), new PointF(1000, 600),
                new PointF(0, 300), new PointF(1000, 300), new PointF(500, 0), new PointF(500, 600) };
            foreach (PointF edge in edges)
            {
                input = fresh(); input.Hero = edge;
                input.Target = new PointF(edge.X + (edge.X < 500 ? 40 : -40), edge.Y + (edge.Y < 300 ? 40 : -40));
                input.Threats = new[] { input.Target }; decision = new CombatTactics().Plan(input);
                check(decision.Move && Finite(decision.Destination), "wall or corner always has a traversable escape");
                check(decision.Destination.X >= 0 && decision.Destination.X <= 1000 && decision.Destination.Y >= 0 && decision.Destination.Y <= 600, "edge escape stays in legal bounds");
            }
            input = fresh(); input.Hero = input.Target; decision = tactics.Plan(input);
            check(decision.Move && Finite(decision.Destination), "overlapping actors choose a finite deterministic escape");
            input.MovementBounds = new RectangleF(input.Hero, SizeF.Empty); decision = tactics.Plan(input);
            check(!decision.Move && !decision.Attack && Finite(decision.Destination), "zero-size screen has no impossible movement");
            input = fresh(); input.WeaponRange = Double.NaN; input.Scale = Double.NaN; input.Threats = new[] { new PointF(Single.NaN, 0) }; decision = tactics.Plan(input);
            check(decision.Move && Finite(decision.Destination), "invalid optional telemetry does not poison movement");
            input.Target = new PointF(Single.NaN, 0); decision = tactics.Plan(input);
            check(!decision.Move && !decision.Attack && decision.Mode == "hold", "invalid target fails closed");
            input = fresh(); input.Hero = new PointF(-1000, -400); input.Target = new PointF(-950, -400);
            input.MovementBounds = new RectangleF(-1200, -600, 1000, 600); input.Threats = new[] { input.Target };
            decision = tactics.Plan(input);
            check(decision.Move && decision.Destination.X >= -1200 && decision.Destination.X <= -200 &&
                decision.Destination.Y >= -600 && decision.Destination.Y <= 0, "negative monitor coordinates preserve legal escape bounds");

            // Simulate pursuit and a real cooldown: dodging must still yield regular
            // attacks, and every movement segment must fit a desktop at 1x or 2x DPI.
            foreach (double scale in new[] { 1.0, 2.0 }) foreach (double range in new[] { 88.0, 124.0, 304.0, 520.0, 720.0 }) foreach (bool corner in new[] { false, true })
            {
                input = fresh(); input.Scale = scale; input.WeaponRange = range * scale;
                input.Hero = new PointF((float)(250 * scale), (float)(250 * scale));
                input.Target = new PointF((float)(320 * scale), (float)(250 * scale));
                if (corner) { input.Hero = PointF.Empty; input.Target = new PointF((float)(50 * scale), (float)(50 * scale)); }
                input.MovementBounds = new RectangleF(0, 0, (float)(1000 * scale), (float)(600 * scale));
                input.Cooldown = 0; input.HealthRatio = .25;
                int attacks = 0, retreats = 0; double totalTravel = 0;
                tactics = new CombatTactics();
                for (int frame = 0; frame < 1500; frame++)
                {
                    const double dt = .02;
                    input.Time = frame * dt; input.Cooldown = Math.Max(0, input.Cooldown - dt); input.Threats = new[] { input.Target };
                    decision = tactics.Plan(input);
                    if (decision.Attack) { attacks++; input.Cooldown = .58; }
                    if (decision.Move)
                    {
                        if (decision.Mode == "retreat") retreats++;
                        double vx = decision.Destination.X - input.Hero.X, vy = decision.Destination.Y - input.Hero.Y, length = Length(vx, vy);
                        double move = Math.Min(length, 200 * scale * dt); totalTravel += move;
                        input.Hero = new PointF((float)(input.Hero.X + vx / length * move), (float)(input.Hero.Y + vy / length * move));
                    }
                    double enemyX = input.Hero.X - input.Target.X, enemyY = input.Hero.Y - input.Target.Y, enemyDistance = Length(enemyX, enemyY);
                    if (enemyDistance > 70 * scale)
                    {
                        double move = Math.Min(enemyDistance - 70 * scale, 80 * scale * dt);
                        input.Target = new PointF((float)(input.Target.X + enemyX / enemyDistance * move), (float)(input.Target.Y + enemyY / enemyDistance * move));
                    }
                    if (!Finite(input.Hero) || input.Hero.X < -.001 || input.Hero.Y < -.001 || input.Hero.X > input.MovementBounds.Right + .001 || input.Hero.Y > input.MovementBounds.Bottom + .001)
                        throw new InvalidOperationException("Combat tactics simulation left its legal bounds.");
                }
                check(attacks >= 15, "thirty-second simulation repeatedly counterattacks for every range and DPI");
                check(retreats > 30 && totalTravel > 600 * scale, "simulation uses real evasive travel rather than a stationary attack loop");
                check(Finite(input.Hero), "fifteen hundred movement steps remain finite and bounded");
            }
            return "Combat tactics: " + checks + " assertions passed (pure movement, including 30000 simulated frames; no filesystem, network, cleanup, or progress writes).";
        }
    }
}
