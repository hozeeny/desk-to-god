using System;
using System.Drawing;

namespace NetBunny
{
    internal sealed class HeroFootPose
    {
        internal PointF Hip, Knee, Ankle;
        internal double Phase, SoleY, GroundY, FootAngle;
        internal bool Planted;
    }
    internal sealed class HeroWeaponGrip
    {
        internal int Index;
        internal float Width, Height;
        internal PointF Main, Support;
        internal bool TwoHands;
    }
    internal sealed class HeroPose
    {
        internal HeroFootPose NearFoot, FarFoot;
        internal PointF NearShoulder, NearElbow, NearHand, FarShoulder, FarElbow, FarHand;
        internal double Bob, Lean, Attack, WeaponAngle;
        internal HeroWeaponGrip Weapon;
        internal PointF BodyPoint(PointF point)
        {
            double a = Lean * Math.PI / 180, x = point.X - 110, y = point.Y - 143;
            return new PointF((float)(110 - Attack * 8 + x * Math.Cos(a) - y * Math.Sin(a)), (float)(143 + Bob + x * Math.Sin(a) + y * Math.Cos(a)));
        }
    }
    internal static class HeroPoseRig
    {
        // Controller advances the cycle from actual travelled distance. During stance,
        // the foot travels backwards at exactly this rate in the character's local frame.
        internal const double StanceFraction = .52;
        internal const double TravelPerCycle = 46.0 / StanceFraction;
        internal static HeroPose Evaluate(AdventureRenderState state, HeroWeaponGrip weapon)
        {
            string phase = (state.Phase ?? "run").ToLowerInvariant(); bool run = phase == "run" || phase == "running";
            bool pant = phase == "pant" || phase == "panting" || phase == "idle" || phase == "rest";
            bool attacking = phase == "attack" || state.AttackProgress > .03;
            double cycle = RasterActorRenderer.NormalizedCycle(state), a = cycle * Math.PI * 2;
            HeroPose pose = new HeroPose(); pose.Weapon = weapon; pose.Attack = attacking ? Math.Sin(Clamp(state.AttackProgress) * Math.PI) : 0;
            pose.Bob = run ? Math.Cos(a * 2) * 1.6 : -6.5 + (pant ? Math.Sin(state.Time * 6) * .7 : Math.Sin(state.Time * 2) * .4);
            pose.Lean = run ? 7 + Math.Sin(a) * .7 : pant ? 7 + Math.Sin(state.Time * 6) * .8 : attacking ? 1 + pose.Attack * 7 : 2;
            pose.NearFoot = Foot(pose.BodyPoint(new PointF(104, 146)), 104, 191, cycle, run);
            pose.FarFoot = Foot(pose.BodyPoint(new PointF(120, 144)), 120, 188, cycle + .5, run);
            pose.NearShoulder = new PointF(96, 112); pose.FarShoulder = new PointF(126, 111);
            pose.NearHand = new PointF((float)(116 + pose.Attack * 4), (float)(139 - pose.Attack * 8 + (run ? Math.Sin(a) * .65 : 1)));
            pose.WeaponAngle = attacking ? -20 + pose.Attack * 18 : pant ? 3 : -3 + Math.Sin(a) * .8;
            double angle = pose.WeaponAngle * Math.PI / 180;
            if (weapon.TwoHands)
            {
                double dx = (weapon.Support.X - weapon.Main.X) * weapon.Width, dy = (weapon.Support.Y - weapon.Main.Y) * weapon.Height;
                pose.FarHand = new PointF((float)(pose.NearHand.X + dx * Math.Cos(angle) - dy * Math.Sin(angle)), (float)(pose.NearHand.Y + dx * Math.Sin(angle) + dy * Math.Cos(angle)));
            }
            else pose.FarHand = new PointF((float)(143 + Math.Sin(a) * (run ? 1.5 : 0)), (float)(150 - Math.Sin(a) * (run ? 1 : 0)));
            pose.NearElbow = Joint(pose.NearShoulder, pose.NearHand, 24, 23, -1);
            pose.FarElbow = Joint(pose.FarShoulder, pose.FarHand, 22, 23, -1);
            return pose;
        }
        private static HeroFootPose Foot(PointF hip, double centerX, double ground, double rawPhase, bool run)
        {
            double phase = rawPhase - Math.Floor(rawPhase), halfStride = TravelPerCycle * StanceFraction / 2;
            double x = centerX + (centerX < 110 ? -4 : 5), y = ground, footAngle = 0; bool planted = true;
            if (run)
            {
                planted = phase <= StanceFraction;
                if (planted) x = centerX + halfStride - TravelPerCycle * phase;
                else
                {
                    double u = (phase - StanceFraction) / (1 - StanceFraction), u2 = u * u, u3 = u2 * u;
                    double tangent = -TravelPerCycle * (1 - StanceFraction);
                    x = centerX + (2 * u3 - 3 * u2 + 1) * -halfStride + (-2 * u3 + 3 * u2) * halfStride + (u3 - 2 * u2 + u + u3 - u2) * tangent;
                    double lift = Math.Sin(Math.PI * u); y = ground - 25 * lift * lift;
                    footAngle = 12 * Math.Sin(2 * Math.PI * u) * lift;
                }
            }
            HeroFootPose foot = new HeroFootPose { Hip = hip, Ankle = new PointF((float)x, (float)(y - 10)), SoleY = y, GroundY = ground, Phase = phase, Planted = planted, FootAngle = footAngle };
            foot.Knee = Joint(hip, foot.Ankle, 23, 25, 1); return foot;
        }
        internal static PointF Joint(PointF start, PointF end, double upper, double lower, int bend)
        {
            double dx = end.X - start.X, dy = end.Y - start.Y, distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < .001) return new PointF(start.X + (float)upper, start.Y);
            double d = Math.Min(upper + lower - .001, Math.Max(Math.Abs(upper - lower) + .001, distance));
            double along = (upper * upper - lower * lower + d * d) / (2 * d), height = Math.Sqrt(Math.Max(0, upper * upper - along * along));
            return new PointF((float)(start.X + dx / distance * along + bend * dy / distance * height), (float)(start.Y + dy / distance * along - bend * dx / distance * height));
        }
        private static double Clamp(double x) { return Math.Max(0, Math.Min(1, x)); }
        internal static string SelfTest()
        {
            int checks = 0; Action<bool, string> check = delegate(bool ok, string name) { if (!ok) throw new InvalidOperationException("Grounded hero rig: " + name); checks++; };
            HeroWeaponGrip weapon = new HeroWeaponGrip { Width = 137, Height = 72, Main = new PointF(.34f,.58f), Support = new PointF(.54f,.43f), TwoHands = true };
            double previousNearX = 0, previousFarX = 0; HeroPose previous = null;
            for (int n = 0; n <= 240; n++)
            {
                double cycle = n / 240.0; HeroPose pose = Evaluate(new AdventureRenderState { Phase = "run", Cycle = cycle, Time = 0 }, weapon);
                check(pose.NearFoot.Planted || pose.FarFoot.Planted, "at least one foot supports the body");
                foreach (HeroFootPose foot in new[] { pose.NearFoot, pose.FarFoot })
                {
                    check(foot.SoleY <= foot.GroundY + .0001, "sole never sinks below its ground plane");
                    check(Math.Abs(Distance(foot.Hip, foot.Knee) - 23) < .01 && Math.Abs(Distance(foot.Knee, foot.Ankle) - 25) < .01, "leg segments keep physical lengths");
                    double cross = (foot.Ankle.Y - foot.Hip.Y) * (foot.Knee.X - foot.Hip.X) - (foot.Ankle.X - foot.Hip.X) * (foot.Knee.Y - foot.Hip.Y);
                    check(cross >= -.01, "both knees bend toward the character front");
                    if (foot.Planted) check(Math.Abs(foot.SoleY - foot.GroundY) < .0001 && Math.Abs(foot.FootAngle) < .0001, "supporting sole remains flat at ground height");
                }
                double nearX = TravelPerCycle * cycle + pose.NearFoot.Ankle.X, farX = TravelPerCycle * cycle + pose.FarFoot.Ankle.X;
                if (previous != null)
                {
                    if (previous.NearFoot.Planted && pose.NearFoot.Planted && pose.NearFoot.Phase > previous.NearFoot.Phase) check(Math.Abs(nearX - previousNearX) < .0001, "near foot has no world-space sliding during stance");
                    if (previous.FarFoot.Planted && pose.FarFoot.Planted && pose.FarFoot.Phase > previous.FarFoot.Phase) check(Math.Abs(farX - previousFarX) < .0001, "far foot has no world-space sliding during stance");
                    check(Distance(previous.NearFoot.Ankle, pose.NearFoot.Ankle) < 1.6 && Distance(previous.FarFoot.Ankle, pose.FarFoot.Ankle) < 1.6, "recovery trajectory is continuous at heel strike and toe off");
                }
                previous = pose; previousNearX = nearX; previousFarX = farX;
            }
            foreach (string phase in new[] { "idle", "pant", "attack" })
            {
                HeroPose pose = Evaluate(new AdventureRenderState { Phase = phase, Time = 1, AttackProgress = phase == "attack" ? .5 : 0 }, weapon);
                check(pose.NearFoot.Planted && pose.FarFoot.Planted, "stationary " + phase + " plants both feet");
            }
            return "Grounded hero rig: " + checks + " assertions passed; constant world-space stance feet, horizontal contact, forward knees, fixed bone lengths, continuous recovery and stationary support.";
        }
        private static double Distance(PointF a, PointF b) { double dx = a.X - b.X, dy = a.Y - b.Y; return Math.Sqrt(dx * dx + dy * dy); }
    }
}
